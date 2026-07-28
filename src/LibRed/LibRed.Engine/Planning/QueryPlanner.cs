using LibRed.Engine.Plan;
using LibRed.Sql.Ast;
using LibRed.Sql.Binding;

namespace LibRed.Engine.Planning;

/// <summary>
/// Turns a bound statement into a logical <see cref="PlanNode"/> tree. Index
/// selection and other optimisations are applied as rewrites over the tree.
/// </summary>
public sealed class QueryPlanner
{
    public PlanNode Plan(BoundStatement bound)
    {
        return PlanStatement(bound.Statement);
    }

    private static PlanNode PlanStatement(SqlStatement statement) => statement switch
    {
        SelectStatement select => PlanSelect(select),
        SetOperationStatement set => new SetOperationNode(
            PlanStatement(set.Left), PlanStatement(set.Right), set.Operator),
        _ => throw new NotImplementedException(
            $"Planning for {statement.GetType().Name} is not yet implemented."),
    };

    /// <summary>Plans a SELECT statement directly (used for subqueries).</summary>
    public static PlanNode PlanSelect(SelectStatement select)
    {
        // Shape: From (Scan/Join/Derived) → Filter → Sort → Project → Limit. ORDER BY is
        // applied over the source columns (before projection) so it can reference them.
        PlanNode node = PlanFrom(select.From);

        if (select.Where is not null)
            node = PushPredicates(node, select.Where);

        bool aggregate = select.GroupBy.Count > 0 || select.Having is not null
            || select.Projection.Any(i => HasAggregate(i.Value));
        if (aggregate)
            // The aggregate node owns ORDER BY: its keys are evaluated in the group scope (so they can
            // reference grouping expressions / aggregates), not over the already-projected output.
            node = new AggregateNode(node, select.GroupBy, select.Projection, select.Having, select.OrderBy);
        else if (select.OrderBy.Count > 0)
            node = PushSort(node, select.OrderBy);

        // DISTINCTROW dedupes on the *pre-projection* rows of the contributing tables, so it goes below the
        // projection. It is meaningless with aggregation (grouping already collapses rows) and a no-op for
        // SELECT * (output covers every table), so only the plain projected case needs the node.
        if (select.DistinctRow && !aggregate && !select.IsSelectStar)
            node = new DistinctRowNode(node, select.Projection);

        if (!aggregate && !select.IsSelectStar)
            node = new ProjectNode(node, select.Projection);

        if (select.Distinct)
            node = new DistinctNode(node);

        if (select.Top is { } top)
        {
            // `TOP n ... ORDER BY k` only needs the n smallest rows by k, so tell the sort the bound and let it
            // discard rows that can't survive rather than ordering everything. Only sound when nothing between the
            // sort and the limit changes the row count: a projection is 1:1, but DISTINCT/DISTINCTROW collapse
            // rows, so the n rows reaching the limit are not the n the sort would have kept. PERCENT is excluded
            // because it needs the full input count to work out the take at all.
            if (!select.TopPercent && !select.Distinct && !select.DistinctRow)
                node = BoundSort(node, top);

            node = new LimitNode(node, top, select.TopPercent);
        }

        return node;
    }

    /// <summary>
    ///     Places an ORDER BY as deep in the join tree as it can go: when every key comes from one side of a join,
    ///     sorting that side and letting the join stream produces the same order as sorting the join's output —
    ///     without building the product to sort it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>SELECT TOP 1 c.… FROM Customers c, Orders o, Employees e ORDER BY c.CustomerID</c> ordered a
    ///         679,770-row cross product (91 × 830 × 9) to return one row. Sorting the 91 customers instead lets
    ///         the join emit rows in that order, so the enclosing TOP stops after the first: 1,363 ms to ~10 ms.
    ///         It pays off without a TOP as well — EF's Include emits <c>LEFT JOIN … ORDER BY {principal key}</c>,
    ///         where this sorts the principal table rather than the whole joined result, and the join then streams
    ///         instead of being materialised.
    ///     </para>
    ///     <para>
    ///         Only the LEFT input drives output order: the nested loop iterates it in the outer loop, and the hash
    ///         join probes with it (building the right) for INNER/LEFT. A RIGHT join instead probes with the right,
    ///         so its output follows the right side and a left-side sort would be lost — hence the kind check. The
    ///         recursion handles left-deep chains, so a key on <c>c</c> sinks past both joins of <c>c, o, e</c>.
    ///     </para>
    ///     <para>
    ///         <see cref="Qualifiers" /> returns null for anything it cannot pin to a table — an unqualified column
    ///         (which might bind to either side), a subquery, a qualified star — and those decline, leaving the sort
    ///         above the join.
    ///     </para>
    ///     <para>
    ///         A pushed sort deliberately gets NO row bound from <see cref="BoundSort" />, which only walks
    ///         row-preserving nodes and so stops at the join. Bounding it would be wrong in general: an INNER join
    ///         can drop left rows, so the first n rows of the sorted side need not yield n joined rows. (It would be
    ///         sound for LEFT and CROSS, which never drop one — a refinement, not done here.)
    ///     </para>
    /// </remarks>
    private static PlanNode PushSort(PlanNode node, IReadOnlyList<OrderByItem> keys)
    {
        if (node is JoinNode { Kind: JoinKind.Inner or JoinKind.Left or JoinKind.Cross } j
            && keys.All(k => Qualifiers(k.Value) is { Count: > 0 } q && q.IsSubsetOf(SubtreeAliases(j.Left))))
        {
            return j with { Left = PushSort(j.Left, keys) };
        }

        return new SortNode(node, keys);
    }

