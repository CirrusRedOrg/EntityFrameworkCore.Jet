using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// Finds subqueries in a join's ON condition that cannot depend on the right side, so a nested loop can
/// evaluate them once per LEFT row instead of once per candidate PAIR.
/// </summary>
/// <remarks>
/// <para>
/// EF emits these. <c>GroupBy_Select_Entire_Entity_Join</c> joins 89 grouped rows to 91 customers on
/// <c>(SELECT TOP 1 o1.CustomerID FROM Orders o1 WHERE o2.CustomerID = o1.CustomerID … ORDER BY o1.OrderID)
/// = c.CustomerID</c>. That subquery correlates on <c>o2</c> — the left subtree — and never mentions
/// <c>c</c>, yet the loop ran it for all 8,099 pairs to produce 89 distinct answers: a full <c>Orders</c>
/// scan and sort each time, measured at 3.5 ms a pair and 28.5 s for the query.
/// </para>
/// <para>
/// This is loop-invariant code motion, not caching. The invariance is a static property of the expression —
/// it references no right-side alias — so it is established once by inspection rather than discovered at
/// runtime by watching values repeat. Nothing is remembered between left rows, so there is no cache to
/// invalidate when a statement writes to a table its own predicate reads, and no dictionary to grow.
/// </para>
/// <para>
/// It cannot decorrelate: a correlated body still runs once per left row. That is <see cref="ExistsSemiJoin"/>
/// and <see cref="ScalarAggregateSemiJoin"/>'s job, and they decline this shape — <c>TOP 1 … ORDER BY</c> is
/// neither an aggregate nor <c>Top: null</c>. What this removes is the repetition those two never see, because
/// it happens below them in the join rather than above them in the row loop.
/// </para>
/// </remarks>
internal static class JoinPredicateHoisting
{
    /// <summary>
    ///     The subquery-bearing subexpressions of <paramref name="on"/> that provably cannot reference
    ///     <paramref name="rightAliases"/>, and so hold still while the inner loop turns. Empty when there is
    ///     nothing to hoist, which is the overwhelmingly common case and costs one walk of a small tree.
    /// </summary>
    /// <remarks>
    ///     Two conditions, both required, and both deliberately conservative:
    ///     <see cref="SubqueryHoisting.MayReferenceOuter(Expression, HashSet{string})"/> rules out a QUALIFIED
    ///     reference to the right side, and <see cref="SubqueryHoisting.HasUnqualifiedColumn"/> rules out a bare
    ///     column name, which only the evaluator's resolver could attribute to a side. The trial-run trick that
    ///     settles bare names elsewhere is unavailable here: this commits to a rewrite before any pair is
    ///     evaluated, which is exactly the case that method documents itself for. EF always qualifies.
    /// </remarks>
    internal static IReadOnlyList<Expression> Invariants(Expression? on, HashSet<string> rightAliases)
    {
        if (on is null || rightAliases.Count == 0)
        {
            return [];
        }

        var found = new List<Expression>();
        Walk(on);
        return found;

        void Walk(Expression e)
        {
            // Only subqueries are worth the machinery: everything else in an ON is cheap enough that evaluating
            // it per pair costs less than the rewrite would.
            if (e is ScalarSubquery or ExistsExpression or InSubqueryExpression)
            {
                if (!SubqueryHoisting.MayReferenceOuter(e, rightAliases)
                    && !SubqueryHoisting.HasUnqualifiedColumn(e))
                {
                    found.Add(e);
                    return; // hoisted whole; no reason to look inside it
                }

                // Not invariant itself, but a nested subquery inside it might be - an IN whose VALUE side is a
                // correlated column still has an outer-independent body.
                if (e is InSubqueryExpression inSub)
                {
                    Walk(inSub.Value);
                }

                return;
            }

            switch (e)
            {
                case BinaryExpression b:
                    Walk(b.Left);
                    Walk(b.Right);
                    break;
                case UnaryExpression u:
                    Walk(u.Operand);
                    break;
                case FunctionCall f:
                    foreach (Expression a in f.Arguments)
                    {
                        Walk(a);
                    }

                    break;
                case InListExpression il:
                    Walk(il.Value);
                    foreach (Expression i in il.Items)
                    {
                        Walk(i);
                    }

                    break;
            }
        }
    }

    /// <summary>
    ///     <paramref name="on"/> with each expression in <paramref name="values"/> replaced by its value, matched
    ///     by reference so two textually identical subqueries stay distinct. Shapes this does not recognise are
    ///     returned untouched, which is safe: an unreplaced subquery is simply evaluated as it was before.
    /// </summary>
    internal static Expression Substitute(Expression on, IReadOnlyDictionary<Expression, object?> values) =>
        values.TryGetValue(on, out object? value)
            ? new LiteralExpression(value)
            : on switch
            {
                BinaryExpression b => b with
                {
                    Left = Substitute(b.Left, values),
                    Right = Substitute(b.Right, values),
                },
                UnaryExpression u => u with { Operand = Substitute(u.Operand, values) },
                FunctionCall f => f with { Arguments = f.Arguments.Select(a => Substitute(a, values)).ToList() },
                InListExpression il => il with
                {
                    Value = Substitute(il.Value, values),
                    Items = il.Items.Select(i => Substitute(i, values)).ToList(),
                },
                InSubqueryExpression isq => isq with { Value = Substitute(isq.Value, values) },
                _ => on,
            };
}
