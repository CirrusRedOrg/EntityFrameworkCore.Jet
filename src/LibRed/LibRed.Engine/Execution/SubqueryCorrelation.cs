using LibRed.Catalog;
using LibRed.Engine.Plan;
using LibRed.Engine.Planning;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// A correlated subquery's WHERE and HAVING split into the equalities that tie it to the outer row and the rest.
/// </summary>
/// <param name="Plan">The body's plan, used to tell inner aliases and column types from outer ones.</param>
/// <param name="InnerKeys">The subquery sides of the correlation equalities.</param>
/// <param name="OuterKeys">The outer sides, positionally matched to <paramref name="InnerKeys" />.</param>
/// <param name="NullSafe">
/// Per key, whether NULL matches NULL there — true for EF's <c>a = b OR (a IS NULL AND b IS NULL)</c> form. A
/// plain <c>=</c> key drops null-bearing rows from the hash and fails a null probe outright, because a null can
/// never satisfy it; a null-safe key must instead hash the null and match it.
/// </param>
/// <param name="Residual">The WHERE conjuncts that were not correlation equalities, re-ANDed, or null if none.</param>
/// <param name="ResidualHaving">
/// The same for HAVING. The key query must use this and not the original HAVING, which still holds the
/// correlation conjunct that was lifted out of it.
/// </param>
internal readonly record struct CorrelationSplit(
    PlanNode Plan,
    IReadOnlyList<Expression> InnerKeys,
    IReadOnlyList<Expression> OuterKeys,
    IReadOnlyList<bool> NullSafe,
    Expression? Residual,
    Expression? ResidualHaving);

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
        if (subquery is not { From: not null })
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
        var nullSafe = new List<bool>();
        var residual = new List<Expression>();
        var residualHaving = new List<Expression>();

        // HAVING is searched for correlations too, but only for conjuncts whose subquery side is a GROUPING KEY.
        // Such a predicate is constant within a group, so it selects whole groups rather than filtering rows
        // inside them — which is exactly what makes lifting it out equivalent to a WHERE correlation, and what
        // leaves the remaining aggregates (COUNT(*) and friends) computed over the same rows as before. A
        // correlation against an aggregate or a non-grouping column would change which rows the aggregate sees,
        // so it stays put. EF emits the grouping-key form: `GROUP BY o0.CustomerID HAVING COUNT(*) > 30 AND
        // o0.CustomerID = o.CustomerID` (its Contains-over-a-GroupBy shape), which without this ran the whole
        // grouping once per outer row — 3.0 s on Northwind.
        foreach ((Expression conjunct, bool fromHaving) in
            IndexSelection.Conjuncts(subquery.Where).Select(c => (c, false))
                .Concat(IndexSelection.Conjuncts(subquery.Having).Select(c => (c, true))))
        {
            // A correlation is either a plain equality or EF's null-safe form. Both give the same pair of
            // operands; they differ only in what a null on either side means.
            (Expression Left, Expression Right)? operands = conjunct switch
            {
                BinaryExpression { Operator: BinaryOperator.Equal } eq => (eq.Left, eq.Right),
                _ => NullSafeEquality(conjunct),
            };

            if (operands is var (left, right)
                && Orient(left, right, innerAliases, outerAliases) is var (inner, outer)
                && inner is ColumnReference innerCol
                && (!fromHaving || IsGroupingKey(inner, subquery.GroupBy))
                && SameKind(innerCol, plan, catalog, outer, outerColumns))
            {
                innerKeys.Add(inner);
                outerKeys.Add(outer);
                nullSafe.Add(conjunct is not BinaryExpression { Operator: BinaryOperator.Equal });
            }
            else
            {
                (fromHaving ? residualHaving : residual).Add(conjunct);
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
        // The GROUP BY keys and whatever is left of HAVING stay in the key query, so they face the same test as a
        // residual WHERE conjunct.
        if (innerKeys.Count == 0
            || residual.Concat(residualHaving).Concat(subquery.GroupBy)
                .Any(r => SubqueryHoisting.MayReferenceOuter(r, outerAliases)
                    || SubqueryHoisting.HasUnqualifiedColumn(r)))
        {
            return null;
        }

        return new CorrelationSplit(plan, innerKeys, outerKeys, nullSafe, And(residual), And(residualHaving));

        static Expression? And(List<Expression> conjuncts)
            => conjuncts.Count == 0 ? null : conjuncts.Aggregate((a, b) => new BinaryExpression(BinaryOperator.And, a, b));
    }

    /// <summary>
    ///     Whether <paramref name="inner" /> is one of the GROUP BY key expressions — the condition for lifting a
    ///     correlation out of HAVING.
    /// </summary>
    private static bool IsGroupingKey(Expression inner, IReadOnlyList<Expression> groupBy)
        => groupBy.Any(k => SameOperand(k, inner));

    /// <summary>
    ///     Recognises EF's null-safe equality — <c>a = b OR (a IS NULL AND b IS NULL)</c> — returning the two
    ///     operands, or null when the conjunct is not that shape.
    /// </summary>
    /// <remarks>
    ///     EF emits this wherever a correlation involves a nullable column, so it is not an edge case but the
    ///     common form. Read as one opaque disjunction it looked like a residual mentioning the outer row, which
    ///     declined the whole rewrite: measured on Northwind, the <c>Late_subquery_pushdown</c> shape took
    ///     <b>9,632 ms</b> written this way against <b>55 ms</b> with a plain <c>=</c>, the SQL being otherwise
    ///     identical.
    ///     <para>
    ///         It really is null-safe equality: <c>a = b</c> is UNKNOWN if either side is null, so the disjunction
    ///         is true exactly when both are non-null and equal, or both are null.
    ///     </para>
    /// </remarks>
    private static (Expression Left, Expression Right)? NullSafeEquality(Expression conjunct)
    {
        if (conjunct is not BinaryExpression { Operator: BinaryOperator.Or } or)
        {
            return null;
        }

        // EF puts the equality first, but don't depend on the operand order of either connective.
        foreach ((Expression eqSide, Expression nullSide) in new[] { (or.Left, or.Right), (or.Right, or.Left) })
        {
            if (eqSide is not BinaryExpression { Operator: BinaryOperator.Equal } eq
                || nullSide is not BinaryExpression { Operator: BinaryOperator.And } and)
            {
                continue;
            }

            if ((IsNullTestOf(and.Left, eq.Left) && IsNullTestOf(and.Right, eq.Right))
                || (IsNullTestOf(and.Left, eq.Right) && IsNullTestOf(and.Right, eq.Left)))
            {
                return (eq.Left, eq.Right);
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="test" /> is <c>operand IS NULL</c> for that same operand.</summary>
    private static bool IsNullTestOf(Expression test, Expression operand)
        => test is UnaryExpression { Operator: UnaryOperator.IsNull, Operand: { } tested } && SameOperand(tested, operand);

    /// <summary>
    ///     Whether two operands of the null-safe form are the same expression. Column references compare
    ///     case-insensitively, since SQL identifiers are; anything else falls back to the AST's own structural
    ///     equality (the expression records are value types by construction).
    /// </summary>
    private static bool SameOperand(Expression a, Expression b)
        => (a, b) switch
        {
            (ColumnReference x, ColumnReference y) =>
                string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase),
            _ => a.Equals(b),
        };

    /// <summary>Orients an equality so the first element is the subquery side and the second the outer side.</summary>
    private static (Expression Inner, Expression Outer)? Orient(
        Expression left, Expression right, HashSet<string> innerAliases, HashSet<string> outerAliases)
        => IndexSelection.ReferencesOnly(left, innerAliases) && IndexSelection.ReferencesOnly(right, outerAliases)
            ? (left, right)
            : IndexSelection.ReferencesOnly(right, innerAliases) && IndexSelection.ReferencesOnly(left, outerAliases)
                ? (right, left)
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
