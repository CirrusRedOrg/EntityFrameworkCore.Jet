using LibRed.Catalog;
using LibRed.Engine.Plan;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Planning;

/// <summary>
/// A post-planning optimizer pass that replaces a <c>Filter(pred) over Scan(t)</c> with
/// <c>Filter(pred) over IndexSeek(t, index, key)</c> when <c>pred</c> has an equality on a column that is the
/// sole column of one of <c>t</c>'s indexes. The <see cref="FilterNode"/> is KEPT: the index key encoding is
/// order-preserving but lossy (text/binary collation), so the seek may over-return and the residual predicate
/// re-checks each candidate — the seek only narrows what has to be read.
/// </summary>
internal static class IndexSelection
{
    private static readonly HashSet<string> NoOuter = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="outer">Aliases of the enclosing query (empty at top level). A value referencing only these
    /// is a constant from this query's view, so a correlated predicate <c>t.col = outer.col</c> becomes an
    /// index seek keyed off the outer row — the point of running index selection on correlated subqueries.</param>
    public static PlanNode Apply(PlanNode node, JetCatalog catalog, HashSet<string>? outer = null)
    {
        outer ??= NoOuter;
        return node switch
        {
            FilterNode { Input: ScanNode scan } filter => RewriteFilterOverScan(filter, scan, catalog, outer),
            JoinNode j => RewriteJoin(j, catalog, outer),

            // Otherwise rebuild the node with its children rewritten.
            FilterNode f => f with { Input = Apply(f.Input, catalog, outer) },
            SortNode s => s with { Input = Apply(s.Input, catalog, outer) },
            ProjectNode p => p with { Input = Apply(p.Input, catalog, outer) },
            DistinctNode d => d with { Input = Apply(d.Input, catalog, outer) },
            DistinctRowNode dr => dr with { Input = Apply(dr.Input, catalog, outer) },
            LimitNode l => l with { Input = Apply(l.Input, catalog, outer) },
            AggregateNode a => a with { Input = Apply(a.Input, catalog, outer) },
            DerivedTableNode dt => dt with { Input = Apply(dt.Input, catalog, outer) },
            SetOperationNode so => so with { Left = Apply(so.Left, catalog, outer), Right = Apply(so.Right, catalog, outer) },
            _ => node,
        };
    }

    private static PlanNode RewriteFilterOverScan(FilterNode filter, ScanNode scan, JetCatalog catalog, HashSet<string> outer)
    {
        if (catalog.FindTable(scan.Table) is not { } def)
            return filter;
        string alias = scan.Alias ?? scan.Table;
        var conjuncts = Conjuncts(filter.Predicate).ToList();

        // 1. Equality seek. Collect every `col = value` where col is a column of this scan and value is
        //    evaluable without a row of it (a constant, a parameter, or — in a correlated subquery — an outer
        //    column), then use the index (composite preferred) all of whose columns are so constrained. This is
        //    an exact point seek; a fully-constrained composite key (e.g. a correlated PK match) hits one row.
        var equalities = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
        foreach (Expression conjunct in conjuncts)
        {
            if (conjunct is not BinaryExpression { Operator: BinaryOperator.Equal } eq)
                continue;
            (ColumnReference col, Expression value)? match =
                Column(eq.Left, alias, def) is { } c1 && Seekable(eq.Right, outer) ? (c1, eq.Right)
                : Column(eq.Right, alias, def) is { } c2 && Seekable(eq.Left, outer) ? (c2, eq.Left)
                : null;
            if (match is { } m)
                equalities.TryAdd(m.col.Column, m.value);
        }
        if (equalities.Count > 0)
            // Most columns first, so a correlated composite PK match is preferred over a single-column index.
            foreach (IndexDef index in def.Indexes.OrderByDescending(i => i.Columns.Count))
                if (index.Columns.All(c => equalities.ContainsKey(c.Column.Name)))
                {
                    var keys = index.Columns.Select(c => equalities[c.Column.Name]).ToList();
                    return filter with { Input = new IndexSeekNode(scan.Table, scan.Alias, index, keys) };
                }

        // 2. Range comparison(s) on a single-column index → a range scan (lower and/or upper bound).
        foreach (IndexDef index in def.Indexes.Where(i => i.Columns.Count == 1))
        {
            string colName = index.Columns[0].Column.Name;
            Expression? low = null, high = null;
            foreach (Expression conjunct in conjuncts)
            {
                if (Bound(conjunct, colName, alias, def) is not { } b)
                    continue;
                if (b.Op is BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual)
                    low ??= b.Value;
                else
                    high ??= b.Value; // LessThan / LessThanOrEqual
            }
            if (low is not null || high is not null)
                return filter with { Input = new IndexRangeSeekNode(scan.Table, scan.Alias, index, low, high) };
        }

        return filter;
    }

