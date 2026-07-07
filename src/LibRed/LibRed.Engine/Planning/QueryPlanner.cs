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
            node = new FilterNode(node, select.Where);

        bool aggregate = select.GroupBy.Count > 0 || select.Having is not null
            || select.Projection.Any(i => HasAggregate(i.Value));
        if (aggregate)
            // The aggregate node owns ORDER BY: its keys are evaluated in the group scope (so they can
            // reference grouping expressions / aggregates), not over the already-projected output.
            node = new AggregateNode(node, select.GroupBy, select.Projection, select.Having, select.OrderBy);
        else if (select.OrderBy.Count > 0)
            node = new SortNode(node, select.OrderBy);

        if (!aggregate && !select.IsSelectStar)
            node = new ProjectNode(node, select.Projection);

        if (select.Distinct)
            node = new DistinctNode(node);

        if (select.Top is { } top)
            node = new LimitNode(node, top);

        return node;
    }

    /// <summary>The aggregate function names recognised by the planner/executor. Includes the Access statistical
    /// aggregates StDev/StDevP (sample/population standard deviation) and Var/VarP (sample/population variance);
    /// the "StdDev"/"StdDevP" spellings are accepted as aliases.</summary>
    internal static bool IsAggregate(string name) =>
        name.ToUpperInvariant() is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX"
            or "STDEV" or "STDEVP" or "STDDEV" or "STDDEVP" or "VAR" or "VARP";

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
        SubqueryTable s => new DerivedTableNode(PlanStatement(s.Query), s.Alias), // alias optional (Access allows aliasless)
        _ => throw new NotSupportedException($"Unsupported FROM source {from.GetType().Name}."),
    };
}
