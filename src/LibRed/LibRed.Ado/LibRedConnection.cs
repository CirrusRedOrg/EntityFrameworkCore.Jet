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
        set
        {
            if (State != ConnectionState.Closed)
                throw new InvalidOperationException("The connection string cannot be changed while the connection is open.");

            _connectionString = value ?? string.Empty;
        }
    }

    /// <summary>The open database, or <c>null</c> when closed. Used by commands.</summary>
    internal QueryEngine? Engine { get; private set; }

    /// <summary>The transaction currently open on this connection, or <c>null</c>.</summary>
    internal LibRedTransaction? CurrentTransaction { get; private set; }

    /// <summary>Commits the page-level transaction and clears it as the active one.</summary>
    internal void CommitTransaction(LibRedTransaction transaction)
    {
        if (!ReferenceEquals(CurrentTransaction, transaction)) return;
        _database?.Commit();
        CurrentTransaction = null;
    }

    /// <summary>Rolls the page-level transaction back and clears it as the active one.</summary>
    internal void RollbackTransaction(LibRedTransaction transaction)
    {
        if (!ReferenceEquals(CurrentTransaction, transaction)) return;
        _database?.Rollback();
        CurrentTransaction = null;
    }

    /// <summary>Opens a savepoint in the connection's active transaction (called by
    /// <see cref="LibRedTransaction.Save"/>).</summary>
    internal LibRed.IO.Savepoint CreateSavepoint() =>
        (_database ?? throw new InvalidOperationException("The connection is not open.")).CreateSavepoint();

    /// <summary>Rolls the active transaction back to a savepoint (called by <see cref="LibRedTransaction.Rollback"/>).</summary>
    internal void RollbackToSavepoint(LibRed.IO.Savepoint savepoint) => _database?.RollbackToSavepoint(savepoint);

    /// <summary>Releases a savepoint in the active transaction (called by <see cref="LibRedTransaction.Release"/>).</summary>
    internal void ReleaseSavepoint(LibRed.IO.Savepoint savepoint) => _database?.ReleaseSavepoint(savepoint);

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
    /// <paramref name="connectionString"/> — **natively, no DAO/ADOX, cross-platform**.
    /// </summary>
    /// <remarks>
    /// <see cref="Storage.DatabaseCreator.CreateEmpty"/> synthesises the file from scratch (page 0,
    /// the free map, and the bootstrap system catalog), then LibRed's ordinary writers populate it.
    /// Produces an ACE 2007-format (<c>.accdb</c>) database that LibRed reads and writes fully; the
    /// remaining Access-compatibility system tables are still being filled in.
    /// </remarks>
    public static void CreateDatabase(string connectionString)
    {
        string path = ParseDataSource(connectionString);
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("The connection string is missing a Data Source.", nameof(connectionString));

        Storage.DatabaseCreator.CreateEmpty(path);
        CreateDualTable(path);
    }

    // EFCore.Jet's query generator renders FROM-less scalar queries (All/Any/Count/constant projections) as
    // `FROM (SELECT COUNT(*) FROM `#Dual`)`, so every provider-created database needs a single-row `#Dual`
    // helper table (Jet has no Oracle-style DUAL). The DAO/ADOX creation path did this via
    // ISchemaOperationsProvider.EnsureDualTable; native creation must do the same or those queries fail to bind.
    // The leading '#' keeps it out of Catalog.UserTables (so HasUserTables() ignores it) while the binder still
    // resolves it by name.
    private static void CreateDualTable(string path)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable(
            JetConnection.DefaultDualTableName,
            [new LibRed.Catalog.ColumnSpec("ID", LibRed.Catalog.JetDataType.Int32, 4, IsFixedLength: true, IsNullable: false)],
            primaryKey: ["ID"]);
        db.OpenTable(JetConnection.DefaultDualTableName).Insert([1]);
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
        // An open transaction that was never committed is abandoned: roll it back so its writes
        // don't leak onto disk (matches ADO.NET's implicit rollback on connection close).
        if (CurrentTransaction is not null)
        {
            _database?.Rollback();
            CurrentTransaction = null;
        }

        _database?.Dispose();
        _database = null;
        Engine = null;
        _state = ConnectionState.Closed;
    }

    public override void ChangeDatabase(string databaseName) =>
        throw new NotSupportedException("A Jet/ACE connection maps to a single file.");

    protected override DbCommand CreateDbCommand() => new LibRedCommand { Connection = this };

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (_database is null || _state != ConnectionState.Open)
            throw new InvalidOperationException("The connection is not open.");
        if (CurrentTransaction is not null)
            throw new InvalidOperationException("A transaction is already in progress; nested transactions are not supported.");

        _database.BeginTransaction();
        return CurrentTransaction = new LibRedTransaction(this, isolationLevel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Close();
        base.Dispose(disposing);
    }

    private static string ParseDataSource(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return string.Empty;

        // Allow a bare path as the whole connection string.
        if (!connectionString.Contains('='))
            return ExpandPath(connectionString.Trim());

        // Let the framework parser handle quoted/escaped values. In particular, splitting on
        // semicolons selects the wrong file for a valid value such as Data Source="a;b.accdb".
        // When aliases are mixed, prefer the canonical spelling, then DataSource, then DBQ.
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        string? raw = TryGetString(builder, "Data Source")
            ?? TryGetString(builder, "DataSource")
            ?? TryGetString(builder, "DBQ");

        return string.IsNullOrEmpty(raw) ? string.Empty : ExpandPath(raw);
    }

    private static string? TryGetString(DbConnectionStringBuilder builder, string key) =>
        builder.TryGetValue(key, out object? value) ? Convert.ToString(value) : null;

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