    /// <summary>
    ///     Attaches a row bound to the sort at the top of <paramref name="node" />, descending through the
    ///     row-preserving nodes above it. Returns the tree unchanged when there is no sort to bound.
    /// </summary>
    private static PlanNode BoundSort(PlanNode node, Expression limit) => node switch
    {
        SortNode s => s with { Limit = limit },
        ProjectNode p => p with { Input = BoundSort(p.Input, limit) },
        _ => node,
    };

    /// <summary>The aggregate function names recognised by the planner/executor. Includes the Access statistical
    /// aggregates StDev/StDevP (sample/population standard deviation) and Var/VarP (sample/population variance);
    /// the "StdDev"/"StdDevP" spellings are accepted as aliases.</summary>
    internal static bool IsAggregate(string name) =>
        name.ToUpperInvariant() is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX" or "FIRST" or "LAST"
            or "STDEV" or "STDEVP" or "STDDEV" or "STDDEVP" or "VAR" or "VARP";

    internal static bool HasAggregate(Expression e) => e switch
    {
        FunctionCall f when IsAggregate(f.Name) => true,
        FunctionCall f => f.Arguments.Any(HasAggregate),
        BinaryExpression b => HasAggregate(b.Left) || HasAggregate(b.Right),
        UnaryExpression u => HasAggregate(u.Operand),
        _ => false,
    };

    private static PlanNode PlanFrom(TableReference? from) => from switch
    {
        null => new SingleRowNode(), // FROM-less SELECT (e.g. `SELECT 2`) — one row, no columns
        NamedTable t => new ScanNode(t.Name, t.Alias),
        JoinTable j => new JoinNode(PlanFrom(j.Left), PlanFrom(j.Right), j.Kind, j.On),
        SubqueryTable s => new DerivedTableNode(PlanStatement(s.Query), s.Alias), // alias optional (Access allows aliasless)
        _ => throw new NotSupportedException($"Unsupported FROM source {from.GetType().Name}."),
    };

    /// <summary>
    /// Places the WHERE clause's AND-conjuncts as low in the join tree as each can go — a single-table
    /// predicate onto that table's scan, a two-table equality onto the join of those tables — instead of
    /// filtering the full cross product on top. This is what makes an Access comma-join
    /// (<c>FROM a, b, c WHERE a.x=b.x AND …</c>, planned as CROSS joins) tractable: the nested-loop join
    /// evaluates the pushed predicate inside its loop, so intermediate results stay small.
    /// </summary>
    private static PlanNode PushPredicates(PlanNode joinTree, Expression where)
    {
        var conjuncts = new List<Expression>();
        SplitAnd(where, conjuncts);
        (PlanNode node, List<Expression> unplaced) = Place(joinTree, conjuncts);
        return unplaced.Count == 0 ? node : new FilterNode(node, CombineAnd(unplaced));
    }

    /// <summary>Pushes each conjunct that lies entirely within <paramref name="node"/>'s subtree as deep as it
    /// fits; returns the rewritten node and the conjuncts that reference tables outside it (to bubble up).</summary>
    private static (PlanNode Node, List<Expression> Unplaced) Place(PlanNode node, List<Expression> conjuncts)
    {
        HashSet<string> aliases = SubtreeAliases(node);
        var candidates = new List<Expression>();
        var outside = new List<Expression>();
        foreach (Expression c in conjuncts)
            (Qualifiers(c) is { } q && q.Count > 0 && q.IsSubsetOf(aliases) ? candidates : outside).Add(c);

        // Only CROSS/INNER joins are safe to push below (an outer join's null-supplying side changes meaning).
        if (node is JoinNode { Kind: JoinKind.Cross or JoinKind.Inner } j)
        {
            (PlanNode left, List<Expression> afterLeft) = Place(j.Left, candidates);
            (PlanNode right, List<Expression> both) = Place(j.Right, afterLeft);
            // `both` reference columns from each side → this is the lowest join that sees them: fold into ON.
            Expression? on = j.On;
            foreach (Expression c in both) on = on is null ? c : new BinaryExpression(BinaryOperator.And, on, c);
            return (new JoinNode(left, right, j.Kind, on), outside);
        }

        PlanNode placed = candidates.Count > 0 ? new FilterNode(node, CombineAnd(candidates)) : node;
        return (placed, outside);
    }

