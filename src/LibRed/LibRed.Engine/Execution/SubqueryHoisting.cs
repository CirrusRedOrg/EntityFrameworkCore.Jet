using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// Decides whether a subquery's result is the same for every outer row, so it can be evaluated once per
/// statement instead of once per row.
/// </summary>
/// <remarks>
/// <para>
/// EF emits uncorrelated subqueries inside DML predicates. The <c>ExecuteDelete</c> for a <c>GROUP BY</c>
/// predicate becomes <c>… WHERE o.OrderID &lt; (SELECT TOP 1 (…) FROM Orders o0 GROUP BY … HAVING COUNT(*) &gt;
/// 11)</c>: that subquery never mentions the outer alias, yet it was re-evaluated for all 2155 candidate rows,
/// costing ~42.8 s in one Northwind test and ~11.9 s in another (as an <c>IN</c>). The <c>GROUP BY</c> only makes
/// each repeat expensive — the defect is repeating it at all.
/// </para>
/// <para>
/// Two checks must both pass, because each covers what the other misses:
/// </para>
/// <list type="number">
///   <item>
///     No <b>qualified</b> reference to an outer alias anywhere inside, including nested subqueries. This is
///     needed because a conditional can hide one from evaluation — <c>IIF(x, o.Col, 1)</c> may never touch
///     <c>o.Col</c> on the row a trial happens to look at.
///   </item>
///   <item>
///     The subquery <b>runs with no outer scope</b> (see the caller). That settles <b>unqualified</b> references
///     using the evaluator's own resolver: one that binds to an inner table succeeds, while one that would bind
///     outward walks out, finds nothing, and throws. Deferring to <see cref="EvalScope.TryResolve" /> rather than
///     re-implementing name resolution is the point — the two can then never disagree about what a bare column
///     name means.
///   </item>
/// </list>
/// </remarks>
internal static class SubqueryHoisting
{
    /// <summary>
    ///     Whether <paramref name="query" /> qualifies a column with one of <paramref name="outerAliases" />, or
    ///     contains something this walk cannot inspect. True means "do not hoist": unrecognised shapes are
    ///     treated as correlated so a missed case costs speed, never correctness.
    /// </summary>
    internal static bool MayReferenceOuter(SelectStatement query, HashSet<string> outerAliases)
        => outerAliases.Count > 0 && Statement(query, outerAliases);

    private static bool Statement(SqlStatement statement, HashSet<string> outer)
    {
        if (statement is not SelectStatement s)
        {
            return true; // a set operation or anything else: not inspected, so assume correlated
        }

        return s.Projection.Any(p => Expr(p.Value, outer))
            || (s.From is { } from && Table(from, outer))
            || Expr(s.Where, outer)
            || s.GroupBy.Any(g => Expr(g, outer))
            || Expr(s.Having, outer)
            || s.OrderBy.Any(o => Expr(o.Value, outer))
            || Expr(s.Top, outer);
    }

    private static bool Table(TableReference table, HashSet<string> outer) => table switch
    {
        NamedTable => false,
        SubqueryTable t => Statement(t.Query, outer),
        JoinTable j => Table(j.Left, outer) || Table(j.Right, outer) || Expr(j.On, outer),
        _ => true, // unknown source shape
    };

    private static bool Expr(Expression? e, HashSet<string> outer) => e switch
    {
        null => false,
        ColumnReference { Table: { } t } => outer.Contains(t),
        // Unqualified: the trial execution decides, using the evaluator's own resolution rules.
        ColumnReference => false,
        LiteralExpression or ParameterExpression or SystemVariableExpression
            or StarExpression or QualifiedStarExpression => false,
        BinaryExpression b => Expr(b.Left, outer) || Expr(b.Right, outer),
        UnaryExpression u => Expr(u.Operand, outer),
        FunctionCall f => f.Arguments.Any(a => Expr(a, outer)),
        InListExpression il => Expr(il.Value, outer) || il.Items.Any(i => Expr(i, outer)),
        ScalarSubquery sq => Statement(sq.Query, outer),
        ExistsExpression ex => Statement(ex.Query, outer),
        InSubqueryExpression isq => Expr(isq.Value, outer) || Statement(isq.Query, outer),
        _ => true, // unknown expression shape
    };
}
