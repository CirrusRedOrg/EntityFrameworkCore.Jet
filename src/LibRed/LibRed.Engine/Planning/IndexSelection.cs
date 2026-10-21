using LibRed.Catalog;
using LibRed.Engine.Execution;
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
    // IDE0028's only fix here is `[]`, which would silently drop the comparer.
#pragma warning disable IDE0028
    private static readonly HashSet<string> NoOuter = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028

    /// <param name="node">The plan subtree to rewrite.</param>
    /// <param name="database">The database the statement runs on. A table's candidate indexes are read from the
    /// catalog of the database it is in — this one, or the file its FROM names with IN externaldatabase.</param>
    /// <param name="outer">Aliases of the enclosing query (empty at top level). A value referencing only these
    /// is a constant from this query's view, so a correlated predicate <c>t.col = outer.col</c> becomes an
    /// index seek keyed off the outer row — the point of running index selection on correlated subqueries.</param>
    public static PlanNode Apply(PlanNode node, JetDatabase database, HashSet<string>? outer = null)
    {
        outer ??= NoOuter;
        return node switch
        {
            FilterNode { Input: ScanNode scan } filter => RewriteFilterOverScan(filter, scan, database, outer),
            JoinNode j => RewriteJoin(j, database, outer),

            // `TOP n … ORDER BY <indexed columns>` — read the index in order and stop after n, instead of
            // scanning the table to feed a sort. Must precede the general SortNode arm below.
            SortNode { Input: ScanNode ordered, Limit: not null } sort
                when OrderedIndexRead(sort, ordered, database) is { } indexScan => indexScan,

            FilterNode { Input: DerivedTableNode derived } filter
                when IntoDerived(filter, derived, database, outer) is { } moved => moved,

            // Otherwise rebuild the node with its children rewritten.
            FilterNode f => f with { Input = Apply(f.Input, database, outer) },
            SortNode s => s with { Input = Apply(s.Input, database, outer) },
            ProjectNode p => p with { Input = Apply(p.Input, database, outer) },
            DistinctNode d => d with { Input = Apply(d.Input, database, outer) },
            DistinctRowNode dr => dr with { Input = Apply(dr.Input, database, outer) },
            LimitNode l => l with { Input = Apply(l.Input, database, outer) },
            AggregateNode a => a with { Input = Apply(a.Input, database, outer) },
            DerivedTableNode dt => dt with { Input = Apply(dt.Input, database, outer) },
            // Must be here, not left to the `_` arm below: that arm does not merely decline to rewrite this
            // node, it stops DESCENDING, so the whole subtree loses index selection. A WindowNode sits directly
            // above the Filter-over-Scan that RewriteFilterOverScan turns into a seek, so omitting this would
            // silently make every windowed query a full scan — correct results, catastrophic plans.
            WindowNode w => w with { Input = Apply(w.Input, database, outer) },
            SetOperationNode so => so with { Left = Apply(so.Left, database, outer), Right = Apply(so.Right, database, outer) },
            _ => node,
        };
    }

    /// <summary>
    /// A filter over a derived table — which is how a saved query is read — with each conjunct that reads only the
    /// derived table's plain columns moved into its own FROM, where the seeks of its table can serve it. Left above
    /// it, a WHERE on a saved query read the saved query's table whole. Null when nothing moves.
    /// </summary>
    /// <remarks>A conjunct moves down through a projection, a sort and DISTINCTROW, each of which passes a row on
    /// whole or not at all, so filtering their input drops the same rows. Not through TOP, GROUP BY, a window,
    /// DISTINCT or a set operation, whose rows depend on the rows that would be filtered out first.</remarks>
    private static PlanNode? IntoDerived(FilterNode filter, DerivedTableNode derived, JetDatabase database, HashSet<string> outer)
    {
        var moved = new List<Expression>();
        var kept = new List<Expression>();
        foreach (Expression conjunct in Conjuncts(filter.Predicate))
        {
            if (InDerived(conjunct) is { } inner)
                moved.Add(inner);
            else
                kept.Add(conjunct);
        }
        if (moved.Count == 0 || Into(derived.Input, QueryPlanner.CombineAnd(moved)) is not { } body)
            return null;

        PlanNode result = derived with { Input = Apply(body, database, outer) };
        return kept.Count == 0 ? result : new FilterNode(result, QueryPlanner.CombineAnd(kept));

        static PlanNode? Into(PlanNode node, Expression predicate) => node switch
        {
            ProjectNode p => Into(p.Input, predicate) is { } input ? p with { Input = input } : null,
            SortNode { Limit: null, Ties: null } s => Into(s.Input, predicate) is { } input ? s with { Input = input } : null,
            DistinctRowNode d => Into(d.Input, predicate) is { } input ? d with { Input = input } : null,
            ScanNode or JoinNode or FilterNode => QueryPlanner.PushPredicates(node, predicate),
            _ => null,
        };

        // The conjunct in the derived table's own columns, or null when it reads anything else — a subquery, which
        // may read the derived table, among them.
        Expression? InDerived(Expression conjunct)
        {
            bool movable = true;
            Expression inner = Map(conjunct);
            return movable ? inner : null;

            Expression Map(Expression e)
            {
                switch (e)
                {
                    case ColumnReference c when c.Table is null || string.Equals(c.Table, derived.Alias, StringComparison.OrdinalIgnoreCase):
                        if (DerivedColumn(derived, c.Column, database) is { } column)
                            return column;
                        break;
                    case LiteralExpression or ParameterExpression or SystemVariableExpression:
                        return e;
                    case not ColumnReference when e.Operands() is not null:
                        return e.MapOperands(Map);
                }
                movable = false;
                return e;
            }
        }
    }

    /// <summary>The column, in its own FROM's scope, that <paramref name="derived"/> outputs as <paramref name="name"/>:
    /// the one item of its projection with that name, where it is a column — or by position where a column list
    /// renames them — or, where no item has the name, the column a <c>*</c> takes from the one table it reads. Null
    /// for anything else: an expression, a name projected twice (which ACE names apart), a <c>*</c> over more than
    /// one table, or a derived table that is not a projection of its FROM.</summary>
    private static ColumnReference? DerivedColumn(DerivedTableNode derived, string name, JetDatabase database)
    {
        PlanNode top = derived.Input;
        while (top is LimitNode or DistinctNode)
            top = top.Children[0];
        IReadOnlyList<SelectItem>? projection = top switch
        {
            ProjectNode p => p.Projection,
            // A SELECT * of one source plans no projection.
            ScanNode or JoinNode or FilterNode or SortNode or DerivedTableNode => [new SelectItem(new StarExpression(), null)],
            _ => null,
        };
        if (projection is null)
            return null;

        bool star = projection.Any(i => i.Value is StarExpression or QualifiedStarExpression);
        if (derived.Columns is { } names)
        {
            int position = names.ToList().FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            return position >= 0 && position < projection.Count && !star ? projection[position].Value as ColumnReference : null;
        }

        var items = projection.Where(i => i.Value is not (StarExpression or QualifiedStarExpression)
            && string.Equals(i.Alias ?? (i.Value as ColumnReference)?.Column, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (items.Count > 0 || !star)
            return items is [{ Value: ColumnReference column }] ? column : null;

        // The one source the * takes its columns from.
        return (BaseTables(top).ToList(), DerivedTables(top).ToList()) switch
        {
            ([var (alias, table, external)], []) when ExternalDatabases.Open(database, external).Catalog.FindTable(table)?.FindColumn(name) is not null
                => new ColumnReference(alias, name),
            ([], [var source]) when DerivedColumn(source, name, database) is not null => new ColumnReference(source.Alias, name),
            _ => null,
        };
    }

    private static FilterNode RewriteFilterOverScan(FilterNode filter, ScanNode scan, JetDatabase database, HashSet<string> outer)
    {
        if (ExternalDatabases.Open(database, scan.Database).Catalog.FindTable(scan.Table) is not { } def)
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
        {
            // Most columns first, so a correlated composite PK match is preferred over a single-column index.
            // Select the widest match without sorting every index. Keep the first on a tie, as the
            // stable ordering did, and retain every index column in its declared key order.
            IndexDef? chosen = null;
            foreach (IndexDef index in def.Indexes)
                if ((chosen is null || index.Columns.Count > chosen.Columns.Count)
                    && index.Columns.All(c => equalities.ContainsKey(c.Column.Name)))
                    chosen = index;
            if (chosen is not null)
            {
                var keys = chosen.Columns.Select(c => equalities[c.Column.Name]).ToList();
                return filter with { Input = new IndexSeekNode(scan.Table, scan.Alias, chosen, keys, scan.Database) { RowIds = scan.RowIds } };
            }
        }

        // 2. Range comparison(s) on a single-column index → a range scan (lower and/or upper bound).
        foreach (IndexDef index in def.Indexes.Where(i => i.Columns.Count == 1))
        {
            string colName = index.Columns[0].Column.Name;
            Expression? low = null, high = null;
            bool unordered = false;
            foreach (Expression conjunct in conjuncts)
            {
                // A BETWEEN's two bounds are ordered only when the seek runs, so they are taken as a pair or not at
                // all — never mixed with a bound another conjunct set. The FilterNode above re-checks every conjunct.
                if (Between(conjunct, colName, alias, def) is { } range)
                {
                    if (low is null && high is null)
                        (low, high, unordered) = (range.Low, range.High, true);
                    continue;
                }
                if (unordered)
                    continue;
                if (Bound(conjunct, colName, alias, def) is not { } b)
                    continue;
                if (b.Op is BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual)
                    low ??= b.Value;
                else
                    high ??= b.Value; // LessThan / LessThanOrEqual
            }
            if (low is not null || high is not null)
                return filter with
                {
                    Input = new IndexRangeSeekNode(scan.Table, scan.Alias, index, low, high, scan.Database)
                    {
                        Unordered = unordered,
                        RowIds = scan.RowIds,
                    },
                };
        }

        return filter;
    }

    /// <summary>
    /// Turns <c>Sort(keys, bound) over Scan(t)</c> into an ordered read of an index whose key IS those sort keys,
    /// dropping the sort: the rows already arrive in the wanted order, so the enclosing <c>TOP n</c> stops after
    /// n instead of the sort consuming the whole table to find them. Returns null when no index qualifies.
    /// </summary>
    /// <remarks>
    /// <para>Only applied under a bound (an enclosing <c>TOP n</c>), which is where the win is. Unbounded, it is
    /// a wash: an ordered index read costs ~2.7 µs/row against ~0.44 µs/row for a scan plus ~2.4 µs/row to sort,
    /// so reading the whole table through the index buys nothing but a smaller peak footprint (measured — see
    /// test/LibRed.Benchmarks, `sort.by_indexed` against `StorageBenchmarks.RangeSeek`). With the bound, only n
    /// rows are ever read.</para>
    /// <para>The conditions are what make the substitution produce the <b>same rows in the same order</b>, not
    /// merely a valid order:</para>
    /// <list type="bullet">
    /// <item><description><b>Every index column is a sort key, in the same order, all ascending.</b> A mere
    /// prefix match would leave the index ordering ties by its remaining columns while the sort leaves them in
    /// input order — a legal but different answer, and one that would silently change results EF asserts on.</description></item>
    /// <item><description><b>Numeric or temporal columns only.</b> Their key encoding is order-preserving with no
    /// collation involved, so index order and <see cref="ExpressionEvaluator.CompareForSort"/> cannot disagree.
    /// Text keys are NLS sort keys and lossy; whether they order identically to the evaluator is a separate
    /// question that wants checking against ACE before being relied on here.</description></item>
    /// <item><description><b>Not a WITH IGNORE NULL index</b>, which omits null-keyed rows from the B-tree
    /// altogether — reading it in order would silently drop rows. An ordinary index stores nulls, sorted first
    /// ascending (<c>AscNull</c> 0x00 &lt; <c>AscStart</c> 0x7F), exactly as CompareForSort puts them.</description></item>
    /// </list>
    /// <para>Ties then match too: equal-keyed index entries are ordered by row pointer, i.e. physical position,
    /// which is the order a scan feeds the sort in — and the sort breaks ties by input position.</para>
    /// </remarks>
    private static IndexRangeSeekNode? OrderedIndexRead(SortNode sort, ScanNode scan, JetDatabase database)
    {
        if (ExternalDatabases.Open(database, scan.Database).Catalog.FindTable(scan.Table) is not { } def)
            return null;
        string alias = scan.Alias ?? scan.Table;

        foreach (IndexDef index in def.Indexes)
        {
            if (index.IgnoreNulls || index.RootPage <= 0 || index.Columns.Count != sort.Keys.Count)
                continue;

            bool matches = true;
            for (int i = 0; i < sort.Keys.Count && matches; i++)
            {
                (ColumnDef indexColumn, bool ascending) = index.Columns[i];
                matches = ascending
                    && sort.Keys[i].Direction == SortDirection.Ascending
                    && Classify(indexColumn.Type) is TypeKind.Numeric or TypeKind.Temporal
                    && Column(sort.Keys[i].Value, alias, def) is { } key
                    && string.Equals(key.Column, indexColumn.Name, StringComparison.OrdinalIgnoreCase);
            }

            // Both bounds open: the range seek descends to the leftmost leaf and walks the chain, which is the
            // whole index in key order — lazily, so the enclosing limit ends the read.
            if (matches)
                return new IndexRangeSeekNode(scan.Table, scan.Alias, index, Low: null, High: null, scan.Database) { RowIds = scan.RowIds };
        }

        return null;
    }

    /// <summary>Whether <paramref name="e"/> can be evaluated as a seek key without a row of the table being
    /// scanned — a constant, a parameter, or a column of the enclosing (outer) query in a correlated subquery.</summary>
    private static bool Seekable(Expression e, HashSet<string> outer) => ReferencesOnly(e, outer);

    /// <summary>If <paramref name="conjunct"/> is a range comparison of column <paramref name="colName"/> against
    /// a value (either orientation), returns the operator as if the column were on the left (so <c>5 &lt; K</c>
    /// yields <c>K &gt; 5</c>) and the value expression; else null.</summary>
    private static (BinaryOperator Op, Expression Value)? Bound(Expression conjunct, string colName, string alias, TableDefinition def)
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

    /// <summary>If <paramref name="conjunct"/> is <c>col BETWEEN a AND b</c> on column <paramref name="colName"/>, its
    /// two bounds as written; else null.</summary>
    /// <remarks>BETWEEN takes its bounds in either order, and the executor puts them in the index's order once their
    /// values are known (<see cref="IndexRangeSeekNode.Unordered"/>), so a bound is anything without a column in it, as
    /// for <see cref="Bound"/>. A Null bound makes the whole test Null, which a seek has no way to say, so it is left
    /// to the filter; so is text on a column whose collation has no index keys to order it by.</remarks>
    private static (Expression Low, Expression High)? Between(Expression conjunct, string colName, string alias, TableDefinition def)
    {
        if (conjunct is not BetweenExpression { Negated: false } between
            || Column(between.Value, alias, def) is not { } column
            || !string.Equals(column.Column, colName, StringComparison.OrdinalIgnoreCase)
            || HasColumnRef(between.Low) || HasColumnRef(between.High)
            || between.Low is LiteralExpression { Value: null } || between.High is LiteralExpression { Value: null }
            || def.FindColumn(colName) is not { } indexed
            || Classify(indexed.Type) == TypeKind.Text && !indexed.Collation.IsIndexKeyEncodable)
            return null;
        return (between.Low, between.High);
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
    // Not a HashJoinNode as CA1859 reads it: a join that cannot be hashed is returned as the join it was.
#pragma warning disable CA1859
    private static PlanNode RewriteJoin(JoinNode j, JetDatabase database, HashSet<string> outer)
    {
        PlanNode left = Apply(j.Left, database, outer); // rewrite the outer side first (independent of this join)
        if (j.On is { } joinOn && j.Kind is JoinKind.Inner or JoinKind.Cross or JoinKind.Left or JoinKind.Right)
            j = j with { On = InInnerType(joinOn, j.Kind, left, j.Right, database) };

        // A LEFT join preserves the probing left row: ExecuteJoin's seek loop emits its null-padded result
        // when no candidate passes ON. RIGHT/FULL must also preserve unmatched inner rows, so still hash/scan.
        // Keep hashing an unconstrained LEFT outer scan; a point-constrained outer input avoids building the
        // entire inner table for just its few matching keys.
        bool seekJoin = j.Kind is JoinKind.Inner or JoinKind.Cross
            || j.Kind == JoinKind.Left && left is FilterNode { Input: IndexSeekNode };
        if (seekJoin && j.On is { } on
            && j.Right is ScanNode scan && ExternalDatabases.Open(database, scan.Database).Catalog.FindTable(scan.Table) is { } def)
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

                // A Single beside a Double left as written compares as Singles (InInnerType), which a seek of the
                // Double's index cannot do: its keys are Doubles. The join hashes instead.
                if (def.FindColumn(match.innerCol.Column)?.Type == JetDataType.Double && match.outerKey is ColumnReference key
                    && ResolveColumn(key, left, database)?.Column.Type == JetDataType.Single)
                    continue;

                IndexDef? index = def.Indexes.FirstOrDefault(
                    i => i.Columns.Count == 1 && string.Equals(i.Columns[0].Column.Name, match.innerCol.Column, StringComparison.OrdinalIgnoreCase));
                if (index is null)
                    continue;

                var seek = new IndexSeekNode(scan.Table, scan.Alias, index, [match.outerKey], scan.Database) { RowIds = scan.RowIds };
                return j with { Left = left, Right = seek }; // ON kept as residual
            }
        }

        // A lateral (APPLY) right side is re-executed per left row with the left row in scope, so its own
        // index selection may treat the left side's columns as seekable constants — exactly as it does for a
        // correlated subquery. Without this the right side rescans a base table for every left row, which is
        // the one shape where that cost is paid over and over.
        HashSet<string> rightOuter = j.Kind is JoinKind.CrossApply or JoinKind.OuterApply
            ? QueryPlanner.SubtreeAliases(left).Union(outer).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : outer;
        PlanNode right = Apply(j.Right, database, rightOuter);

        // No index-nested-loop available: if this is an equi-join whose keys are same-kind columns, hash it
        // (O(n+m)) instead of leaving the O(n·m) nested loop. See HashJoinNode for why same-kind is required.
        // RIGHT joins hash too (the executor builds the left and probes with the right), and so does FULL, which
        // additionally emits the build rows the probe never matched.
        if (j.Kind is (JoinKind.Inner or JoinKind.Left or JoinKind.Right or JoinKind.Full) && j.On is { } cond
            && TryHashKeys(cond, left, right, database) is { } keys)
            return new HashJoinNode(left, right, j.Kind, keys.Left, keys.Right, cond) { KeyTypes = keys.Types };

        return j with { Left = left, Right = right };
    }
#pragma warning restore CA1859

    /// <summary>
    /// <paramref name="on"/> with each <c>Single = Double</c> between the join's two sides compared as ACE compares it:
    /// in the type of the join's inner table, the one ACE looks rows up in. Where that is the Double, the Single is
    /// widened; where it is the Single, the conjunct compares as Singles as written.
    /// </summary>
    /// <remarks>Measured against ACE, with tables of 1 to 50 rows: an outer join's inner table is its optional side;
    /// an inner or comma join's is the side with an index on its column, else the side with more rows, else the
    /// first. A comparison ACE makes row by row narrows to single precision, as LibRed's does, and a seek of a real
    /// index converts to that index's type, as LibRed's does.</remarks>
    private static Expression InInnerType(Expression on, JoinKind kind, PlanNode left, PlanNode right, JetDatabase database)
    {
        bool widened = false;
        List<Expression> conjuncts = [.. Conjuncts(on).Select(InnerType)];
        return widened ? QueryPlanner.CombineAnd(conjuncts) : on;

        Expression InnerType(Expression conjunct)
        {
            if (conjunct is not BinaryExpression { Operator: BinaryOperator.Equal, Left: ColumnReference a, Right: ColumnReference b } eq)
                return conjunct;
            var (l, r) = ResolveColumn(a, left, database) is not null ? (a, b) : (b, a);
            if (ResolveColumn(l, left, database) is not { } lc || ResolveColumn(r, right, database) is not { } rc
                || ResolveColumn(l, right, database) is not null || ResolveColumn(r, left, database) is not null)
                return conjunct;

            bool singleLeft = lc.Column.Type == JetDataType.Single;
            if (!(singleLeft ? rc.Column.Type == JetDataType.Double
                    : lc.Column.Type == JetDataType.Double && rc.Column.Type == JetDataType.Single))
                return conjunct;

            bool innerLeft = kind switch
            {
                JoinKind.Left => false,
                JoinKind.Right => true,
                _ => LeadsIndex(lc.Table, lc.Column) != LeadsIndex(rc.Table, rc.Column) ? LeadsIndex(lc.Table, lc.Column)
                    : Rows(lc) != Rows(rc) ? Rows(lc) > Rows(rc) : true,
            };
            if (innerLeft == singleLeft)
                return conjunct;

            widened = true;
            ColumnReference single = singleLeft ? l : r;
            Expression wide = new FunctionCall("CDbl", [single]);
            return ReferenceEquals(eq.Left, single) ? eq with { Left = wide } : eq with { Right = wide };
        }

        static int Rows((JetDatabase Database, TableDefinition Table, ColumnDef Column) c) =>
            c.Database.OpenTable(c.Table.Name).RowCount;
    }

    /// <summary>Whether an index of <paramref name="table"/> has <paramref name="column"/> first, so ACE can seek it for
    /// that column.</summary>
    internal static bool LeadsIndex(TableDefinition table, ColumnDef column) => table.Indexes.Any(i =>
        i.Columns.Count > 0 && string.Equals(i.Columns[0].Column.Name, column.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Splits the join condition into equi-key column pairs (left-referencing = right-referencing)
    /// whose columns are of the same type kind, or null if there is no such pair. The full ON is still applied
    /// as a residual, so a partial split (extra non-equi conjuncts) is fine.</summary>
    private static (IReadOnlyList<Expression> Left, IReadOnlyList<Expression> Right, IReadOnlyList<Type?>? Types)? TryHashKeys(
        Expression on, PlanNode left, PlanNode right, JetDatabase database)
    {
        HashSet<string> leftAliases = QueryPlanner.SubtreeAliases(left);
        HashSet<string> rightAliases = QueryPlanner.SubtreeAliases(right);
        var leftKeys = new List<Expression>();
        var rightKeys = new List<Expression>();
        var types = new List<Type?>();

        foreach (Expression conjunct in Conjuncts(on))
        {
            if (conjunct is not BinaryExpression { Operator: BinaryOperator.Equal } eq)
                continue;

            // Orient the equality so L is the left-subtree side and R the right-subtree side.
            (Expression L, Expression R)? oriented =
                ReferencesOnly(eq.Left, leftAliases) && ReferencesOnly(eq.Right, rightAliases) ? (eq.Left, eq.Right)
                : ReferencesOnly(eq.Right, leftAliases) && ReferencesOnly(eq.Left, rightAliases) ? (eq.Right, eq.Left)
                : null;
            // A key is a column, or a Single widened to a Double (InInnerType).
            if (oriented is not { } o || KeyColumn(o.L) is not { } lc || KeyColumn(o.R) is not { } rc)
                continue;

            // A hash consistent with the evaluator's equality only exists within one type kind (5 = '5' and
            // 5 = 5.0 but '5' ≠ '5.0'), so both key columns must resolve to the same kind.
            var lt = ResolveColumn(lc, left, database);
            var rt = ResolveColumn(rc, right, database);
            if (lt is null || rt is null || Classify(lt.Value.Column.Type) is not { } kind || Classify(rt.Value.Column.Type) != kind)
                continue;

            leftKeys.Add(o.L);
            rightKeys.Add(o.R);
            // A Single beside another number left as written compares as Singles, so both are hashed as Singles.
            types.Add(o.L is ColumnReference && o.R is ColumnReference
                && lt.Value.Column.Type == JetDataType.Single != (rt.Value.Column.Type == JetDataType.Single)
                ? typeof(float) : null);
        }

        return leftKeys.Count > 0 ? (leftKeys, rightKeys, types.Any(t => t is not null) ? types : null) : null;

        static ColumnReference? KeyColumn(Expression key) => key switch
        {
            ColumnReference c => c,
            FunctionCall { Name: "CDbl", Arguments: [ColumnReference c] } => c,
            _ => null,
        };
    }

    internal enum TypeKind { Numeric, Text, Binary, Temporal, Guid }

    /// <summary>The kind a value compares as — the value-side partner of <see cref="Classify(JetDataType)"/>.
    /// Null for <c>Null</c>, which has no kind of its own.</summary>
    internal static TypeKind? KindOf(object? value) => value switch
    {
        null => null,
        string or char => TypeKind.Text,
        DateTime => TypeKind.Temporal,
        Guid => TypeKind.Guid,
        byte[] => TypeKind.Binary,
        _ when ExpressionEvaluator.IsNumeric(value) => TypeKind.Numeric,
        _ => null,
    };

    internal static TypeKind? Classify(JetDataType t) => t switch
    {
        JetDataType.Boolean or JetDataType.Byte or JetDataType.Int16 or JetDataType.Int32 or JetDataType.Int64
            or JetDataType.Single or JetDataType.Double or JetDataType.Currency or JetDataType.FixedPoint => TypeKind.Numeric,
        JetDataType.Text or JetDataType.Memo => TypeKind.Text,
        JetDataType.Binary or JetDataType.BigBinary or JetDataType.Ole => TypeKind.Binary,
        JetDataType.DateTime or JetDataType.DateTimeExtended => TypeKind.Temporal,
        JetDataType.Guid => TypeKind.Guid,
        _ => null,
    };

    /// <summary>The base-table column <paramref name="col"/> is in <paramref name="subtree"/> — directly, or a
    /// derived-table column followed through its projection — with its table and that table's database; null if it
    /// can't be pinned down (e.g. the projection is a computed expression), in which case a join on it is left as a
    /// nested loop rather than risk an unsound hash.</summary>
    internal static (JetDatabase Database, TableDefinition Table, ColumnDef Column)? ResolveColumn(
        ColumnReference col, PlanNode subtree, JetDatabase database)
    {
        foreach ((string alias, string table, ExternalDatabase? external) in BaseTables(subtree))
        {
            if (col.Table is { } t && !string.Equals(t, alias, StringComparison.OrdinalIgnoreCase))
                continue;
            JetDatabase source = ExternalDatabases.Open(database, external);
            if (source.Catalog.FindTable(table) is { } def
                && def.Columns.FirstOrDefault(cd => string.Equals(cd.Name, col.Column, StringComparison.OrdinalIgnoreCase)) is { } c)
                return (source, def, c);
        }

        // A derived-table column: match its alias, find what its SELECT list projects for this name, and resolve
        // that expression against the derived query's own tables (only if it is itself a plain column).
        foreach (DerivedTableNode d in DerivedTables(subtree))
        {
            if (col.Table is { } t && !string.Equals(t, d.Alias, StringComparison.OrdinalIgnoreCase))
                continue;
            if (DerivedColumn(d, col.Column, database) is { } inner)
                return ResolveColumn(inner, d.Input, database);
        }
        return null;
    }

    // A derived table is an opaque boundary: its inner tables/aliases are not visible outside it, so BaseTables
    // stops there (DerivedTables handles the derived level separately).
    private static IEnumerable<(string Alias, string Table, ExternalDatabase? Database)> BaseTables(PlanNode node) => node switch
    {
        ScanNode s => [(s.Alias ?? s.Table, s.Table, s.Database)],
        IndexSeekNode s => [(s.Alias ?? s.Table, s.Table, s.Database)],
        IndexRangeSeekNode s => [(s.Alias ?? s.Table, s.Table, s.Database)],
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

    /// <summary>Whether every column an expression references is one of the given aliases (and it has no
    /// subquery) — i.e. it can be evaluated from the outer row alone, as an index-seek key.</summary>
    internal static bool ReferencesOnly(Expression e, HashSet<string> aliases) => e switch
    {
        ColumnReference { Table: { } t } => aliases.Contains(t),
        ColumnReference => false, // unqualified — can't attribute it to the outer side safely
        LiteralExpression or ParameterExpression or SystemVariableExpression => true,
        _ => e.Operands()?.All(a => ReferencesOnly(a, aliases)) ?? false,
    };

    /// <summary>The column reference if <paramref name="e"/> is a column of the given scan (its qualifier is
    /// the scan's alias, or it is unqualified and the table has such a column), else null.</summary>
    private static ColumnReference? Column(Expression e, string alias, TableDefinition def)
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
        // Same reasoning, and the same reason to be explicit: this switch's default means "no column reference"
        // — i.e. usable as a seek bound — so an unlisted node fails towards a WRONG seek, not a missed one.
        WindowFunction => true,
        _ => e.Operands()?.Any(HasColumnRef) ?? false, // no operands: literals, parameters, system vars
    };

    /// <summary>The AND-conjuncts of a predicate; none at all for an absent one (a missing WHERE or HAVING).</summary>
    internal static IEnumerable<Expression> Conjuncts(Expression? e)
    {
        if (e is null)
            return [];
        if (e is BinaryExpression { Operator: BinaryOperator.And } b)
            return Conjuncts(b.Left).Concat(Conjuncts(b.Right));
        return [e];
    }
}
