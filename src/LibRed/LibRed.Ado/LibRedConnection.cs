using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using EntityFrameworkCore.Jet.Data;
using LibRed.Engine;

namespace LibRed.Data;

/// <summary>
/// ADO.NET connection over a native LibRed engine instance. The connection string's
/// <c>Data Source</c> names the .mdb/.accdb file to open.
/// </summary>
public sealed class LibRedConnection : DbConnection
{
    private string _connectionString = string.Empty;
    private ConnectionState _state = ConnectionState.Closed;
    private JetDatabase? _database;

    public LibRedConnection() { }

    public LibRedConnection(string connectionString) => _connectionString = connectionString ?? string.Empty;

    [AllowNull]
    public override string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value ?? string.Empty;
    }

    /// <summary>The open database, or <c>null</c> when closed. Used by commands.</summary>
    internal QueryEngine? Engine { get; private set; }

    public override string Database => DataSource;

    public override string DataSource => ParseDataSource(_connectionString);

    public override string ServerVersion => _database?.Format.Version.ToString() ?? string.Empty;

    public override ConnectionState State => _state;

    /// <summary>True if the database file named by <paramref name="connectionString"/> exists.</summary>
    public static bool DatabaseExists(string? connectionString)
    {
        string path = ParseDataSource(connectionString ?? string.Empty);
        return !string.IsNullOrEmpty(path) && File.Exists(path);
    }

    /// <summary>
    /// Builds a canonical <c>Data Source=...</c> connection string from a bare file name. If
    /// <paramref name="fileNameOrConnectionString"/> already looks like a connection string
    /// (contains a <c>key=value</c> pair), it is returned unchanged.
    /// </summary>
    public static string GetConnectionString(string fileNameOrConnectionString)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrConnectionString))
            return string.Empty;

        return fileNameOrConnectionString.Contains('=')
            ? fileNameOrConnectionString
            : $"Data Source={fileNameOrConnectionString}";
    }

    /// <summary>
    /// Creates a new, empty Jet/ACE database file at the location named by
    /// <paramref name="connectionString"/>.
    /// </summary>
    /// <remarks>
    /// Bootstrap: LibRed can read and write into an existing Jet/ACE file, but can't yet write a
    /// brand-new file's header/catalog/system tables from scratch. So - same as EFCore.Jet itself,
    /// which also can't create a database via pure SQL - this hands off to
    /// <see cref="JetConnection.CreateDatabase(string, DatabaseVersion, CollatingOrder, string, SchemaProviderType, DataAccessProviderType?)"/>,
    /// which creates the file via DAO/ADOX. Windows-only for now; replace with a native writer once
    /// LibRed.Core/LibRed.Engine can create a file itself.
    /// </remarks>
    public static void CreateDatabase(string connectionString)
    {
        string path = ParseDataSource(connectionString);
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("The connection string is missing a Data Source.", nameof(connectionString));

        JetConnection.CreateDatabase(path);
    }

    /// <summary>Deletes the database file named by <paramref name="connectionString"/>, if it exists.</summary>
    public static void DropDatabase(string connectionString)
    {
        string path = ParseDataSource(connectionString);
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// No-op: LibRed opens/closes the underlying file directly and does not pool connections.
    /// Exists for API parity with <c>JetConnection.ClearPool</c>.
    /// </summary>
    public static void ClearPool(LibRedConnection connection)
    {
    }

    /// <summary>
    /// True if the open database has any user (non-system) tables. Read from LibRed's own catalog —
    /// no INFORMATION_SCHEMA or DAO/ADOX — so schema checks work cross-platform on LibRed files.
    /// </summary>
    public bool HasUserTables()
    {
        if (_database is null)
            throw new InvalidOperationException("The connection is not open.");
        return _database.Catalog.UserTables.Any();
    }

    public override void Open()
    {
        if (_state == ConnectionState.Open) return;

        string path = DataSource;
        if (string.IsNullOrEmpty(path))
            throw new InvalidOperationException("Connection string is missing a Data Source.");

        _database = JetDatabase.Open(path, readOnly: false);
        Engine = new QueryEngine(_database);
        _state = ConnectionState.Open;
    }

    public override void Close()
    {
        _database?.Dispose();
        _database = null;
        Engine = null;
        _state = ConnectionState.Closed;
    }

    public override void ChangeDatabase(string databaseName) =>
        throw new NotSupportedException("A Jet/ACE connection maps to a single file.");

    protected override DbCommand CreateDbCommand() => new LibRedCommand { Connection = this };

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        new LibRedTransaction(this, isolationLevel);

    protected override void Dispose(bool disposing)
    {
        if (disposing) Close();
        base.Dispose(disposing);
    }

    private static string ParseDataSource(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return string.Empty;

        string? raw = null;
        foreach (string part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq < 0) continue;
            string key = part[..eq].Trim();
            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("DataSource", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("DBQ", StringComparison.OrdinalIgnoreCase))
            {
                raw = part[(eq + 1)..].Trim().Trim('"');
                break;
            }
        }

        // Allow a bare path as the whole connection string.
        raw ??= connectionString.Contains('=') ? null : connectionString.Trim();

        return string.IsNullOrEmpty(raw) ? string.Empty : ExpandPath(raw);
    }

    /// <summary>
    /// Resolves to a full path and defaults to a ".accdb" extension - matches EFCore.Jet.Data's
    /// internal <c>JetStoreDatabaseHandling.ExpandFileName</c>/<c>EnsureFileExtension</c>, so
    /// LibRedConnection agrees with <see cref="JetConnection" />'s bootstrap (see
    /// <see cref="CreateDatabase" />) about which file e.g. <c>Data Source=Foo</c> actually names.
    /// Without this, a bare name like "Foo" would resolve here to a different path than the
    /// "Foo.accdb" that DAO/ADOX actually creates, and DatabaseExists/DropDatabase/Open would all
    /// silently look in the wrong place.
    /// </summary>
    private static string ExpandPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return string.IsNullOrEmpty(Path.GetExtension(fullPath)) ? fullPath + ".accdb" : fullPath;
    }
}
