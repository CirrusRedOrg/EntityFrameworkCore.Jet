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
        // The magic INFORMATION_SCHEMA.<view> tables are virtual (no catalog TableDef) — expose a synthetic
        // schema so binding validates; the executor materialises their rows from the catalog.
        if (InformationSchema.IsInformationSchema(name))
            return new InformationSchemaTable(name);

        TableDef? def = _catalog.FindTable(name);
        return def is null ? null : new TableSchema(def);
    }

    private sealed class InformationSchemaTable(string name) : ITableSchema
    {
        public string Name => name;
        public IReadOnlyList<IColumnSchema> Columns { get; } =
            InformationSchema.ColumnsOf(name).Select(c => (IColumnSchema)new VirtualColumn(c)).ToList();
        public IColumnSchema? FindColumn(string col) =>
            Columns.FirstOrDefault(c => string.Equals(c.Name, col, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class VirtualColumn(string name) : IColumnSchema
    {
        public string Name => name;
        public bool IsNullable => true;
        public Type ClrType => typeof(object);
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
