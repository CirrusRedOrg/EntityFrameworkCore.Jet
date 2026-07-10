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

    /// <summary>Opens a database file (read-only by default).</summary>
    public static JetDatabase Open(string path, bool readOnly = true) =>
        new(PageChannel.Open(path, readOnly));

    /// <summary>The resolved on-disk format/version of the database.</summary>
    public JetFormatBase Format => _channel.Format;

    /// <summary>Whether a transaction is currently open.</summary>
    public bool InTransaction => _channel.InTransaction;

    /// <summary>Begins a page-level transaction; writes are undoable until <see cref="Commit"/>.</summary>
    public void BeginTransaction() => _channel.BeginTransaction();

    /// <summary>Commits the current transaction (writes are already on disk).</summary>
    public void Commit() => _channel.CommitTransaction();

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

    /// <summary>Opens a table by name for row access.</summary>
    public Table OpenTable(string name)
    {
        TableDef def = Catalog.FindTable(name)
            ?? throw new ArgumentException($"Table '{name}' was not found.", nameof(name));
        return new Table(_channel, def);
    }

    public void Dispose() => _channel.Dispose();
}
