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
    private readonly SessionState? _session;

    public QueryExecutor(JetDatabase database, IReadOnlyDictionary<string, object?>? parameters = null, SessionState? session = null)
    {
        _database = database;
        _parameters = new ParameterBag(parameters);
        _session = session;
    }

    public ResultSet ExecuteQuery(PlanNode plan)
    {
        var (columns, rows) = Execute(plan, null);
        return new ResultSet(columns.Select(c => c.Name).ToList(), rows);
    }

    /// <summary>Runs a FROM-less <c>SELECT @@IDENTITY</c> / <c>SELECT @@ROWCOUNT</c>: evaluates each system
    /// variable against the session state and yields a single row. Each output column is named by its alias,
    /// or the variable name if unaliased.</summary>
    public ResultSet ExecuteSystemVariableSelect(SystemVariableSelectStatement statement)
    {
        var evaluator = new ExpressionEvaluator(new EvalScope([], [], null), this, _parameters, _session);
        var names = new List<string>(statement.Projection.Count);
        var row = new object?[statement.Projection.Count];
        for (int i = 0; i < statement.Projection.Count; i++)
        {
            SelectItem item = statement.Projection[i];
            row[i] = evaluator.Evaluate(item.Value);
            names.Add(item.Alias ?? ((SystemVariableExpression)item.Value).Name);
        }
        return new ResultSet(names, [row]);
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

    IEnumerable<object?> IScalarSubqueryRunner.ExecuteColumn(SelectStatement query, EvalScope outerScope)
    {
        var (_, rows) = Execute(QueryPlanner.PlanSelect(query), outerScope);
        // Materialize: the outer scope is reused across the enclosing row loop, so don't defer.
        return rows.Select(r => r.Length > 0 ? r[0] : null).ToList();
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

                // Flatten the projection, expanding a qualified star (Table.*) into the input columns of
                // that source (passed through by index); every other item is an evaluated expression.
                var plan = new List<(OutputColumn Column, int InputIndex, Expression? Expr)>();
                foreach (SelectItem item in project.Projection)
                {
                    if (item.Value is QualifiedStarExpression star)
                    {
                        for (int ci = 0; ci < columns.Count; ci++)
                            if (string.Equals(columns[ci].Qualifier, star.Table, StringComparison.OrdinalIgnoreCase))
                                plan.Add((columns[ci], ci, null));
                    }
                    else
                    {
                        string name = item.Alias ?? (item.Value is ColumnReference c ? c.Column : $"Expr{plan.Count + 1}");
                        plan.Add((new OutputColumn(null, name), -1, item.Value));
                    }
                }

                var projected = rows.Select(row =>
                {
                    var eval = Eval(columns, row, outer);
                    return plan.Select(p => p.InputIndex >= 0 ? row[p.InputIndex] : eval.Evaluate(p.Expr!)).ToArray();
                });

                return (plan.Select(p => p.Column).ToList(), projected);
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
                // The count is literal/parameter/arithmetic (no column refs), so an empty row scope suffices.
                object? countValue = new ExpressionEvaluator(new EvalScope([], [], outer), this, parameters: _parameters, session: _session)
                    .Evaluate(limit.Count);
                int count = Convert.ToInt32(countValue, System.Globalization.CultureInfo.InvariantCulture);
                return (columns, rows.Take(count));
            }

            case DistinctNode distinct:
            {
                var (columns, rows) = Execute(distinct.Input, outer);
                return (columns, Distinct(rows));
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
        new(new EvalScope(columns, row, outer), this, parameters: _parameters, session: _session);

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteAggregate(AggregateNode node, EvalScope? outer)
    {
        var (inColumns, inRowsEnum) = Execute(node.Input, outer);
        var inRows = inRowsEnum.ToList();
        // Aggregates can appear in the projection, HAVING (e.g. HAVING COUNT(*) > 30) and ORDER BY
        // (e.g. ORDER BY COUNT(*)); precompute all of them per group so each instance resolves.
        var aggregateCalls = node.Projection.SelectMany(i => Aggregates(i.Value))
            .Concat(node.Having is { } h ? Aggregates(h) : [])
            .Concat(node.OrderBy.SelectMany(k => Aggregates(k.Value)))
            .ToList();

        var outColumns = node.Projection
            .Select((item, i) => new OutputColumn(null, item.Alias ?? (item.Value is ColumnReference c ? c.Column : $"Expr{i + 1}")))
            .ToList();

        // Each output row carries its ORDER BY key values, evaluated in the same group scope as the
        // projection (so a key like a grouping expression resolves), to sort the groups afterward.
        var outRows = new List<(object?[] Row, object?[] SortKeys)>();
        foreach (List<object?[]> group in GroupRows(inRows, node.GroupBy, inColumns, outer))
        {
            var values = new Dictionary<FunctionCall, object?>(ReferenceComparer.Instance);
            foreach (FunctionCall call in aggregateCalls)
            {
                // An aggregate collected from a nested subquery may belong to that subquery (its argument
                // references the subquery's own columns, not this group's) — it can't be computed here, so
                // skip it; the subquery computes it itself. A genuine outer aggregate resolves fine.
                try { values[call] = ComputeAggregate(call, group, inColumns, outer); }
                catch (InvalidOperationException) { }
            }

            // Within a group every key value is constant, so the first row resolves group keys; aggregate
            // calls resolve from the precomputed map (threaded via the scope so correlated subqueries can
            // reach an outer aggregate). An empty group only happens for an aggregate with no GROUP BY over
            // zero rows (e.g. COUNT(*) -> 0); there are no key columns to resolve, so a null row suffices.
            object?[] keyRow = group.Count > 0 ? group[0] : new object?[inColumns.Count];
            var eval = new ExpressionEvaluator(new EvalScope(inColumns, keyRow, outer, values), this, _parameters, _session);

            // HAVING filters whole groups after aggregation.
            if (node.Having is not null && !eval.IsTrue(node.Having))
                continue;

            object?[] row = node.Projection.Select(item => eval.Evaluate(item.Value)).ToArray();
            object?[] sortKeys = node.OrderBy.Select(k => eval.Evaluate(k.Value)).ToArray();
            outRows.Add((row, sortKeys));
        }

        if (node.OrderBy.Count > 0)
            outRows.Sort((a, b) =>
            {
                for (int i = 0; i < node.OrderBy.Count; i++)
                {
                    int c = ExpressionEvaluator.CompareForSort(a.SortKeys[i], b.SortKeys[i]);
                    if (node.OrderBy[i].Direction == SortDirection.Descending) c = -c;
                    if (c != 0) return c;
                }
                return 0;
            });

        return (outColumns, outRows.Select(x => x.Row));
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

        // COUNT is an Access Long Integer (32-bit) — EF reads it with GetInt32, so return int, not long.
        if (name == "COUNT")
            return arg is StarExpression or null
                ? group.Count
                : group.Count(r => Eval(columns, r, outer).Evaluate(arg) is not null);

        var values = group.Select(r => Eval(columns, r, outer).Evaluate(arg!)).Where(v => v is not null).ToList();
        if (values.Count == 0)
            return null; // SUM/AVG/MIN/MAX of nothing is NULL (COUNT already returned above)

        // Result types: SUM **preserves the input type** (int→int, long→long, decimal→decimal, …) so the EF
        // provider (which emits a bare SUM and reads by the LINQ operand type) round-trips without a cast.
        // AVG is Double unless the input is Currency/Decimal (matches Access and LINQ). MIN/MAX keep the
        // column's own value and type.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return name switch
        {
            "SUM" => SumPreservingType(values, inv),
            "AVG" => values[0] is decimal ? values.Average(v => Convert.ToDecimal(v, inv)) : values.Average(v => Convert.ToDouble(v, inv)),
            "MIN" => values.Aggregate((a, b) => ExpressionEvaluator.CompareForSort(a, b) <= 0 ? a : b),
            "MAX" => values.Aggregate((a, b) => ExpressionEvaluator.CompareForSort(a, b) >= 0 ? a : b),
            // Statistical aggregates. Sample forms (StDev/Var) divide by n-1 and are NULL for a single value;
            // population forms (StDevP/VarP) divide by n. Verified vs ACE.
            "VAR" or "STDEV" or "STDDEV" or "VARP" or "STDEVP" or "STDDEVP" => Statistic(name, values, inv),
            _ => throw new NotSupportedException($"Aggregate {call.Name} is not supported."),
        };
    }

    /// <summary>Access statistical aggregates over the non-null values (verified vs ACE). VAR/STDEV are the
    /// **sample** forms (divide by n−1, NULL for a single value); VARP/STDEVP the **population** forms (divide by
    /// n). STDEV/STDEVP are the square roots of VAR/VARP.</summary>
    private static object? Statistic(string name, List<object?> values, System.Globalization.CultureInfo inv)
    {
        int n = values.Count;
        double mean = values.Average(v => Convert.ToDouble(v, inv));
        double sumSq = values.Sum(v => { double d = Convert.ToDouble(v, inv) - mean; return d * d; });
        bool sample = !name.EndsWith("P", StringComparison.Ordinal);   // VAR/STDEV sample; VARP/STDEVP population
        if (sample && n < 2) return null;                              // sample variance of one value is undefined
        double variance = sumSq / (sample ? n - 1 : n);
        bool stdev = name.Contains("DEV", StringComparison.Ordinal);
        return stdev ? Math.Sqrt(variance) : variance;
    }

    /// <summary>SUM keeping the operand's numeric type (as LINQ's <c>Sum</c> overloads do): integer types
    /// (byte/short/int) sum to Int32, Int64 to Int64, Single to Single, Double to Double, Decimal/Currency
    /// to Decimal.</summary>
    private static object SumPreservingType(List<object?> values, System.Globalization.CultureInfo inv) =>
        values[0] switch
        {
            decimal => values.Sum(v => Convert.ToDecimal(v, inv)),
            double => values.Sum(v => Convert.ToDouble(v, inv)),
            float => (float)values.Sum(v => Convert.ToDouble(v, inv)),
            long or ulong => values.Sum(v => Convert.ToInt64(v, inv)),
            _ => values.Sum(v => Convert.ToInt32(v, inv)),
        };

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
            // Descend into subqueries: an aggregate over an *outer* column may appear there (a correlated
            // subquery). Its own aggregates come along too, but are skipped when they can't be computed in
            // this group's scope.
            case ScalarSubquery s:
                foreach (FunctionCall a in AggregatesInSelect(s.Query)) yield return a;
                break;
            case ExistsExpression x:
                foreach (FunctionCall a in AggregatesInSelect(x.Query)) yield return a;
                break;
            case InSubqueryExpression i:
                foreach (FunctionCall a in Aggregates(i.Value).Concat(AggregatesInSelect(i.Query))) yield return a;
                break;
        }
    }

    /// <summary>All aggregate calls anywhere in a subquery's clauses (projection, WHERE, HAVING, GROUP BY,
    /// ORDER BY) — used to surface outer aggregates that a correlated subquery references.</summary>
    private static IEnumerable<FunctionCall> AggregatesInSelect(SelectStatement s) =>
        s.Projection.SelectMany(i => Aggregates(i.Value))
            .Concat(s.Where is { } w ? Aggregates(w) : [])
            .Concat(s.Having is { } h ? Aggregates(h) : [])
            .Concat(s.GroupBy.SelectMany(Aggregates))
            .Concat(s.OrderBy.SelectMany(o => Aggregates(o.Value)));

    /// <summary>Groups by structural equality of the key value tuple.</summary>
    // DISTINCT / GROUP BY / INTERSECT / EXCEPT key. String keys use Access text semantics — case-insensitive
    // and trailing-space-insensitive — so 'London' and 'LONDON ' group together as Access does.
    private sealed class GroupKey(object?[] values) : IEquatable<GroupKey>
    {
        private readonly object?[] _values = values;

        public bool Equals(GroupKey? other) =>
            other is not null && _values.Length == other._values.Length
            && _values.Zip(other._values).All(p => KeyEquals(p.First, p.Second));

        public override bool Equals(object? obj) => Equals(obj as GroupKey);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (object? v in _values)
                hash.Add(v is string s ? StringComparer.InvariantCultureIgnoreCase.GetHashCode(s.TrimEnd(' ')) : v?.GetHashCode() ?? 0);
            return hash.ToHashCode();
        }

        private static bool KeyEquals(object? a, object? b) =>
            a is string sa && b is string sb
                ? string.Equals(sa.TrimEnd(' '), sb.TrimEnd(' '), StringComparison.InvariantCultureIgnoreCase)
                : Equals(a, b);
    }

    private sealed class ReferenceComparer : IEqualityComparer<FunctionCall>
    {
        public static readonly ReferenceComparer Instance = new();
        public bool Equals(FunctionCall? x, FunctionCall? y) => ReferenceEquals(x, y);
        public int GetHashCode(FunctionCall obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
