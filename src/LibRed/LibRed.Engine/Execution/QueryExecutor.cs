using LibRed.Engine.Plan;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>A column produced by a plan node: an optional table-alias qualifier and a name.</summary>
internal readonly record struct OutputColumn(string? Qualifier, string Name);

/// <summary>
/// Interprets a logical plan tree against the storage layer, producing a
/// <see cref="ResultSet"/>. Rows flow as <c>object?[]</c>; each node carries the schema
/// (alias-qualified columns) so column references resolve across joins and derived tables.
/// </summary>
public sealed class QueryExecutor(JetDatabase database)
{
    private readonly JetDatabase _database = database;

    public ResultSet ExecuteQuery(PlanNode plan)
    {
        var (columns, rows) = Execute(plan);
        return new ResultSet(columns.Select(c => c.Name).ToList(), rows);
    }

    public int ExecuteNonQuery(PlanNode plan)
    {
        _ = plan;
        throw new NotSupportedException("Only SELECT statements are supported so far.");
    }

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) Execute(PlanNode node)
    {
        switch (node)
        {
            case ScanNode scan:
            {
                var table = _database.OpenTable(scan.Table);
                string alias = scan.Alias ?? scan.Table;
                var columns = table.Definition.Columns.Select(c => new OutputColumn(alias, c.Name)).ToList();
                return (columns, table.Rows());
            }

            case DerivedTableNode derived:
            {
                var (inner, rows) = Execute(derived.Input);
                var columns = inner.Select(c => new OutputColumn(derived.Alias, c.Name)).ToList();
                return (columns, rows);
            }

            case FilterNode filter:
            {
                var (columns, rows) = Execute(filter.Input);
                var resolve = Resolver(columns);
                return (columns, rows.Where(row => new ExpressionEvaluator(resolve, row).IsTrue(filter.Predicate)));
            }

            case JoinNode join:
                return ExecuteJoin(join);

            case SortNode sort:
            {
                var (columns, rows) = Execute(sort.Input);
                var resolve = Resolver(columns);
                var sorted = rows.ToList();
                sorted.Sort((a, b) => CompareKeys(sort.Keys, resolve, a, b));
                return (columns, sorted);
            }

            case ProjectNode project:
            {
                var (columns, rows) = Execute(project.Input);
                var resolve = Resolver(columns);

                var output = project.Projection
                    .Select((item, i) => new OutputColumn(null, item.Alias ?? (item.Value is ColumnReference c ? c.Column : $"Expr{i + 1}")))
                    .ToList();

                var projected = rows.Select(row =>
                {
                    var eval = new ExpressionEvaluator(resolve, row);
                    return project.Projection.Select(item => eval.Evaluate(item.Value)).ToArray();
                });

                return (output, projected);
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

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteJoin(JoinNode join)
    {
        var (leftColumns, leftRows) = Execute(join.Left);
        var (rightColumns, rightRowsEnum) = Execute(join.Right);

        var columns = leftColumns.Concat(rightColumns).ToList();
        var rightRows = rightRowsEnum.ToList(); // re-iterated per left row
        var resolve = Resolver(columns);
        Expression on = join.On ?? throw new NotSupportedException("Joins require an ON condition.");
        bool leftOuter = join.Kind == JoinKind.Left;

        IEnumerable<object?[]> Rows()
        {
            foreach (object?[] left in leftRows)
            {
                bool matched = false;
                foreach (object?[] right in rightRows)
                {
                    object?[] combined = [.. left, .. right];
                    if (new ExpressionEvaluator(resolve, combined).IsTrue(on))
                    {
                        matched = true;
                        yield return combined;
                    }
                }

                if (leftOuter && !matched)
                    yield return [.. left, .. new object?[rightColumns.Count]];
            }
        }

        return (columns, Rows());
    }

    private static int CompareKeys(IReadOnlyList<OrderByItem> keys, Func<ColumnReference, int> resolve, object?[] a, object?[] b)
    {
        foreach (OrderByItem key in keys)
        {
            object? va = new ExpressionEvaluator(resolve, a).Evaluate(key.Value);
            object? vb = new ExpressionEvaluator(resolve, b).Evaluate(key.Value);
            int c = ExpressionEvaluator.CompareForSort(va, vb);
            if (key.Direction == SortDirection.Descending) c = -c;
            if (c != 0) return c;
        }
        return 0;
    }

    /// <summary>Builds a column-reference → ordinal resolver over a schema (qualifier + name aware).</summary>
    private static Func<ColumnReference, int> Resolver(IReadOnlyList<OutputColumn> columns) => reference =>
    {
        int found = -1;
        for (int i = 0; i < columns.Count; i++)
        {
            bool nameMatch = string.Equals(columns[i].Name, reference.Column, StringComparison.OrdinalIgnoreCase);
            bool qualifierMatch = reference.Table is null
                || string.Equals(columns[i].Qualifier, reference.Table, StringComparison.OrdinalIgnoreCase);
            if (!nameMatch || !qualifierMatch) continue;

            if (found >= 0)
                throw new InvalidOperationException($"Column reference '{Describe(reference)}' is ambiguous.");
            found = i;
        }

        return found >= 0
            ? found
            : throw new InvalidOperationException($"Column '{Describe(reference)}' was not found.");
    };

    private static string Describe(ColumnReference r) => r.Table is null ? r.Column : $"{r.Table}.{r.Column}";
}
