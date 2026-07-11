using LibRed.Catalog;
using LibRed.Engine.Execution;
using LibRed.Engine.Planning;
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
        SqlStatement parsed = _parser.ParseStatement(sql);
        if (parsed is ExecuteStatement exec) return ExecuteProcedure(exec, parameters).Rows;
        SqlStatement ast = ViewExpander.Expand(parsed, _database.Catalog.Views, _parser);
        BoundStatement bound = _binder.Bind(ast);
        var executor = new QueryExecutor(_database, parameters, _session);
        return bound.Statement is SystemVariableSelectStatement sysSelect
            ? executor.ExecuteSystemVariableSelect(sysSelect)
            : executor.ExecuteQuery(_planner.Plan(bound));
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
        if (parsed is ExecuteStatement exec) return ExecuteProcedure(exec, parameters);
        SqlStatement ast = ViewExpander.Expand(parsed, _database.Catalog.Views, _parser);
        BoundStatement bound = _binder.Bind(ast);

        if (bound.Statement is SystemVariableSelectStatement sysSelect)
        {
            ResultSet rows = new QueryExecutor(_database, parameters, _session).ExecuteSystemVariableSelect(sysSelect);
            return new CommandResult(rows, RecordsAffected: -1);
        }

        if (bound.Statement is SelectStatement or SetOperationStatement)
        {
            ResultSet rows = new QueryExecutor(_database, parameters, _session).ExecuteQuery(_planner.Plan(bound));
            return new CommandResult(rows, RecordsAffected: -1);
        }

        int affected = new StatementExecutor(_database, parameters, _parser, _session).Execute(bound.Statement);
        return new CommandResult(ResultSet.Empty, affected);
    }

    /// <summary>
    /// Runs an <c>EXECUTE|EXEC procedure arg, …</c>: resolves the stored procedure/query by name, binds the
    /// positional argument values to its declared parameters (in declaration order), and runs it — a stored
    /// SELECT returns its rows, a stored action query (INSERT/DDL) returns its rows-affected count.
    /// </summary>
    private CommandResult ExecuteProcedure(ExecuteStatement exec, IReadOnlyDictionary<string, object?>? parameters)
    {
        JetCatalog catalog = _database.Catalog;

        // Evaluate the positional arguments (literals, or @params from the caller's command).
        var executor = new QueryExecutor(_database, parameters, _session);
        var evaluator = new ExpressionEvaluator(new EvalScope([], [], null), executor,
            new ParameterBag(parameters), _session);
        var argValues = exec.Arguments.Select(evaluator.Evaluate).ToList();

        IReadOnlyList<string> paramNames = catalog.QueryParameters.TryGetValue(exec.Procedure, out var ns)
            ? ns : [];
        if (argValues.Count != paramNames.Count)
            throw new InvalidOperationException(
                $"Procedure '{exec.Procedure}' declares {paramNames.Count} parameter(s) but was executed with {argValues.Count} argument(s).");

        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < paramNames.Count; i++) args[paramNames[i]] = argValues[i];

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
