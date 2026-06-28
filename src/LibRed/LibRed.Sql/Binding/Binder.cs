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
        BindStatement(statement);
        return new BoundStatement(statement);
    }

    private void BindStatement(SqlStatement statement)
    {
        switch (statement)
        {
            case SelectStatement select:
                BindSelect(select);
                break;
            case SetOperationStatement set:
                BindStatement(set.Left);
                BindStatement(set.Right);
                break;
        }
    }

    private void BindSelect(SelectStatement select)
    {
        // Validate that referenced tables exist. Column existence is resolved at execution
        // time, because columns may be alias-qualified across joins/derived tables or
        // correlated to an outer query, which single-table binding cannot see.
        ValidateSources(select.From);
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
}
