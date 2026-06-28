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
        SetOperationStatement set => new UnionNode(
            PlanStatement(set.Left), PlanStatement(set.Right), set.Operator == SetOperator.Union),
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
            node = new FilterNode(node, select.Where);

        bool aggregate = select.GroupBy.Count > 0 || select.Projection.Any(i => HasAggregate(i.Value));
        if (aggregate)
            node = new AggregateNode(node, select.GroupBy, select.Projection);

        if (select.OrderBy.Count > 0)
            node = new SortNode(node, select.OrderBy);

        if (!aggregate && !select.IsSelectStar)
            node = new ProjectNode(node, select.Projection);

        if (select.Top is { } top)
            node = new LimitNode(node, top);

        return node;
    }

    /// <summary>The aggregate function names recognised by the planner/executor.</summary>
    internal static bool IsAggregate(string name) =>
        name.ToUpperInvariant() is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX";

    private static bool HasAggregate(Expression e) => e switch
    {
        FunctionCall f when IsAggregate(f.Name) => true,
        FunctionCall f => f.Arguments.Any(HasAggregate),
        BinaryExpression b => HasAggregate(b.Left) || HasAggregate(b.Right),
        UnaryExpression u => HasAggregate(u.Operand),
        _ => false,
    };

    private static PlanNode PlanFrom(TableReference from) => from switch
    {
        NamedTable t => new ScanNode(t.Name, t.Alias),
        JoinTable j => new JoinNode(PlanFrom(j.Left), PlanFrom(j.Right), j.Kind, j.On),
        SubqueryTable s => new DerivedTableNode(PlanSelect(s.Query), s.Alias
            ?? throw new NotSupportedException("A derived table requires an alias.")),
        _ => throw new NotSupportedException($"Unsupported FROM source {from.GetType().Name}."),
    };
}
