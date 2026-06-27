using LibRed.Sql.Ast;

namespace LibRed.Sql.Binding;

/// <summary>
/// Resolves names and types in a parsed statement against an <see cref="ISchemaProvider"/>:
/// verifies tables/columns exist, attaches column types, expands <c>SELECT *</c>, and
/// validates expression operand types. Produces a <see cref="BoundStatement"/> the
/// engine can plan without re-checking the schema.
/// </summary>
public sealed class Binder(ISchemaProvider schema)
{
    private readonly ISchemaProvider _schema = schema;

    public BoundStatement Bind(SqlStatement statement)
    {
        // TODO: resolve table references, expand projections, type-check expressions.
        _ = _schema;
        return new BoundStatement(statement);
    }
}
