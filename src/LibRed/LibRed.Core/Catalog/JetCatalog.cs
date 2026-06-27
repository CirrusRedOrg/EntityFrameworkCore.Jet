using LibRed.IO;

namespace LibRed.Catalog;

/// <summary>
/// Reads the system catalog (<c>MSysObjects</c>, <c>MSysColumns</c>, …) to enumerate
/// the user tables, queries and relationships in a database.
/// </summary>
/// <remarks>
/// Bootstrap order: page 0 (database definition) → the catalog table lives at a
/// well-known page → read MSysObjects as an ordinary table to discover everything else.
/// </remarks>
public sealed class JetCatalog(PageChannel channel)
{
    private readonly PageChannel _channel = channel;
    private List<TableDef>? _tables;

    /// <summary>All user (non-system) tables in the database.</summary>
    public IReadOnlyList<TableDef> Tables => _tables ??= LoadTables();

    public TableDef? FindTable(string name) =>
        Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private List<TableDef> LoadTables()
    {
        // TODO: parse MSysObjects to recover (Name, Id/page, Type, Flags) rows, filter to
        // user tables, then resolve each table's TDEF page into a TableDef.
        _ = _channel;
        return [];
    }
}
