using LibRed.Catalog;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Plan;

/// <summary>Full-table scan of a base table, exposing its columns under <paramref name="Alias"/>.</summary>
public sealed record ScanNode(string Table, string? Alias) : PlanNode;

/// <summary>A FROM-less SELECT source: yields exactly one row with no columns, so a constant projection like
/// <c>SELECT 2</c> evaluates once. ACE accepts a bare <c>SELECT 2</c> (verified) — this matches that.</summary>
public sealed record SingleRowNode : PlanNode;

/// <summary>
/// An index seek: reads the rows of <paramref name="Table"/> whose <paramref name="Index"/> key equals the
/// evaluated <paramref name="Keys"/> (one per index column, in index order), instead of a full scan. Exposes
/// the same columns as a scan of the table. The seek is an access path that may over-return (the key encoding
/// is lossy for text/binary), so the residual predicate is re-checked by the <see cref="FilterNode"/> above.
/// A key may reference an outer row (index-nested-loop join).
/// </summary>
public sealed record IndexSeekNode(string Table, string? Alias, IndexDef Index, IReadOnlyList<Expression> Keys) : PlanNode;

/// <summary>
/// An index range scan: reads the rows of <paramref name="Table"/> whose single-column <paramref name="Index"/>
/// key lies in [<paramref name="Low"/>, <paramref name="High"/>] (either bound null = open), instead of a full
/// scan. Emitted for range predicates (<c>&gt;</c>/<c>&gt;=</c>/<c>&lt;</c>/<c>&lt;=</c>/<c>BETWEEN</c>) on an
/// indexed column. The residual <see cref="FilterNode"/> above re-applies the exact bounds (the seek is an
/// over-returning access path — lossy keys, strict-vs-inclusive boundaries).
/// </summary>
public sealed record IndexRangeSeekNode(string Table, string? Alias, IndexDef Index, Expression? Low, Expression? High) : PlanNode;

/// <summary>A derived table: the output of <paramref name="Input"/> re-exposed under an alias. The alias
/// is optional (Access permits an aliasless derived table); its columns are then unqualified.</summary>
public sealed record DerivedTableNode(PlanNode Input, string? Alias) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}

/// <summary>Index seek/range scan over a named index.</summary>
public sealed record IndexScanNode(string Table, string Index, Expression? Predicate) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [];
}

/// <summary>Applies a boolean predicate to its input rows.</summary>
public sealed record FilterNode(PlanNode Input, Expression Predicate) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}

/// <summary>Projects (and optionally renames) a set of expressions from its input.</summary>
public sealed record ProjectNode(PlanNode Input, IReadOnlyList<SelectItem> Projection) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}

/// <summary>Joins two inputs on a condition.</summary>
public sealed record JoinNode(PlanNode Left, PlanNode Right, JoinKind Kind, Expression? On) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Left, Right];
}

/// <summary>
/// An equi-join executed by hashing: the <paramref name="Right"/> side is materialised into a hash table keyed
/// by <paramref name="RightKeys"/>, then each <paramref name="Left"/> row probes it by <paramref name="LeftKeys"/>
/// — O(n+m) instead of the nested loop's O(n·m). The full <paramref name="On"/> is kept as a residual re-check
/// (hash buckets can collide, and the ON may carry non-equi conjuncts). Produced by the planner only when every
/// key pair is columns of the same type kind, since the evaluator's equality is not transitive across kinds
/// (5 = '5' and 5 = 5.0 but '5' ≠ '5.0'), so a hash consistent with it exists only within one kind.
/// </summary>
public sealed record HashJoinNode(
    PlanNode Left, PlanNode Right, JoinKind Kind,
    IReadOnlyList<Expression> LeftKeys, IReadOnlyList<Expression> RightKeys, Expression On) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Left, Right];
}

/// <summary>
/// Groups input rows by the <paramref name="GroupBy"/> key expressions and emits one row per
/// group by evaluating <paramref name="Projection"/> — where aggregate calls are computed over
/// the group and other expressions see the group's key values.
/// </summary>
public sealed record AggregateNode(
    PlanNode Input,
    IReadOnlyList<Expression> GroupBy,
    IReadOnlyList<SelectItem> Projection,
    Expression? Having,
    IReadOnlyList<OrderByItem> OrderBy) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}