    /// <summary>Whether <paramref name="e"/> can be evaluated as a seek key without a row of the table being
    /// scanned — a constant, a parameter, or a column of the enclosing (outer) query in a correlated subquery.</summary>
    private static bool Seekable(Expression e, HashSet<string> outer) => ReferencesOnly(e, outer);

    /// <summary>If <paramref name="conjunct"/> is a range comparison of column <paramref name="colName"/> against
    /// a value (either orientation), returns the operator as if the column were on the left (so <c>5 &lt; K</c>
    /// yields <c>K &gt; 5</c>) and the value expression; else null.</summary>
    private static (BinaryOperator Op, Expression Value)? Bound(Expression conjunct, string colName, string alias, TableDef def)
    {
        if (conjunct is not BinaryExpression { Operator: var op } cmp
            || op is not (BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual
                or BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual))
            return null;

        bool IsCol(Expression e) => Column(e, alias, def) is { } c && string.Equals(c.Column, colName, StringComparison.OrdinalIgnoreCase);
        if (IsCol(cmp.Left) && !HasColumnRef(cmp.Right)) return (op, cmp.Right);
        if (IsCol(cmp.Right) && !HasColumnRef(cmp.Left)) return (Flip(op), cmp.Left);
        return null;
    }

    private static BinaryOperator Flip(BinaryOperator op) => op switch
    {
        BinaryOperator.GreaterThan => BinaryOperator.LessThan,
        BinaryOperator.GreaterThanOrEqual => BinaryOperator.LessThanOrEqual,
        BinaryOperator.LessThan => BinaryOperator.GreaterThan,
        BinaryOperator.LessThanOrEqual => BinaryOperator.GreaterThanOrEqual,
        _ => op,
    };

    /// <summary>
    /// Turns a nested-loop join whose inner (right) side is a base-table scan into an <b>index-nested-loop</b>
    /// join: if the join's ON has an equality <c>right.col = leftExpr</c> where <c>right.col</c> is the sole
    /// column of an index of the inner table and <c>leftExpr</c> references only the outer (left) side, the
    /// inner scan becomes an <see cref="IndexSeekNode"/> keyed off the outer row. Executed per left row, it
    /// seeks the index instead of scanning the whole inner table. The join keeps its ON as the residual check.
    /// </summary>
    private static PlanNode RewriteJoin(JoinNode j, JetCatalog catalog, HashSet<string> outer)
    {
        PlanNode left = Apply(j.Left, catalog, outer); // rewrite the outer side first (independent of this join)

        // Only a plain nested-loop join with an ON can become index-nested-loop; an outer join is left alone
        // for now (the right side must still contribute null rows when nothing matches — a scan, not a seek).
        if (j.Kind is (JoinKind.Inner or JoinKind.Cross) && j.On is { } on
            && j.Right is ScanNode scan && catalog.FindTable(scan.Table) is { } def)
        {
            string rAlias = scan.Alias ?? scan.Table;
            // The seek key may reference the join's own left side or the enclosing (outer) query.
            HashSet<string> keySources = QueryPlanner.SubtreeAliases(left).Union(outer).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (Expression conjunct in Conjuncts(on))
            {
                if (conjunct is not BinaryExpression { Operator: BinaryOperator.Equal } eq)
                    continue;

                // One side is an inner-table column; the other references only the outer side (the seek key).
                (ColumnReference innerCol, Expression outerKey)? m =
                    Column(eq.Left, rAlias, def) is { } c1 && ReferencesOnly(eq.Right, keySources) ? (c1, eq.Right)
                    : Column(eq.Right, rAlias, def) is { } c2 && ReferencesOnly(eq.Left, keySources) ? (c2, eq.Left)
                    : null;
                if (m is not { } match)
                    continue;

                IndexDef? index = def.Indexes.FirstOrDefault(
                    i => i.Columns.Count == 1 && string.Equals(i.Columns[0].Column.Name, match.innerCol.Column, StringComparison.OrdinalIgnoreCase));
                if (index is null)
                    continue;

                var seek = new IndexSeekNode(scan.Table, scan.Alias, index, [match.outerKey]);
                return j with { Left = left, Right = seek }; // ON kept as residual
            }
        }

        PlanNode right = Apply(j.Right, catalog, outer);

        // No index-nested-loop available: if this is an equi-join whose keys are same-kind columns, hash it
        // (O(n+m)) instead of leaving the O(n·m) nested loop. See HashJoinNode for why same-kind is required.
        // RIGHT joins hash too (the executor builds the left and probes with the right).
        if (j.Kind is (JoinKind.Inner or JoinKind.Left or JoinKind.Right) && j.On is { } cond
            && TryHashKeys(cond, left, right, catalog) is { } keys)
            return new HashJoinNode(left, right, j.Kind, keys.Left, keys.Right, cond);

        return j with { Left = left, Right = right };
    }

