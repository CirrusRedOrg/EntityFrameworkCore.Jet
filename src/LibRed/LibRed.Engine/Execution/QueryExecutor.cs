using LibRed.Engine.Plan;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// Interprets a logical plan tree against the storage layer, producing a
/// <see cref="ResultSet"/> for queries.
/// </summary>
public sealed class QueryExecutor(JetDatabase database)
{
    private readonly JetDatabase _database = database;

    public ResultSet ExecuteQuery(PlanNode plan)
    {
        var (columns, rows) = Execute(plan);
        return new ResultSet(columns, rows);
    }

    public int ExecuteNonQuery(PlanNode plan)
    {
        _ = plan;
        throw new NotSupportedException("Only SELECT statements are supported so far.");
    }

    private (IReadOnlyList<string> Columns, IEnumerable<object?[]> Rows) Execute(PlanNode node)
    {
        switch (node)
        {
            case ScanNode scan:
            {
                var table = _database.OpenTable(scan.Table);
                var columns = table.Definition.Columns.Select(c => c.Name).ToList();
                return (columns, table.Rows());
            }

            case FilterNode filter:
            {
                var (columns, rows) = Execute(filter.Input);
                var ordinals = Ordinals(columns);
                return (columns, rows.Where(row => new ExpressionEvaluator(c => ordinals[c.Column], row).IsTrue(filter.Predicate)));
            }

            case ProjectNode project:
            {
                var (columns, rows) = Execute(project.Input);
                var ordinals = Ordinals(columns);

                var outputColumns = project.Projection
                    .Select((item, i) => item.Alias ?? (item.Value is ColumnReference c ? c.Column : $"Expr{i + 1}"))
                    .ToList();

                var projected = rows.Select(row =>
                {
                    var eval = new ExpressionEvaluator(c => ordinals[c.Column], row);
                    return project.Projection.Select(item => eval.Evaluate(item.Value)).ToArray();
                });

                return (outputColumns, projected);
            }

            case LimitNode limit:
            {
                var (columns, rows) = Execute(limit.Input);
                return (columns, rows.Take(limit.Count));
            }

            default:
                throw new NotSupportedException($"Plan node {node.GetType().Name} is not supported yet.");
        }
    }

    private static Dictionary<string, int> Ordinals(IReadOnlyList<string> columns)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Count; i++)
            map[columns[i]] = i; // last wins on duplicate names
        return map;
    }
}
