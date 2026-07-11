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
    public static PlanNode Apply(PlanNode node, JetCatalog catalog) => node switch
    {
        FilterNode { Input: ScanNode scan } filter => RewriteFilterOverScan(filter, scan, catalog),
        JoinNode j => RewriteJoin(j, catalog),

        // Otherwise rebuild the node with its children rewritten.
        FilterNode f => f with { Input = Apply(f.Input, catalog) },
        SortNode s => s with { Input = Apply(s.Input, catalog) },
        ProjectNode p => p with { Input = Apply(p.Input, catalog) },
        DistinctNode d => d with { Input = Apply(d.Input, catalog) },
        DistinctRowNode dr => dr with { Input = Apply(dr.Input, catalog) },
        LimitNode l => l with { Input = Apply(l.Input, catalog) },
        AggregateNode a => a with { Input = Apply(a.Input, catalog) },
        DerivedTableNode dt => dt with { Input = Apply(dt.Input, catalog) },
        SetOperationNode so => so with { Left = Apply(so.Left, catalog), Right = Apply(so.Right, catalog) },
        _ => node,
    };

    private static PlanNode RewriteFilterOverScan(FilterNode filter, ScanNode scan, JetCatalog catalog)
    {
        if (catalog.FindTable(scan.Table) is not { } def)
            return filter;
        string alias = scan.Alias ?? scan.Table;
        var conjuncts = Conjuncts(filter.Predicate).ToList();

        // 1. Equality on a single-column index → an exact point seek (the tightest access path).
        foreach (Expression conjunct in conjuncts)
        {
            if (conjunct is not BinaryExpression { Operator: BinaryOperator.Equal } eq)
                continue;

            // One side must be a column of this scan; the other a value with no column references (a constant
            // or a parameter) so it can be evaluated to seek. (Correlated keys — the join case — are elsewhere.)
            (ColumnReference col, Expression value)? match =
                Column(eq.Left, alias, def) is { } c1 && !HasColumnRef(eq.Right) ? (c1, eq.Right)
                : Column(eq.Right, alias, def) is { } c2 && !HasColumnRef(eq.Left) ? (c2, eq.Left)
                : null;
            if (match is { } m && SingleColumnIndex(def, m.col.Column) is { } index)
                return filter with { Input = new IndexSeekNode(scan.Table, scan.Alias, index, [m.value]) };
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

    /// <summary>The index whose sole column is <paramref name="colName"/>, or null.</summary>
    private static IndexDef? SingleColumnIndex(TableDef def, string colName) => def.Indexes.FirstOrDefault(
        i => i.Columns.Count == 1 && string.Equals(i.Columns[0].Column.Name, colName, StringComparison.OrdinalIgnoreCase));

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
    private static PlanNode RewriteJoin(JoinNode j, JetCatalog catalog)
    {
        PlanNode left = Apply(j.Left, catalog); // rewrite the outer side first (independent of this join)

        // Only a plain nested-loop join with an ON can become index-nested-loop; an outer join is left alone
        // for now (the right side must still contribute null rows when nothing matches — a scan, not a seek).
        if (j.Kind is (JoinKind.Inner or JoinKind.Cross) && j.On is { } on
            && j.Right is ScanNode scan && catalog.FindTable(scan.Table) is { } def)
        {
            string rAlias = scan.Alias ?? scan.Table;
            HashSet<string> leftAliases = QueryPlanner.SubtreeAliases(left);

            foreach (Expression conjunct in Conjuncts(on))
            {
                if (conjunct is not BinaryExpression { Operator: BinaryOperator.Equal } eq)
                    continue;

                // One side is an inner-table column; the other references only the outer side (the seek key).
                (ColumnReference innerCol, Expression outerKey)? m =
                    Column(eq.Left, rAlias, def) is { } c1 && ReferencesOnly(eq.Right, leftAliases) ? (c1, eq.Right)
                    : Column(eq.Right, rAlias, def) is { } c2 && ReferencesOnly(eq.Left, leftAliases) ? (c2, eq.Left)
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

        return j with { Left = left, Right = Apply(j.Right, catalog) };
    }

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
        BinaryExpression b => HasColumnRef(b.Left) || HasColumnRef(b.Right),
        UnaryExpression u => HasColumnRef(u.Operand),
        FunctionCall f => f.Arguments.Any(HasColumnRef),
        InListExpression il => HasColumnRef(il.Value) || il.Items.Any(HasColumnRef),
        _ => false, // literals, parameters, system vars; subqueries are opaque and left as residual-only
    };

    private static IEnumerable<Expression> Conjuncts(Expression e)
    {
        if (e is BinaryExpression { Operator: BinaryOperator.And } b)
            return Conjuncts(b.Left).Concat(Conjuncts(b.Right));
        return [e];
    }
}
