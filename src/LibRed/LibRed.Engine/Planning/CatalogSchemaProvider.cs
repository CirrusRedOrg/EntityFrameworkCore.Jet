using LibRed.Catalog;
using LibRed.Sql.Binding;

namespace LibRed.Engine.Planning;

/// <summary>
/// Adapts the Core <see cref="JetCatalog"/> to the SQL binder's <see cref="ISchemaProvider"/>.
/// This is the inverted dependency in action: LibRed.Sql defines the interface,
/// LibRed.Engine supplies the storage-backed implementation.
/// </summary>
public sealed class CatalogSchemaProvider(JetCatalog catalog) : ISchemaProvider
{
    private readonly JetCatalog _catalog = catalog;

    public ITableSchema? GetTable(string name)
    {
        TableDef? def = _catalog.FindTable(name);
        return def is null ? null : new TableSchema(def);
    }

    private sealed class TableSchema(TableDef def) : ITableSchema
    {
        public string Name => def.Name;
        public IReadOnlyList<IColumnSchema> Columns { get; } =
            def.Columns.Select(c => (IColumnSchema)new ColumnSchema(c)).ToList();

        public IColumnSchema? FindColumn(string name) =>
            Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ColumnSchema(ColumnDef col) : IColumnSchema
    {
        public string Name => col.Name;
        public bool IsNullable => col.IsNullable;
        public Type ClrType => JetClrTypeMap.ToClrType(col.Type);
    }
}
