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

    public QueryEngine(JetDatabase database, ISqlParser? parser = null)
    {
        _database = database;
        _parser = parser ?? new AntlrSqlParser();
        _binder = new Binder(new CatalogSchemaProvider(database.Catalog));
    }

    public JetDatabase Database => _database;

    public ResultSet ExecuteQuery(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        var plan = Compile(sql);
        return new QueryExecutor(_database, parameters).ExecuteQuery(plan);
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
        SqlStatement ast = ViewExpander.Expand(_parser.ParseStatement(sql), _database.Catalog.Views, _parser);
        BoundStatement bound = _binder.Bind(ast);

        if (bound.Statement is SelectStatement or SetOperationStatement)
        {
            ResultSet rows = new QueryExecutor(_database, parameters).ExecuteQuery(_planner.Plan(bound));
            return new CommandResult(rows, RecordsAffected: -1);
        }

        int affected = new StatementExecutor(_database, parameters, _parser).Execute(bound.Statement);
        return new CommandResult(ResultSet.Empty, affected);
    }

    private Plan.PlanNode Compile(string sql)
    {
        SqlStatement ast = ViewExpander.Expand(_parser.ParseStatement(sql), _database.Catalog.Views, _parser);
        BoundStatement bound = _binder.Bind(ast);
        return _planner.Plan(bound);
    }
}

/// <summary>The outcome of <see cref="QueryEngine.Execute"/>: query rows (with
/// <see cref="RecordsAffected"/> = -1) or an empty set with the DML rows-affected count.</summary>
public sealed record CommandResult(ResultSet Rows, int RecordsAffected);
