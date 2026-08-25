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

    /// <summary>Commits the page-level transaction and clears it as the active one. Goes through the shared
    /// nested-transaction controller so SQL <c>COMMIT</c> and this ADO commit track one depth.</summary>
    internal void CommitTransaction(LibRedTransaction transaction)
    {
        if (!ReferenceEquals(CurrentTransaction, transaction)) return;
        _database?.CommitNested();
        CurrentTransaction = null;
    }

    /// <summary>Rolls the page-level transaction back and clears it as the active one.</summary>
    internal void RollbackTransaction(LibRedTransaction transaction)
    {
        if (!ReferenceEquals(CurrentTransaction, transaction)) return;
        _database?.RollbackNested();
        CurrentTransaction = null;
    }

    /// <summary>Keeps an ADO transaction handle honest when SQL COMMIT/ROLLBACK closes its transaction.</summary>
    internal void ReconcileSqlTransactionControl()
    {
        if (_database?.TransactionDepth != 0 || CurrentTransaction is null) return;
        CurrentTransaction.CompleteFromSql();
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
    /// <param name="collation">The database's default text collating order, written to page 0 and inherited
    /// by every column created in it. Defaults to General-Legacy (the order the engine writes); pass
    /// <see cref="Catalog.Collation.General"/> for the "General" order Access 2010+ offers.</param>
    public static void CreateDatabase(string connectionString, Catalog.Collation? collation = null)
    {
        string path = ParseDataSource(connectionString);
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("The connection string is missing a Data Source.", nameof(connectionString));

        Storage.DatabaseCreator.CreateEmpty(path, collation: collation);
        CreateDualTable(path);
    }

    /// <summary>
    /// The single-row helper table every provider-created database carries. EFCore.Jet's query generator
    /// renders FROM-less scalar queries (All/Any/Count/constant projections) as
    /// <c>FROM (SELECT COUNT(*) FROM `#Dual`)</c>, because Jet has no Oracle-style DUAL. The leading '#'
    /// keeps it out of <c>Catalog.UserTables</c> (so <c>HasUserTables()</c> ignores it) while the binder
    /// still resolves it by name.
    /// </summary>
    /// <remarks>
    /// Spelled out here rather than read from <c>JetConnection.DefaultDualTableName</c>: it is the same
    /// name by necessity — EF's generated SQL is what has to find the table — but sharing one constant was
    /// the last thing tying this assembly to the Windows-only EFCore.Jet.Data, for no benefit beyond a
    /// five-character string.
    /// </remarks>
    internal const string DualTableName = "#Dual";

    // The DAO/ADOX creation path created this via ISchemaOperationsProvider.EnsureDualTable; native
    // creation must do the same or those queries fail to bind.
    private static void CreateDualTable(string path)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable(
            DualTableName,
            [new LibRed.Catalog.ColumnSpec("ID", LibRed.Catalog.JetDataType.Int32, 4, IsFixedLength: true, IsNullable: false)],
            primaryKey: ["ID"]);
        db.OpenTable(DualTableName).Insert([1]);
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
        OnStateChange(new StateChangeEventArgs(ConnectionState.Closed, ConnectionState.Open));
    }

    public override void Close()
    {
        // An open transaction that was never committed is abandoned: roll it back (all nesting levels) so its
        // writes don't leak onto disk (matches ADO.NET's implicit rollback on connection close). Keyed off the
        // actual transaction depth so a SQL-opened BEGIN with no ADO handle is rolled back too.
        if (_database is { TransactionDepth: > 0 })
        {
            _database.RollbackAll();
            CurrentTransaction = null;
        }

        _database?.Dispose();
        _database = null;
        Engine = null;

        // Only a real transition raises the event. Close() is not guarded against being called on an already
        // closed connection - and Dispose() calls it - so firing unconditionally would report a second close
        // that never happened. EF's connection diagnostics count these.
        if (_state == ConnectionState.Closed) return;

        _state = ConnectionState.Closed;
        OnStateChange(new StateChangeEventArgs(ConnectionState.Open, ConnectionState.Closed));
    }

    public override void ChangeDatabase(string databaseName) =>
        throw new NotSupportedException("A Jet/ACE connection maps to a single file.");

    protected override DbCommand CreateDbCommand() => new LibRedCommand { Connection = this };

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (_database is null || _state != ConnectionState.Open)
            throw new InvalidOperationException("The connection is not open.");
        if (CurrentTransaction is not null)
            throw new InvalidOperationException("A transaction is already in progress on this connection; use SQL BEGIN/COMMIT or savepoints to nest.");

        _database.BeginNested();
        // Resolve the ADO default (Unspecified) to a concrete level the way real providers do — EF and its
        // TransactionStarted interceptor expect a started transaction to report a real IsolationLevel, not
        // Unspecified. LibRed serialises writers via page-level locking; ReadCommitted is the reported default.
        if (isolationLevel == IsolationLevel.Unspecified)
            isolationLevel = IsolationLevel.ReadCommitted;
        return CurrentTransaction = new LibRedTransaction(this, isolationLevel);
    }

    protected override void Dispose(bool disposing)
    {
        // Clearing the connection string is what makes a disposed connection unusable, as ADO.NET requires:
        // Open() then fails its existing "missing a Data Source" guard instead of quietly reopening the file.
        // SqlConnection and JetConnection both do exactly this, and it is why the resulting exception is an
        // InvalidOperationException rather than an ObjectDisposedException. Assigned to the field directly
        // because the property setter refuses to change while the connection is still open.
        _connectionString = string.Empty;

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
