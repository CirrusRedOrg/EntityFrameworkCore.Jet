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
    Expression? Having) : PlanNode
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
/// but LibRed also accepts a parameter or a +/- expression) evaluated once at execution.</summary>
public sealed record LimitNode(PlanNode Input, Expression Count) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}

/// <summary>Removes duplicate rows (SELECT <c>DISTINCT</c>).</summary>
public sealed record DistinctNode(PlanNode Input) : PlanNode
{
    public override IReadOnlyList<PlanNode> Children => [Input];
}
