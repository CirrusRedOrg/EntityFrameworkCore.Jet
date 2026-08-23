using LibRed.Catalog;
using LibRed.IO;
using LibRed.Engine.Execution;
using LibRed.Engine.Plan;
using LibRed.Engine.Planning;
using LibRed.Engine.Schema;
using LibRed.Sql.Ast;
using LibRed.Sql.Binding;
using LibRed.Sql.Parsing;

namespace LibRed.Engine;

/// <summary>
/// The engine facade: wires the full pipeline parse → bind → plan → execute over an
/// open <see cref="JetDatabase"/>. This is what the ADO provider sits on top of.
/// </summary>
public sealed class QueryEngine
{
    private readonly JetDatabase _database;
    private readonly ISqlParser _parser;
    private readonly Binder _binder;
    private readonly QueryPlanner _planner = new();
    private readonly SessionState _session = new();

    public QueryEngine(JetDatabase database, ISqlParser? parser = null)
    {
        _database = database;
        _parser = parser ?? new AntlrSqlParser();
        _binder = new Binder(new CatalogSchemaProvider(database.Catalog));
    }

    public JetDatabase Database => _database;

    /// <summary>Connection-scoped <c>@@ROWCOUNT</c>/<c>@@IDENTITY</c> state, shared across the statements
    /// of a batch (which the ADO command layer splits and runs through this one engine).</summary>
    public SessionState Session => _session;

    public ResultSet ExecuteQuery(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        // Parse outside the gate — it touches no pages, and holding a file-wide scope across it would
        // serialize parsing for no isolation benefit. The parsed shape then picks the scope.
        SqlStatement parsed = _parser.ParseStatement(sql);
        return Scoped(parsed, () => ExecuteQueryCore(parsed, parameters));
    }

    /// <summary>Runs <paramref name="action"/> in the page scope <paramref name="statement"/> needs: shared
    /// (concurrent with other readers) for a statement that cannot write, exclusive otherwise so its pages
    /// publish as one unit. Anything not provably read-only takes the exclusive scope — a shared scope that
    /// then writes is rejected, not silently upgraded.</summary>
    private T Scoped<T>(SqlStatement statement, Func<T> action) =>
        // A SELECT with INTO is a make-table query: it looks like a read and writes a table, so it takes the
        // exclusive scope with the other writers. The guard above caught this rather than letting it through.
        statement is SelectStatement { Into: null } or SetOperationStatement or SystemVariableSelectStatement
            ? _database.ReadConsistent(action)
            : _database.WriteExclusive(action);

