using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;

namespace LibRed;

/// <summary>
/// The public entry point to the Core layer: opens a Jet/ACE database file and
/// exposes its catalog and tables. This is what the SQL engine and ADO provider
/// build on; consumers wanting raw storage access start here.
/// </summary>
public sealed class JetDatabase : IDisposable
{
    private readonly PageChannel _channel;

    private JetDatabase(PageChannel channel)
    {
        _channel = channel;

        DefinitionPage = new DatabaseDefinitionPage();
        DefinitionPage.Read(channel.ReadPage(0), channel.Format);

        Catalog = new JetCatalog(channel);
    }

    /// <summary>The decoded database definition page (page 0).</summary>
    public DatabaseDefinitionPage DefinitionPage { get; }

    /// <summary>Reads and decodes the table definition (TDEF) page at <paramref name="pageNumber"/>.</summary>
    public TableDefinitionPage ReadTableDefinition(int pageNumber)
    {
        var tdef = new TableDefinitionPage();
        tdef.Read(_channel, pageNumber);
        return tdef;
    }

    /// <summary>Reads and decodes the data page at <paramref name="pageNumber"/>.</summary>
    public DataPage ReadDataPage(int pageNumber)
    {
        var page = new DataPage();
        page.Read(_channel.ReadPage(pageNumber), _channel.Format);
        return page;
    }

    /// <summary>Opens a database file (read-only by default).</summary>
    public static JetDatabase Open(string path, bool readOnly = true) =>
        new(PageChannel.Open(path, readOnly));

    /// <summary>The resolved on-disk format/version of the database.</summary>
    public JetFormatBase Format => _channel.Format;

    /// <summary>The system catalog, used to enumerate and resolve tables.</summary>
    public JetCatalog Catalog { get; }

    /// <summary>
    /// Creates a new table and registers it in the catalog. An optional primary key creates a
    /// unique index over the named columns. The database must have been opened writable; the
    /// table is usable immediately for inserts and scans.
    /// </summary>
    public void CreateTable(string name, IReadOnlyList<ColumnSpec> columns, IReadOnlyList<string>? primaryKey = null)
    {
        new Storage.TableCreator(_channel, Catalog).Create(name, columns, primaryKey);
        Catalog.Invalidate();
    }

    /// <summary>Opens a table by name for row access.</summary>
    public Table OpenTable(string name)
    {
        TableDef def = Catalog.FindTable(name)
            ?? throw new ArgumentException($"Table '{name}' was not found.", nameof(name));
        return new Table(_channel, def);
    }

    public void Dispose() => _channel.Dispose();
}
