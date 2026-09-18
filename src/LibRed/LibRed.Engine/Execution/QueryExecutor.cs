using LibRed.Engine.Plan;
using LibRed.Engine.Planning;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>A column produced by a plan node: an optional table-alias qualifier and a name. <paramref name="Currency"/>
/// marks a Currency value, which shares <see cref="decimal"/> with Decimal but calculates differently, and
/// <paramref name="Scale"/> a Decimal's places. <paramref name="Null"/> marks a column that is a bare <c>NULL</c>, which
/// has no type of its own, unlike one whose type is merely unknown.</summary>
internal readonly record struct OutputColumn(
    string? Qualifier, string Name, Type? ClrType = null, bool Currency = false, int? Scale = null, bool Null = false)
{
    /// <summary>The output of a stored column.</summary>
    public static OutputColumn Of(string? qualifier, LibRed.Catalog.ColumnDef column) =>
        new(qualifier, column.Name, Schema.JetClrTypeMap.ToClrType(column.Type),
            column.Type == LibRed.Catalog.JetDataType.Currency,
            column.Type == LibRed.Catalog.JetDataType.FixedPoint ? column.Scale : null);

    /// <summary>A computed column of <paramref name="type"/>, computed by <paramref name="expression"/>.</summary>
    public static OutputColumn Computed(string name, Type? clrType, NumberType type, Expression expression) =>
        new(null, name, clrType, type.Class == NumberClass.Currency,
            type.Class == NumberClass.Decimal ? type.Places : null, expression is LiteralExpression { Value: null });

    /// <summary>The column <paramref name="reference"/> names, or null when none or more than one does (execution
    /// reports the ambiguous reference).</summary>
    public static OutputColumn? Find(IReadOnlyList<OutputColumn> columns, ColumnReference reference)
    {
        OutputColumn? result = null;
        foreach (OutputColumn column in columns)
        {
            if (!string.Equals(column.Name, reference.Column, StringComparison.OrdinalIgnoreCase)
                || reference.Table is not null
                && !string.Equals(column.Qualifier, reference.Table, StringComparison.OrdinalIgnoreCase))
                continue;

            if (result is not null) return null;
            result = column;
        }
        return result;
    }
}

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
    private readonly Dictionary<SqlStatement, PlanNode> _subqueryPlans = new(ReferenceEqualityComparer.Instance);

    // Flattened projection schema (output columns + per-item source), keyed by the ProjectNode. Depends only on
    // the node and its input column structure — both invariant across outer rows — so, like the subquery plans,
    // it must be built ONCE. Rebuilding it per row re-ran DeclaredType (linear column scans, string allocation)
    // for every outer row of a correlated subquery / nested-loop inner.
    private readonly Dictionary<ProjectNode, ProjectionSchema> _projectionSchemas = new(ReferenceEqualityComparer.Instance);

    // Decorrelated EXISTS subqueries, keyed by AST node. A present-but-null value records "analysed, not
    // decorrelatable", so an unsound-to-rewrite subquery isn't re-analysed on every outer row.
    private readonly Dictionary<SqlStatement, ExistsSemiJoin?> _semiJoins = new(ReferenceEqualityComparer.Instance);

    // The same for `x IN (subquery)`, kept separate because the plan there carries the IN value as an extra key
    // column — the same SelectStatement node reached as an EXISTS body would need a different one.
    private readonly Dictionary<SqlStatement, ExistsSemiJoin?> _inSemiJoins = new(ReferenceEqualityComparer.Instance);

    // And for a correlated scalar aggregate, which maps each key to one value rather than testing membership.
    private readonly Dictionary<SqlStatement, ScalarAggregateSemiJoin?> _scalarSemiJoins = new(ReferenceEqualityComparer.Instance);

    // Results of subqueries that turned out not to depend on the outer row: same answer every time, so they are
    // evaluated once per statement. Keyed by AST node; the boxed value may legitimately be null (SQL NULL), hence
    // separate dictionaries rather than a null-means-absent convention.
    private readonly Dictionary<SqlStatement, object?> _hoistedScalar = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SqlStatement, List<object?>> _hoistedColumn = new(ReferenceEqualityComparer.Instance);

    // Hash membership over a hoisted IN body's values, so testing an outer row is a lookup rather than a walk of
    // that list. A present-but-null entry records "built, not usable" (mixed or non-hashable kinds), so the
    // decision is made once rather than re-attempted for every row.
    private readonly Dictionary<SqlStatement, HoistedInSet?> _hoistedInSets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SqlStatement, bool> _hoistedExists = new(ReferenceEqualityComparer.Instance);

    // Subqueries proven to depend on the outer row. Recorded so a correlated subquery pays ONE failed hoist
    // attempt per statement rather than one per row.
    private readonly HashSet<SqlStatement> _correlated = new(ReferenceEqualityComparer.Instance);

    // Per-row time spent on each decorrelatable subquery, which is what decides when to switch over. See
    // DecorrelationGate: the rewrite is sound from the first probe but not always cheaper, and the outer row count
    // that would settle it isn't known until the outer scan has finished.
    private readonly Dictionary<SqlStatement, DecorrelationGate> _gates = new(ReferenceEqualityComparer.Instance);

    private DecorrelationGate Gate(SqlStatement query)
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

    /// <summary>
    ///     Runs a plan against an enclosing scope, so a <b>correlated</b> one resolves the outer row's columns.
    ///     Columns come back eagerly and rows lazily, as everywhere else, so a caller that only wants the schema
    ///     can pass a null-filled outer row and read nothing.
    /// </summary>
    /// <remarks>
    ///     For <see cref="StatementExecutor"/>'s lateral joins. Its join loop cannot reuse the one here because
    ///     DML is identity-oriented — it carries a RowId per table to know which physical row to write, and
    ///     shares a row's value array across the combinations it appears in so a SET that reads the row's own
    ///     value accumulates per match — where this pipeline yields flat value arrays and keeps no identity. So
    ///     the two join loops stay separate, and this is the seam that lets the DML one borrow execution.
    /// </remarks>
    internal (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteCorrelated(
        PlanNode plan, EvalScope outer)
        => Execute(plan, outer);

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

    object? IScalarSubqueryRunner.ExecuteScalar(SqlStatement query, EvalScope outerScope)
    {
        if (_hoistedScalar.TryGetValue(query, out object? hoisted))
            return hoisted;

        if (TryHoist(query, outerScope, ScalarOf, out object? once))
            return _hoistedScalar[query] = once;

        // A correlated aggregate is one grouped pass over the body rather than one aggregate per outer row.
        if (!_scalarSemiJoins.TryGetValue(query, out ScalarAggregateSemiJoin? semi))
        {
            // Only a plain SELECT is analysable: the rewrite reads the body's projection, FROM and WHERE, none of
            // which a set operation or a table value constructor has. Declining costs speed, never correctness —
            // the per-row path below runs the body as written.
            _scalarSemiJoins[query] = semi = query is SelectStatement scalarBody
                ? ScalarAggregateSemiJoin.TryBuild(
                    scalarBody, outerScope.AllColumns(),
                    outerScope.VisibleAliases().ToHashSet(StringComparer.OrdinalIgnoreCase), _database.Catalog)
                : null;
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

    bool IScalarSubqueryRunner.ExecuteExists(SqlStatement query, EvalScope outerScope)
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
            // As in ExecuteScalar: only a plain SELECT can be decorrelated, and declining is the safe direction.
            _semiJoins[query] = semi = query is SelectStatement existsBody
                ? ExistsSemiJoin.TryBuild(
                    existsBody, outerScope.AllColumns(),
                    outerScope.VisibleAliases().ToHashSet(StringComparer.OrdinalIgnoreCase), _database.Catalog)
                : null;
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
    private bool TryHoist<T>(SqlStatement query, EvalScope outerScope, Func<EvalScope?, T> run, out T result)
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
    /// <param name="nullSafe">
    ///     Per correlation column, whether a null there is a value to be hashed rather than a row to discard. See
    ///     <see cref="CorrelationSplit.NullSafe" />.
    /// </param>
    internal (HashSet<object?[]> Keys, HashSet<object?[]> NullTailKeys) BuildSemiJoinKeys(
        SelectStatement keyQuery, int keyWidth, IReadOnlyList<bool> nullSafe, bool trackNullTail = false)
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
                usable = (key[i] = row[i]) is not null || (i < nullSafe.Count && nullSafe[i]);
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
    internal Dictionary<object?[], object?> BuildGroupedAggregate(
        SelectStatement keyQuery, int keyWidth, IReadOnlyList<bool> nullSafe)
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
                usable = (key[i] = row[i]) is not null || (i < nullSafe.Count && nullSafe[i]);
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
        SqlStatement query, Expression value, object? evaluated, EvalScope outerScope)
    {
        // An uncorrelated IN needs nothing from here: ExecuteColumn hoists it, so the body already runs once and
        // the caller's loop walks a cached list. TryBuildForIn declines it too (there are no correlation keys).
        if (!_inSemiJoins.TryGetValue(query, out ExistsSemiJoin? semi))
        {
            _inSemiJoins[query] = semi = query is SelectStatement inBody
                ? ExistsSemiJoin.TryBuildForIn(
                    inBody, value, outerScope.AllColumns(),
                    outerScope.VisibleAliases().ToHashSet(StringComparer.OrdinalIgnoreCase), _database.Catalog)
                : null;
        }

        // Not ready yet: decline, and the caller's loop runs the body through ExecuteColumn, which charges the gate.
        return semi is not null && Gate(query).Ready
            ? semi.ContainsValue(this, new ExpressionEvaluator(outerScope, this, _parameters, _session), evaluated)
            : null;
    }

    (bool Found, bool HasNull)? IScalarSubqueryRunner.LookupHoistedIn(SqlStatement query, object value) =>
        _hoistedInSets.TryGetValue(query, out HoistedInSet? set) ? set?.Lookup(value) : null;

    IEnumerable<object?> IScalarSubqueryRunner.ExecuteColumn(SqlStatement query, EvalScope outerScope)
    {
        if (_hoistedColumn.TryGetValue(query, out List<object?>? hoisted))
            return hoisted;

        if (TryHoist(query, outerScope, ColumnOf, out List<object?>? once))
        {
            // Built here rather than lazily on first probe: this is the one place that knows the body was
            // hoisted, and the set is only ever worth building for a body that runs once.
            _hoistedInSets[query] = HoistedInSet.TryBuild(once!);
            return _hoistedColumn[query] = once!;
        }

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
    private PlanNode SubqueryPlan(SqlStatement query, EvalScope outerScope)
    {
        if (!_subqueryPlans.TryGetValue(query, out PlanNode? plan))
        {
            var outerAliases = outerScope.VisibleAliases().ToHashSet(StringComparer.OrdinalIgnoreCase);
            _subqueryPlans[query] = plan =
                Planning.IndexSelection.Apply(QueryPlanner.PlanStatement(query), _database.Catalog, outerAliases);
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

            case ValuesNode values:
            {
                // A table value constructor as a query. The row expressions can reference outer columns — EF
                // emits VALUES (`p`.`Int`) inside a correlated subquery — so they are evaluated against the
                // outer scope here, on every run of the node, rather than folded once at planning time.
                var evaluator = new ExpressionEvaluator(
                    new EvalScope([], [], outer), this, parameters: _parameters, session: _session);

                var valueColumns = values.Rows.Count == 0
                    ? []
                    : values.Rows[0]
                        .Select((expr, i) => new OutputColumn(null, $"Expr{i + 1}", DeclaredType(expr, [])))
                        .ToList();

                var valueRows = values.Rows
                    .Select(row => row.Select(evaluator.Evaluate).ToArray())
                    .ToList();

                return (valueColumns, valueRows);
            }

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
                var columns = table.Definition.Columns.Select(c => OutputColumn.Of(alias, c)).ToList();
                return (columns, table.Rows());
            }

            case IndexSeekNode seek:
            {
                var table = _database.OpenTable(seek.Table);
                string alias = seek.Alias ?? seek.Table;
                var columns = table.Definition.Columns.Select(c => OutputColumn.Of(alias, c)).ToList();

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
                var columns = table.Definition.Columns.Select(c => OutputColumn.Of(alias, c)).ToList();

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
                var columns = inner.Select(c => c with { Qualifier = derived.Alias }).ToList();
                return (columns, rows);
            }

            case FilterNode filter:
            {
                var (columns, rows) = Execute(filter.Input, outer);
                return (columns, rows.Where(row => Eval(columns, row, outer).IsTrue(filter.Predicate)));
            }

            // A lateral join re-runs its right side per left row, so it cannot go through ExecuteJoin (which
            // materialises the right side once, against the enclosing scope).
            case JoinNode { Kind: JoinKind.CrossApply or JoinKind.OuterApply } apply:
                return ExecuteApply(apply, outer);

            case JoinNode join:
                return ExecuteJoin(join, outer);

            case HashJoinNode hashJoin:
                return ExecuteHashJoin(hashJoin, outer);

            case WindowNode window:
                return ExecuteWindow(window, outer);

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
                    return plan.Select(p => p.InputIndex >= 0
                        ? row[p.InputIndex]
                        : ExpressionEvaluator.ToResultPlaces(
                            ExpressionEvaluator.AsColumnType(eval.Evaluate(p.Expr!), p.ConvertTo, currency: false), p.Type)).ToArray();
                });

                return (schema.Columns, projected);
            }

            case SetOperationNode setOp:
            {
                // Column names come from the left (leading) query, per SQL; each column's type is the one both
                // queries' values fit, and every value is converted to it before rows are compared.
                var (leftColumns, leftRows) = Execute(setOp.Left, outer);
                var (rightColumns, rightRows) = Execute(setOp.Right, outer);
                if (leftColumns.Count != rightColumns.Count)
                    return (leftColumns, ExecuteSetOp(setOp.Operator, leftRows, rightRows));
                var columns = leftColumns.Zip(rightColumns, SetOperationColumn).ToList();
                return (columns, ExecuteSetOp(setOp.Operator,
                    ToColumnTypes(leftRows, leftColumns, columns), ToColumnTypes(rightRows, rightColumns, columns)));
            }

            case LimitNode limit:
            {
                var (columns, rows) = Execute(limit.Input, outer);
                // Counts are literal/parameter/arithmetic (no column refs), so an empty row scope suffices.
                var limitEval = new ExpressionEvaluator(new EvalScope([], [], outer), this, parameters: _parameters, session: _session);

                // OFFSET n ROWS. Applied before the take, so `OFFSET 10 FETCH NEXT 5` gives rows 11-15. A
                // negative or zero skip is a no-op rather than an error, matching how a zero TOP is handled
                // below; Skip is lazy, so nothing is buffered to discard.
                if (limit.Offset is { } offsetExpr)
                {
                    int skip = Convert.ToInt32(
                        limitEval.Evaluate(offsetExpr), System.Globalization.CultureInfo.InvariantCulture);
                    if (skip > 0)
                        rows = rows.Skip(skip);
                }

                // `OFFSET n ROWS` with no FETCH: skip, then return everything left.
                if (limit.Count is null)
                    return (columns, rows);

                object? countValue = limitEval.Evaluate(limit.Count);
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

    /// <summary>
    /// A set operation's column: the left query's, typed as ACE types it (verified vs ACE). A bare <c>NULL</c> takes
    /// the other query's type. Numbers widen on <see cref="CommonNumericType"/>, a Boolean counting as an Integer
    /// (-1); a GUID or binary value with anything else makes a binary column; any other mix — text, or a date with a
    /// number or a Boolean — makes a text column. An unknown type on either side leaves the column untyped.
    /// </summary>
    private static OutputColumn SetOperationColumn(OutputColumn left, OutputColumn right)
    {
        if (right.Null)
            return left;
        if (left.Null)
            return left with { ClrType = right.ClrType, Currency = right.Currency, Scale = right.Scale, Null = false };

        // A Decimal column is Currency unless one side is a Decimal of its own, and keeps a scale both sides share.
        bool leftDecimal = left.ClrType == typeof(decimal) && !left.Currency;
        bool rightDecimal = right.ClrType == typeof(decimal) && !right.Currency;
        Type? type = left.ClrType is not { } l || right.ClrType is not { } r ? null
            : l == r ? l
            : l == typeof(Guid) || r == typeof(Guid) || l == typeof(byte[]) || r == typeof(byte[]) ? typeof(byte[])
            : CommonNumericType(AsInteger(l), AsInteger(r), currency: !leftDecimal && !rightDecimal) ?? typeof(string);
        bool isDecimal = type == typeof(decimal);
        return left with
        {
            ClrType = type,
            Currency = isDecimal && !leftDecimal && !rightDecimal,
            Scale = !isDecimal ? null
                : leftDecimal && rightDecimal ? (left.Scale == right.Scale ? left.Scale : null)
                : leftDecimal ? left.Scale : rightDecimal ? right.Scale : null,
        };

        static Type AsInteger(Type type) => type == typeof(bool) ? typeof(short) : type;
    }

    /// <summary>The rows with each value converted to its output column's type, where the query's own column had
    /// another.</summary>
    private static IEnumerable<object?[]> ToColumnTypes(
        IEnumerable<object?[]> rows, IReadOnlyList<OutputColumn> from, IReadOnlyList<OutputColumn> to)
    {
        int[] changed = Enumerable.Range(0, to.Count)
            .Where(i => to[i].ClrType is { } type && !from[i].Null && from[i].ClrType != type)
            .ToArray();
        if (changed.Length == 0)
            return rows;
        return rows.Select(row =>
        {
            var converted = (object?[])row.Clone();
            foreach (int i in changed)
                converted[i] = ExpressionEvaluator.AsColumnType(converted[i], to[i].ClrType, from[i].Currency);
            return converted;
        });
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
    /// evaluate, with the type its values are converted to, if any) and the resulting output columns.
    /// Structural — the same for every outer row.</summary>
    private sealed record ProjectionSchema(
        List<(OutputColumn Column, int InputIndex, Expression? Expr, NumberType Type, Type? ConvertTo)> Plan,
        List<OutputColumn> Columns);

    /// <summary>Builds — or reuses — a ProjectNode's schema. Flattens the projection, expanding a qualified star
    /// (Table.*) into the input columns of that source (passed through by index); every other item is an
    /// evaluated expression whose declared type is derived once here.</summary>
    private ProjectionSchema ProjectionSchemaFor(ProjectNode project, IReadOnlyList<OutputColumn> columns)
    {
        if (_projectionSchemas.TryGetValue(project, out ProjectionSchema? cached))
            return cached;

        var plan = new List<(OutputColumn Column, int InputIndex, Expression? Expr, NumberType Type, Type? ConvertTo)>();
        foreach (SelectItem item in project.Projection)
        {
            if (item.Value is QualifiedStarExpression star)
            {
                for (int ci = 0; ci < columns.Count; ci++)
                    if (string.Equals(columns[ci].Qualifier, star.Table, StringComparison.OrdinalIgnoreCase))
                        plan.Add((columns[ci], ci, null, default, null));
            }
            else
            {
                string name = item.Alias ?? (item.Value is ColumnReference c ? c.Column : $"Expr{plan.Count + 1}");
                NumberType type = ExpressionEvaluator.NumberTypeOf(item.Value, columns, e => DeclaredType(e, columns));
                plan.Add((OutputColumn.Computed(name, DeclaredType(item.Value, columns), type, item.Value), -1, item.Value,
                    type, ChoiceConversion(item.Value, columns)));
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
            case ExistsExpression or InSubqueryExpression or InListExpression or BetweenExpression:
                return typeof(bool);
            case UnaryExpression unary:
                return unary.Operator is UnaryOperator.Not or UnaryOperator.IsNull or UnaryOperator.IsNotNull
                    ? typeof(bool) : DeclaredUnaryType(unary.Operator, DeclaredType(unary.Operand, columns));
            case BinaryExpression binary:
                if (binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual
                    or BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual
                    or BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual
                    or BinaryOperator.And or BinaryOperator.Or or BinaryOperator.Xor or BinaryOperator.Eqv
                    or BinaryOperator.Imp or BinaryOperator.Like or BinaryOperator.In)
                    return typeof(bool);
                if (binary.Operator == BinaryOperator.Concat)
                    return typeof(string);
                Type? left = DeclaredType(binary.Left, columns);
                Type? right = DeclaredType(binary.Right, columns);
                return DeclaredBinaryType(binary.Operator, left, right);
            case FunctionCall function:
                return DeclaredFunctionType(function, columns);
            case CaseExpression @case:
                return DeclaredCaseType(@case, columns);
            default:
                return null;
        }
    }

    /// <summary>
    /// The declared type of a CASE — the standard's "highest precedence type from the set of types in
    /// result_expressions and the optional else_result_expression". A branch whose own type is unknown
    /// contributes nothing rather than poisoning the answer, which is what makes a bare <c>NULL</c> arm
    /// harmless: a NULL literal has no type and the standard ignores it for precedence too. Numeric branches
    /// widen on <see cref="CommonNumericType"/>, so <c>THEN 1 ELSE 2.5</c> declares Double. A genuine mix
    /// (a string branch and a numeric one) declares nothing rather than guessing, leaving the column untyped
    /// exactly as it was before CASE was understood at all.
    /// </summary>
    private Type? DeclaredCaseType(CaseExpression @case, IReadOnlyList<OutputColumn> columns)
        => UnifiedType(CaseResults(@case), columns);

    private static IEnumerable<Expression> CaseResults(CaseExpression c)
    {
        foreach (CaseWhen arm in c.WhenClauses)
            yield return arm.Result;
        if (c.ElseResult is not null)
            yield return c.ElseResult;
    }

    /// <summary>The single type a set of alternative expressions declares — shared by CASE, IIF, COALESCE,
    /// GREATEST and LEAST, which the standard defines in terms of CASE or gives the same precedence rule.</summary>
    private Type? UnifiedType(IEnumerable<Expression> alternatives, IReadOnlyList<OutputColumn> columns)
    {
        Type? result = null;
        bool currency = false;   // whether a Decimal result so far is a Currency

        foreach (Expression alternative in alternatives)
        {
            Type? branchType = DeclaredType(alternative, columns);
            if (branchType is null)
                continue;
            bool branchCurrency = branchType == typeof(decimal)
                && ExpressionEvaluator.NumberTypeOf(alternative, columns, e => DeclaredType(e, columns)).Class
                    == NumberClass.Currency;

            if (result is null || result == branchType)
            {
                currency = result is null ? branchCurrency : currency && branchCurrency;
                result = branchType;
                continue;
            }

            bool decimalIsCurrency = result == typeof(decimal) ? currency : branchCurrency;
            result = CommonNumericType(result, branchType, decimalIsCurrency);
            if (result is null)
                return null;
            currency = result == typeof(decimal) && decimalIsCurrency;
        }

        return result;
    }

    /// <summary>
    /// The type an expression that picks one of several alternatives (<see cref="UnifiedType"/>) converts its value
    /// to, so the value has the type the column declares; null when nothing is converted. Only when every
    /// alternative's type is known is the declared type sure to hold each of them.
    /// </summary>
    private Type? ChoiceConversion(Expression expression, IReadOnlyList<OutputColumn> columns)
    {
        IEnumerable<Expression>? alternatives = expression switch
        {
            CaseExpression @case => CaseResults(@case),
            FunctionCall function => function.Name.TrimEnd('$').ToUpperInvariant() switch
            {
                "IIF" when function.Arguments.Count == 3 => function.Arguments.Skip(1),
                "COALESCE" or "GREATEST" or "LEAST" => function.Arguments,
                _ => null,
            },
            _ => null,
        };
        if (alternatives is null
            || alternatives.Any(a => a is not LiteralExpression { Value: null } && DeclaredType(a, columns) is null))
            return null;
        return DeclaredType(expression, columns);
    }

    /// <summary>
    /// The type the values of two numeric types share (verified vs ACE, as it types a UNION): the wider whole
    /// number of the two; a Single with a Byte or an Integer, and a Double for a Single with anything wider; a
    /// Decimal (or Currency) with a whole number, and a Double with a Double. A Currency (<paramref name="currency"/>,
    /// when the Decimal side is one) cannot hold a Large Number, so the two make a Double. A Decimal with a Single,
    /// which was not measured, is a Double too. Null when either side is not a number, meaning the two cannot be
    /// reconciled.
    /// </summary>
    private static Type? CommonNumericType(Type left, Type right, bool currency = false)
    {
        if (!IsNumeric(left) || !IsNumeric(right)) return null;
        if (left == right) return left;
        if (left == typeof(double) || right == typeof(double)) return typeof(double);
        int leftRank = WholeRank(left), rightRank = WholeRank(right);
        if (left == typeof(decimal) || right == typeof(decimal))
            return Math.Max(leftRank, rightRank) is var whole && whole < 0 || currency && whole > 2
                ? typeof(double) : typeof(decimal);
        if (left == typeof(float) || right == typeof(float))
            return Math.Max(leftRank, rightRank) <= 1 ? typeof(float) : typeof(double);
        return WholeTypes[Math.Max(leftRank, rightRank)];
    }

    private static readonly Type[] WholeTypes = [typeof(byte), typeof(short), typeof(int), typeof(long)];

    /// <summary>A whole number type's place in <see cref="WholeTypes"/> (the narrowest that holds it), or -1.</summary>
    private static int WholeRank(Type type) =>
        type == typeof(byte) ? 0
        : type == typeof(sbyte) || type == typeof(short) ? 1
        : type == typeof(ushort) || type == typeof(int) ? 2
        : type == typeof(uint) || IsInt64(type) ? 3
        : -1;

    private static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || IsInt64(type)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static Type? DeclaredColumnType(ColumnReference reference, IReadOnlyList<OutputColumn> columns) =>
        OutputColumn.Find(columns, reference)?.ClrType;

    /// <summary>The declared type of an aggregate (upper-case name) over an argument of <paramref name="argument"/> —
    /// for an ordered-set aggregate, its WITHIN GROUP key — grouped or windowed. Keep in lock-step with
    /// <see cref="RunningAggregate"/> and <see cref="Percentile"/>.</summary>
    internal static Type? AggregateResultType(string name, Type? argument) => RunningAggregate.Canonical(name) switch
    {
        "LISTAGG" => typeof(string),
        "PERCENTILE_CONT" or "PERCENTILE_DISC" => Percentile.ResultType(name, argument),
        "COUNT" or "REGR_COUNT" => typeof(int),
        var pair when RunningAggregate.IsPair(pair) => typeof(double),
        "SUM" => argument == null ? null
            : argument == typeof(decimal) || argument == typeof(float) || argument == typeof(long) ? argument
            : argument == typeof(ulong) ? typeof(long)
            : argument == typeof(double) || argument == typeof(string) || argument == typeof(DateTime) ? typeof(double)
            : typeof(int),
        "AVG" => argument == typeof(decimal) ? typeof(decimal) : typeof(double),
        "VAR" or "VARP" or "STDEV" or "STDEVP" or "STDDEV" or "STDDEVP" => typeof(double),
        _ => argument,   // MIN, MAX, FIRST and LAST keep the argument's type
    };

    private Type? DeclaredFunctionType(FunctionCall function, IReadOnlyList<OutputColumn> columns)
    {
        string name = function.Name.TrimEnd('$').ToUpperInvariant();
        Type? argument = function.Arguments.Count == 0 ? null
            : DeclaredType(function.Arguments[function.WithinGroup is null ? 0 : ^1], columns);
        if (QueryPlanner.IsAggregate(name))
            return AggregateResultType(name, argument);
        return name switch
        {
            "CBOOL" or "ISDATE" => typeof(bool),
            "CBYTE" => typeof(byte),
            "CINT" => typeof(short),
            "CLNG" => typeof(int),
            "CLNGLNG" => typeof(long),
            "CSNG" => typeof(float),
            "CDBL" => typeof(double),
            "CDEC" or "CCUR" => typeof(decimal),
            "CSTR" or "FORMAT" or "LCASE" or "UCASE" or "TRIM" or "LTRIM" or "RTRIM"
                or "LEFT" or "RIGHT" or "MID" or "REPLACE" or "STRING" or "SPACE" or "HEX"
                or "OCT" or "WEEKDAYNAME" or "MONTHNAME" or "PARTITION" => typeof(string),
            // DateDiff's "ms", LibRed's own interval, counts in Int64 — a millisecond difference passes Int32 after
            // 25 days — where every other interval is a Long Integer. Only a written interval says which: one read
            // from a parameter or a column is declared as the Long Integer the rest give.
            "DATEDIFF" => function.Arguments is [LiteralExpression { Value: string interval }, ..]
                          && interval.Equals("ms", StringComparison.OrdinalIgnoreCase)
                ? typeof(long)
                : typeof(int),
            "LEN" or "DATALENGTH" or "INSTR" or "INSTRREV" or "ASC" or "ASCW" or "DATEPART"
                or "YEAR" or "MONTH" or "DAY" or "HOUR" or "MINUTE" or "SECOND" or "WEEKDAY" => typeof(int),
            "CDATE" or "NOW" or "DATE" or "TIME" or "DATEADD" or "DATESERIAL" or "TIMESERIAL"
                or "DATEVALUE" or "TIMEVALUE" => typeof(DateTime),
            "SQR" or "SIN" or "COS" or "TAN" or "ATN" or "LOG" or "EXP" or "RND"
                or "PMT" or "FV" or "PV" or "NPER" or "IPMT" or "PPMT" or "DDB" or "RATE" or "SLN" or "SYD" => typeof(double),
            // IIF chooses between two values as CASE does, so it takes CASE's rule rather than ACE's own (which
            // makes every whole number a Long and lets Currency beat Double).
            "IIF" when function.Arguments.Count == 3 => UnifiedType(function.Arguments.Skip(1), columns),
            // The standard makes COALESCE shorthand for a CASE over its arguments, so it takes the same rule:
            // the highest-precedence type among them. Unified the same way, which also means a bare NULL
            // argument contributes no type rather than erasing the others.
            "COALESCE" => UnifiedType(function.Arguments, columns),
            // GREATEST/LEAST return one of their arguments, so they declare the type the arguments unify to —
            // SQL Server's "highest precedence type" rule, the same as COALESCE.
            "GREATEST" or "LEAST" => UnifiedType(function.Arguments, columns),
            // NULLIF returns its first expression, or a NULL of that expression's type — so unlike COALESCE
            // it takes the first argument's type outright rather than unifying across both. The second
            // argument only ever participates in the comparison.
            "NULLIF" => argument,
            _ => null,
        };
    }


    private static Type? DeclaredBinaryType(BinaryOperator op, Type? left, Type? right)
    {
        if (left is null || right is null)
            return null;

        // '+' concatenates only two texts (a GUID or binary value counts as text). Otherwise an arithmetic
        // operator reads text as a Double. Keep in lock-step with ExpressionEvaluator.Add and NumericOperand.
        if (op == BinaryOperator.Add && IsConcatText(left) && IsConcatText(right))
            return typeof(string);
        if (op is BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide
            or BinaryOperator.IntDivide or BinaryOperator.Modulo or BinaryOperator.Power)
        {
            if (left == typeof(string)) left = typeof(double);
            if (right == typeof(string)) right = typeof(double);
        }
        if (op == BinaryOperator.Power)
            return typeof(double);
        // Keep in lock-step with ExpressionEvaluator.Divide: a Single with only Singles, Integers or Booleans stays one.
        if (op == BinaryOperator.Divide)
            return left == typeof(decimal) || right == typeof(decimal) ? typeof(decimal)
                : (left == typeof(float) || right == typeof(float)) && IsSingleWidth(left) && IsSingleWidth(right) ? typeof(float)
                : typeof(double);
        // Keep in lock-step with ExpressionEvaluator.BitwiseOp: two 16-bit operands give an Integer.
        if (op is BinaryOperator.BitAnd or BinaryOperator.BitOr or BinaryOperator.BitXor
            && IsSixteenBits(left) && IsSixteenBits(right))
            return typeof(short);
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

    /// <summary>The type of <c>-x</c> or <c>BNOT x</c>. Keep in lock-step with ExpressionEvaluator.Negate and BitNot:
    /// negation reads text as a Double and widens an Integer, Byte or Boolean to a Long; BNOT gives an Integer from
    /// an Integer or a Boolean, an Int64 from an Int64, and otherwise a Long.</summary>
    private static Type? DeclaredUnaryType(UnaryOperator op, Type? operand)
    {
        if (operand is null)
            return null;
        if (op == UnaryOperator.BitNot)
            return IsSixteenBits(operand) ? typeof(short) : IsInt64(operand) ? typeof(long) : typeof(int);
        if (operand == typeof(string))
            return typeof(double);
        if (operand == typeof(short) || operand == typeof(byte) || operand == typeof(bool))
            return typeof(int);
        return IsInt64(operand) ? typeof(long) : operand;
    }

    private static bool IsInt64(Type type) => type == typeof(long) || type == typeof(ulong);

    /// <summary>A bitwise operand of 16 bits: an Integer or a Boolean (ExpressionEvaluator.BitOperand).</summary>
    private static bool IsSixteenBits(Type? type) => type == typeof(short) || type == typeof(bool);

    private static bool IsSingleWidth(Type type) => type == typeof(float) || type == typeof(short) || type == typeof(bool);

    private static bool IsConcatText(Type type) => type == typeof(string) || type == typeof(Guid) || type == typeof(byte[]);

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
        _ => expression.Operands()?.SelectMany(ColumnRefs) ?? [],
    };

    /// <summary>
    ///     CROSS/OUTER APPLY: a lateral join. The right side is a table expression that may correlate to the
    ///     left, so it is re-executed once per left row with that row pushed onto the scope chain - the same
    ///     mechanism a correlated subquery uses, only producing rows rather than one value. CROSS APPLY emits
    ///     nothing for a left row whose right side came back empty; OUTER APPLY emits it null-padded.
    /// </summary>
    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteApply(JoinNode apply, EvalScope? outer)
    {
        var (leftColumns, leftRows) = Execute(apply.Left, outer);
        bool preserveLeft = apply.Kind is JoinKind.OuterApply;

        // The right side's rows vary per left row but its schema does not, and the caller needs the joined
        // schema before a single left row is read (a left side with no rows still has one). So run the right
        // side once here against an all-null left row purely to learn its columns, and drop the rows unread:
        // Execute resolves columns eagerly and rows lazily, so for almost every node this reads nothing at all.
        var probeScope = new EvalScope(leftColumns, new object?[leftColumns.Count], outer);
        var (rightColumns, _) = Execute(apply.Right, probeScope);
        var columns = leftColumns.Concat(rightColumns).ToList();

        IEnumerable<object?[]> Rows()
        {
            foreach (object?[] left in leftRows)
            {
                // A fresh scope per left row rather than a rebound one (as the joins use): the right side's
                // row enumerable captures the scope it was built with, and re-planning happens inside Execute
                // anyway, so there is nothing here that a shared scope would save.
                var (_, rightRows) = Execute(apply.Right, new EvalScope(leftColumns, left, outer));

                bool any = false;
                foreach (object?[] right in rightRows)
                {
                    any = true;
                    yield return [.. left, .. right];
                }

                if (!any && preserveLeft)
                    yield return [.. left, .. new object?[rightColumns.Count]];
            }
        }

        return (columns, Rows());
    }

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteJoin(JoinNode join, EvalScope? outer)
    {
        var (leftColumns, leftRows) = Execute(join.Left, outer);
        Expression? on = join.On; // null for a CROSS join (cartesian product)
        // Which sides are preserved. FULL preserves both, so it is left-outer and right-outer at once. The
        // right-preserving half costs more: a right row's fate is not settled until every left row has been
        // tried, so those rows are tracked and emitted after the loop rather than inside it.
        bool leftOuter = join.Kind is JoinKind.Left or JoinKind.Full;
        bool rightOuter = join.Kind is JoinKind.Right or JoinKind.Full;

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
            var seekColumns = innerTable.Definition.Columns.Select(c => OutputColumn.Of(innerAlias, c)).ToList();
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

        // Subqueries in the ON that cannot reference the right side hold still while the inner loop turns, so
        // they are evaluated once per left row and substituted in. See JoinPredicateHoisting for the measurement:
        // without this, one EF-generated join ran the same subquery 8,099 times for 89 distinct answers.
        IReadOnlyList<Expression> invariants = JoinPredicateHoisting.Invariants(on, QueryPlanner.SubtreeAliases(join.Right));

        IEnumerable<object?[]> Rows()
        {
            var onScope = new EvalScope(columns, [], outer); // one scope/evaluator, rebound per combined row
            var onEval = new ExpressionEvaluator(onScope, this, parameters: _parameters, session: _session);
            bool[]? rightMatched = rightOuter ? new bool[rightRows.Count] : null;

            // The hoisted subqueries see the left row only: binding them against the combined row would let a
            // bare name resolve to the right side, which is the case Invariants already declines to hoist.
            EvalScope? leftScope = invariants.Count > 0 ? new EvalScope(leftColumns, [], outer) : null;
            ExpressionEvaluator? leftEval = leftScope is null
                ? null
                : new ExpressionEvaluator(leftScope, this, parameters: _parameters, session: _session);

            foreach (object?[] left in leftRows)
            {
                Expression? rowOn = on;
                if (leftEval is not null && on is not null)
                {
                    leftScope!.Rebind(left);
                    var values = new Dictionary<Expression, object?>(ReferenceEqualityComparer.Instance);
                    foreach (Expression invariant in invariants)
                    {
                        values[invariant] = leftEval.Evaluate(invariant);
                    }

                    rowOn = JoinPredicateHoisting.Substitute(on, values);
                }

                bool matched = false;
                for (int r = 0; r < rightRows.Count; r++)
                {
                    object?[] combined = [.. left, .. rightRows[r]];
                    if (rowOn is null || onEval.Rebind(combined).IsTrue(rowOn))
                    {
                        matched = true;
                        if (rightMatched is not null) rightMatched[r] = true;
                        yield return combined;
                    }
                }

                if (leftOuter && !matched)
                    yield return [.. left, .. new object?[rightColumns.Count]];
            }

            // Right-preserving tail: every right row no left row matched, null-padded on the left.
            for (int r = 0; rightMatched is not null && r < rightMatched.Length; r++)
                if (!rightMatched[r])
                    yield return [.. new object?[leftColumns.Count], .. rightRows[r]];
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
        // FULL builds the right and probes with the left, like INNER/LEFT - but it preserves the build side too,
        // which the others never do, so it additionally tracks which build rows were hit.
        bool buildRight = join.Kind != JoinKind.Right;
        bool preserveProbe = join.Kind is JoinKind.Left or JoinKind.Right or JoinKind.Full; // probe side is outer
        bool preserveBuild = join.Kind is JoinKind.Full;
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
            // A null-key build row can never match, so it is normally dropped outright. Under FULL the build
            // side is preserved, which makes "never matches" a reason to emit it, not to discard it - so those
            // rows are set aside instead and joined to the unmatched tail below.
            List<object?[]>? unhashableBuild = preserveBuild ? [] : null;
            foreach (object?[] b in buildRowsEnum)
            {
                buildScope.Rebind(b);
                var key = new object?[buildKeys.Count];
                if (!EvalKey(buildEval, buildKeys, key)) // null key → unmatchable
                {
                    unhashableBuild?.Add(b);
                    continue;
                }
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
            var matchedBuild = preserveBuild ? new HashSet<object?[]>(RowIdentityComparer.Instance) : null;

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
                            matchedBuild?.Add(b);
                            yield return combined;
                        }
                    }
                }
                if (preserveProbe && !matched)
                    yield return buildRight
                        ? [.. p, .. new object?[rightWidth]]  // probe is the left side; right is null
                        : [.. new object?[leftWidth], .. p];  // probe is the right side; left is null
            }

            // FULL only: the build side is preserved as well, so every build row the probe never matched is
            // emitted null-padded on the other side. This has to trail the whole probe pass - a build row is
            // only unmatched once every probe row has failed to hit it.
            if (preserveBuild)
            {
                object?[] Unmatched(object?[] b) => buildRight
                    ? [.. new object?[leftWidth], .. b]   // build is the right side; left is null
                    : [.. b, .. new object?[rightWidth]]; // build is the left side; right is null

                foreach (List<object?[]> bucket in table.Values)
                    foreach (object?[] b in bucket)
                        if (!matchedBuild!.Contains(b))
                            yield return Unmatched(b);

                foreach (object?[] b in unhashableBuild!) // null-key rows: unmatchable by construction
                    yield return Unmatched(b);
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

    /// <summary>Identity, not value, over a row array: a FULL join's "was this build row ever matched?" set has to
    /// distinguish two rows that happen to hold equal values, so it keys on the reference itself.</summary>
    private sealed class RowIdentityComparer : IEqualityComparer<object?[]>
    {
        public static readonly RowIdentityComparer Instance = new();

        public bool Equals(object?[]? x, object?[]? y) => ReferenceEquals(x, y);

        public int GetHashCode(object?[] obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>Hash/equality over a composite join key that mirrors the evaluator's <c>=</c> within a type kind
    /// (the planner only builds a hash join over same-kind key columns). Key elements are never null.</summary>
    private sealed class HashKeyComparer : IEqualityComparer<object?[]>
    {
        public static readonly HashKeyComparer Instance = new();

        // A null element equals only a null element. KeyEqual/KeyHash are documented for non-null keys, and for a
        // plain `=` correlation no null ever reaches here (such rows are dropped from the build and short-circuit
        // on probe). A null-safe correlation — EF's `a = b OR (a IS NULL AND b IS NULL)` — does hash nulls, so the
        // tuple level owns that case rather than widening KeyEqual's contract.
        public bool Equals(object?[]? a, object?[]? b)
        {
            if (a is null || b is null) return ReferenceEquals(a, b);
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] is null || b[i] is null)
                {
                    if (a[i] is not null || b[i] is not null) return false;
                    continue;
                }

                if (!ExpressionEvaluator.KeyEqual(a[i]!, b[i]!)) return false;
            }
            return true;
        }

        public int GetHashCode(object?[] a)
        {
            var h = new HashCode();
            foreach (object? v in a) h.Add(v is null ? 0 : ExpressionEvaluator.KeyHash(v));
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

    /// <summary>
    ///     Window functions: one value per input row, computed from the other rows of that row's partition.
    ///     Every input row passes through, in input order, with one column appended per function.
    /// </summary>
    /// <remarks>
    ///     The schema is resolved eagerly from the input's schema alone; the rows — and the materialisation a
    ///     window needs — stay inside the iterator, so a caller that only wants the columns (an APPLY probing
    ///     its right side for them) reads nothing.
    /// </remarks>
    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteWindow(WindowNode node, EvalScope? outer)
    {
        var (inColumns, inRowsEnum) = Execute(node.Input, outer);
        var (types, columns) = WindowColumns(node.Outputs, inColumns);

        IEnumerable<object?[]> Rows()
        {
            var rows = inRowsEnum.ToList();
            var values = new object?[rows.Count][];
            for (int i = 0; i < rows.Count; i++)
                values[i] = new object?[node.Outputs.Count];

            // One scope/evaluator rebound per row, as the joins do: partition keys, sort keys and arguments are all
            // evaluated once per row and a fresh pair each time is the dominant cost otherwise.
            var scope = new EvalScope(inColumns, [], outer);
            var eval = new ExpressionEvaluator(scope, this, parameters: _parameters, session: _session);
            for (int slot = 0; slot < node.Outputs.Count; slot++)
            {
                ComputeWindow(node.Outputs[slot].Function, rows.Count, i =>
                {
                    scope.Rebind(rows[i]);
                    return eval;
                }, inColumns, values, slot, types[slot]);
            }

            for (int i = 0; i < rows.Count; i++)
                yield return [.. rows[i], .. values[i]];
        }

        return (columns, Rows());
    }

    /// <summary>The declared type of each window's column, and <paramref name="inColumns"/> with those columns
    /// appended. A windowed aggregate's column is typed as the grouped aggregate's, Currency and places included.</summary>
    private (List<Type?> Types, List<OutputColumn> Columns) WindowColumns(
        IReadOnlyList<WindowOutput> windows, IReadOnlyList<OutputColumn> inColumns)
    {
        var types = windows
            .Select(o => WindowFunctions.Lookup(o.Function.Name).ResultType(new WindowTyping(this, o.Function.Arguments, inColumns)))
            .ToList();
        var columns = inColumns.Concat(windows.Select((o, i) => OutputColumn.Computed(
            o.Name,
            types[i],
            QueryPlanner.IsAggregate(o.Function.Name)
                ? ExpressionEvaluator.NumberTypeOf(
                    new FunctionCall(o.Function.Name, o.Function.Arguments, WithinGroup: o.Function.WithinGroup),
                    inColumns, e => DeclaredType(e, inColumns))
                : default,
            o.Function))).ToList();
        return (types, columns);
    }

    /// <summary>A window function's view of its arguments' declared types.</summary>
    private sealed class WindowTyping(QueryExecutor executor, IReadOnlyList<Expression> arguments, IReadOnlyList<OutputColumn> columns)
        : IWindowTyping
    {
        public Type? ArgumentType(int argument) =>
            argument < arguments.Count ? executor.DeclaredType(arguments[argument], columns) : null;

        public Type? SharedType(params int[] indexes)
        {
            var present = indexes.Where(i => i < arguments.Count).Select(i => arguments[i]).ToList();
            return present.Any(a => a is not LiteralExpression { Value: null } && executor.DeclaredType(a, columns) is null)
                ? null
                : executor.UnifiedType(present, columns);
        }
    }

    /// <summary>Computes one window function into <paramref name="slot"/> of every row's value array, each value as
    /// the <paramref name="declared"/> type the column has. The rows are <paramref name="count"/> input rows or, over
    /// a grouped query, groups; <paramref name="evaluatorFor"/> gives the evaluator that sees the one at an index, and
    /// <paramref name="columns"/> is their schema.</summary>
    private void ComputeWindow(
        WindowFunction fn, int count, Func<int, ExpressionEvaluator> evaluatorFor, IReadOnlyList<OutputColumn> columns,
        object?[][] values, int slot, Type? declared)
    {
        WindowFunctionDef def = WindowFunctions.Lookup(fn.Name);
        if (fn.Arguments.Count < def.MinArguments || fn.Arguments.Count > def.MaxArguments)
            throw new InvalidOperationException(
                $"{fn.Name} takes {(def.MinArguments == def.MaxArguments ? $"{def.MinArguments}" : $"{def.MinArguments} to {def.MaxArguments}")} argument(s).");
        CheckOptions(fn, def);
        WindowFrame? frame = fn.Over.Frame;
        if (frame is not null)
            CheckFrame(fn, frame);

        // Partition, preserving input order within each. A null partition key groups with other nulls exactly as
        // GROUP BY does, because this is the same key type.
        var partitions = new Dictionary<GroupKey, List<int>>();
        var sortKeys = new object?[count][];
        var arguments = new object?[count][];
        // A frame's offsets, as each row evaluates them; the standard makes them constants, which is a special case.
        var startOffsets = frame?.Start.Offset is null ? null : new object?[count];
        var endOffsets = frame?.End.Offset is null ? null : new object?[count];
        var included = fn.Filter is null ? null : new bool[count];
        for (int i = 0; i < count; i++)
        {
            ExpressionEvaluator eval = evaluatorFor(i);
            var key = new object?[fn.Over.PartitionBy.Count];
            for (int k = 0; k < key.Length; k++)
                key[k] = eval.Evaluate(fn.Over.PartitionBy[k]);

            var groupKey = new GroupKey(key);
            if (!partitions.TryGetValue(groupKey, out List<int>? members))
                partitions[groupKey] = members = [];
            members.Add(i);

            sortKeys[i] = new object?[fn.Over.OrderBy.Count];
            for (int k = 0; k < sortKeys[i].Length; k++)
                sortKeys[i][k] = eval.Evaluate(fn.Over.OrderBy[k].Value);

            arguments[i] = fn.Arguments.Count == 0 ? [] : new object?[fn.Arguments.Count];
            for (int k = 0; k < fn.Arguments.Count; k++)
                arguments[i][k] = fn.Arguments[k] is StarExpression ? null : eval.Evaluate(fn.Arguments[k]);
            if (startOffsets is not null)
                startOffsets[i] = eval.Evaluate(frame!.Start.Offset!);
            if (endOffsets is not null)
                endOffsets[i] = eval.Evaluate(frame!.End.Offset!);
            if (included is not null)
                included[i] = eval.IsTrue(fn.Filter!);
        }

        bool star = fn.Arguments is [StarExpression];
        var call = new WindowCall(
            Star: star,
            Currency: fn.Arguments is [var first] && !star && IsCurrency(first, columns),
            Distinct: fn.Distinct,
            IgnoreNulls: fn.IgnoreNulls == true,
            FromLast: fn.FromLast == true,
            WithinGroup: fn.WithinGroup);

        foreach (List<int> members in partitions.Values)
        {
            // Ties break on the original position, so the window order is stable and a window with no ORDER BY
            // (every row a peer) still numbers rows in input order rather than arbitrarily.
            if (fn.Over.OrderBy.Count > 0)
                members.Sort((a, b) =>
                {
                    int c = CompareEvaluatedKeys(fn.Over.OrderBy, sortKeys[a], sortKeys[b]);
                    return c != 0 ? c : a.CompareTo(b);
                });

            var peerStart = new int[members.Count];
            var peerOrdinal = new int[members.Count];
            for (int i = 1; i < members.Count; i++)
            {
                // With no ORDER BY every row of the partition is a peer of every other.
                bool samePeer = fn.Over.OrderBy.Count == 0
                    || CompareEvaluatedKeys(fn.Over.OrderBy, sortKeys[members[i - 1]], sortKeys[members[i]]) == 0;
                peerStart[i] = samePeer ? peerStart[i - 1] : i;
                peerOrdinal[i] = samePeer ? peerOrdinal[i - 1] : peerOrdinal[i - 1] + 1;
            }

            WindowFrameInput? frameInput = frame is null ? null : new WindowFrameInput(
                frame,
                startOffsets is null ? [] : members.Select(m => startOffsets[m]).ToList(),
                endOffsets is null ? [] : members.Select(m => endOffsets[m]).ToList(),
                frame.Unit == FrameUnit.Range && fn.Over.OrderBy.Count == 1 ? members.Select(m => sortKeys[m][0]).ToList() : null,
                fn.Over.OrderBy is [{ Direction: SortDirection.Descending }]);

            var output = new object?[members.Count];
            def.Evaluate(
                new WindowPartition(peerStart, peerOrdinal, members.Select(m => arguments[m]).ToList(), call, frameInput,
                    included is null ? null : members.Select(m => included[m]).ToList()),
                output);

            // Scatter back to the input positions: the node emits rows in input order, not window order.
            for (int i = 0; i < members.Count; i++)
                values[members[i]][slot] = ExpressionEvaluator.AsColumnType(output[i], declared, currency: false);
        }
    }

    /// <summary>What the call is written with has to be what the function takes (<see cref="WindowOptions"/>): a frame
    /// clause, RESPECT/IGNORE NULLS, FROM FIRST/LAST, DISTINCT.</summary>
    private static void CheckOptions(WindowFunction fn, WindowFunctionDef def)
    {
        void Require(bool written, WindowOptions option, string what)
        {
            if (written && !def.Options.HasFlag(option))
                throw new InvalidOperationException($"{fn.Name} takes no {what}.");
        }
        Require(fn.Over.Frame is not null, WindowOptions.Frame, "window frame");
        Require(fn.IgnoreNulls is not null, WindowOptions.NullTreatment, "RESPECT NULLS or IGNORE NULLS");
        Require(fn.FromLast is not null, WindowOptions.FromLast, "FROM FIRST or FROM LAST");
        Require(fn.Distinct, WindowOptions.Distinct, "DISTINCT");
        Require(fn.Filter is not null, WindowOptions.Filter, "FILTER");
    }

    /// <summary>
    /// The standard's rules for a frame clause that the window, not the syntax, decides: GROUPS counts peer groups,
    /// so needs an ORDER BY; and a RANGE offset measures from one ORDER BY key, so needs exactly one.
    /// </summary>
    private static void CheckFrame(WindowFunction fn, WindowFrame frame)
    {
        if (frame.Unit == FrameUnit.Groups && fn.Over.OrderBy.Count == 0)
            throw new InvalidOperationException("A GROUPS frame needs an ORDER BY in its window.");
        if (frame.Unit == FrameUnit.Range && (frame.Start.Offset ?? frame.End.Offset) is not null && fn.Over.OrderBy.Count != 1)
            throw new InvalidOperationException("A RANGE frame with an offset needs exactly one ORDER BY key in its window.");
    }

    private (IReadOnlyList<OutputColumn> Columns, IEnumerable<object?[]> Rows) ExecuteAggregate(AggregateNode node, EvalScope? outer)
    {
        var (inColumns, inRowsEnum) = Execute(node.Input, outer);

        // Windows over the groups publish a column each, which the projection and ORDER BY read as any other.
        IReadOnlyList<WindowOutput> windows = node.Windows ?? [];
        var (windowTypes, columns) = WindowColumns(windows, inColumns);

        var outTypes = node.Projection
            .Select(item => ExpressionEvaluator.NumberTypeOf(item.Value, columns, e => DeclaredType(e, columns))).ToList();
        var outColumns = node.Projection
            .Select((item, i) => OutputColumn.Computed(
                item.Alias ?? (item.Value is ColumnReference c ? c.Column : $"Expr{i + 1}"),
                DeclaredType(item.Value, columns),
                outTypes[i],
                item.Value))
            .ToList();
        var conversions = node.Projection.Select(item => ChoiceConversion(item.Value, columns)).ToList();

        // A bare `SELECT COUNT(*)` wants the number of rows, not the rows. Everything below materialises the
        // whole input first — which for this shape is the entire cost, and pure waste: holding every decoded row
        // alive at once made counting a table cost more than reading it (measured, `scan.count_star` against
        // `scan.star`). Counting the same sequence lazily is the identical answer with nothing retained.
        if (IsBareCountStar(node))
            return (outColumns, [[CountRows(inRowsEnum)]]);

        var inRows = inRowsEnum.ToList();
        // Aggregates can appear in the projection, HAVING (e.g. HAVING COUNT(*) > 30) and ORDER BY
        // (e.g. ORDER BY COUNT(*)); precompute all of them per group so each instance resolves.
        // A window's arguments and keys may hold aggregates too — RANK() OVER (ORDER BY SUM(x)).
        var aggregateCalls = node.Projection.SelectMany(i => Aggregates(i.Value))
            .Concat(node.Having is { } h ? Aggregates(h) : [])
            .Concat(node.OrderBy.SelectMany(k => Aggregates(k.Value)))
            .Concat(windows.SelectMany(w => w.Function.Expressions().SelectMany(Aggregates)))
            .ToList();

        // The groups HAVING keeps, each with the row that resolves its keys and its aggregates' values.
        var groups = new List<(object?[] KeyRow, Dictionary<FunctionCall, object?> Values, ExpressionEvaluator Eval)>();
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
            groups.Add((keyRow, values, eval));
        }

        // The windows see the groups as their rows, as the standard orders it: after HAVING, before the projection.
        var windowValues = new object?[groups.Count][];
        for (int i = 0; i < groups.Count; i++)
            windowValues[i] = new object?[windows.Count];
        for (int slot = 0; slot < windows.Count; slot++)
            ComputeWindow(windows[slot].Function, groups.Count, i => groups[i].Eval, inColumns, windowValues, slot, windowTypes[slot]);

        // Each output row carries its ORDER BY key values AND its grouping-key values, evaluated in the same
        // group scope as the projection, to sort the groups afterward: by ORDER BY if present, otherwise —
        // matching Access, which returns GROUP BY results ascending by the grouping columns — by the group key
        // (this also makes a TOP-1-over-a-GROUP-BY deterministic, as Access/SQL Server do).
        var outRows = new List<(object?[] Row, object?[] SortKeys, object?[] GroupKeys)>();
        for (int g = 0; g < groups.Count; g++)
        {
            var (keyRow, values, eval) = groups[g];
            if (windows.Count > 0)
                eval = new ExpressionEvaluator(
                    new EvalScope(columns, [.. keyRow, .. windowValues[g]], outer, values), this, _parameters, _session);

            object?[] row = node.Projection
                .Select((item, i) => ExpressionEvaluator.ToResultPlaces(
                    ExpressionEvaluator.AsColumnType(eval.Evaluate(item.Value), conversions[i], currency: false), outTypes[i]))
                .ToArray();
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

    /// <summary>
    /// Whether <paramref name="node"/> is exactly <c>SELECT COUNT(*)</c> over its input — one output column,
    /// nothing to group by, no HAVING and no ORDER BY — so the answer is the input's row count and the rows
    /// themselves are never looked at.
    /// </summary>
    /// <remarks>
    /// Every condition earns its place. A GROUP BY needs the rows to partition them; a HAVING or ORDER BY may
    /// reference other aggregates over them; a second projection item may be any expression at all; and
    /// <c>COUNT(DISTINCT …)</c> or <c>COUNT(col)</c> counts values, not rows, so only the star form qualifies.
    /// </remarks>
    private static bool IsBareCountStar(AggregateNode node) =>
        node.GroupBy.Count == 0
        && node.Having is null
        && node.OrderBy.Count == 0
        && node.Projection is [{ Value: FunctionCall { Distinct: false, Filter: null, Arguments: [StarExpression] } call }]
        && string.Equals(call.Name, "COUNT", StringComparison.OrdinalIgnoreCase);

    /// <summary>Counts a row sequence without retaining it. COUNT is an Access Long Integer, so the count is an
    /// <see cref="int"/> — the same type <see cref="ComputeAggregate"/> returns, which EF reads with GetInt32.</summary>
    private static int CountRows(IEnumerable<object?[]> rows)
    {
        int count = 0;
        foreach (object?[] _ in rows)
            count++;
        return count;
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
        ExpressionEvaluator.ValidateArity(name, call.Arguments.Count);
        Expression? arg = call.Arguments.Count > 0 ? call.Arguments[0] : null;

        // FILTER (WHERE …) narrows the group before anything else looks at it — COUNT(*) included.
        if (call.Filter is { } filter)
            group = group.Where(r => Eval(columns, r, outer).IsTrue(filter)).ToList();

        // COUNT(*) counts rows; DISTINCT is meaningless there (and EF never emits it).
        if (name == "COUNT" && arg is StarExpression or null)
            return group.Count;

        // FIRST/LAST return the argument's value from the first/last row of the group in scan order — NOT
        // null-filtered (verified vs ACE: First over a leading NULL row returns NULL).
        if (name == "FIRST")
            return group.Count == 0 ? null : Eval(columns, group[0], outer).Evaluate(arg!);
        if (name == "LAST")
            return group.Count == 0 ? null : Eval(columns, group[^1], outer).Evaluate(arg!);

        if (call.WithinGroup is { } directions)
        {
            if (group.Count == 0)
                return null;
            if (name == "LISTAGG")
            {
                IReadOnlyList<Expression> keys = call.WithinGroupKeys;
                return ListAgg.Of(
                    group.Select(r =>
                    {
                        ExpressionEvaluator e = Eval(columns, r, outer);
                        return (e.Evaluate(call.Arguments[0]), keys.Select(k => e.Evaluate(k)).ToArray());
                    }),
                    call.Arguments.Count - keys.Count == 2 ? (string)((LiteralExpression)call.Arguments[1]).Value! : "",
                    directions,
                    call.Distinct);
            }
            // The fraction is the group's, so any row gives it; the standard makes it a constant.
            return Percentile.Of(name,
                group.Select(r => Eval(columns, r, outer).Evaluate(call.Arguments[1])),
                Eval(columns, group[0], outer).Evaluate(call.Arguments[0]),
                directions[0]);
        }

        // A binary set function reads a pair from each row; the standard gives it no DISTINCT.
        if (RunningAggregate.IsPair(name))
        {
            if (call.Distinct)
                throw new NotSupportedException($"{call.Name} takes no DISTINCT.");
            var pair = new RunningAggregate(name, countRows: false, currency: false);
            foreach (object?[] row in group)
            {
                ExpressionEvaluator rowEval = Eval(columns, row, outer);
                pair.AddPair(rowEval.Evaluate(call.Arguments[0]), rowEval.Evaluate(call.Arguments[1]));
            }
            return pair.Result;
        }

        IEnumerable<object?> values = group.Select(r => Eval(columns, r, outer).Evaluate(arg!));
        // COUNT(DISTINCT)/SUM(DISTINCT)/… aggregate the distinct set of the argument's values. MIN/MAX are
        // unaffected by dedup, but applying it uniformly keeps the one code path.
        if (call.Distinct)
            values = DistinctValues(values.Where(v => v is not null));

        var aggregate = new RunningAggregate(name, countRows: false, currency: IsCurrency(arg!, columns));
        foreach (object? value in values)
            aggregate.Add(value);
        return aggregate.Result;
    }

    /// <summary>Whether an aggregate's argument is a Currency, which the statistical aggregates square exactly.</summary>
    private bool IsCurrency(Expression argument, IReadOnlyList<OutputColumn> columns) =>
        ExpressionEvaluator.NumberTypeOf(argument, columns, e => DeclaredType(e, columns)).Class == NumberClass.Currency;

    private static IEnumerable<FunctionCall> Aggregates(Expression e)
    {
        switch (e)
        {
            case FunctionCall f when QueryPlanner.IsAggregate(f.Name):
                yield return f;
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
            // Operands include both halves of every CASE arm, and the ELSE. An aggregate in a CASE is computed for
            // the group up front and handed to the evaluator by reference — the standard specifies the same order,
            // that aggregates in a WHEN are evaluated before the CASE rather than by it. Conditions matter as much
            // as results: `HAVING CASE WHEN COUNT(*) > 1 THEN …` carries the aggregate in the condition.
            default:
                foreach (FunctionCall a in e.Operands()?.SelectMany(Aggregates) ?? [])
                    yield return a;
                break;
        }
    }

    /// <summary>All aggregate calls anywhere in a subquery's clauses (projection, WHERE, HAVING, GROUP BY,
    /// ORDER BY) — used to surface outer aggregates that a correlated subquery references.</summary>
    /// <summary>The aggregate calls anywhere in a subquery. A set operation is walked into on both sides rather
    /// than skipped: an aggregate over an OUTER column can sit in either arm, and missing one leaves it
    /// uncomputed for the group — the evaluator would then fail to resolve it, so declining is not safe here the
    /// way it is for the decorrelation rewrites.</summary>
    private static IEnumerable<FunctionCall> AggregatesInSelect(SqlStatement statement) => statement switch
    {
        SelectStatement s => s.Projection.SelectMany(i => Aggregates(i.Value))
            .Concat(s.Where is { } w ? Aggregates(w) : [])
            .Concat(s.Having is { } h ? Aggregates(h) : [])
            .Concat(s.GroupBy.SelectMany(Aggregates))
            .Concat(s.OrderBy.SelectMany(o => Aggregates(o.Value))),
        SetOperationStatement so => AggregatesInSelect(so.Left).Concat(AggregatesInSelect(so.Right)),
        ValuesStatement v => v.Rows.SelectMany(r => r.SelectMany(Aggregates)),
        _ => [],
    };

    /// <summary>Groups by structural equality of the key value tuple.</summary>
    // DISTINCT / GROUP BY / INTERSECT / EXCEPT key. String keys use Access text semantics — case-insensitive
    // and trailing-space-insensitive — so 'London' and 'LONDON ' group together as Access does.
    internal sealed class GroupKey(object?[] values) : IEquatable<GroupKey>
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
