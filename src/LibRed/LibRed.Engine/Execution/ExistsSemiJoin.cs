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

    private HashSet<object?[]>? _keys;

    private ExistsSemiJoin(SelectStatement keyQuery, IReadOnlyList<Expression> outerKeys)
        => (_keyQuery, _outerKeys) = (keyQuery, outerKeys);

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
    {
        // (ORDER BY and DISTINCT are irrelevant to EXISTS.)
        if (subquery is not { Where: { } where, From: not null }
            || !TopCannotChangeExistence(subquery)
            || !EmptyInputMeansNoRows(subquery))
        {
            return null;
        }

        PlanNode plan;
        try
        {
            plan = QueryPlanner.PlanSelect(subquery);
        }
        catch (Exception)
        {
            return null; // Unplannable here is not our problem to report; let the normal path raise it.
        }

        HashSet<string> innerAliases = QueryPlanner.SubtreeAliases(plan);

        var innerKeys = new List<Expression>();
        var outerKeys = new List<Expression>();
        var residual = new List<Expression>();

        foreach (Expression conjunct in IndexSelection.Conjuncts(where))
        {
            if (conjunct is BinaryExpression { Operator: BinaryOperator.Equal } eq
                && Orient(eq, innerAliases, outerAliases) is var (inner, outer)
                && inner is ColumnReference innerCol
                && SameKind(innerCol, plan, catalog, outer, outerColumns))
            {
                innerKeys.Add(inner);
                outerKeys.Add(outer);
            }
            else
            {
                residual.Add(conjunct);
            }
        }

        // Every outer reference must have been consumed as a key, or the body would still vary per outer row.
        //
        // The test is "references no OUTER alias", not "references only inner aliases": the latter (ReferencesOnly)
        // treats a nested subquery as opaque and refuses, which declined an EXISTS whose residual merely holds an
        // outer-INDEPENDENT `IN (…)` — the Delete_Where_predicate_with_GroupBy_aggregate_2 shape. MayReferenceOuter
        // descends into nested subqueries instead, so such a residual is recognised as outer-independent.
        //
        // Unqualified columns are refused outright. A bare name may bind outward and only the evaluator's resolver
        // can tell; the hoisting path settles that with a trial run, but this rewrite commits before the body is
        // ever executed (the key set is built lazily on first probe), so a wrong guess would surface as a query
        // error rather than a fallback. EF always qualifies, so nothing real is lost.
        //
        // GROUP BY and HAVING stay in the key query, so they face the same test as a residual conjunct.
        if (innerKeys.Count == 0
            || residual.Concat(subquery.GroupBy).Concat(subquery.Having is { } h ? [h] : Array.Empty<Expression>())
                .Any(r => SubqueryHoisting.MayReferenceOuter(r, outerAliases)
                    || SubqueryHoisting.HasUnqualifiedColumn(r)))
        {
            return null;
        }

        var keyQuery = subquery with
        {
            Projection = innerKeys.Select(k => new SelectItem(k, null)).ToList(),
            IsSelectStar = false,
            Where = residual.Count == 0 ? null : residual.Aggregate((a, b) => new BinaryExpression(BinaryOperator.And, a, b)),
            // Grouping by the correlation columns as well splits each group by key, which is exactly the
            // partition the correlation predicate produced one key at a time — so a group passes HAVING here
            // if and only if it passed for that outer row. The keys must also be grouping columns to be
            // projectable at all. (Empty for a non-grouped body, leaving the plan unchanged.)
            GroupBy = subquery.GroupBy.Count == 0 ? [] : [.. subquery.GroupBy, .. innerKeys],
            OrderBy = [],
            Distinct = false,
            DistinctRow = false,
            // Dropped deliberately: the key query must yield EVERY matching key, not the first n. Existence per
            // key is unaffected, which is what TopCannotChangeExistence establishes.
            Top = null,
            TopPercent = false,
        };

        return new ExistsSemiJoin(keyQuery, outerKeys);
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

    /// <summary>Orients an equality so the first element is the subquery side and the second the outer side.</summary>
    private static (Expression Inner, Expression Outer)? Orient(
        BinaryExpression eq, HashSet<string> innerAliases, HashSet<string> outerAliases)
        => IndexSelection.ReferencesOnly(eq.Left, innerAliases) && IndexSelection.ReferencesOnly(eq.Right, outerAliases)
            ? (eq.Left, eq.Right)
            : IndexSelection.ReferencesOnly(eq.Right, innerAliases) && IndexSelection.ReferencesOnly(eq.Left, outerAliases)
                ? (eq.Right, eq.Left)
                : null;

    /// <summary>
    ///     Whether both sides of a correlation equality are the same type kind. A hash agreeing with the
    ///     evaluator's <c>=</c> only exists within a kind (<c>5 = '5'</c> and <c>5 = 5.0</c>, but
    ///     <c>'5' ≠ '5.0'</c>), so a cross-kind correlation must stay a per-row comparison.
    /// </summary>
    private static bool SameKind(
        ColumnReference innerCol,
        PlanNode innerPlan,
        JetCatalog catalog,
        Expression outer,
        IReadOnlyList<OutputColumn> outerColumns)
    {
        IndexSelection.TypeKind? innerKind = IndexSelection.ResolveKind(innerCol, innerPlan, catalog);
        if (innerKind is null)
        {
            return false;
        }

        // The outer side's kind comes from the scope's CLR types rather than the catalog: it may be any
        // expression over already-materialised rows, not necessarily a base-table column.
        if (outer is not ColumnReference outerCol)
        {
            return false;
        }

        Type? clr = outerColumns
            .Where(c => string.Equals(c.Name, outerCol.Column, StringComparison.OrdinalIgnoreCase)
                && (outerCol.Table is null || string.Equals(c.Qualifier, outerCol.Table, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.ClrType)
            .FirstOrDefault();

        return clr is not null && KindOf(clr) == innerKind;
    }

    private static IndexSelection.TypeKind? KindOf(Type t)
    {
        Type u = Nullable.GetUnderlyingType(t) ?? t;
        if (u == typeof(string)) return IndexSelection.TypeKind.Text;
        if (u == typeof(byte[])) return IndexSelection.TypeKind.Binary;
        if (u == typeof(DateTime)) return IndexSelection.TypeKind.Temporal;
        if (u == typeof(Guid)) return IndexSelection.TypeKind.Guid;
        if (u == typeof(bool) || u == typeof(byte) || u == typeof(short) || u == typeof(int) || u == typeof(long)
            || u == typeof(float) || u == typeof(double) || u == typeof(decimal)) return IndexSelection.TypeKind.Numeric;
        return null;
    }

    /// <summary>
    ///     Tests one outer row. The key set is built on first use — once per statement, not per row.
    /// </summary>
    internal bool Matches(QueryExecutor executor, ExpressionEvaluator outerEval)
    {
        _keys ??= executor.BuildSemiJoinKeys(_keyQuery, _outerKeys.Count);

        var probe = new object?[_outerKeys.Count];
        for (var i = 0; i < _outerKeys.Count; i++)
        {
            // A null can never satisfy an equi-predicate, so EXISTS is false without probing.
            if ((probe[i] = outerEval.Evaluate(_outerKeys[i])) is null)
            {
                return false;
            }
        }

        return _keys.Contains(probe);
    }
}
