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
    private readonly QueryExecutor _executor;

    public QueryEngine(JetDatabase database, ISqlParser? parser = null)
    {
        _database = database;
        _parser = parser ?? new AccessSqlParser();
        _binder = new Binder(new CatalogSchemaProvider(database.Catalog));
        _executor = new QueryExecutor(database);
    }

    public JetDatabase Database => _database;

    public ResultSet ExecuteQuery(string sql)
    {
        var plan = Compile(sql);
        return _executor.ExecuteQuery(plan);
    }

    public int ExecuteNonQuery(string sql)
    {
        var plan = Compile(sql);
        return _executor.ExecuteNonQuery(plan);
    }

    private Plan.PlanNode Compile(string sql)
    {
        SqlStatement ast = _parser.ParseStatement(sql);
        BoundStatement bound = _binder.Bind(ast);
        return _planner.Plan(bound);
    }
}
