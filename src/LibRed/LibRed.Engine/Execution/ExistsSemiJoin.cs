using LibRed.Catalog;
using LibRed.Engine.Plan;
using LibRed.Engine.Planning;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// Turns a correlated <c>EXISTS</c> into a hash semi-join: run the subquery <b>once</b> without its correlation
/// predicate, hash the values it would have been correlated on, then test each outer row against that set.
/// </summary>
/// <remarks>
/// <para>
/// A correlated subquery is evaluated per outer row, so an <c>EXISTS</c> whose body is a join re-runs that join
/// for every candidate row. Measured on Northwind: the <c>ExecuteDelete</c> EF generates for a predicate over a
/// navigation puts a three-table join inside <c>EXISTS</c>, and deleting 164 of 2155 rows took <b>92.4 s</b> —
/// running that join once costs <b>101 ms</b>. Correlating barely narrowed the work (61.5 ms per iteration
/// against 101 ms for the whole unfiltered join), because the correlation filters after the joins have already
/// happened. Decorrelating is the standard fix — SQL Server's <c>Left Semi Join</c>, PostgreSQL's
/// <c>JOIN_SEMI</c>, Oracle's subquery unnesting.
/// </para>
/// <para>
/// The rewrite is only valid when every outer reference sits in a top-level <c>inner = outer</c> equality, so
/// removing those conjuncts leaves a subquery that means the same thing for every outer row. Anything else — an
/// outer reference in a residual conjunct, a <c>GROUP BY</c> key, a <c>HAVING</c>, or a nested subquery — declines,
/// and the caller falls back to per-row evaluation.
/// </para>
/// <para>
/// A <c>GROUP BY</c> body is still decorrelated, by grouping on the correlation columns as well: that splits each
/// group by key, giving the same partition the correlation produced one key at a time, so <c>HAVING</c> decides
/// each group identically. What must decline is a body that yields a row over an EMPTY input — see
/// <see cref="EmptyInputMeansNoRows" />.
/// </para>
/// </remarks>
internal sealed class ExistsSemiJoin
{
    /// <summary>The subquery stripped of its correlation, projecting the inner sides of the equalities.</summary>
    private readonly SelectStatement _keyQuery;

    /// <summary>The outer sides of the equalities, in the same order as the key query's projection.</summary>
    private readonly IReadOnlyList<Expression> _outerKeys;

    /// <summary>True when the key query projects the <c>IN</c> value after the correlation columns.</summary>
    private readonly bool _hasInValue;

    private HashSet<object?[]>? _keys;
    private HashSet<object?[]>? _nullTail;

    private ExistsSemiJoin(SelectStatement keyQuery, IReadOnlyList<Expression> outerKeys, bool hasInValue)
        => (_keyQuery, _outerKeys, _hasInValue) = (keyQuery, outerKeys, hasInValue);

    /// <summary>
    ///     Analyses an <c>EXISTS</c> subquery, returning a semi-join plan or null when the rewrite would not be
    ///     sound. <paramref name="outerColumns" /> supplies the outer side's types, since a hash is only
    ///     consistent with the evaluator's <c>=</c> within one type kind.
    /// </summary>
    internal static ExistsSemiJoin? TryBuild(
        SelectStatement subquery,
        IReadOnlyList<OutputColumn> outerColumns,
        HashSet<string> outerAliases,
        JetCatalog catalog)
        => TryBuild(subquery, null, outerColumns, outerAliases, catalog);

