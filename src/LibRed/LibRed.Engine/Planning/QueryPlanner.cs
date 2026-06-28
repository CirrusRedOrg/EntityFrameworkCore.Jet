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
        return bound.Statement switch
        {
            SelectStatement select => PlanSelect(select),
            _ => throw new NotImplementedException(
                $"Planning for {bound.Statement.GetType().Name} is not yet implemented."),
        };
    }

    private static PlanNode PlanSelect(SelectStatement select)
    {
        // Naive single-table shape: Scan → Filter → Project → Limit. (Joins, aggregation,
        // and ORDER BY, plus index-based scans, are future node types.)
        PlanNode node = select.From switch
        {
            NamedTable t => new ScanNode(t.Name),
            _ => throw new NotImplementedException("Only single-table FROM is implemented."),
        };

        if (select.Where is not null)
            node = new FilterNode(node, select.Where);

        if (!select.IsSelectStar)
            node = new ProjectNode(node, select.Projection);

        if (select.Top is { } top)
            node = new LimitNode(node, top);

        return node;
    }
}
