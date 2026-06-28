using LibRed.Engine.Plan;
using LibRed.Engine.Planning;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>A column produced by a plan node: an optional table-alias qualifier and a name.</summary>
internal readonly record struct OutputColumn(string? Qualifier, string Name);

/// <summary>
/// Interprets a logical plan tree against the storage layer, producing a
/// <see cref="ResultSet"/>. Rows flow as <c>object?[]</c>; each node carries the schema
/// (alias-qualified columns), and an optional outer <see cref="EvalScope"/> is threaded so
/// correlated subqueries can resolve outer columns.
/// </summary>
public sealed class QueryExecutor : IScalarSubqueryRunner
{
    private readonly JetDatabase _database;
    private readonly ParameterBag _parameters;

    public QueryExecutor(JetDatabase database, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        _database = database;
        _parameters = new ParameterBag(parameters);
    }

    public ResultSet ExecuteQuery(PlanNode plan)
    {
        var (columns, rows) = Execute(plan, null);
        return new ResultSet(columns.Select(c => c.Name).ToList(), rows);
    }

    object? IScalarSubqueryRunner.ExecuteScalar(SelectStatement query, EvalScope outerScope)
    {
        var (_, rows) = Execute(QueryPlanner.PlanSelect(query), outerScope);
        foreach (object?[] row in rows)
            return row.Length > 0 ? row[0] : null;
        return null; // no rows → NULL
    }