    /// <summary>
    ///     The same analysis for <c>x [NOT] IN (subquery)</c>, where <paramref name="inValue" /> is the outer
    ///     expression being tested. A correlated <c>IN</c> is a semi-join too — membership is one more equality
    ///     between the subquery's output column and <paramref name="inValue" /> — so the value joins the hash key
    ///     as a final column and the probe supplies it alongside the correlation values.
    /// </summary>
    /// <remarks>
    ///     Stricter than the <c>EXISTS</c> form in two ways, both because <c>IN</c> asks for the body's VALUES and
    ///     not merely for a row:
    ///     <list type="bullet">
    ///       <item>
    ///         Any <c>TOP</c> declines. <c>TOP n</c> cannot change whether a row exists, but it certainly changes
    ///         which values are in the set, so it cannot be dropped from the key query.
    ///       </item>
    ///       <item>
    ///         <c>GROUP BY</c>/<c>HAVING</c> decline. The <c>EXISTS</c> form groups by the correlation columns as
    ///         well, which is sound when only existence is asked; here the output column would have to be added to
    ///         the grouping too, and whether that preserves the projected values depends on what a non-grouping
    ///         column projects out of a group. Not worth guessing at — EF emits the ungrouped shape.
    ///       </item>
    ///     </list>
    /// </remarks>
    internal static ExistsSemiJoin? TryBuildForIn(
        SelectStatement subquery,
        Expression inValue,
        IReadOnlyList<OutputColumn> outerColumns,
        HashSet<string> outerAliases,
        JetCatalog catalog)
        => subquery is { Top: null, GroupBy.Count: 0, Having: null, IsSelectStar: false, Projection: [{ Value: ColumnReference }] }
            ? TryBuild(subquery, inValue, outerColumns, outerAliases, catalog)
            : null;

    private static ExistsSemiJoin? TryBuild(
        SelectStatement subquery,
        Expression? inValue,
        IReadOnlyList<OutputColumn> outerColumns,
        HashSet<string> outerAliases,
        JetCatalog catalog)
    {
        // (ORDER BY and DISTINCT are irrelevant to EXISTS.)
        if (!TopCannotChangeExistence(subquery)
            || !EmptyInputMeansNoRows(subquery)
            || SubqueryCorrelation.TrySplit(subquery, outerColumns, outerAliases, catalog) is not { } split)
        {
            return null;
        }

        // The IN value is a key column like any other — the difference is only that its inner side comes from the
        // projection rather than from a WHERE equality, and that a null there means UNKNOWN rather than no match
        // (see BuildSemiJoinKeys). It goes last so the probe can append the already-evaluated value.
        var projected = new List<Expression>(split.InnerKeys);
        if (inValue is not null)
        {
            Expression output = subquery.Projection[0].Value;
            if (!SubqueryCorrelation.SameKind((ColumnReference)output, split.Plan, catalog, inValue, outerColumns))
            {
                return null;
            }

            projected.Add(output);
        }

        var keyQuery = subquery with
        {
            Projection = projected.Select(k => new SelectItem(k, null)).ToList(),
            IsSelectStar = false,
            Where = split.Residual,
            // Grouping by the correlation columns as well splits each group by key, which is exactly the
            // partition the correlation predicate produced one key at a time — so a group passes HAVING here
            // if and only if it passed for that outer row. The keys must also be grouping columns to be
            // projectable at all. (Empty for a non-grouped body, leaving the plan unchanged.)
            GroupBy = subquery.GroupBy.Count == 0 ? [] : [.. subquery.GroupBy, .. split.InnerKeys],
            OrderBy = [],
            Distinct = false,
            DistinctRow = false,
            // Dropped deliberately: the key query must yield EVERY matching key, not the first n. Existence per
            // key is unaffected, which is what TopCannotChangeExistence establishes.
            Top = null,
            TopPercent = false,
        };

        return new ExistsSemiJoin(keyQuery, split.OuterKeys, inValue is not null);
    }

    /// <summary>
    ///     Whether a body over zero rows returns zero rows — which is what makes "the key is absent from the set"
    ///     mean "EXISTS is false".
    /// </summary>
    /// <remarks>
    ///     It fails for exactly one shape: an aggregate with no <c>GROUP BY</c>, which has a single group even
    ///     when nothing matched, so it yields a row regardless — <c>EXISTS (SELECT COUNT(*) FROM I WHERE I.K =
    ///     o.K)</c> is true for every outer row, and a key test would say false for the unmatched ones. A
    ///     <c>HAVING</c> without <c>GROUP BY</c> filters that same lone group and can admit it on empty input
    ///     (<c>HAVING COUNT(*) = 0</c>), so it goes the same way.
    ///     <para>
    ///         With a <c>GROUP BY</c> there is no such row: no input rows means no groups. Before this test the
    ///         non-grouped aggregate declined only by accident — <c>SubtreeAliases</c> had no
    ///         <c>AggregateNode</c> case, so no inner alias was ever found and every conjunct fell to the
    ///         residual. Adding that case to support grouped bodies removed the accident.
    ///     </para>
    /// </remarks>
    private static bool EmptyInputMeansNoRows(SelectStatement subquery)
        => subquery.GroupBy.Count > 0
            || (subquery.Having is null && !subquery.Projection.Any(p => QueryPlanner.HasAggregate(p.Value)));

