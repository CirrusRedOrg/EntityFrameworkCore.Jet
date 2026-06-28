using LibRed.Sql.Ast;

namespace LibRed.Sql.Binding;

/// <summary>
/// Resolves names in a parsed statement against an <see cref="ISchemaProvider"/>: verifies
/// the table and every referenced column exists, so the planner can assume validity.
/// </summary>
public sealed class Binder(ISchemaProvider schema)
{
    private readonly ISchemaProvider _schema = schema;

    public BoundStatement Bind(SqlStatement statement)
    {
        if (statement is SelectStatement select)
            BindSelect(select);

        return new BoundStatement(statement);
    }

    private void BindSelect(SelectStatement select)
    {
        if (select.From is not NamedTable named)
            throw new SqlBindException("Only a single named table is supported in FROM.");

        ITableSchema table = _schema.GetTable(named.Name)
            ?? throw new SqlBindException($"Table '{named.Name}' does not exist.");

        var referenced = select.Projection.SelectMany(i => ColumnsOf(i.Value));
        if (select.Where is not null)
            referenced = referenced.Concat(ColumnsOf(select.Where));

        foreach (ColumnReference column in referenced)
            if (table.FindColumn(column.Column) is null)
                throw new SqlBindException($"Column '{column.Column}' does not exist in table '{table.Name}'.");
    }

    private static IEnumerable<ColumnReference> ColumnsOf(Expression expression) => expression switch
    {
        ColumnReference c => [c],
        BinaryExpression b => ColumnsOf(b.Left).Concat(ColumnsOf(b.Right)),
        UnaryExpression u => ColumnsOf(u.Operand),
        FunctionCall f => f.Arguments.SelectMany(ColumnsOf),
        _ => [],
    };
}