    /// <summary>Splits the join condition into equi-key column pairs (left-referencing = right-referencing)
    /// whose columns are of the same type kind, or null if there is no such pair. The full ON is still applied
    /// as a residual, so a partial split (extra non-equi conjuncts) is fine.</summary>
    private static (IReadOnlyList<Expression> Left, IReadOnlyList<Expression> Right)? TryHashKeys(
        Expression on, PlanNode left, PlanNode right, JetCatalog catalog)
    {
        HashSet<string> leftAliases = QueryPlanner.SubtreeAliases(left);
        HashSet<string> rightAliases = QueryPlanner.SubtreeAliases(right);
        var leftKeys = new List<Expression>();
        var rightKeys = new List<Expression>();

        foreach (Expression conjunct in Conjuncts(on))
        {
            if (conjunct is not BinaryExpression { Operator: BinaryOperator.Equal } eq)
                continue;

            // Orient the equality so L is the left-subtree side and R the right-subtree side.
            (Expression L, Expression R)? oriented =
                ReferencesOnly(eq.Left, leftAliases) && ReferencesOnly(eq.Right, rightAliases) ? (eq.Left, eq.Right)
                : ReferencesOnly(eq.Right, leftAliases) && ReferencesOnly(eq.Left, rightAliases) ? (eq.Right, eq.Left)
                : null;
            if (oriented is not { } o || o.L is not ColumnReference lc || o.R is not ColumnReference rc)
                continue;

            // A hash consistent with the evaluator's equality only exists within one type kind (5 = '5' and
            // 5 = 5.0 but '5' ≠ '5.0'), so both key columns must resolve to the same kind.
            TypeKind? lk = ResolveKind(lc, left, catalog);
            TypeKind? rk = ResolveKind(rc, right, catalog);
            if (lk is null || lk != rk)
                continue;

            leftKeys.Add(o.L);
            rightKeys.Add(o.R);
        }

        return leftKeys.Count > 0 ? (leftKeys, rightKeys) : null;
    }

    private enum TypeKind { Numeric, Text, Binary, Temporal, Guid }

    private static TypeKind? Classify(JetDataType t) => t switch
    {
        JetDataType.Boolean or JetDataType.Byte or JetDataType.Int16 or JetDataType.Int32 or JetDataType.Int64
            or JetDataType.Single or JetDataType.Double or JetDataType.Currency or JetDataType.FixedPoint => TypeKind.Numeric,
        JetDataType.Text or JetDataType.Memo => TypeKind.Text,
        JetDataType.Binary or JetDataType.Ole => TypeKind.Binary,
        JetDataType.DateTime or JetDataType.DateTimeExtended => TypeKind.Temporal,
        JetDataType.Guid => TypeKind.Guid,
        _ => null,
    };

    /// <summary>The type kind of a column resolved against <paramref name="subtree"/> — a base-table column
    /// directly, or a derived-table column followed through its projection to the underlying base column — or
    /// null if it can't be pinned down (e.g. the projection is a computed expression), in which case the join
    /// is left as a nested loop rather than risk an unsound hash.</summary>
    private static TypeKind? ResolveKind(ColumnReference col, PlanNode subtree, JetCatalog catalog)
    {
        foreach ((string alias, string table) in BaseTables(subtree))
        {
            if (col.Table is { } t && !string.Equals(t, alias, StringComparison.OrdinalIgnoreCase))
                continue;
            ColumnDef? c = catalog.FindTable(table)?.Columns
                .FirstOrDefault(cd => string.Equals(cd.Name, col.Column, StringComparison.OrdinalIgnoreCase));
            if (c is not null)
                return Classify(c.Type);
        }

        // A derived-table column: match its alias, find what its SELECT list projects for this name, and resolve
        // that expression's kind against the derived query's own tables (only if it is itself a plain column).
        foreach (DerivedTableNode d in DerivedTables(subtree))
        {
            if (col.Table is { } t && !string.Equals(t, d.Alias, StringComparison.OrdinalIgnoreCase))
                continue;
            if (ProjectionExprFor(d.Input, col.Column) is ColumnReference inner)
                return ResolveKind(inner, d.Input, catalog);
        }
        return null;
    }

