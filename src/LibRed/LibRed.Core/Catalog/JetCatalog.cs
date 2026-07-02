using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;

namespace LibRed.Catalog;

/// <summary>
/// Reads the system catalog (<c>MSysObjects</c>) to enumerate the tables in a database.
/// </summary>
/// <remarks>
/// Bootstrap: MSysObjects' own TDEF is at a fixed page (<see cref="Formats.JetFormatBase.CatalogPage"/>),
/// so we build a <see cref="TableDef"/> for it from that page and read its rows like any
/// other table. For a table object, the row's <c>Id</c> is its TDEF page number.
/// </remarks>
public sealed class JetCatalog(PageChannel channel)
{
    /// <summary>MSysObjects.Type value for a table object.</summary>
    private const short ObjectTypeTable = 1;

    /// <summary>MSysObjects.Flags bits marking a system object (`0x80000000` system, `0x00000002`
    /// system attribute).</summary>
    private const uint SystemObjectFlags = 0x80000002;

    /// <summary>MSysObjects.Flags bit marking a <b>hidden</b> object (`0x08`, observed on Access's
    /// nav-pane tables and on EFCore.Jet's `#Dual` helper). Access excludes hidden objects from its
    /// user-table list, so we treat them as non-user too.</summary>
    private const uint HiddenObjectFlags = 0x00000008;

    // MSysRelationships.grbit flags (DAO RelationAttributeEnum).
    private const int RelationshipDontEnforce = 0x00000002;
    private const int RelationshipUpdateCascade = 0x00000100;
    private const int RelationshipDeleteCascade = 0x00001000;

    private readonly PageChannel _channel = channel;
    private List<TableDef>? _tables;
    private List<ForeignKey>? _relationships;

    /// <summary>All tables in the database (user and system).</summary>
    public IReadOnlyList<TableDef> Tables => _tables ??= LoadTables();

    /// <summary>Drops the cached catalog so a freshly created table is picked up on next read.</summary>
    public void Invalidate()
    {
        _tables = null;
        _relationships = null;
    }

    /// <summary>All relationships (foreign keys) defined in the database.</summary>
    public IReadOnlyList<ForeignKey> Relationships => _relationships ??= LoadRelationships();

    /// <summary>Relationships for which <paramref name="table"/> is the referencing (child) table.</summary>
    public IEnumerable<ForeignKey> ForeignKeysOf(string table) =>
        Relationships.Where(r => string.Equals(r.Table, table, StringComparison.OrdinalIgnoreCase));

    /// <summary>User (non-system) tables only.</summary>
    public IEnumerable<TableDef> UserTables => Tables.Where(t => !t.IsSystem);

    public TableDef? FindTable(string name) =>
        Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private List<TableDef> LoadTables()
    {
        // Build a TableDef for MSysObjects from its own (fixed) TDEF page, then scan its rows.
        TableDef catalogDef = ReadTableDefinition(_channel.Format.CatalogPage, "MSysObjects", isSystem: true);
        var columns = catalogDef.Columns;

        int idIndex = ColumnIndex(columns, "Id");
        int typeIndex = ColumnIndex(columns, "Type");
        int nameIndex = ColumnIndex(columns, "Name");
        int flagsIndex = ColumnIndex(columns, "Flags");

        var catalog = new Table(_channel, catalogDef);
        var tables = new List<TableDef>();

        foreach (object?[] row in catalog.Rows())
        {
            if (row[typeIndex] is not short type || type != ObjectTypeTable) continue;

            int definitionPage = (int)row[idIndex]!;
            string name = (string)row[nameIndex]!;
            uint flags = unchecked((uint)(int)row[flagsIndex]!);
            // A table is "system" (excluded from the user-table list, as Access's own schema view
            // does) if it is flagged system or hidden, or is named as engine/temporary infrastructure:
            // MSys*, a leading '~' (temp), or a leading '#' (e.g. EFCore.Jet's hidden #Dual helper).
            bool isSystem = (flags & (SystemObjectFlags | HiddenObjectFlags)) != 0
                            || name.StartsWith("MSys", StringComparison.Ordinal)
                            || name.StartsWith('~')
                            || name.StartsWith('#');

            tables.Add(ReadTableDefinition(definitionPage, name, isSystem));
        }

        return tables;
    }

    private List<ForeignKey> LoadRelationships()
    {
        TableDef? def = FindTable("MSysRelationships");
        if (def is null) return [];

        var c = def.Columns;
        int nameIdx = ColumnIndex(c, "szRelationship");
        int childTableIdx = ColumnIndex(c, "szObject");
        int childColumnIdx = ColumnIndex(c, "szColumn");
        int parentTableIdx = ColumnIndex(c, "szReferencedObject");
        int parentColumnIdx = ColumnIndex(c, "szReferencedColumn");
        int orderIdx = ColumnIndex(c, "icolumn");
        int flagsIdx = ColumnIndex(c, "grbit");

        // One row per column; group by relationship name and order columns by icolumn.
        var groups = new Dictionary<string, (string Child, string Parent, int Flags,
            List<(int Order, string Column, string ReferencedColumn)> Columns)>();

        foreach (object?[] row in new Table(_channel, def).Rows())
        {
            string name = (string)row[nameIdx]!;
            if (!groups.TryGetValue(name, out var g))
            {
                g = ((string)row[childTableIdx]!, (string)row[parentTableIdx]!,
                     (int)row[flagsIdx]!, []);
                groups[name] = g;
            }
            g.Columns.Add(((int)row[orderIdx]!, (string)row[childColumnIdx]!, (string)row[parentColumnIdx]!));
        }

        return groups
            .Select(kvp => new ForeignKey(
                kvp.Key,
                kvp.Value.Child,
                kvp.Value.Parent,
                kvp.Value.Columns.OrderBy(x => x.Order).Select(x => (x.Column, x.ReferencedColumn)).ToList(),
                (kvp.Value.Flags & RelationshipDontEnforce) == 0,
                (kvp.Value.Flags & RelationshipUpdateCascade) != 0,
                (kvp.Value.Flags & RelationshipDeleteCascade) != 0))
            .ToList();
    }

    private TableDef ReadTableDefinition(int definitionPage, string name, bool isSystem)
    {
        var tdef = new TableDefinitionPage();
        tdef.Read(_channel, definitionPage);

        return new TableDef
        {
            Name = name,
            DefinitionPage = definitionPage,
            Columns = tdef.Columns,
            Indexes = tdef.Indexes,
            IsSystem = isSystem,
        };
    }

    private static int ColumnIndex(IReadOnlyList<ColumnDef> columns, string name)
    {
        for (int i = 0; i < columns.Count; i++)
            if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return columns[i].Index;
        throw new InvalidOperationException($"MSysObjects is missing the '{name}' column.");
    }
}
