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
        LibRed.IO.LibRedDiagnostics.EnterJetDatabase();
        _channel = channel;

        DefinitionPage = new DatabaseDefinitionPage();
        DefinitionPage.Read(channel.ReadPage(0), channel.Format);

        // Find MSysObjects via the page-0 bootstrap pointer (0x20); fall back to the format default
        // if it reads as 0 (never observed — every file points at page 2).
        int catalogPage = DefinitionPage.CatalogRootPage > 0 ? DefinitionPage.CatalogRootPage : channel.Format.CatalogPage;
        Catalog = new JetCatalog(channel, catalogPage);
    }

    /// <summary>The decoded database definition page (page 0).</summary>
    public DatabaseDefinitionPage DefinitionPage { get; }

    /// <summary>The instant the database file was created, decoded from the obfuscated OLE
    /// timestamp on page 0 (see <see cref="DatabaseDefinitionPage.DatabaseCreationDate"/>).</summary>
    public DateTime CreationDate => DefinitionPage.DatabaseCreationDate;

    /// <summary>The database's ANSI code page (e.g. 1252, 1250), decoded from page 0.</summary>
    public int CodePage => DefinitionPage.CodePage;

    /// <summary>The database's default collation LCID (e.g. 1033 = en-US), decoded from page 0.</summary>
    public int DefaultCollationLcid => DefinitionPage.DefaultCollationLcid;

    /// <summary>The database default sort-order version (0 = General Legacy, 1 = General), from page 0.</summary>
    public byte DefaultCollationVersion => DefinitionPage.DefaultCollationVersion;

    /// <summary>The database's default text collating order — the source of truth for the LCID and
    /// sort-order version written into new columns, in place of a hardcoded constant. Defaults to General
    /// legacy (locale 1033, version 0), which is what every file LibRed currently handles uses; decoding
    /// the actual value from the page-0 sort order (obfuscated region) is a follow-up, after which this
    /// property would be populated from <see cref="DefinitionPage"/>.</summary>
    public Collation Collation { get; internal set; } = Collation.GeneralLegacy;

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

    /// <summary>Opens a database file (read-only by default). For a password-encrypted ACCDB, supply
    /// <paramref name="password"/> — encrypted databases open read-only.</summary>
    public static JetDatabase Open(string path, bool readOnly = true, string? password = null)
    {
        // Coordinate page access between every handle open on this file (EF holds several connections on one
        // .accdb): readers share a page, a writer excludes them. PageChannel shares one per-path manager
        // (refcounted, freed on the last close). Process-local for now; the file-based managers replace it.
        var channel = PageChannel.Open(path, readOnly, password);
        try
        {
            return new JetDatabase(channel);
        }
        catch
        {
            // PageChannel.Open owns the file handle and shared cache lease. Ownership transfers
            // to JetDatabase only after its page-0/catalog initialization has completed.
            channel.Dispose();
            throw;
        }
    }

    /// <summary>The resolved on-disk format/version of the database.</summary>
    public JetFormatBase Format => _channel.Format;

    /// <summary>Whether a transaction is currently open.</summary>
    public bool InTransaction => _channel.InTransaction;

    /// <summary>Begins a page-level transaction; writes are undoable until <see cref="Commit"/>.</summary>
    public void BeginTransaction() => _channel.BeginTransaction();

    /// <summary>Commits the current transaction (writes are already on disk). <paramref name="flush"/> forces
    /// durability (fsync) for an explicit user commit; an implicit per-statement autocommit passes false.</summary>
    public void Commit(bool flush = true) => _channel.CommitTransaction(flush);

    /// <summary>
    /// Rolls the current transaction back, restoring every touched page and dropping any pages the
    /// transaction allocated. The catalog cache is invalidated so subsequent reads pick up the
    /// restored TDEFs/row counts rather than stale in-memory copies.
    /// </summary>
    public void Rollback()
    {
        if (!_channel.InTransaction) return;
        _channel.RollbackTransaction();
        Catalog.Invalidate();
    }

    /// <summary>Opens a savepoint within the current transaction (used to make a single statement atomic
    /// inside a larger user transaction). Requires a transaction to be open.</summary>
    public Savepoint CreateSavepoint() => _channel.CreateSavepoint();

    /// <summary>Rolls back to <paramref name="savepoint"/>, undoing writes made since it was created; the
    /// transaction (and savepoint) stay open. Invalidates the catalog cache, as a full rollback does, since a
    /// restored page may be a TDEF/catalog page.</summary>
    public void RollbackToSavepoint(Savepoint savepoint)
    {
        _channel.RollbackToSavepoint(savepoint);
        Catalog.Invalidate();
    }

    /// <summary>Releases <paramref name="savepoint"/>, merging its writes into the enclosing scope.</summary>
    public void ReleaseSavepoint(Savepoint savepoint) => _channel.ReleaseSavepoint(savepoint);

    // --- nested transactions (shared by the ADO API and SQL BEGIN/COMMIT/ROLLBACK) ---
    // One physical transaction; nesting maps onto the savepoint stack. The depth counts every open level, so a
    // COMMIT/ROLLBACK at the innermost level releases/rolls back just that level and the outermost commits or
    // rolls back the whole transaction — Jet/DAO nested semantics. See docs/design/transactions.md §4.
    private int _txnDepth;
    private readonly Stack<Savepoint> _nestedSavepoints = new();

    /// <summary>Current transaction nesting depth (0 = none). Shared authority for both front doors.</summary>
    public int TransactionDepth => _txnDepth;

    /// <summary>Opens a transaction level: the outermost begins the real transaction, an inner one pushes a
    /// savepoint.</summary>
    public void BeginNested()
    {
        if (_txnDepth == 0) BeginTransaction();
        else _nestedSavepoints.Push(CreateSavepoint());
        _txnDepth++;
    }

    /// <summary>Commits the innermost level: the outermost commits the transaction, an inner one releases its
    /// savepoint (merging into the enclosing level).</summary>
    public void CommitNested()
    {
        if (_txnDepth == 0) throw new InvalidOperationException("No transaction is in progress.");
        if (_txnDepth == 1) Commit();
        else ReleaseSavepoint(_nestedSavepoints.Pop());
        _txnDepth--;
    }

    /// <summary>Rolls back the entire transaction regardless of nesting depth (all levels at once) and resets the
    /// controller — used when a connection closes with a transaction still open, so its writes don't leak.</summary>
    public void RollbackAll()
    {
        if (_txnDepth == 0) return;
        Rollback(); // a full rollback restores every frame's before-images
        _txnDepth = 0;
        _nestedSavepoints.Clear();
    }

    /// <summary>Rolls back the innermost level: the outermost rolls back the whole transaction, an inner one
    /// rolls back to (and closes) its savepoint.</summary>
    public void RollbackNested()
    {
        if (_txnDepth == 0) throw new InvalidOperationException("No transaction is in progress.");
        if (_txnDepth == 1)
        {
            Rollback();
        }
        else
        {
            Savepoint sp = _nestedSavepoints.Pop();
            RollbackToSavepoint(sp); // undo this level's writes
            ReleaseSavepoint(sp);    // then drop the (now empty) level, returning to the parent
        }
        _txnDepth--;
    }

    /// <summary>The system catalog, used to enumerate and resolve tables.</summary>
    public JetCatalog Catalog { get; }

    /// <summary>
    /// Creates a new table and registers it in the catalog. An optional primary key creates a
    /// unique index over the named columns. The database must have been opened writable; the
    /// table is usable immediately for inserts and scans.
    /// </summary>
    public void CreateTable(
        string name,
        IReadOnlyList<ColumnSpec> columns,
        IReadOnlyList<string>? primaryKey = null,
        IReadOnlyList<RelationshipSpec>? relationships = null,
        IReadOnlyList<UniqueIndexSpec>? uniqueConstraints = null,
        IReadOnlyList<(string Column, string DefaultSql)>? columnDefaults = null,
        IReadOnlyList<(string Name, string Expression)>? checkConstraints = null,
        string? primaryKeyName = null)
    {
        new Storage.TableCreator(_channel, Catalog, Collation)
            .Create(name, columns, primaryKey, relationships, uniqueConstraints, columnDefaults, checkConstraints,
                primaryKeyName);
        Catalog.Invalidate();
    }

    /// <summary>
    /// Adds an index to an existing (currently empty) table — the CREATE INDEX statement. WITH PRIMARY
    /// makes it the primary key; WITH DISALLOW NULL marks it required.
    /// </summary>
    public void CreateIndex(string table, string index, IReadOnlyList<(string Column, bool Descending)> columns,
        bool isUnique = false, bool isPrimary = false, bool disallowNull = false, bool ignoreNulls = false)
    {
        new Storage.TableCreator(_channel, Catalog).AddIndex(table, index, columns, isUnique, isPrimary, disallowNull, ignoreNulls);
        Catalog.Invalidate();
    }

    /// <summary>Adds a foreign key (relationship) to an existing table — ALTER TABLE ADD CONSTRAINT …
    /// FOREIGN KEY. Writes the child backing index, the parent's incoming block and MSysRelationships.</summary>
    public void AddForeignKey(string childTable, RelationshipSpec relationship)
    {
        new Storage.TableCreator(_channel, Catalog).AddForeignKey(childTable, relationship);
        Catalog.Invalidate();
    }

    /// <summary>Adds a table-level CHECK constraint to an existing table — ALTER TABLE ADD CONSTRAINT … CHECK.
    /// Merges into the table's LvProp CheckConstraints property; the engine enforces it on insert/update.</summary>
    public void AddCheckConstraint(string table, string name, string expression)
    {
        new Storage.TableCreator(_channel, Catalog).AddCheckConstraint(table, name, expression);
        Catalog.Invalidate();
    }

    /// <summary>Drops a named table-level CHECK constraint — ALTER TABLE … DROP CONSTRAINT. Removes it from the
    /// LvProp CheckConstraints property. Returns false if no CHECK of that name exists.</summary>
    public bool DropCheckConstraint(string table, string name)
    {
        bool dropped = new Storage.TableCreator(_channel, Catalog).DropCheckConstraint(table, name);
        if (dropped) Catalog.Invalidate();
        return dropped;
    }

    /// <summary>Changes a column's declared type — ALTER TABLE … ALTER COLUMN. A variable text/binary length
    /// change is an in-place descriptor edit; a storage-type change rebuilds the table (converting values).</summary>
    public void AlterColumn(string table, string column, ColumnSpec newSpec)
    {
        new Storage.TableCreator(_channel, Catalog).AlterColumn(table, column, newSpec);
        Catalog.Invalidate();
    }

    /// <summary>Just the in-place TDEF descriptor edit of a column type-change (bump 0x29 + rewrite only the
    /// target descriptor), matching ACE byte-for-byte — the TDEF-page step of <see cref="AlterColumnTypeInPlace"/>,
    /// exposed on its own so a byte-diff test can isolate the TDEF page. It does NOT re-lay rows or rebuild
    /// indexes; call <see cref="AlterColumnTypeInPlace"/> for the full, self-consistent change.</summary>
    public void AlterColumnTypeInPlaceTdef(string table, string column, ColumnSpec newSpec)
    {
        new Storage.TableCreator(_channel, Catalog).AlterColumnTypeInPlaceTdef(table, column, newSpec);
        Catalog.Invalidate();
    }

    /// <summary>Full in-place column type change, byte-for-byte like ACE for every shape (fixed/variable columns
    /// and targets, fixed↔variable, and indexed targets): TDEF edit + row re-lay preserving the dead old slot +
    /// index rebuild. Falls back to the logical rebuild only for a Memo/OLE (long-value) source or target.</summary>
    public void AlterColumnTypeInPlace(string table, string column, ColumnSpec newSpec)
    {
        new Storage.TableCreator(_channel, Catalog).AlterColumnTypeInPlace(table, column, newSpec);
        Catalog.Invalidate();
    }

    /// <summary>Sets a column's DEFAULT — ALTER TABLE … ALTER COLUMN … DEFAULT. Replaces the column's
    /// DefaultValue in the LvProp blob; the engine applies it on an omit-insert.</summary>
    public void SetColumnDefault(string table, string column, string defaultSql)
    {
        new Storage.TableCreator(_channel, Catalog).SetColumnDefault(table, column, defaultSql);
        Catalog.Invalidate();
    }

    /// <summary>Removes a column's DEFAULT — ALTER TABLE … ALTER COLUMN … DROP DEFAULT. Drops only the
    /// DefaultValue property from the LvProp blob; the column's type and Required (NOT NULL) are left intact
    /// (ACE-verified). A no-op if the column had no default.</summary>
    public void DropColumnDefault(string table, string column)
    {
        new Storage.TableCreator(_channel, Catalog).DropColumnDefault(table, column);
        Catalog.Invalidate();
    }

    /// <summary>Sets or clears a column's Required (NOT NULL) property — ALTER TABLE … ALTER COLUMN … NOT NULL /
    /// NULL. Adds the <c>Required</c> LvProp property when <paramref name="required"/>, removes it otherwise;
    /// the engine enforces it on insert and ACE reads it byte-faithfully (verified).</summary>
    public void SetColumnRequired(string table, string column, bool required)
    {
        new Storage.TableCreator(_channel, Catalog).SetColumnRequired(table, column, required);
        Catalog.Invalidate();
    }

    /// <summary>Drops a named FOREIGN KEY constraint from a table — ALTER TABLE … DROP CONSTRAINT. Returns
    /// false if no such relationship exists (e.g. the name is a primary-key/unique index, not yet handled).</summary>
    public bool DropConstraint(string childTable, string name) =>
        new Storage.TableCreator(_channel, Catalog).DropConstraint(childTable, name);

    /// <summary>Drops a column — ALTER TABLE … DROP COLUMN. A metadata-only TDEF edit (survivors and rows are
    /// untouched). Returns false if the column doesn't exist; throws for an indexed/keyed or memo/OLE column.</summary>
    public bool DropColumn(string table, string column) =>
        new Storage.TableCreator(_channel, Catalog).DropColumn(table, column);

    /// <summary>Adds a column — ALTER TABLE … ADD COLUMN. Appends the descriptor/name and bumps the counts;
    /// existing rows read it as NULL. Returns false if the column already exists; throws for memo/OLE.</summary>
    public bool AddColumn(string table, Catalog.ColumnSpec column, string? defaultValue = null) =>
        new Storage.TableCreator(_channel, Catalog, Collation).AddColumn(table, column, defaultValue);

    /// <summary>Drops an index — DROP INDEX … ON table. Removes its TDEF blocks and frees its B-tree root.
    /// Returns false if the index doesn't exist; throws if it backs a relationship.</summary>
    public bool DropIndex(string table, string index) =>
        new Storage.TableCreator(_channel, Catalog).DropIndex(table, index);

    /// <summary>Drops a table — DROP TABLE. Removes its MSysObjects + MSysACEs rows and frees its pages.
    /// Returns false if the table doesn't exist; throws if it is in a relationship.</summary>
    public bool DropTable(string table) =>
        new Storage.TableCreator(_channel, Catalog).DropTable(table);

    /// <summary>Drops a view or stored procedure — DROP VIEW / DROP PROCEDURE (interchangeable, both target a
    /// type-5 query object). Removes its MSysObjects + MSysQueries + MSysACEs rows. Returns false if absent.</summary>
    public bool DropQueryObject(string name) =>
        new Storage.TableCreator(_channel, Catalog).DropQueryObject(name);

    /// <summary>Creates a view (a stored SELECT query) — the CREATE VIEW statement. Written the way Access
    /// does: an MSysObjects type-5 row plus the query decomposed into MSysQueries rows.</summary>
    public void CreateView(string name, ViewSpec spec)
    {
        new Storage.ViewCreator(_channel, Catalog).Create(name, spec);
        Catalog.Invalidate();
    }

    /// <summary>Creates a stored action query — a data-definition (CREATE/DROP TABLE) or append (INSERT)
    /// query, as CREATE PROCEDURE persists them. Written byte-faithfully the way Access does.</summary>
    public void CreateActionQuery(string name, ActionQuerySpec spec)
    {
        new Storage.ViewCreator(_channel, Catalog).CreateAction(name, spec);
        Catalog.Invalidate();
    }

    /// <summary>Opens a table directly from its TDEF page, bypassing the catalog — used during database
    /// creation to seed the system tables before they self-register in <c>MSysObjects</c>.</summary>
    public Storage.Table OpenTableAt(int tdefPage, string name, bool isSystem = true) =>
        new(_channel, Catalog.ReadTableDefinitionAt(tdefPage, name, isSystem));

    /// <summary>Opens a table by name for row access.</summary>
    public Table OpenTable(string name)
    {
        TableDef def = Catalog.FindTable(name)
            ?? throw new ArgumentException($"Table '{name}' was not found.", nameof(name));
        return new Table(_channel, def);
    }

    public void Dispose()
    {
        LibRed.IO.LibRedDiagnostics.ExitJetDatabase();
        _channel.Dispose();
    }
}