/// <summary>One window function and the name of the column <see cref="WindowNode"/> publishes its value under.
/// The planner mints the name and rewrites the call in the projection into a reference to it.</summary>
public sealed record WindowOutput(string Name, WindowFunction Function);

/// <summary>
/// Computes window functions over its input, appending one column per <see cref="WindowOutput"/> and passing
/// every input row through unchanged and <b>in input order</b>. Order preservation is load-bearing: the sort
/// sits above this node, so emitting rows in partition order would silently reorder any query that both uses a
/// window and has its own ORDER BY.
/// </summary>
/// <remarks>
/// A window value depends on the other rows of its partition, and partitions are only known once the input is
/// exhausted, so this node materialises its input — as <see cref="AggregateNode"/> does. Its schema, however, is
/// derived from the input schema alone, so it is still resolved without reading a row.
/// </remarks>
public sealed record WindowNode(PlanNode Input, IReadOnlyList<WindowOutput> Outputs) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}

/// <summary>Orders rows.</summary>
/// <param name="Limit">
/// When set, only this many rows are needed from the ordering, so the sort keeps the smallest n as it goes instead
/// of ordering its whole input (see QueryExecutor.SortRows). The planner sets it from an enclosing <c>TOP n</c>
/// when nothing between the two changes the row count; the <see cref="LimitNode"/> still applies the count itself,
/// so this is purely a way to avoid ordering rows that cannot survive it.
/// </param>
public sealed record SortNode(PlanNode Input, IReadOnlyList<OrderByItem> Keys, Expression? Limit = null) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}

/// <summary>
/// A table value constructor used as a query — <c>VALUES (1), (2)</c> — producing one row per list from
/// expressions rather than from a table. The leaf analogue of <see cref="SingleRowNode"/>, which is the same
/// idea for a FROM-less SELECT's single empty row.
/// </summary>
/// <remarks>
/// The expressions may reference outer columns (EF emits <c>VALUES (`p`.`Int`)</c> inside a correlated
/// subquery), so they are evaluated against the outer scope each time the node runs, not once at planning.
/// The columns are unnamed: today this only ever appears as an operand of a set operation, whose names come
/// from the leading query.
/// </remarks>
public sealed record ValuesNode(IReadOnlyList<IReadOnlyList<Expression>> Rows) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [];
}

/// <summary>Combines two inputs by a set operation (UNION / UNION ALL / INTERSECT / EXCEPT).</summary>
public sealed record SetOperationNode(PlanNode Left, PlanNode Right, SetOperator Operator) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Left, Right];
}

/// <summary>Limits the number of rows (Access <c>TOP n</c>, or ANSI <c>OFFSET</c>/<c>FETCH</c>). The count is
/// an expression (usually a literal, but LibRed also accepts a parameter or a +/- expression) evaluated once
/// at execution. When <paramref name="Percent"/> is set (<c>TOP n PERCENT</c>) the count is a percentage of
/// the input row count, taken as <c>ceil(rows × n / 100)</c> (verified vs ACE).</summary>
/// <param name="Count">
/// Rows to return, or null for <c>OFFSET n ROWS</c> with no <c>FETCH</c> — skip and then return the rest.
/// </param>
/// <param name="Offset">
/// Rows to skip before returning any, from <c>OFFSET n ROWS</c>. Null means start at the first row. The skip
/// is applied before the count, so <c>OFFSET 10 ROWS FETCH NEXT 5</c> yields rows 11-15. Cannot combine with
/// <paramref name="Percent"/>: ANSI paging has no PERCENT form and Access's TOP has no OFFSET.
/// </param>
public sealed record LimitNode(PlanNode Input, Expression? Count, bool Percent = false, Expression? Offset = null) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}

/// <summary>Removes duplicate rows (SELECT <c>DISTINCT</c>).</summary>
public sealed record DistinctNode(PlanNode Input) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}

/// <summary>
/// Access <c>SELECT DISTINCTROW</c>: dedupes on the underlying rows of the tables that contribute output
/// columns, applied over the pre-projection row so it can see all their columns. The
/// <paramref name="Projection"/> identifies the contributing tables at execution (via each output column's
/// source qualifier). A no-op when the query has a single source table or draws columns from every table —
/// exactly Access's "ignored unless output is from a strict subset of the tables" rule.
/// </summary>
public sealed record DistinctRowNode(PlanNode Input, IReadOnlyList<SelectItem> Projection) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}
