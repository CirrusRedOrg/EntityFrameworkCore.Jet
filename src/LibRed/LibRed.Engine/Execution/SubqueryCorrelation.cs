using LibRed.Catalog;
using LibRed.Engine.Plan;
using LibRed.Engine.Planning;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// A correlated subquery's WHERE split into the equalities that tie it to the outer row and the rest.
/// </summary>
/// <param name="Plan">The body's plan, used to tell inner aliases and column types from outer ones.</param>
/// <param name="InnerKeys">The subquery sides of the correlation equalities.</param>
/// <param name="OuterKeys">The outer sides, positionally matched to <paramref name="InnerKeys" />.</param>
/// <param name="Residual">The conjuncts that were not correlation equalities, re-ANDed, or null if there were none.</param>
internal readonly record struct CorrelationSplit(
    PlanNode Plan,
    IReadOnlyList<Expression> InnerKeys,
    IReadOnlyList<Expression> OuterKeys,
    Expression? Residual);

/// <summary>
/// The analysis every decorrelation rewrite starts from: which conjuncts of a subquery's WHERE are
/// <c>inner = outer</c> equalities, and whether removing them leaves a body that means the same thing for every
/// outer row. What each rewrite then does with the split differs — <see cref="ExistsSemiJoin" /> hashes the keys
/// for a membership test, <see cref="ScalarAggregateSemiJoin" /> maps them to one aggregate value each — but this
/// part, and the reasons it declines, is common to all of them.
/// </summary>
internal static class SubqueryCorrelation
{
    /// <summary>
    ///     Splits <paramref name="subquery" />'s WHERE, or returns null when decorrelating would not be sound.
    ///     <paramref name="outerColumns" /> supplies the outer side's types, since a hash is only consistent with
    ///     the evaluator's <c>=</c> within one type kind.
    /// </summary>
    internal static CorrelationSplit? TrySplit(
        SelectStatement subquery,
        IReadOnlyList<OutputColumn> outerColumns,
        HashSet<string> outerAliases,
        JetCatalog catalog)
    {
        if (subquery is not { Where: { } where, From: not null })
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
        // can tell; the hoisting path settles that with a trial run, but these rewrites commit before the body is
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

        return new CorrelationSplit(
            plan, innerKeys, outerKeys,
            residual.Count == 0 ? null : residual.Aggregate((a, b) => new BinaryExpression(BinaryOperator.And, a, b)));
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
    internal static bool SameKind(
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
}