    /// <summary>
    ///     Whether the body's <c>TOP</c> can be ignored when all we are asking is whether ANY row exists.
    /// </summary>
    /// <remarks>
    ///     <c>TOP n</c> for <c>n &gt;= 1</c> cannot change existence: if the body matches at all it still returns a
    ///     row, and if it matches nothing no limit conjures one. EF emits <c>EXISTS (SELECT TOP 1 …)</c> for
    ///     <c>Any()</c>, so declining on any <c>TOP</c> at all left that common shape running per outer row.
    ///     <para>
    ///         <c>TOP 0</c> does change it — the body returns nothing, so EXISTS is always false — and so does
    ///         <c>TOP 0 PERCENT</c>. A non-literal <c>TOP</c> (parameter or expression) cannot be judged here.
    ///         All of those decline. PERCENT is refused outright rather than reasoned about: for n &gt; 0 it
    ///         rounds up to at least one row and would be safe, but that is a rule worth verifying against ACE
    ///         before relying on it.
    ///     </para>
    /// </remarks>
    private static bool TopCannotChangeExistence(SelectStatement subquery)
    {
        if (subquery.Top is null)
        {
            return true;
        }

        return !subquery.TopPercent
            && subquery.Top is LiteralExpression { Value: { } value }
            && IsNumeric(value)
            && Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) >= 1m;
    }

    private static bool IsNumeric(object v)
        => v is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    /// <summary>
    ///     Tests one outer row. The key set is built on first use — once per statement, not per row.
    /// </summary>
    internal bool Matches(QueryExecutor executor, ExpressionEvaluator outerEval)
        => Probe(executor, outerEval, null) is { } probe && _keys!.Contains(probe);

    /// <summary>
    ///     Tests one outer row for <c>x IN (subquery)</c>, reporting membership <b>and</b> whether the subquery's
    ///     column yielded a null for this row — the caller needs both to reproduce SQL's three-valued <c>IN</c>,
    ///     where "no match" and "no match but a null was seen" differ (FALSE against UNKNOWN).
    /// </summary>
    /// <param name="value">
    ///     The already-evaluated left side. The caller has evaluated it to check for null before reaching here, so
    ///     re-evaluating it would only repeat that work.
    /// </param>
    internal (bool Found, bool HasNull) ContainsValue(QueryExecutor executor, ExpressionEvaluator outerEval, object? value)
    {
        if (Probe(executor, outerEval, value) is not { } probe)
        {
            // A null correlation value matches no inner row, so the subquery is empty for this outer row —
            // which is FALSE, not UNKNOWN, even for `x IN ()`.
            return (false, false);
        }

        return _keys!.Contains(probe)
            ? (true, false) // Short-circuits like the row-by-row loop: on a match the null flag is unused.
            : (false, _nullTail!.Contains(probe[..^1]));
    }

    /// <summary>
    ///     Builds the key set on first use — once per statement, not per row — and evaluates this outer row's
    ///     probe. Null when a correlation value is null, which no inner row can equal.
    /// </summary>
    private object?[]? Probe(QueryExecutor executor, ExpressionEvaluator outerEval, object? inValue)
    {
        if (_keys is null)
        {
            (_keys, _nullTail) = executor.BuildSemiJoinKeys(
                _keyQuery, _outerKeys.Count + (_hasInValue ? 1 : 0), _hasInValue);
        }

        var probe = new object?[_outerKeys.Count + (_hasInValue ? 1 : 0)];
        for (var i = 0; i < _outerKeys.Count; i++)
        {
            if ((probe[i] = outerEval.Evaluate(_outerKeys[i])) is null)
            {
                return null;
            }
        }

        if (_hasInValue)
        {
            probe[^1] = inValue;
        }

        return probe;
    }
}
