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
/// removing those conjuncts leaves a subquery that means the same thing for every outer row. Anything else —
/// an outer reference in a residual conjunct or a nested subquery, <c>TOP</c>, <c>GROUP BY</c>/<c>HAVING</c>
/// (whose result depends on which rows the correlation admitted) — declines, and the caller falls back to
/// per-row evaluation.
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
        // TOP/GROUP BY/HAVING make the body's result depend on which rows the correlation admitted, so the body
        // cannot be evaluated once for all outer rows. (ORDER BY and DISTINCT are irrelevant to EXISTS.)
        if (subquery is not { Top: null, GroupBy.Count: 0, Having: null, Where: { } where, From: not null })
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

        // Every outer reference must have been consumed as a key. ReferencesOnly is false for anything holding a
        // subquery, so a residual with a nested subquery declines rather than risk a hidden correlation.
        if (innerKeys.Count == 0 || residual.Any(r => !IndexSelection.ReferencesOnly(r, innerAliases)))
        {
            return null;
        }

        var keyQuery = subquery with
        {
            Projection = innerKeys.Select(k => new SelectItem(k, null)).ToList(),
            IsSelectStar = false,
            Where = residual.Count == 0 ? null : residual.Aggregate((a, b) => new BinaryExpression(BinaryOperator.And, a, b)),
            OrderBy = [],
            Distinct = false,
            DistinctRow = false,
        };

        return new ExistsSemiJoin(keyQuery, outerKeys);
    }

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
