using LibRed.Engine.Plan;
using LibRed.Sql.Ast;
using LibRed.Sql.Binding;

namespace LibRed.Engine.Planning;

/// <summary>
/// Turns a bound statement into a logical <see cref="PlanNode"/> tree. Index
/// selection and other optimisations are applied as rewrites over the tree.
/// </summary>
public static class QueryPlanner
{
    public static PlanNode Plan(BoundStatement bound)
    {
        return PlanStatement(bound.Statement);
    }

    /// <summary>Plans a SELECT or a set operation over SELECTs. Public because an append query's source is
    /// either — <c>INSERT INTO t SELECT …</c>, or a UNION feeding one.</summary>
    public static PlanNode PlanStatement(SqlStatement statement) => statement switch
    {
        SelectStatement select => PlanSelect(select),
        // ORDER BY and paging on a set operation apply to its combined result, so they sit ABOVE the node —
        // sorting an operand instead is what `A UNION B ORDER BY x` used to do, and it silently returned the
        // rows in the wrong order (measured against ACE).
        SetOperationStatement set => PageAndSort(
            new SetOperationNode(PlanStatement(set.Left), PlanStatement(set.Right), set.Operator),
            OrderByPositions(set.OrderBy ?? [], _ => null), set.Top, set.Offset),
        ValuesStatement values => new ValuesNode(values.Rows),
        _ => throw new NotImplementedException(
            $"Planning for {statement.GetType().Name} is not yet implemented."),
    };

    /// <summary>Sorts and then pages a set operation's combined result. The sort takes the row bound from the
    /// paging for the same reason <see cref="BoundSort"/> does — only that many rows can survive it — except
    /// under OFFSET, where the skipped rows must be produced before they can be discarded.</summary>
    private static PlanNode PageAndSort(
        PlanNode node, List<OrderByItem> orderBy, Expression? top, Expression? offset)
    {
        if (orderBy.Count > 0)
        {
            Expression? bound = top is not null && offset is null ? top : null;
            node = new SortNode(node, orderBy, bound);
        }

        return top is null && offset is null ? node : new LimitNode(node, top, Offset: offset);
    }

    /// <summary>Plans a SELECT statement directly (used for subqueries).</summary>
    public static PlanNode PlanSelect(SelectStatement select)
    {
        // Shape: From (Scan/Join/Derived) → Filter → Sort → Project → Limit. ORDER BY is
        // applied over the source columns (before projection) so it can reference them.
        PlanNode node = PlanFrom(select.From);

        // The sort runs on the source rows, below the projection, so a position becomes the projected expression
        // it names; only a SELECT * sorts rows that are already its output.
        if (select.OrderBy.Count > 0)
        {
            SelectStatement written = select;
            select = select with
            {
                OrderBy = OrderByPositions(written.OrderBy,
                    written.IsSelectStar ? _ => null : position => ProjectedAt(written, position)),
            };
        }

        if (select.Where is not null)
            node = PushPredicates(node, select.Where);

        // Window functions see the post-WHERE rows and are computed before ORDER BY, which may sort by one — so
        // the node goes here. Each call is lifted out of the projection into a reference to the column the node
        // publishes for it, which is what leaves everything above (declared types, sorting, DISTINCT, LIMIT)
        // looking at an ordinary column and needing no changes at all.
        var windows = new List<WindowOutput>();
        select = ExtractWindows(select, windows);

        // A window whose arguments or keys hold an aggregate — RANK() OVER (ORDER BY SUM(x)) — makes the query
        // grouped by itself, as an aggregate in the projection does.
        bool aggregate = select.GroupBy.Count > 0 || select.Having is not null
            || select.Projection.Any(i => HasAggregate(i.Value))
            || windows.Any(w => w.Function.Expressions().Any(HasAggregate));

        // Over a grouped query the windows run over the groups, after HAVING and before the projection and ORDER BY
        // — the standard's order — so the aggregate node computes them itself.
        if (windows.Count > 0 && !aggregate)
            node = new WindowNode(node, windows);

        if (aggregate)
            // The aggregate node owns ORDER BY: its keys are evaluated in the group scope (so they can
            // reference grouping expressions / aggregates), not over the already-projected output.
            node = new AggregateNode(node, select.GroupBy, select.Projection, select.Having, select.OrderBy, windows);
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

        if (select.Top is { } top || select.Offset is not null)
        {
            // `TOP n ... ORDER BY k` only needs the n smallest rows by k, so tell the sort the bound and let it
            // discard rows that can't survive rather than ordering everything. Only sound when nothing between the
            // sort and the limit changes the row count: a projection is 1:1, but DISTINCT/DISTINCTROW collapse
            // rows, so the n rows reaching the limit are not the n the sort would have kept. PERCENT is excluded
            // because it needs the full input count to work out the take at all.
            //
            // With an OFFSET the sort must keep skip + take rows, not take: the ones it would otherwise discard
            // are exactly the ones the skip consumes. A bare OFFSET has no bound at all - every row can survive
            // it - so the sort orders its whole input, as it did before paging existed.
            if (!select.TopPercent && !select.Distinct && !select.DistinctRow && select.Top is not null)
                node = BoundSort(node, select.Offset is { } skip
                    ? new BinaryExpression(BinaryOperator.Add, skip, select.Top)
                    : select.Top);

            node = new LimitNode(node, select.Top, select.TopPercent, select.Offset);
        }

        return node;
    }

