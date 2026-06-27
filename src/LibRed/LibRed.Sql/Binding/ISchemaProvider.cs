namespace LibRed.Sql.Binding;

/// <summary>
/// The schema information the binder needs to resolve and type-check a statement.
/// Defined here (not in Core) to keep the dependency inverted: LibRed.Sql has no
/// reference to the storage engine. The Engine project supplies an implementation
/// backed by the Jet catalog.
/// </summary>
public interface ISchemaProvider
{
    /// <summary>Resolves a table by name, or returns <c>null</c> if it does not exist.</summary>
    ITableSchema? GetTable(string name);
}

/// <summary>Minimal table shape needed for binding.</summary>
public interface ITableSchema
{
    string Name { get; }
    IReadOnlyList<IColumnSchema> Columns { get; }
    IColumnSchema? FindColumn(string name);
}

/// <summary>Minimal column shape needed for binding.</summary>
public interface IColumnSchema
{
    string Name { get; }

    /// <summary>The CLR type the column maps to, used for expression type checking.</summary>
    Type ClrType { get; }

    bool IsNullable { get; }
}
