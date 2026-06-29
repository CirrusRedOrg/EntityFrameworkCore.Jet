using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
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

        foreach (string part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq < 0) continue;
            string key = part[..eq].Trim();
            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("DataSource", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("DBQ", StringComparison.OrdinalIgnoreCase))
            {
                return part[(eq + 1)..].Trim().Trim('"');
            }
        }

        // Allow a bare path as the whole connection string.
        return connectionString.Contains('=') ? string.Empty : connectionString.Trim();
    }
}
