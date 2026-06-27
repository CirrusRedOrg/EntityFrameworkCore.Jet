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

    /// <summary>MSysObjects.Flags bits marking a system object.</summary>
    private const uint SystemObjectFlags = 0x80000002;

    private readonly PageChannel _channel = channel;
    private List<TableDef>? _tables;

    /// <summary>All tables in the database (user and system).</summary>
    public IReadOnlyList<TableDef> Tables => _tables ??= LoadTables();

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
            bool isSystem = (flags & SystemObjectFlags) != 0
                            || name.StartsWith("MSys", StringComparison.Ordinal)
                            || name.StartsWith('~');

            tables.Add(ReadTableDefinition(definitionPage, name, isSystem));
        }

        return tables;
    }

    private TableDef ReadTableDefinition(int definitionPage, string name, bool isSystem)
    {
        var tdef = new TableDefinitionPage();
        tdef.Read(_channel.ReadPage(definitionPage), _channel.Format);

        return new TableDef
        {
            Name = name,
            DefinitionPage = definitionPage,
            Columns = tdef.Columns,
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