    private ResultSet ExecuteQueryCore(SqlStatement parsed, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parsed is ExecuteStatement exec) return ExecuteProcedure(exec, parameters).Rows;
        // A make-table query is an action query however it was invoked: run it and return nothing, rather
        // than handing the caller the rows it just wrote into a table.
        if (parsed is SelectStatement { Into: not null }) return Route(parsed, parameters).Rows;
        SqlStatement ast = ViewExpander.Expand(parsed, _database.Catalog.Views, _parser);
        BoundStatement bound = _binder.Bind(ast);
        if (bound.Statement is TransactionControlStatement txnControl)
        {
            RunTransactionControl(txnControl.Kind);
            return ResultSet.Empty;
        }
        var executor = new QueryExecutor(_database, parameters, _session);
        return bound.Statement is SystemVariableSelectStatement sysSelect
            ? executor.ExecuteSystemVariableSelect(sysSelect)
            : executor.ExecuteQuery(PlanWithIndexes(bound));
    }

    public int ExecuteNonQuery(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
        => Execute(sql, parameters).RecordsAffected;

    /// <summary>Executes a stored action query (a CREATE PROCEDURE body that is not a SELECT) by name — the
    /// read-back counterpart of <see cref="JetDatabase.CreateActionQuery"/>. The query is reconstructed from
    /// its catalog rows and run; a kind LibRed cannot execute (e.g. INSERT … SELECT) throws
    /// <see cref="NotSupportedException"/>, and an unknown name throws <see cref="InvalidOperationException"/>.</summary>
    public int ExecuteStoredActionQuery(string name)
    {
        if (!_database.Catalog.ActionQueries.TryGetValue(name, out StoredActionQuery? query))
            throw new InvalidOperationException($"No stored action query named '{name}'.");
        if (query.Sql is null)
            throw new NotSupportedException(query.UnsupportedReason ?? $"Stored query '{name}' cannot be executed by LibRed yet.");
        return ExecuteNonQuery(query.Sql);
    }

    /// <summary>
    /// Parses and binds once, then routes by statement kind: a query (<c>SELECT</c> / set operation)
    /// is planned and executed into a <see cref="ResultSet"/>; a DML/DDL statement (<c>INSERT</c>,
    /// <c>CREATE TABLE</c>) runs directly against storage and reports rows affected. This is the
    /// single entry point the ADO layer uses so that <c>ExecuteReader</c> works for *any* statement —
    /// EF Core executes inserts through the reader path to read results back.
    /// </summary>
    public CommandResult Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        SqlStatement parsed = _parser.ParseStatement(sql);
        return Scoped(parsed, () => ExecuteCore(parsed, parameters));
    }

    private CommandResult ExecuteCore(SqlStatement parsed, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parsed is ExecuteStatement exec) return ExecuteProcedure(exec, parameters);
        SqlStatement ast = ViewExpander.Expand(parsed, _database.Catalog.Views, _parser);
        BoundStatement bound = _binder.Bind(ast);
        return Route(bound.Statement, parameters);
    }

    /// <summary>Drives the nested-transaction controller on the shared <see cref="JetDatabase"/> — the same L2
    /// the ADO front door uses, so the two can't open parallel transactions.</summary>
    private void RunTransactionControl(TransactionControlKind kind)
    {
        switch (kind)
        {
            case TransactionControlKind.Begin: _database.BeginNested(); break;
            case TransactionControlKind.Commit: _database.CommitNested(); break;
            case TransactionControlKind.Rollback: _database.RollbackNested(); break;
        }
    }

    /// <summary>Routes an already-bound statement to the right executor (system-variable select, IF…THEN,
    /// query, or a DML/DDL non-query). Split out so IF…THEN can run its guarded body through the same path.</summary>
    private CommandResult Route(SqlStatement statement, IReadOnlyDictionary<string, object?>? parameters)
    {
        switch (statement)
        {
            case SystemVariableSelectStatement sysSelect:
                return new CommandResult(
                    new QueryExecutor(_database, parameters, _session).ExecuteSystemVariableSelect(sysSelect), -1);
            case IfThenStatement ifThen:
                return ExecuteIfThen(ifThen, parameters);
            // A make-table query looks like a SELECT but IS an action query: it writes a table and returns no
            // rows, so it belongs below with the writing statements — inside the implicit transaction, so a
            // failure part-way cannot leave a half-populated table behind.
            case SelectStatement { Into: not null }:
                return ExecuteWritingStatement(statement, parameters);
            case SelectStatement or SetOperationStatement:
                return new CommandResult(
                    new QueryExecutor(_database, parameters, _session).ExecuteQuery(PlanWithIndexes(new BoundStatement(statement))), -1);
            case TransactionControlStatement txnControl:
                // Transaction control manages the transaction itself — it must NOT go through the implicit
                // per-statement wrap below (which would open/commit a transaction around it).
                RunTransactionControl(txnControl.Kind);
                return new CommandResult(ResultSet.Empty, 0);
            default:
                return ExecuteWritingStatement(statement, parameters);
        }
    }

    /// <summary>
    /// Runs a DML/DDL statement so that it is atomic on its own: with no user transaction open it runs inside
    /// an implicit transaction (commit on success, roll back on any failure — a late constraint/I/O error can
    /// never leave a half-written row, index, LVAL chain, or catalog entry); inside a user transaction it runs
    /// under a savepoint, so a statement failure undoes just that statement and leaves the user's transaction
    /// intact. Only writing statements reach here — reads never open a transaction.
    /// </summary>
    private CommandResult ExecuteWritingStatement(SqlStatement statement, IReadOnlyDictionary<string, object?>? parameters)
    {
        int Run() => new StatementExecutor(_database, parameters, _parser, _session).Execute(statement);

        if (_database.InTransaction)
        {
            Savepoint savepoint = _database.CreateSavepoint();
            try
            {
                int affected = Run();
                _database.ReleaseSavepoint(savepoint);
                return new CommandResult(ResultSet.Empty, affected);
            }
            catch
            {
                _database.RollbackToSavepoint(savepoint);
                throw;
            }
        }

        _database.BeginTransaction();
        try
        {
            int affected = Run();
            _database.Commit(flush: false); // autocommit: durability stays on Dispose, as before transactions existed
            return new CommandResult(ResultSet.Empty, affected);
        }
        catch
        {
            _database.Rollback();
            throw;
        }
    }

    /// <summary>Evaluates the condition subquery; runs the guarded THEN statement only when the EXISTS test holds
    /// (or, for NOT EXISTS, when it doesn't). @@ROWCOUNT reflects the THEN, or 0 when the guard skips it.</summary>
    private CommandResult ExecuteIfThen(IfThenStatement ifThen, IReadOnlyDictionary<string, object?>? parameters)
    {
        var condPlan = IndexSelection.Apply(QueryPlanner.PlanSelect(ifThen.Condition), _database.Catalog);
        bool hasRows = new QueryExecutor(_database, parameters, _session).ExecuteQuery(condPlan).Rows.Any();
        if (hasRows == ifThen.Negated)
            return new CommandResult(ResultSet.Empty, 0); // guard not satisfied — skip the body
        return Route(ifThen.Then, parameters);
    }

    /// <summary>Plans a bound statement, then applies index selection (turning scans into index seeks where a
    /// predicate allows).</summary>
    private PlanNode PlanWithIndexes(BoundStatement bound) =>
        IndexSelection.Apply(_planner.Plan(bound), _database.Catalog);

    /// <summary>The optimised plan for a query — exposed for tests to assert the chosen access path/strategy
    /// (e.g. that an unindexed equi-join becomes a hash join).</summary>
    internal PlanNode PlanFor(string sql)
    {
        SqlStatement ast = ViewExpander.Expand(_parser.ParseStatement(sql), _database.Catalog.Views, _parser);
        return PlanWithIndexes(_binder.Bind(ast));
    }

    /// <summary>
    /// Runs an <c>EXECUTE|EXEC procedure arg, …</c>: resolves the stored procedure/query by name, binds the
    /// positional argument values to its declared parameters (in declaration order), and runs it — a stored
    /// SELECT returns its rows, a stored action query (INSERT/DDL) returns its rows-affected count.
    /// </summary>
    private CommandResult ExecuteProcedure(ExecuteStatement exec, IReadOnlyDictionary<string, object?>? parameters)
    {
        JetCatalog catalog = _database.Catalog;
        var bag = new ParameterBag(parameters);
        var executor = new QueryExecutor(_database, parameters, _session);
        var evaluator = new ExpressionEvaluator(new EvalScope([], [], null), executor, bag, _session);

        IReadOnlyList<string> paramNames = catalog.QueryParameters.TryGetValue(exec.Procedure, out var ns)
            ? ns : [];

        // Access EXEC arguments take three shapes (EF emits all of them):
        //   procParam = value  → a NAMED argument (bind the proc's named parameter to the value)
        //   name               → a bare identifier REFERENCING a supplied command parameter (`EXEC p CustomerID`)
        //   literal / @param   → a positional value
        var named = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<object?>();
        foreach (Expression arg in exec.Arguments)
        {
            switch (arg)
            {
                case BinaryExpression { Operator: BinaryOperator.Equal, Left: ColumnReference { Table: null } target } eq:
                    named[target.Column] = evaluator.Evaluate(eq.Right);
                    break;
                case ColumnReference { Table: null } paramRef:
                    positional.Add(bag.Resolve(paramRef.Column));
                    break;
                default:
                    positional.Add(evaluator.Evaluate(arg));
                    break;
            }
        }

        Dictionary<string, object?> args;
        if (named.Count > 0)
        {
            if (positional.Count > 0)
                throw new InvalidOperationException(
                    $"Procedure '{exec.Procedure}' was executed with a mix of named and positional arguments.");
            args = named; // keyed by the procedure's parameter names directly
        }
        else
        {
            if (positional.Count != paramNames.Count)
                throw new InvalidOperationException(
                    $"Procedure '{exec.Procedure}' declares {paramNames.Count} parameter(s) but was executed with {positional.Count} argument(s).");
            args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < paramNames.Count; i++) args[paramNames[i]] = positional[i];
        }

        // A stored SELECT (with its PARAMETERS clause) → rows; a stored action query → rows-affected.
        if (catalog.Views.TryGetValue(exec.Procedure, out string? viewSql))
            return Execute(viewSql, args);

        if (catalog.ActionQueries.TryGetValue(exec.Procedure, out StoredActionQuery? action))
        {
            if (action.Sql is null)
                throw new NotSupportedException(
                    action.UnsupportedReason ?? $"Stored query '{exec.Procedure}' cannot be executed by LibRed yet.");
            return new CommandResult(ResultSet.Empty, ExecuteNonQuery(action.Sql, args));
        }

        throw new InvalidOperationException($"No stored procedure or query named '{exec.Procedure}'.");
    }
}

/// <summary>The outcome of <see cref="QueryEngine.Execute"/>: query rows (with
/// <see cref="RecordsAffected"/> = -1) or an empty set with the DML rows-affected count.</summary>
public sealed record CommandResult(ResultSet Rows, int RecordsAffected);
