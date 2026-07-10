using LibRed.Sql.Ast;

namespace LibRed.Engine.Plan;

/// <summary>Full-table scan of a base table, exposing its columns under <paramref name="Alias"/>.</summary>
public sealed record ScanNode(string Table, string? Alias) : PlanNode;

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

/// <summary>Orders rows.</summary>
public sealed record SortNode(PlanNode Input, IReadOnlyList<OrderByItem> Keys) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}

/// <summary>Combines two inputs by a set operation (UNION / UNION ALL / INTERSECT / EXCEPT).</summary>
public sealed record SetOperationNode(PlanNode Left, PlanNode Right, SetOperator Operator) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Left, Right];
}

/// <summary>Limits the number of rows (Access <c>TOP n</c>). The count is an expression (usually a literal,
/// but LibRed also accepts a parameter or a +/- expression) evaluated once at execution. When
/// <paramref name="Percent"/> is set (<c>TOP n PERCENT</c>) the count is a percentage of the input row
/// count, taken as <c>ceil(rows × n / 100)</c> (verified vs ACE).</summary>
public sealed record LimitNode(PlanNode Input, Expression Count, bool Percent = false) : PlanNode
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