    /// <summary>
    /// ORDER BY items that are a whole number written as such name the output column at that position (verified vs
    /// ACE, as SQL-92 has it: ORDER BY 2 DESC sorts by the second column, and so does (2)); any other constant —
    /// 1.5, '2', 1 + 1 — is left as a constant. A position below 1 names nothing and is an error, as is one past the
    /// last column. <paramref name="projected"/> gives the expression at a position, or null where the rows sorted
    /// are the output itself and the position is read from them.
    /// </summary>
    private static List<OrderByItem> OrderByPositions(
        IReadOnlyList<OrderByItem> orderBy, Func<int, Expression?> projected) =>
        orderBy.Select(item => item.Value is LiteralExpression { Value: int or long or short or byte } literal
            ? item with
            {
                Value = Convert.ToInt32(literal.Value, System.Globalization.CultureInfo.InvariantCulture) is var position
                        && position >= 1
                    ? projected(position) ?? new OutputColumnPosition(position)
                    : throw NoSuchPosition(literal.Value!),
            }
            : item).ToList();

    private static InvalidOperationException NoSuchPosition(object position) =>
        new($"'{position}' is not a valid field name or expression: ORDER BY {position} names no output column.");

    /// <summary>The projected expression at a 1-based position. A <c>table.*</c> before it would need the table's
    /// columns counted, which the planner cannot, and is refused.</summary>
    private static Expression ProjectedAt(SelectStatement select, int position)
    {
        if (position > select.Projection.Count)
            throw NoSuchPosition(position);
        if (select.Projection.Take(position).Any(item => item.Value is StarExpression or QualifiedStarExpression))
            throw new NotSupportedException($"ORDER BY {position} after a table.* in the projection is not supported.");
        return select.Projection[position - 1].Value;
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
    // Not a SortNode as CA1859 reads it: pushing through a join returns the join.
#pragma warning disable CA1859
    private static PlanNode PushSort(PlanNode node, IReadOnlyList<OrderByItem> keys)
    {
        if (node is JoinNode { Kind: JoinKind.Inner or JoinKind.Left or JoinKind.Cross } j
            && keys.All(k => Qualifiers(k.Value) is { Count: > 0 } q && q.IsSubsetOf(SubtreeAliases(j.Left))))
        {
            return j with { Left = PushSort(j.Left, keys) };
        }

        return new SortNode(node, keys);
    }
#pragma warning restore CA1859

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
    /// the "StdDev"/"StdDevP" spellings are accepted as aliases. Beyond Access, a LibRed extension: the standard's
    /// names for the statistics, its binary set functions (CORR, COVAR_*, REGR_*) — all of them
    /// <see cref="Execution.RunningAggregate"/>'s — and its ordered-set aggregates PERCENTILE_CONT, PERCENTILE_DISC
    /// and LISTAGG.</summary>
    internal static bool IsAggregate(string name) =>
        name.ToUpperInvariant() is var upper
        && (upper is "FIRST" or "LAST" or "PERCENTILE_CONT" or "PERCENTILE_DISC" or "LISTAGG" or "STRING_AGG"
            || Execution.RunningAggregate.Supports(upper));

    internal static bool HasAggregate(Expression e) => e switch
    {
        FunctionCall f when IsAggregate(f.Name) => true,
        // Operands include a CASE's: an aggregate inside a CASE has to be found here so it is computed per group
        // and handed to the evaluator, rather than being reached during evaluation when no group scope can
        // resolve it. The standard says the same: aggregates in a WHEN are evaluated before the CASE, not by it.
        // Conditions count as well as results — HAVING CASE WHEN COUNT(*) > 1 … puts the aggregate in the condition.
        _ => e.Operands()?.Any(HasAggregate) ?? false,
    };

    /// <summary>
    /// Replaces every window function in the projection and ORDER BY with a reference to the column a
    /// <see cref="WindowNode"/> will publish for it, appending one <see cref="WindowOutput"/> per call.
    /// Returns the statement unchanged when there are none.
    /// </summary>
    /// <remarks>
    /// Identical calls are not shared. EF Core never repeats one, and comparing two specs for equality to
    /// dedupe would cost more than the extra column it saves. A subquery is opaque here — its own
    /// <see cref="PlanSelect"/> handles any window inside it, in its own scope.
    /// </remarks>
    private static SelectStatement ExtractWindows(SelectStatement select, List<WindowOutput> windows)
    {
        // WHERE and HAVING cannot contain a window function (its value is not defined until they have run), so
        // only these two clauses are walked.
        if (!select.Projection.Any(i => HasWindow(i.Value)) && !select.OrderBy.Any(k => HasWindow(k.Value)))
            return select;

        return select with
        {
            Projection = select.Projection.Select(i => i with { Value = LiftWindows(i.Value, windows) }).ToList(),
            OrderBy = select.OrderBy.Select(k => k with { Value = LiftWindows(k.Value, windows) }).ToList(),
        };
    }

    private static bool HasWindow(Expression e) => e switch
    {
        WindowFunction => true,
        _ => e.Operands()?.Any(HasWindow) ?? false,
    };

    private static Expression LiftWindows(Expression e, List<WindowOutput> windows)
    {
        if (e is not WindowFunction w)
            return e.MapOperands(o => LiftWindows(o, windows));

        // A name no identifier can spell: IDENTIFIER allows '$' only as a trailing character, so this cannot
        // collide with a real column and be silently shadowed.
        string name = $"$window{windows.Count}";
        windows.Add(new WindowOutput(name, w));
        return new ColumnReference(null, name);
    }

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
        // The aliases this FROM clause introduces. Any other qualifier belongs to an enclosing query — this is
        // a correlated subquery's WHERE — and an outer column is readable at every depth of this tree, so it
        // must not be the thing that stops a conjunct from sinking. That mattered little before APPLY, because
        // a correlated subquery's predicate still landed as a Filter directly over its own scan, which index
        // selection recognises. Over a lateral join it lands above the join instead, and the difference is
        // seeking the inner table once per outer row versus rescanning all of it.
        HashSet<string> introduced = SubtreeAliases(joinTree);
        (PlanNode node, List<Expression> unplaced) = Place(joinTree, conjuncts, introduced);
        return unplaced.Count == 0 ? node : new FilterNode(node, CombineAnd(unplaced));
    }

