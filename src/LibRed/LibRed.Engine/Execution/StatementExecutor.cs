using LibRed.Catalog;
using LibRed.Sql.Ast;
using LibRed.Storage;

namespace LibRed.Engine.Execution;

/// <summary>
/// Executes non-query statements (DDL/DML) against the storage layer: CREATE TABLE and INSERT.
/// Returns the number of affected rows (0 for DDL).
/// </summary>
internal sealed class StatementExecutor(JetDatabase database, IReadOnlyDictionary<string, object?>? parameters)
{
    private readonly JetDatabase _database = database;
    private readonly ParameterBag _parameters = new(parameters);
    // For evaluating VALUES expressions (literals, parameters, and any scalar subqueries).
    private readonly QueryExecutor _scalarRunner = new(database, parameters);

    public int Execute(SqlStatement statement) => statement switch
    {
        CreateTableStatement create => ExecuteCreateTable(create),
        InsertStatement insert => ExecuteInsert(insert),
        _ => throw new NotSupportedException($"{statement.GetType().Name} cannot be executed as a non-query."),
    };

    private int ExecuteCreateTable(CreateTableStatement statement)
    {
        var columns = statement.Columns.Select(AccessTypeMapper.ToColumnSpec).ToList();
        IReadOnlyList<string>? primaryKey = statement.PrimaryKey.Count > 0 ? statement.PrimaryKey : null;
        _database.CreateTable(statement.Table, columns, primaryKey);
        return 0;
    }

    private int ExecuteInsert(InsertStatement statement)
    {
        Table table = _database.OpenTable(statement.Table);
        var columns = table.Definition.Columns;

        // Target columns: the explicit list, or all columns in order.
        IReadOnlyList<string> targets = statement.Columns.Count > 0
            ? statement.Columns
            : columns.Select(c => c.Name).ToList();

        var evaluator = new ExpressionEvaluator(
            new EvalScope([], [], null), _scalarRunner, parameters: _parameters);

        int affected = 0;
        foreach (IReadOnlyList<Expression> rowExprs in statement.Rows)
        {
            if (rowExprs.Count != targets.Count)
                throw new InvalidOperationException(
                    $"INSERT has {rowExprs.Count} values but {targets.Count} target columns.");

            var values = new object?[columns.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                ColumnDef column = table.Definition.FindColumn(targets[i])
                    ?? throw new InvalidOperationException($"Column '{targets[i]}' does not exist in '{statement.Table}'.");
                values[column.Index] = evaluator.Evaluate(rowExprs[i]);
            }

            table.Insert(values);
            affected++;
        }

        return affected;
    }
}