    bool IScalarSubqueryRunner.ExecuteExists(SelectStatement query, EvalScope outerScope)
    {
        var (_, rows) = Execute(QueryPlanner.PlanSelect(query), outerScope);
        return rows.Any();
    }

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) Execute(PlanNode node, EvalScope? outer)
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
                var (inner, rows) = Execute(derived.Input, outer);
                var columns = inner.Select(c => new OutputColumn(derived.Alias, c.Name)).ToList();
                return (columns, rows);
            }

            case FilterNode filter:
            {
                var (columns, rows) = Execute(filter.Input, outer);
                return (columns, rows.Where(row => Eval(columns, row, outer).IsTrue(filter.Predicate)));
            }

            case JoinNode join:
                return ExecuteJoin(join, outer);

            case AggregateNode aggregate:
                return ExecuteAggregate(aggregate, outer);

            case SortNode sort:
            {
                var (columns, rows) = Execute(sort.Input, outer);
                var sorted = rows.ToList();
                sorted.Sort((a, b) => CompareKeys(sort.Keys, columns, outer, a, b));
                return (columns, sorted);
            }

            case ProjectNode project:
            {
                var (columns, rows) = Execute(project.Input, outer);

                var output = project.Projection
                    .Select((item, i) => new OutputColumn(null, item.Alias ?? (item.Value is ColumnReference c ? c.Column : $"Expr{i + 1}")))
                    .ToList();

                var projected = rows.Select(row =>
                {
                    var eval = Eval(columns, row, outer);
                    return project.Projection.Select(item => eval.Evaluate(item.Value)).ToArray();
                });

                return (output, projected);
            }

            case SetOperationNode setOp:
            {
                // Column names come from the left (leading) query, per SQL.
                var (columns, leftRows) = Execute(setOp.Left, outer);
                var (_, rightRows) = Execute(setOp.Right, outer);
                return (columns, ExecuteSetOp(setOp.Operator, leftRows, rightRows));
            }

            case LimitNode limit:
            {
                var (columns, rows) = Execute(limit.Input, outer);
                return (columns, rows.Take(limit.Count));
            }

            default:
                throw new NotSupportedException($"Plan node {node.GetType().Name} is not supported yet.");
        }
    }

    private static IEnumerable<object?[]> ExecuteSetOp(SetOperator op, IEnumerable<object?[]> left, IEnumerable<object?[]> right)
    {
        switch (op)
        {
            case SetOperator.UnionAll:
                return left.Concat(right);
            case SetOperator.Union:
                return Distinct(left.Concat(right));
            case SetOperator.Intersect:
            {
                var keep = new HashSet<GroupKey>(right.Select(r => new GroupKey(r)));
                return Distinct(left).Where(r => keep.Contains(new GroupKey(r)));
            }
            case SetOperator.Except:
            {
                var remove = new HashSet<GroupKey>(right.Select(r => new GroupKey(r)));
                return Distinct(left).Where(r => !remove.Contains(new GroupKey(r)));
            }
            default:
                throw new NotSupportedException($"Set operator {op} is not supported.");
        }
    }

    /// <summary>Yields rows with duplicates removed by structural (value-wise) equality.</summary>
    private static IEnumerable<object?[]> Distinct(IEnumerable<object?[]> rows)
    {
        var seen = new HashSet<GroupKey>();
        foreach (object?[] row in rows)
            if (seen.Add(new GroupKey(row)))
                yield return row;
    }

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteJoin(JoinNode join, EvalScope? outer)
    {
        var (leftColumns, leftRows) = Execute(join.Left, outer);
        var (rightColumns, rightRowsEnum) = Execute(join.Right, outer);

        var columns = leftColumns.Concat(rightColumns).ToList();
        var rightRows = rightRowsEnum.ToList(); // re-iterated per left row
        Expression? on = join.On; // null for a CROSS join (cartesian product)
        if (on is null && join.Kind != JoinKind.Cross)
            throw new NotSupportedException("Joins require an ON condition.");
        bool leftOuter = join.Kind == JoinKind.Left;

        IEnumerable<object?[]> Rows()
        {
            foreach (object?[] left in leftRows)
            {
                bool matched = false;
                foreach (object?[] right in rightRows)
                {
                    object?[] combined = [.. left, .. right];
                    if (on is null || Eval(columns, combined, outer).IsTrue(on))
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

    private int CompareKeys(IReadOnlyList<OrderByItem> keys, IReadOnlyList<OutputColumn> columns, EvalScope? outer, object?[] a, object?[] b)
    {
        foreach (OrderByItem key in keys)
        {
            object? va = Eval(columns, a, outer).Evaluate(key.Value);
            object? vb = Eval(columns, b, outer).Evaluate(key.Value);
            int c = ExpressionEvaluator.CompareForSort(va, vb);
            if (key.Direction == SortDirection.Descending) c = -c;
            if (c != 0) return c;
        }
        return 0;
    }

    private ExpressionEvaluator Eval(IReadOnlyList<OutputColumn> columns, object?[] row, EvalScope? outer) =>
        new(new EvalScope(columns, row, outer), this, parameters: _parameters);

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteAggregate(AggregateNode node, EvalScope? outer)
    {
        var (inColumns, inRowsEnum) = Execute(node.Input, outer);
        var inRows = inRowsEnum.ToList();
        // Aggregates can appear in both the projection and HAVING (e.g. HAVING COUNT(*) > 30).
        var aggregateCalls = node.Projection.SelectMany(i => Aggregates(i.Value))
            .Concat(node.Having is { } h ? Aggregates(h) : []).ToList();

        var outColumns = node.Projection
            .Select((item, i) => new OutputColumn(null, item.Alias ?? (item.Value is ColumnReference c ? c.Column : $"Expr{i + 1}")))
            .ToList();

        var outRows = new List<object?[]>();
        foreach (List<object?[]> group in GroupRows(inRows, node.GroupBy, inColumns, outer))
        {
            var values = new Dictionary<FunctionCall, object?>(ReferenceComparer.Instance);
            foreach (FunctionCall call in aggregateCalls)
                values[call] = ComputeAggregate(call, group, inColumns, outer);

            // Within a group every key value is constant, so the first row resolves group keys;
            // aggregate calls resolve from the precomputed map. An empty group only happens for
            // an aggregate with no GROUP BY over zero rows (e.g. COUNT(*) -> 0); there are no key
            // columns to resolve, so a null row suffices.
            object?[] keyRow = group.Count > 0 ? group[0] : new object?[inColumns.Count];
            var eval = new ExpressionEvaluator(new EvalScope(inColumns, keyRow, outer), this, values, _parameters);

            // HAVING filters whole groups after aggregation.
            if (node.Having is not null && !eval.IsTrue(node.Having))
                continue;

            outRows.Add(node.Projection.Select(item => eval.Evaluate(item.Value)).ToArray());
        }

        return (outColumns, outRows);
    }

    private List<List<object?[]>> GroupRows(List<object?[]> rows, IReadOnlyList<Expression> keys, IReadOnlyList<OutputColumn> columns, EvalScope? outer)
    {
        if (keys.Count == 0)
            return [rows]; // a single group over all rows (even if empty)

        var order = new List<GroupKey>();
        var groups = new Dictionary<GroupKey, List<object?[]>>();
        foreach (object?[] row in rows)
        {
            var eval = Eval(columns, row, outer);
            var key = new GroupKey(keys.Select(k => eval.Evaluate(k)).ToArray());
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
                order.Add(key);
            }
            list.Add(row);
        }
        return order.Select(k => groups[k]).ToList();
    }

    private object? ComputeAggregate(FunctionCall call, List<object?[]> group, IReadOnlyList<OutputColumn> columns, EvalScope? outer)
    {
        string name = call.Name.ToUpperInvariant();
        Expression? arg = call.Arguments.Count > 0 ? call.Arguments[0] : null;

        if (name == "COUNT")
            return arg is StarExpression or null
                ? (long)group.Count
                : (long)group.Count(r => Eval(columns, r, outer).Evaluate(arg) is not null);

        var values = group.Select(r => Eval(columns, r, outer).Evaluate(arg!)).Where(v => v is not null).ToList();
        if (values.Count == 0)
            return name == "COUNT" ? 0L : null; // SUM/AVG/MIN/MAX of nothing is NULL

        return name switch
        {
            "SUM" => values.Sum(v => Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture)),
            "AVG" => values.Average(v => Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture)),
            "MIN" => values.Aggregate((a, b) => ExpressionEvaluator.CompareForSort(a, b) <= 0 ? a : b),
            "MAX" => values.Aggregate((a, b) => ExpressionEvaluator.CompareForSort(a, b) >= 0 ? a : b),
            _ => throw new NotSupportedException($"Aggregate {call.Name} is not supported."),
        };
    }

    private static IEnumerable<FunctionCall> Aggregates(Expression e)
    {
        switch (e)
        {
            case FunctionCall f when QueryPlanner.IsAggregate(f.Name):
                yield return f;
                break;
            case FunctionCall f:
                foreach (FunctionCall a in f.Arguments.SelectMany(Aggregates)) yield return a;
                break;
            case BinaryExpression b:
                foreach (FunctionCall a in Aggregates(b.Left).Concat(Aggregates(b.Right))) yield return a;
                break;
            case UnaryExpression u:
                foreach (FunctionCall a in Aggregates(u.Operand)) yield return a;
                break;
        }
    }

    /// <summary>Groups by structural equality of the key value tuple.</summary>
    private sealed class GroupKey(object?[] values) : IEquatable<GroupKey>
    {
        private readonly object?[] _values = values;

        public bool Equals(GroupKey? other) =>
            other is not null && _values.Length == other._values.Length
            && _values.Zip(other._values).All(p => Equals(p.First, p.Second));

        public override bool Equals(object? obj) => Equals(obj as GroupKey);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (object? v in _values) hash.Add(v);
            return hash.ToHashCode();
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<FunctionCall>
    {
        public static readonly ReferenceComparer Instance = new();
        public bool Equals(FunctionCall? x, FunctionCall? y) => ReferenceEquals(x, y);
        public int GetHashCode(FunctionCall obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