    /// <summary>Pushes each conjunct that lies entirely within <paramref name="node"/>'s subtree as deep as it
    /// fits; returns the rewritten node and the conjuncts that reference tables outside it (to bubble up).
    /// <paramref name="introduced"/> is the alias set of the whole tree — qualifiers outside it are the
    /// enclosing query's and are ignored when deciding where a conjunct fits.</summary>
    private static (PlanNode Node, List<Expression> Unplaced) Place(
        PlanNode node, List<Expression> conjuncts, HashSet<string> introduced)
    {
        HashSet<string> aliases = SubtreeAliases(node);
        var candidates = new List<Expression>();
        var outside = new List<Expression>();
        foreach (Expression c in conjuncts)
            (Qualifiers(c) is { } q && q.Count > 0 && q.Where(introduced.Contains).All(aliases.Contains)
                ? candidates : outside).Add(c);

        // Only CROSS/INNER joins are safe to push below (an outer join's null-supplying side changes meaning).
        if (node is JoinNode { Kind: JoinKind.Cross or JoinKind.Inner } j)
        {
            (PlanNode left, List<Expression> afterLeft) = Place(j.Left, candidates, introduced);
            (PlanNode right, List<Expression> both) = Place(j.Right, afterLeft, introduced);
            // `both` reference columns from each side → this is the lowest join that sees them: fold into ON.
            Expression? on = j.On;
            foreach (Expression c in both) on = on is null ? c : new BinaryExpression(BinaryOperator.And, on, c);
            return (new JoinNode(left, right, j.Kind, on), outside);
        }

        // Both APPLY kinds preserve their left side, so a conjunct confined to it can be pushed there: dropping
        // a left row before the lateral runs removes exactly the output rows the WHERE would have removed
        // after, and saves re-running the whole right side for it. Nothing is pushed into the RIGHT side —
        // under OUTER APPLY a filter there can empty an otherwise non-empty result and so manufacture the very
        // null-padded row the WHERE was there to drop.
        if (node is JoinNode { Kind: JoinKind.CrossApply or JoinKind.OuterApply } a)
        {
            (PlanNode left, List<Expression> rest) = Place(a.Left, candidates, introduced);
            PlanNode lateral = a with { Left = left };
            return (rest.Count > 0 ? new FilterNode(lateral, CombineAnd(rest)) : lateral, outside);
        }

        PlanNode placed = candidates.Count > 0 ? new FilterNode(node, CombineAnd(candidates)) : node;
        return (placed, outside);
    }

    /// <summary>The table aliases (or names) exposed by a plan subtree — a scan's alias/name, or the union
    /// over a join. A derived table exposes only its own alias (its inner columns are already projected).</summary>
    // Aliases compare case-insensitively, and IDE0028's only fix for these arms is a collection expression,
    // which would drop the comparer.
#pragma warning disable IDE0028
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
        // A window appends a column without renaming a source, so it passes its input's aliases through — the
        // same reasoning as ProjectNode above, and the same bug if omitted.
        WindowNode w => SubtreeAliases(w.Input),
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
#pragma warning restore IDE0028

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
            // Anything without operands — subqueries (scalar/EXISTS/IN), qualified star, etc. — isn't pushed.
            _ => e.Operands()?.All(a => Collect(a, acc)) ?? false,
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