    /// <summary>The table aliases (or names) exposed by a plan subtree — a scan's alias/name, or the union
    /// over a join. A derived table exposes only its own alias (its inner columns are already projected).</summary>
    internal static HashSet<string> SubtreeAliases(PlanNode node) => node switch
    {
        ScanNode s => new(StringComparer.OrdinalIgnoreCase) { s.Alias ?? s.Table },
        IndexSeekNode s => new(StringComparer.OrdinalIgnoreCase) { s.Alias ?? s.Table },
        DerivedTableNode d => d.Alias is { } a ? new(StringComparer.OrdinalIgnoreCase) { a } : new(StringComparer.OrdinalIgnoreCase),
        IndexRangeSeekNode s => new(StringComparer.OrdinalIgnoreCase) { s.Alias ?? s.Table },
        JoinNode j => SubtreeAliases(j.Left).Union(SubtreeAliases(j.Right)).ToHashSet(StringComparer.OrdinalIgnoreCase),
        HashJoinNode h => SubtreeAliases(h.Left).Union(SubtreeAliases(h.Right)).ToHashSet(StringComparer.OrdinalIgnoreCase),
        FilterNode f => SubtreeAliases(f.Input),
        // Pass-through nodes keep their input's aliases: they reshape or restrict rows without renaming a
        // source. A ProjectNode in particular sits at the root of every planned SELECT, so omitting it made
        // SubtreeAliases(PlanSelect(q)) report NO aliases at all — which silently defeated any caller asking
        // "which aliases does this query introduce?" (ExistsSemiJoin declined every subquery because of it).
        // A DerivedTableNode is deliberately NOT pass-through: above it only its own alias is visible.
        ProjectNode p => SubtreeAliases(p.Input),
        SortNode s => SubtreeAliases(s.Input),
        LimitNode l => SubtreeAliases(l.Input),
        DistinctNode d => SubtreeAliases(d.Input),
        DistinctRowNode d => SubtreeAliases(d.Input),
        // Grouping collapses rows but doesn't rename their source: the group scope still exposes the input's
        // columns (which is what lets HAVING and the projection qualify them), so the aliases carry through.
        AggregateNode a => SubtreeAliases(a.Input),
        IndexScanNode s => new(StringComparer.OrdinalIgnoreCase) { s.Table },
        _ => new(StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>The set of table qualifiers an expression's column references use, or <see langword="null"/>
    /// if it can't be safely placed — an unqualified column (ambiguous) or a subquery (references an inner
    /// scope). Such a conjunct stays at the top filter.</summary>
    private static HashSet<string>? Qualifiers(Expression e)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Collect(e, result) ? result : null;

        static bool Collect(Expression e, HashSet<string> acc) => e switch
        {
            ColumnReference { Table: { } t } => Add(acc, t),
            ColumnReference => false, // unqualified — can't determine its table
            LiteralExpression or ParameterExpression or SystemVariableExpression => true,
            BinaryExpression b => Collect(b.Left, acc) && Collect(b.Right, acc),
            UnaryExpression u => Collect(u.Operand, acc),
            FunctionCall f => f.Arguments.All(a => Collect(a, acc)),
            InListExpression il => Collect(il.Value, acc) && il.Items.All(a => Collect(a, acc)),
            _ => false, // subqueries (scalar/EXISTS/IN), qualified star, etc. — don't push
        };
        static bool Add(HashSet<string> acc, string t) { acc.Add(t); return true; }
    }

    private static void SplitAnd(Expression e, List<Expression> into)
    {
        if (e is BinaryExpression { Operator: BinaryOperator.And } b)
        {
            SplitAnd(b.Left, into);
            SplitAnd(b.Right, into);
        }
        else into.Add(e);
    }

    private static Expression CombineAnd(IReadOnlyList<Expression> conjuncts) =>
        conjuncts.Aggregate((a, b) => new BinaryExpression(BinaryOperator.And, a, b));
}
