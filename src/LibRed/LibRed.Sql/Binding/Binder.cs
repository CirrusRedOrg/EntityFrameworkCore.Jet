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
        ValidateSources(select.From);

        // Column-existence validation only for the simple single-table case; with joins and
        // derived tables, columns are alias-qualified and resolved at execution time.
        if (select.From is NamedTable named)
        {
            ITableSchema table = _schema.GetTable(named.Name)!;

            var referenced = select.Projection.SelectMany(i => ColumnsOf(i.Value));
            if (select.Where is not null)
                referenced = referenced.Concat(ColumnsOf(select.Where));

            foreach (ColumnReference column in referenced)
                if (table.FindColumn(column.Column) is null)
                    throw new SqlBindException($"Column '{column.Column}' does not exist in table '{table.Name}'.");
        }
    }

    private void ValidateSources(TableReference from)
    {
        switch (from)
        {
            case NamedTable n when _schema.GetTable(n.Name) is null:
                throw new SqlBindException($"Table '{n.Name}' does not exist.");
            case JoinTable j:
                ValidateSources(j.Left);
                ValidateSources(j.Right);
                break;
            case SubqueryTable s:
                BindSelect(s.Query);
                break;
        }
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
