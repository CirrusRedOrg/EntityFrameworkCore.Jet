using LibRed.Engine.Plan;
using LibRed.Engine.Planning;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>A column produced by a plan node: an optional table-alias qualifier and a name.</summary>
internal readonly record struct OutputColumn(string? Qualifier, string Name, Type? ClrType = null);

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

    // Optimised plans for subqueries, keyed by their AST node (reference identity). A correlated subquery is
    // executed once per outer row, so planning + index selection must be done ONCE, not on every evaluation.
    private readonly Dictionary<SelectStatement, PlanNode> _subqueryPlans = new(ReferenceEqualityComparer.Instance);

    // Flattened projection schema (output columns + per-item source), keyed by the ProjectNode. Depends only on
    // the node and its input column structure — both invariant across outer rows — so, like the subquery plans,
    // it must be built ONCE. Rebuilding it per row re-ran DeclaredType (linear column scans, string allocation)
    // for every outer row of a correlated subquery / nested-loop inner.
    private readonly Dictionary<ProjectNode, ProjectionSchema> _projectionSchemas = new(ReferenceEqualityComparer.Instance);

    // Decorrelated EXISTS subqueries, keyed by AST node. A present-but-null value records "analysed, not
    // decorrelatable", so an unsound-to-rewrite subquery isn't re-analysed on every outer row.
    private readonly Dictionary<SelectStatement, ExistsSemiJoin?> _semiJoins = new(ReferenceEqualityComparer.Instance);

    // The same for `x IN (subquery)`, kept separate because the plan there carries the IN value as an extra key
    // column — the same SelectStatement node reached as an EXISTS body would need a different one.
    private readonly Dictionary<SelectStatement, ExistsSemiJoin?> _inSemiJoins = new(ReferenceEqualityComparer.Instance);

    // And for a correlated scalar aggregate, which maps each key to one value rather than testing membership.
    private readonly Dictionary<SelectStatement, ScalarAggregateSemiJoin?> _scalarSemiJoins = new(ReferenceEqualityComparer.Instance);

    // Results of subqueries that turned out not to depend on the outer row: same answer every time, so they are
    // evaluated once per statement. Keyed by AST node; the boxed value may legitimately be null (SQL NULL), hence
    // separate dictionaries rather than a null-means-absent convention.
    private readonly Dictionary<SelectStatement, object?> _hoistedScalar = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SelectStatement, List<object?>> _hoistedColumn = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SelectStatement, bool> _hoistedExists = new(ReferenceEqualityComparer.Instance);

    // Subqueries proven to depend on the outer row. Recorded so a correlated subquery pays ONE failed hoist
    // attempt per statement rather than one per row.
    private readonly HashSet<SelectStatement> _correlated = new(ReferenceEqualityComparer.Instance);

    // Per-row time spent on each decorrelatable subquery, which is what decides when to switch over. See
    // DecorrelationGate: the rewrite is sound from the first probe but not always cheaper, and the outer row count
    // that would settle it isn't known until the outer scan has finished.
    private readonly Dictionary<SelectStatement, DecorrelationGate> _gates = new(ReferenceEqualityComparer.Instance);

    private DecorrelationGate Gate(SelectStatement query)
        => _gates.TryGetValue(query, out DecorrelationGate? gate) ? gate : _gates[query] = new DecorrelationGate();

    public QueryExecutor(JetDatabase database, IReadOnlyDictionary<string, object?>? parameters = null, SessionState? session = null)
    {
        _database = database;
        _parameters = new ParameterBag(parameters);
        _session = session;
    }

    public ResultSet ExecuteQuery(PlanNode plan)
    {
        var (columns, rows) = Execute(plan, null);
        return new ResultSet(
            columns.Select(c => c.Name).ToList(),
            rows,
            columns.Select(c => c.ClrType ?? typeof(object)).ToList());
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
        return new ResultSet(names, [row], statement.Projection
            .Select(item => DeclaredType(item.Value, []) ?? typeof(object)).ToList());
    }

    object? IScalarSubqueryRunner.ExecuteScalar(SelectStatement query, EvalScope outerScope)
    {
        if (_hoistedScalar.TryGetValue(query, out object? hoisted))
            return hoisted;

        if (TryHoist(query, outerScope, ScalarOf, out object? once))
            return _hoistedScalar[query] = once;

        // A correlated aggregate is one grouped pass over the body rather than one aggregate per outer row.
        if (!_scalarSemiJoins.TryGetValue(query, out ScalarAggregateSemiJoin? semi))
        {
            _scalarSemiJoins[query] = semi = ScalarAggregateSemiJoin.TryBuild(
                query, outerScope.AllColumns(), outerScope.VisibleAliases().ToHashSet(StringComparer.OrdinalIgnoreCase),
                _database.Catalog);
        }

        if (semi is null)
            return ScalarOf(outerScope);

        DecorrelationGate gate = Gate(query);
        if (gate.Ready)
            return semi.Evaluate(this, new ExpressionEvaluator(outerScope, this, _parameters, _session));

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        object? perRow = ScalarOf(outerScope);
        gate.Charge(started);
        return perRow;

        object? ScalarOf(EvalScope? scope)
        {
            var (_, rows) = Execute(SubqueryPlan(query, outerScope), scope);
            foreach (object?[] row in rows)
                return row.Length > 0 ? row[0] : null;
            return null; // no rows → NULL
        }
    }

    bool IScalarSubqueryRunner.ExecuteExists(SelectStatement query, EvalScope outerScope)
    {
        // An EXISTS that doesn't depend on the outer row at all has one answer for the whole statement.
        if (_hoistedExists.TryGetValue(query, out bool hoisted))
            return hoisted;

        if (TryHoist(query, outerScope, ExistsOf, out bool once))
            return _hoistedExists[query] = once;

        // A correlated EXISTS runs once per outer row, so if it can be turned into a hash semi-join the body is
        // executed once for the whole statement instead of once per row. See ExistsSemiJoin for the measurements.
        if (!_semiJoins.TryGetValue(query, out ExistsSemiJoin? semi))
        {
            _semiJoins[query] = semi = ExistsSemiJoin.TryBuild(
                query, outerScope.AllColumns(), outerScope.VisibleAliases().ToHashSet(StringComparer.OrdinalIgnoreCase),
                _database.Catalog);
        }

        if (semi is null)
        {
            return ExistsOf(outerScope);
        }

        DecorrelationGate gate = Gate(query);
        if (gate.Ready)
        {
            return semi.Matches(this, new ExpressionEvaluator(outerScope, this, _parameters, _session));
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool perRow = ExistsOf(outerScope);
        gate.Charge(started);
        return perRow;

        bool ExistsOf(EvalScope? scope)
        {
            var (_, r) = Execute(SubqueryPlan(query, outerScope), scope);
            return r.Any();
        }
    }

    /// <summary>
    ///     Tries to evaluate a subquery once for the whole statement, which is valid exactly when its result does
    ///     not depend on the outer row. See <see cref="SubqueryHoisting" /> for why this takes two checks: a
    ///     static one for qualified outer references (which a conditional could hide from any single evaluation),
    ///     and this trial run with <b>no outer scope</b>, which settles unqualified references by letting the
    ///     evaluator's own resolver fail on anything that would bind outward.
    /// </summary>
    /// <remarks>
    ///     Catching broadly is safe in the harmless direction: a body that throws for an unrelated reason is
    ///     recorded as correlated and re-run per row, which raises the same error the caller would have seen
    ///     anyway. A subquery is a SELECT, so the abandoned attempt has no side effects.
    /// </remarks>
    private bool TryHoist<T>(SelectStatement query, EvalScope outerScope, Func<EvalScope?, T> run, out T result)
    {
        result = default!;
        if (_correlated.Contains(query)
            || SubqueryHoisting.MayReferenceOuter(query, outerScope.VisibleAliases().ToHashSet(StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        try
        {
            result = run(null);
            return true;
        }
        catch (Exception)
        {
            _correlated.Add(query);
            return false;
        }
    }

    /// <summary>
    ///     Runs a decorrelated subquery body once and hashes the values it correlates on. Tuples containing a null
    ///     are dropped: a null can never satisfy an equi-predicate, exactly as the hash join's build phase does.
    /// </summary>
    /// <param name="trackNullTail">
    ///     For <c>IN</c>, whose last key column is the subquery's own output rather than a correlation. A null
    ///     there is not "no match" but SQL's UNKNOWN, so instead of dropping the row its correlation prefix goes
    ///     to <c>NullTailKeys</c> — that is how the caller learns the column held a null for a given outer row.
    ///     A null in the correlation prefix still drops the row from both sets.
    /// </param>
    internal (HashSet<object?[]> Keys, HashSet<object?[]> NullTailKeys) BuildSemiJoinKeys(
        SelectStatement keyQuery, int keyWidth, bool trackNullTail = false)
    {
        var keys = new HashSet<object?[]>(HashKeyComparer.Instance);
        var nullTail = new HashSet<object?[]>(HashKeyComparer.Instance);
        var (_, rows) = Execute(SubqueryPlan(keyQuery, new EvalScope([], [], null)), null);
        foreach (object?[] row in rows)
        {
            if (row.Length < keyWidth)
            {
                continue;
            }

            // The correlation prefix — every column but the IN value, which is the whole key for EXISTS.
            int prefix = trackNullTail ? keyWidth - 1 : keyWidth;
            var key = new object?[keyWidth];
            var usable = true;
            for (var i = 0; i < prefix && usable; i++)
            {
                usable = (key[i] = row[i]) is not null;
            }

            if (!usable)
            {
                continue;
            }

            if (!trackNullTail)
            {
                keys.Add(key);
            }
            else if ((key[prefix] = row[prefix]) is null)
            {
                nullTail.Add(key[..prefix]);
            }
            else
            {
                keys.Add(key);
            }
        }

        return (keys, nullTail);
    }

    /// <summary>
    ///     Runs a decorrelated aggregate body grouped by its correlation columns, mapping each key tuple to the one
    ///     value that group aggregated to. Key tuples containing a null are dropped, as in
    ///     <see cref="BuildSemiJoinKeys" />; the aggregate itself may legitimately be null (<c>SUM</c> of nulls).
    /// </summary>
    internal Dictionary<object?[], object?> BuildGroupedAggregate(SelectStatement keyQuery, int keyWidth)
    {
        var values = new Dictionary<object?[], object?>(HashKeyComparer.Instance);
        var (_, rows) = Execute(SubqueryPlan(keyQuery, new EvalScope([], [], null)), null);
        foreach (object?[] row in rows)
        {
            if (row.Length <= keyWidth)
            {
                continue;
            }

            var key = new object?[keyWidth];
            var usable = true;
            for (var i = 0; i < keyWidth && usable; i++)
            {
                usable = (key[i] = row[i]) is not null;
            }

            if (usable)
            {
                // Grouping is by exactly these columns, so a key cannot repeat; indexing rather than Add would
                // hide it if that ever stopped holding, so let a duplicate throw.
                values.Add(key, row[keyWidth]);
            }
        }

        return values;
    }

    /// <summary>
    ///     What an aggregate call evaluates to over an empty group — <c>COUNT</c> is 0 where <c>SUM</c>/<c>MIN</c>
    ///     are null. This is what a correlated aggregate returns for an outer row with no matching inner rows, and
    ///     it comes from the same computation the per-row path uses so the two cannot disagree.
    /// </summary>
    internal object? EmptyGroupAggregate(FunctionCall call)
    {
        try
        {
            return ComputeAggregate(call, [], [], null);
        }
        catch (InvalidOperationException)
        {
            return null; // As in ExecuteAggregate: a call that can't be computed here isn't ours to compute.
        }
    }

    (bool Found, bool HasNull)? IScalarSubqueryRunner.ExecuteInSubquery(
        SelectStatement query, Expression value, object? evaluated, EvalScope outerScope)
    {
        // An uncorrelated IN needs nothing from here: ExecuteColumn hoists it, so the body already runs once and
        // the caller's loop walks a cached list. TryBuildForIn declines it too (there are no correlation keys).
        if (!_inSemiJoins.TryGetValue(query, out ExistsSemiJoin? semi))
        {
            _inSemiJoins[query] = semi = ExistsSemiJoin.TryBuildForIn(
                query, value, outerScope.AllColumns(),
                outerScope.VisibleAliases().ToHashSet(StringComparer.OrdinalIgnoreCase), _database.Catalog);
        }

        // Not ready yet: decline, and the caller's loop runs the body through ExecuteColumn, which charges the gate.
        return semi is not null && Gate(query).Ready
            ? semi.ContainsValue(this, new ExpressionEvaluator(outerScope, this, _parameters, _session), evaluated)
            : null;
    }

    IEnumerable<object?> IScalarSubqueryRunner.ExecuteColumn(SelectStatement query, EvalScope outerScope)
    {
        if (_hoistedColumn.TryGetValue(query, out List<object?>? hoisted))
            return hoisted;

        if (TryHoist(query, outerScope, ColumnOf, out List<object?>? once))
            return _hoistedColumn[query] = once!;

        // This is the per-row cost of an IN, so it is what the gate measures — the membership comparison the caller
        // then does over the returned values is negligible beside running the body.
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        List<object?> values = ColumnOf(outerScope);
        Gate(query).Charge(started);
        return values;

        List<object?> ColumnOf(EvalScope? scope)
        {
            var (_, rows) = Execute(SubqueryPlan(query, outerScope), scope);
            // Materialize: the outer scope is reused across the enclosing row loop, so don't defer.
            return rows.Select(r => r.Length > 0 ? r[0] : null).ToList();
        }
    }

    /// <summary>The optimised plan for a subquery, planned once and cached: index selection (index seeks, hash
    /// joins) is applied just like a top-level query, and the outer query's aliases are passed so a correlated
    /// predicate (<c>inner.col = outer.col</c>) becomes an index seek keyed off the outer row — a correlated
    /// subquery runs per outer row, so an unoptimised plan re-run thousands of times is what made correlated
    /// EXISTS/scalar pathological.</summary>
    private PlanNode SubqueryPlan(SelectStatement query, EvalScope outerScope)
    {
        if (!_subqueryPlans.TryGetValue(query, out PlanNode? plan))
        {
            var outerAliases = outerScope.VisibleAliases().ToHashSet(StringComparer.OrdinalIgnoreCase);
            _subqueryPlans[query] = plan =
                Planning.IndexSelection.Apply(QueryPlanner.PlanSelect(query), _database.Catalog, outerAliases);
        }
        return plan;
    }

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) Execute(PlanNode node, EvalScope? outer)
    {
        switch (node)
        {
            case SingleRowNode:
                // FROM-less SELECT: one row, no columns — the projection above evaluates its constants once.
                return ([], [new object?[0]]);

            case ScanNode scan when Schema.InformationSchema.IsInformationSchema(scan.Table):
            {
                // Virtual INFORMATION_SCHEMA.<view> table: materialise rows from the catalog.
                string alias = scan.Alias ?? scan.Table;
                var columns = Schema.InformationSchema.ColumnsOf(scan.Table)
                    .Zip(Schema.InformationSchema.ColumnTypesOf(scan.Table),
                        (name, type) => new OutputColumn(alias, name, type)).ToList();
                return (columns, Schema.InformationSchema.Rows(scan.Table, _database.Catalog));
            }

            case ScanNode scan:
            {
                var table = _database.OpenTable(scan.Table);
                string alias = scan.Alias ?? scan.Table;
                var columns = table.Definition.Columns
                    .Select(c => new OutputColumn(alias, c.Name, Schema.JetClrTypeMap.ToClrType(c.Type))).ToList();
                return (columns, table.Rows());
            }

            case IndexSeekNode seek:
            {
                var table = _database.OpenTable(seek.Table);
                string alias = seek.Alias ?? seek.Table;
                var columns = table.Definition.Columns
                    .Select(c => new OutputColumn(alias, c.Name, Schema.JetClrTypeMap.ToClrType(c.Type))).ToList();

                // Evaluate the key(s) in the outer scope (so an index-nested-loop join can key off the outer
                // row); a single-table seek's key is a constant/parameter.
                var evaluator = new ExpressionEvaluator(new EvalScope([], [], outer), this, parameters: _parameters, session: _session);
                var keyValues = new object?[table.Definition.Columns.Count];
                for (int i = 0; i < seek.Keys.Count; i++)
                    keyValues[seek.Index.Columns[i].Column.Index] = evaluator.Evaluate(seek.Keys[i]);

                return (columns, table.SeekRows(seek.Index, keyValues));
            }

            case IndexRangeSeekNode range:
            {
                var table = _database.OpenTable(range.Table);
                string alias = range.Alias ?? range.Table;
                var columns = table.Definition.Columns
                    .Select(c => new OutputColumn(alias, c.Name, Schema.JetClrTypeMap.ToClrType(c.Type))).ToList();

                var evaluator = new ExpressionEvaluator(new EvalScope([], [], outer), this, parameters: _parameters, session: _session);
                int col = range.Index.Columns[0].Column.Index;
                object?[]? Bound(Expression? e)
                {
                    if (e is null) return null;
                    var v = new object?[table.Definition.Columns.Count];
                    v[col] = evaluator.Evaluate(e);
                    return v;
                }
                return (columns, table.SeekRangeRows(range.Index, Bound(range.Low), Bound(range.High)));
            }

            case DerivedTableNode derived:
            {
                var (inner, rows) = Execute(derived.Input, outer);
                var columns = inner.Select(c => new OutputColumn(derived.Alias, c.Name, c.ClrType)).ToList();
                return (columns, rows);
            }

            case FilterNode filter:
            {
                var (columns, rows) = Execute(filter.Input, outer);
                return (columns, rows.Where(row => Eval(columns, row, outer).IsTrue(filter.Predicate)));
            }

            case JoinNode join:
                return ExecuteJoin(join, outer);

            case HashJoinNode hashJoin:
                return ExecuteHashJoin(hashJoin, outer);

            case AggregateNode aggregate:
                return ExecuteAggregate(aggregate, outer);

            case SortNode sort:
            {
                var (columns, rows) = Execute(sort.Input, outer);
                // As in LimitNode: the count is literal/parameter/arithmetic, so an empty row scope suffices.
                int? bound = sort.Limit is { } lim
                    ? Convert.ToInt32(Eval([], [], outer).Evaluate(lim), System.Globalization.CultureInfo.InvariantCulture)
                    : null;
                return (columns, SortRows(sort.Keys, columns, outer, rows, bound));
            }

            case ProjectNode project:
            {
                var (columns, rows) = Execute(project.Input, outer);

                // The output schema is invariant across outer rows, so build (or reuse) it once. Rows are still
                // produced fresh — only the per-item plan (which ran DeclaredType) is cached.
                ProjectionSchema schema = ProjectionSchemaFor(project, columns);
                var plan = schema.Plan;

                var projected = rows.Select(row =>
                {
                    var eval = Eval(columns, row, outer);
                    return plan.Select(p => p.InputIndex >= 0 ? row[p.InputIndex] : eval.Evaluate(p.Expr!)).ToArray();
                });

                return (schema.Columns, projected);
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
                int n = Convert.ToInt32(countValue, System.Globalization.CultureInfo.InvariantCulture);

                // Nothing can be returned, so don't read the input at all. This matters for the PERCENT branch
                // below, which materialises its whole input before it can compute the take — so `TOP 0 PERCENT`
                // otherwise buffers every row only to discard all of them, once per outer row when it sits inside
                // a correlated subquery. (Plain `TOP 0` was already cheap: Take(0) never pulls from the source.)
                if (n <= 0)
                    return (columns, []);

                if (!limit.Percent)
                    return (columns, rows.Take(n));

                // TOP n PERCENT: ceil(rowCount × n / 100), verified vs ACE (10% of 9 → 1, 25% of 9 → 3,
                // 1% of 830 → 9). Materialize to count; integer ceil-division avoids float rounding.
                var buffered = rows.ToList();
                int take = (int)(((long)buffered.Count * n + 99) / 100);
                return (columns, buffered.Take(take));
            }

            case DistinctNode distinct:
            {
                var (columns, rows) = Execute(distinct.Input, outer);
                return (columns, Distinct(rows));
            }

            case DistinctRowNode distinctRow:
            {
                var (columns, rows) = Execute(distinctRow.Input, outer);

                // Which source tables (qualifiers) contribute output columns, and which exist at all.
                var contributing = ContributingQualifiers(distinctRow.Projection, columns);
                var all = columns.Select(c => c.Qualifier)
                    .Where(q => q is not null).Select(q => q!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Access ignores DISTINCTROW when there is a single source table, when every table
                // contributes output, or (our guard) when nothing does — leaving the rows untouched.
                if (all.Count <= 1 || contributing.Count == 0 || all.All(contributing.Contains))
                    return (columns, rows);

                // Otherwise dedupe on the full set of columns belonging to the contributing tables.
                int[] keyIndexes = Enumerable.Range(0, columns.Count)
                    .Where(i => columns[i].Qualifier is { } q && contributing.Contains(q))
                    .ToArray();
                return (columns, DistinctByIndexes(rows, keyIndexes));
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

    /// <summary>Yields the first row for each distinct combination of the values at <paramref name="indexes"/>
    /// (the columns of the DISTINCTROW contributing tables), preserving order.</summary>
    private static IEnumerable<object?[]> DistinctByIndexes(IEnumerable<object?[]> rows, int[] indexes)
    {
        var seen = new HashSet<GroupKey>();
        foreach (object?[] row in rows)
        {
            var key = new object?[indexes.Length];
            for (int i = 0; i < indexes.Length; i++) key[i] = row[indexes[i]];
            if (seen.Add(new GroupKey(key)))
                yield return row;
        }
    }

    /// <summary>Infers a stable declared CLR type from schema and expression shape. It deliberately returns
    /// null for expressions whose result type depends on runtime coercion; the ADO layer can still fall back
    /// to a non-null runtime value in those cases without publishing misleading metadata for empty results.</summary>
    /// <summary>A ProjectNode's flattened output: the per-item plan (source input index, or an expression to
    /// evaluate) and the resulting output columns. Structural — the same for every outer row.</summary>
    private sealed record ProjectionSchema(
        List<(OutputColumn Column, int InputIndex, Expression? Expr)> Plan, List<OutputColumn> Columns);

    /// <summary>Builds — or reuses — a ProjectNode's schema. Flattens the projection, expanding a qualified star
    /// (Table.*) into the input columns of that source (passed through by index); every other item is an
    /// evaluated expression whose declared type is derived once here.</summary>
    private ProjectionSchema ProjectionSchemaFor(ProjectNode project, IReadOnlyList<OutputColumn> columns)
    {
        if (_projectionSchemas.TryGetValue(project, out ProjectionSchema? cached))
            return cached;

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
                plan.Add((new OutputColumn(null, name, DeclaredType(item.Value, columns)), -1, item.Value));
            }
        }

        var schema = new ProjectionSchema(plan, plan.Select(p => p.Column).ToList());
        _projectionSchemas[project] = schema;
        return schema;
    }

    private Type? DeclaredType(Expression expression, IReadOnlyList<OutputColumn> columns)
    {
        switch (expression)
        {
            case LiteralExpression { Value: { } value }:
                return value.GetType();
            case LiteralExpression:
                return null;
            case ColumnReference column:
                return DeclaredColumnType(column, columns)
                    ?? (column.Table is null && column.Column.Equals("Now", StringComparison.OrdinalIgnoreCase)
                        ? typeof(DateTime) : null);
            case SystemVariableExpression variable:
                return variable.Name.Equals("ROWCOUNT", StringComparison.OrdinalIgnoreCase)
                    ? typeof(int) : _session?.LastIdentity?.GetType();
            case ExistsExpression or InSubqueryExpression or InListExpression:
                return typeof(bool);
            case UnaryExpression unary:
                return unary.Operator is UnaryOperator.Not or UnaryOperator.IsNull or UnaryOperator.IsNotNull
                    ? typeof(bool) : DeclaredType(unary.Operand, columns);
            case BinaryExpression binary:
                if (binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual
                    or BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual
                    or BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual
                    or BinaryOperator.And or BinaryOperator.Or or BinaryOperator.Like or BinaryOperator.In)
                    return typeof(bool);
                if (binary.Operator == BinaryOperator.Concat)
                    return typeof(string);
                Type? left = DeclaredType(binary.Left, columns);
                Type? right = DeclaredType(binary.Right, columns);
                return DeclaredBinaryType(binary.Operator, left, right);
            case FunctionCall function:
                return DeclaredFunctionType(function, columns);
            default:
                return null;
        }
    }

    private static Type? DeclaredColumnType(ColumnReference reference, IReadOnlyList<OutputColumn> columns)
    {
        Type? result = null;
        bool found = false;
        foreach (OutputColumn column in columns)
        {
            if (!string.Equals(column.Name, reference.Column, StringComparison.OrdinalIgnoreCase)
                || reference.Table is not null
                && !string.Equals(column.Qualifier, reference.Table, StringComparison.OrdinalIgnoreCase))
                continue;

            if (found) return null; // execution will report the ambiguous reference
            found = true;
            result = column.ClrType;
        }
        return result;
    }

    private Type? DeclaredFunctionType(FunctionCall function, IReadOnlyList<OutputColumn> columns)
    {
        string name = function.Name.TrimEnd('$').ToUpperInvariant();
        Type? argument = function.Arguments.Count > 0 ? DeclaredType(function.Arguments[0], columns) : null;
        return name switch
        {
            "COUNT" => typeof(int),
            "SUM" or "MIN" or "MAX" or "FIRST" or "LAST" => argument,
            "AVG" => argument == typeof(decimal) ? typeof(decimal) : typeof(double),
            "CBOOL" or "ISDATE" => typeof(bool),
            "CBYTE" => typeof(byte),
            "CINT" => typeof(short),
            "CLNG" => typeof(int),
            "CSNG" => typeof(float),
            "CDBL" => typeof(double),
            "CDEC" or "CCUR" => typeof(decimal),
            "CSTR" or "FORMAT" or "LCASE" or "UCASE" or "TRIM" or "LTRIM" or "RTRIM"
                or "LEFT" or "RIGHT" or "MID" or "REPLACE" or "STRING" or "SPACE" or "HEX"
                or "OCT" or "WEEKDAYNAME" or "MONTHNAME" or "PARTITION" => typeof(string),
            "LEN" or "INSTR" or "INSTRREV" or "ASC" or "ASCW" or "DATEPART" or "DATEDIFF"
                or "YEAR" or "MONTH" or "DAY" or "HOUR" or "MINUTE" or "SECOND" or "WEEKDAY" => typeof(int),
            "CDATE" or "NOW" or "DATE" or "TIME" or "DATEADD" or "DATESERIAL" or "TIMESERIAL"
                or "DATEVALUE" or "TIMEVALUE" => typeof(DateTime),
            "SQR" or "SIN" or "COS" or "TAN" or "ATN" or "LOG" or "EXP" or "RND"
                or "PMT" or "FV" or "PV" or "NPER" or "IPMT" or "PPMT" or "DDB" or "RATE" => typeof(double),
            "IIF" when function.Arguments.Count == 3 => SameType(
                DeclaredType(function.Arguments[1], columns), DeclaredType(function.Arguments[2], columns)),
            _ => null,
        };
    }

    private static Type? SameType(Type? left, Type? right) => left == right ? left : null;

    private static Type? DeclaredBinaryType(BinaryOperator op, Type? left, Type? right)
    {
        if (left is null || right is null)
            return null;

        if (op == BinaryOperator.Add && (left == typeof(string) || right == typeof(string)))
            return typeof(string);
        if (op == BinaryOperator.Power)
            return typeof(double);
        if (op == BinaryOperator.Divide)
            return left == typeof(decimal) || right == typeof(decimal) ? typeof(decimal) : typeof(double);
        if (op is BinaryOperator.Modulo or BinaryOperator.IntDivide
            or BinaryOperator.BitAnd or BinaryOperator.BitOr or BinaryOperator.BitXor)
            return IsInt64(left) || IsInt64(right) ? typeof(long) : typeof(int);

        if (op is not (BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply))
            return null;

        // Keep this in lock-step with ExpressionEvaluator.Arithmetic. Date +/- number produces a date,
        // date-date subtraction produces a day count, while date multiplication is numeric.
        if (left == typeof(DateTime) || right == typeof(DateTime))
        {
            if (op == BinaryOperator.Subtract && left == typeof(DateTime) && right == typeof(DateTime))
                return typeof(double);
            return op is BinaryOperator.Add or BinaryOperator.Subtract ? typeof(DateTime) : typeof(double);
        }
        if (left == typeof(decimal) || right == typeof(decimal)) return typeof(decimal);
        if (left == typeof(double) || right == typeof(double)) return typeof(double);
        if (left == typeof(float) || right == typeof(float)) return typeof(float);
        if (IsInt64(left) || IsInt64(right)) return typeof(long);
        return typeof(int);
    }

    private static bool IsInt64(Type type) => type == typeof(long) || type == typeof(ulong);

    /// <summary>The set of source-table qualifiers that supply the DISTINCTROW projection's output columns.
    /// An unqualified column is resolved to its source table via the input's columns.</summary>
    private static HashSet<string> ContributingQualifiers(
        IReadOnlyList<SelectItem> projection, IReadOnlyList<OutputColumn> columns)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SelectItem item in projection)
            foreach ((string? qualifier, string column) in ColumnRefs(item.Value))
            {
                if (qualifier is not null) { result.Add(qualifier); continue; }
                OutputColumn match = columns.FirstOrDefault(
                    c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));
                if (match.Qualifier is not null) result.Add(match.Qualifier);
            }
        return result;
    }

    /// <summary>The column references an expression reads from the current row scope (a <c>t.*</c> counts as
    /// its table). Subqueries are opaque — their column refs bind in the inner scope, not here.</summary>
    private static IEnumerable<(string? Qualifier, string Column)> ColumnRefs(Expression expression) => expression switch
    {
        ColumnReference c => [(c.Table, c.Column)],
        QualifiedStarExpression qs => [(qs.Table, "*")],
        BinaryExpression b => ColumnRefs(b.Left).Concat(ColumnRefs(b.Right)),
        UnaryExpression u => ColumnRefs(u.Operand),
        FunctionCall f => f.Arguments.SelectMany(ColumnRefs),
        InListExpression il => ColumnRefs(il.Value).Concat(il.Items.SelectMany(ColumnRefs)),
        _ => [],
    };

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteJoin(JoinNode join, EvalScope? outer)
    {
        var (leftColumns, leftRows) = Execute(join.Left, outer);
        Expression? on = join.On; // null for a CROSS join (cartesian product)
        bool leftOuter = join.Kind == JoinKind.Left;

        // Index-nested-loop: the right side is a *correlated* index seek (keyed off the outer row). Re-execute
        // it per left row — seeking the inner index — instead of materialising and scanning the whole inner
        // table. IndexSelection produces this for a join whose ON is an equality on an indexed inner column.
        if (join.Right is IndexSeekNode seek)
        {
            // Open the inner table, resolve its columns, and precompute the seek key-column positions ONCE —
            // not per left row. Per left row we only rebuild the tiny key-value array and seek; this is the
            // hot path of the join, so everything hoistable stays out of the loop.
            var innerTable = _database.OpenTable(seek.Table);
            string innerAlias = seek.Alias ?? seek.Table;
            int innerWidth = innerTable.Definition.Columns.Count;
            var seekColumns = innerTable.Definition.Columns
                .Select(c => new OutputColumn(innerAlias, c.Name, Schema.JetClrTypeMap.ToClrType(c.Type))).ToList();
            var joinColumns = leftColumns.Concat(seekColumns).ToList();
            int[] keyCols = seek.Index.Columns.Select(c => c.Column.Index).ToArray();

            IEnumerable<object?[]> SeekRows()
            {
                // Build the scopes + evaluators once and rebind them to each row (see EvalScope.Rebind): the
                // key eval fires per outer row and the residual-ON eval per matched row, so allocating a fresh
                // pair each time was the join's dominant per-row cost once the page cache made reads free.
                var keyScope = new EvalScope(leftColumns, [], outer);
                var keyEval = new ExpressionEvaluator(keyScope, this, parameters: _parameters, session: _session);
                var onScope = new EvalScope(joinColumns, [], outer);
                var onEval = new ExpressionEvaluator(onScope, this, parameters: _parameters, session: _session);
                var keyValues = new object?[innerWidth]; // reused; the inner seek is fully drained each outer row

                foreach (object?[] left in leftRows)
                {
                    keyScope.Rebind(left);
                    for (int i = 0; i < seek.Keys.Count; i++)
                        keyValues[keyCols[i]] = keyEval.Evaluate(seek.Keys[i]);

                    bool matched = false;
                    foreach (object?[] right in innerTable.SeekRows(seek.Index, keyValues))
                    {
                        object?[] combined = [.. left, .. right];
                        if (on is null || onEval.Rebind(combined).IsTrue(on))
                        {
                            matched = true;
                            yield return combined;
                        }
                    }
                    if (leftOuter && !matched)
                        yield return [.. left, .. new object?[innerWidth]];
                }
            }

            return (joinColumns, SeekRows());
        }

        var (rightColumns, rightRowsEnum) = Execute(join.Right, outer);

        var columns = leftColumns.Concat(rightColumns).ToList();
        var rightRows = rightRowsEnum.ToList(); // re-iterated per left row
        if (on is null && join.Kind != JoinKind.Cross)
            throw new NotSupportedException("Joins require an ON condition.");

        IEnumerable<object?[]> Rows()
        {
            var onScope = new EvalScope(columns, [], outer); // one scope/evaluator, rebound per combined row
            var onEval = new ExpressionEvaluator(onScope, this, parameters: _parameters, session: _session);

            foreach (object?[] left in leftRows)
            {
                bool matched = false;
                foreach (object?[] right in rightRows)
                {
                    object?[] combined = [.. left, .. right];
                    if (on is null || onEval.Rebind(combined).IsTrue(on))
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

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteHashJoin(HashJoinNode join, EvalScope? outer)
    {
        var (leftColumns, leftRows) = Execute(join.Left, outer);
        var (rightColumns, rightRowsEnum) = Execute(join.Right, outer);
        var joinColumns = leftColumns.Concat(rightColumns).ToList();
        int leftWidth = leftColumns.Count, rightWidth = rightColumns.Count;
        Expression on = join.On;

        // INNER/LEFT build the right side and probe with the left; RIGHT builds the left and probes with the
        // right (so the preserved — outer — side is always the probe side). The emitted row is [left.., right..]
        // regardless of which side was built.
        bool buildRight = join.Kind != JoinKind.Right;
        bool preserveProbe = join.Kind is JoinKind.Left or JoinKind.Right; // probe side is the outer/preserved side
        var buildColumns = buildRight ? rightColumns : leftColumns;
        var buildKeys = buildRight ? join.RightKeys : join.LeftKeys;
        var buildRowsEnum = buildRight ? rightRowsEnum : leftRows;
        var probeColumns = buildRight ? leftColumns : rightColumns;
        var probeKeys = buildRight ? join.LeftKeys : join.RightKeys;
        var probeRowsEnum = buildRight ? leftRows : rightRowsEnum;

        IEnumerable<object?[]> Rows()
        {
            // Build phase: hash the build side by its keys. A row with any null key can never satisfy an
            // equi-join (SQL null = null is not true), so it is dropped from the table.
            var table = new Dictionary<object?[], List<object?[]>>(HashKeyComparer.Instance);
            var buildScope = new EvalScope(buildColumns, [], outer);
            var buildEval = new ExpressionEvaluator(buildScope, this, parameters: _parameters, session: _session);
            foreach (object?[] b in buildRowsEnum)
            {
                buildScope.Rebind(b);
                var key = new object?[buildKeys.Count];
                if (!EvalKey(buildEval, buildKeys, key)) continue; // null key → unmatchable
                if (!table.TryGetValue(key, out List<object?[]>? bucket))
                    table[key] = bucket = [];
                bucket.Add(b);
            }

            // Probe phase: each probe row looks up its bucket; the full ON re-checks each candidate (buckets can
            // collide, and ON may carry extra non-equi conjuncts). An outer join emits a null-padded row on no match.
            var probeScope = new EvalScope(probeColumns, [], outer);
            var probeEval = new ExpressionEvaluator(probeScope, this, parameters: _parameters, session: _session);
            var onScope = new EvalScope(joinColumns, [], outer);
            var onEval = new ExpressionEvaluator(onScope, this, parameters: _parameters, session: _session);
            var probe = new object?[probeKeys.Count]; // reused; only used to look up, never stored

            foreach (object?[] p in probeRowsEnum)
            {
                probeScope.Rebind(p);
                bool matched = false;
                if (EvalKey(probeEval, probeKeys, probe) && table.TryGetValue(probe, out List<object?[]>? bucket))
                {
                    foreach (object?[] b in bucket)
                    {
                        object?[] left = buildRight ? p : b;
                        object?[] right = buildRight ? b : p;
                        object?[] combined = [.. left, .. right];
                        if (onEval.Rebind(combined).IsTrue(on))
                        {
                            matched = true;
                            yield return combined;
                        }
                    }
                }
                if (preserveProbe && !matched)
                    yield return buildRight
                        ? [.. p, .. new object?[rightWidth]]  // probe is the left side; right is null
                        : [.. new object?[leftWidth], .. p];  // probe is the right side; left is null
            }
        }

        return (joinColumns, Rows());
    }

    /// <summary>Evaluates the key expressions into <paramref name="dest"/>; returns false (short-circuiting) if
    /// any key is null, since a null key never participates in an equi-join match.</summary>
    private static bool EvalKey(ExpressionEvaluator eval, IReadOnlyList<Expression> keys, object?[] dest)
    {
        for (int i = 0; i < keys.Count; i++)
            if ((dest[i] = eval.Evaluate(keys[i])) is null)
                return false;
        return true;
    }

    /// <summary>Hash/equality over a composite join key that mirrors the evaluator's <c>=</c> within a type kind
    /// (the planner only builds a hash join over same-kind key columns). Key elements are never null.</summary>
    private sealed class HashKeyComparer : IEqualityComparer<object?[]>
    {
        public static readonly HashKeyComparer Instance = new();

        public bool Equals(object?[]? a, object?[]? b)
        {
            if (a is null || b is null) return ReferenceEquals(a, b);
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!ExpressionEvaluator.KeyEqual(a[i]!, b[i]!)) return false;
            return true;
        }

        public int GetHashCode(object?[] a)
        {
            var h = new HashCode();
            foreach (object? v in a) h.Add(ExpressionEvaluator.KeyHash(v!));
            return h.ToHashCode();
        }
    }

    /// <summary>
    ///     Sorts rows by their ORDER BY keys, evaluating each key ONCE PER ROW rather than inside the comparer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Evaluating in the comparer costs an expression evaluation — plus an <see cref="EvalScope" /> and an
    ///         <see cref="ExpressionEvaluator" /> allocation — for both operands of every comparison, so a sort of
    ///         n rows paid O(n log n) evaluations instead of n. Measured: <c>SELECT TOP 1 c.… FROM Customers c,
    ///         Orders o, Employees e ORDER BY c.CustomerID</c> — 679,770 rows, ~13.4M comparisons, so ~27M
    ///         evaluations — took <b>31.2 s</b>, against <b>6 ms</b> for the identical query without the ORDER BY.
    ///         The cross join was never the problem; evaluating the key 40 times per row was.
    ///     </para>
    ///     <para>
    ///         <see cref="ExecuteAggregate" /> already precomputed its per-group sort keys this way, so this also
    ///         settles a disagreement between the two paths, and they now share
    ///         <see cref="CompareEvaluatedKeys" />.
    ///     </para>
    ///     <para>
    ///         Rows with equal keys must keep their input order: EF's reference and SQL Server both preserve it,
    ///         so an ORDER BY that doesn't fully disambiguate (e.g. ORDER BY CustomerID with several orders per
    ///         customer) has to as well. That used to come from Enumerable.OrderBy being a documented stable sort;
    ///         it now comes from carrying each row's input position and comparing it when the keys tie. That makes
    ///         the ordering total, so an unstable algorithm produces the stable answer anyway — which is what lets
    ///         <paramref name="bound" /> discard rows early without the notion of "first among equals" drifting.
    ///     </para>
    ///     <para>
    ///         With a <paramref name="bound" /> (an enclosing <c>TOP n</c>) only the n smallest rows can survive,
    ///         so the buffer is trimmed back to n whenever it reaches 2n and the input is never fully ordered.
    ///         Trimming in batches rather than per row amortises each sort over the n rows it throws away.
    ///     </para>
    /// </remarks>
    private List<object?[]> SortRows(
        IReadOnlyList<OrderByItem> keys,
        IReadOnlyList<OutputColumn> columns,
        EvalScope? outer,
        IEnumerable<object?[]> rows,
        int? bound = null)
    {
        // Rows paired with their evaluated keys and their input position. The position makes the ordering TOTAL,
        // which is what lets the bounded path below be stable without relying on a stable algorithm.
        var decorated = new List<(object?[] Row, object?[] Keys, int Index)>();
        var index = 0;
        foreach (object?[] row in rows)
        {
            ExpressionEvaluator eval = Eval(columns, row, outer);
            var rowKeys = new object?[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                rowKeys[i] = eval.Evaluate(keys[i].Value);
            }

            decorated.Add((row, rowKeys, index++));
            if (bound is { } max && decorated.Count > max * 2 && max > 0)
            {
                // Keep only the best `max` so far. Doing this in batches (rather than per row) amortises the sort
                // over the rows it discards, so the list never grows past 2·max however large the input is.
                Trim(decorated, keys, max);
            }
        }

        if (bound is { } limit && limit <= 0)
        {
            return [];
        }

        decorated.Sort((a, b) => Compare(a, b));
        if (bound is { } take && decorated.Count > take)
        {
            decorated.RemoveRange(take, decorated.Count - take);
        }

        return decorated.Select(x => x.Row).ToList();

        int Compare((object?[] Row, object?[] Keys, int Index) a, (object?[] Row, object?[] Keys, int Index) b)
        {
            int c = CompareEvaluatedKeys(keys, a.Keys, b.Keys);
            // Ties fall back to input order, reproducing a stable sort's result exactly.
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        }

        static void Trim(List<(object?[] Row, object?[] Keys, int Index)> list, IReadOnlyList<OrderByItem> keys, int max)
        {
            list.Sort((a, b) =>
            {
                int c = CompareEvaluatedKeys(keys, a.Keys, b.Keys);
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            });
            list.RemoveRange(max, list.Count - max);
        }
    }

    /// <summary>Compares two rows' already-evaluated ORDER BY key values, honouring each key's direction.</summary>
    private static int CompareEvaluatedKeys(IReadOnlyList<OrderByItem> keys, object?[] a, object?[] b)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            int c = ExpressionEvaluator.CompareForSort(a[i], b[i]);
            if (keys[i].Direction == SortDirection.Descending)
            {
                c = -c;
            }

            if (c != 0)
            {
                return c;
            }
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
            .Select((item, i) => new OutputColumn(null,
                item.Alias ?? (item.Value is ColumnReference c ? c.Column : $"Expr{i + 1}"),
                DeclaredType(item.Value, inColumns)))
            .ToList();

        // Each output row carries its ORDER BY key values AND its grouping-key values, evaluated in the same
        // group scope as the projection, to sort the groups afterward: by ORDER BY if present, otherwise —
        // matching Access, which returns GROUP BY results ascending by the grouping columns — by the group key
        // (this also makes a TOP-1-over-a-GROUP-BY deterministic, as Access/SQL Server do).
        var outRows = new List<(object?[] Row, object?[] SortKeys, object?[] GroupKeys)>();
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
            object?[] groupKeys = node.GroupBy.Select(k => eval.Evaluate(k)).ToArray();
            outRows.Add((row, sortKeys, groupKeys));
        }

        if (node.OrderBy.Count > 0)
            // Stable (see SortNode): groups with equal ORDER BY keys keep their input (first-appearance) order.
            outRows = outRows.OrderBy(x => x, Comparer<(object?[] Row, object?[] SortKeys, object?[] GroupKeys)>.Create(
                (a, b) => CompareEvaluatedKeys(node.OrderBy, a.SortKeys, b.SortKeys))).ToList();
        else if (node.GroupBy.Count > 0)
            // No explicit ORDER BY: Access orders GROUP BY output ascending by the grouping columns.
            outRows.Sort((a, b) =>
            {
                for (int i = 0; i < node.GroupBy.Count; i++)
                {
                    int c = ExpressionEvaluator.CompareForSort(a.GroupKeys[i], b.GroupKeys[i]);
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

    /// <summary>The distinct set of scalar values, using the same value-equality (<see cref="GroupKey"/>) as
    /// SELECT DISTINCT / GROUP BY, so <c>COUNT(DISTINCT col)</c> dedupes exactly as ACE groups. Order preserved.</summary>
    private static List<object?> DistinctValues(IEnumerable<object?> values)
    {
        var seen = new HashSet<GroupKey>();
        var result = new List<object?>();
        foreach (object? v in values)
            if (seen.Add(new GroupKey([v])))
                result.Add(v);
        return result;
    }

    private object? ComputeAggregate(FunctionCall call, List<object?[]> group, IReadOnlyList<OutputColumn> columns, EvalScope? outer)
    {
        string name = call.Name.ToUpperInvariant();
        Expression? arg = call.Arguments.Count > 0 ? call.Arguments[0] : null;

        // COUNT is an Access Long Integer (32-bit) — EF reads it with GetInt32, so return int, not long.
        if (name == "COUNT")
        {
            if (arg is StarExpression or null)
                return group.Count; // COUNT(*) counts rows; DISTINCT is meaningless (and EF never emits it)
            var counted = group.Select(r => Eval(columns, r, outer).Evaluate(arg)).Where(v => v is not null);
            return call.Distinct ? DistinctValues(counted).Count : counted.Count();
        }

        // FIRST/LAST return the argument's value from the first/last row of the group in scan order — NOT
        // null-filtered (verified vs ACE: First over a leading NULL row returns NULL).
        if (name == "FIRST")
            return group.Count == 0 ? null : Eval(columns, group[0], outer).Evaluate(arg!);
        if (name == "LAST")
            return group.Count == 0 ? null : Eval(columns, group[^1], outer).Evaluate(arg!);

        var values = group.Select(r => Eval(columns, r, outer).Evaluate(arg!)).Where(v => v is not null).ToList();
        // SUM(DISTINCT)/AVG(DISTINCT)/… aggregate the distinct set of the argument's values. MIN/MAX are
        // unaffected by dedup, but applying it uniformly keeps the one code path.
        if (call.Distinct)
            values = DistinctValues(values);
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
            case InListExpression i:
                foreach (FunctionCall a in Aggregates(i.Value).Concat(i.Items.SelectMany(Aggregates))) yield return a;
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