    // A derived table is an opaque boundary: its inner tables/aliases are not visible outside it, so BaseTables
    // stops there (DerivedTables handles the derived level separately).
    private static IEnumerable<(string Alias, string Table)> BaseTables(PlanNode node) => node switch
    {
        ScanNode s => [(s.Alias ?? s.Table, s.Table)],
        IndexSeekNode s => [(s.Alias ?? s.Table, s.Table)],
        IndexRangeSeekNode s => [(s.Alias ?? s.Table, s.Table)],
        DerivedTableNode => [],
        _ => node.Children.SelectMany(BaseTables),
    };

    /// <summary>The derived tables directly visible in <paramref name="node"/> (not descending into a derived
    /// table's own body, which is its private scope).</summary>
    private static IEnumerable<DerivedTableNode> DerivedTables(PlanNode node) => node switch
    {
        DerivedTableNode d => [d],
        _ => node.Children.SelectMany(DerivedTables),
    };

    /// <summary>The expression a derived query projects under output name <paramref name="column"/>, or null.</summary>
    private static Expression? ProjectionExprFor(PlanNode derivedBody, string column)
    {
        if (FindProject(derivedBody) is not { } proj)
            return null;
        foreach (SelectItem item in proj.Projection)
        {
            string name = item.Alias ?? (item.Value as ColumnReference)?.Column ?? "";
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                return item.Value;
        }
        return null;
    }

    private static ProjectNode? FindProject(PlanNode node) => node switch
    {
        ProjectNode p => p,
        SortNode s => FindProject(s.Input),
        LimitNode l => FindProject(l.Input),
        DistinctNode d => FindProject(d.Input),
        DistinctRowNode dr => FindProject(dr.Input),
        FilterNode f => FindProject(f.Input),
        DerivedTableNode dt => FindProject(dt.Input),
        _ => null,
    };

    /// <summary>Whether every column an expression references is one of the given aliases (and it has no
    /// subquery) — i.e. it can be evaluated from the outer row alone, as an index-seek key.</summary>
    private static bool ReferencesOnly(Expression e, HashSet<string> aliases) => e switch
    {
        ColumnReference { Table: { } t } => aliases.Contains(t),
        ColumnReference => false, // unqualified — can't attribute it to the outer side safely
        LiteralExpression or ParameterExpression or SystemVariableExpression => true,
        BinaryExpression b => ReferencesOnly(b.Left, aliases) && ReferencesOnly(b.Right, aliases),
        UnaryExpression u => ReferencesOnly(u.Operand, aliases),
        FunctionCall f => f.Arguments.All(a => ReferencesOnly(a, aliases)),
        InListExpression il => ReferencesOnly(il.Value, aliases) && il.Items.All(a => ReferencesOnly(a, aliases)),
        _ => false,
    };

    /// <summary>The column reference if <paramref name="e"/> is a column of the given scan (its qualifier is
    /// the scan's alias, or it is unqualified and the table has such a column), else null.</summary>
    private static ColumnReference? Column(Expression e, string alias, TableDef def)
    {
        if (e is not ColumnReference c) return null;
        if (c.Table is { } t && !string.Equals(t, alias, StringComparison.OrdinalIgnoreCase)) return null;
        return def.Columns.Any(col => string.Equals(col.Name, c.Column, StringComparison.OrdinalIgnoreCase)) ? c : null;
    }

    private static bool HasColumnRef(Expression e) => e switch
    {
        ColumnReference => true,
        // A subquery is opaque: it may be CORRELATED to this scan's own row (e.g. `t.Id > (SELECT … WHERE
        // t.Id = …)`), so it must never be used as a seek key/bound — treat it as referencing a column. (A
        // seek bound is evaluated once, with no row scope; a correlated subquery would fail to resolve there.)
        ScalarSubquery or ExistsExpression or InSubqueryExpression => true,
        BinaryExpression b => HasColumnRef(b.Left) || HasColumnRef(b.Right),
        UnaryExpression u => HasColumnRef(u.Operand),
        FunctionCall f => f.Arguments.Any(HasColumnRef),
        InListExpression il => HasColumnRef(il.Value) || il.Items.Any(HasColumnRef),
        _ => false, // literals, parameters, system vars
    };

    private static IEnumerable<Expression> Conjuncts(Expression e)
    {
        if (e is BinaryExpression { Operator: BinaryOperator.And } b)
            return Conjuncts(b.Left).Concat(Conjuncts(b.Right));
        return [e];
    }
}
