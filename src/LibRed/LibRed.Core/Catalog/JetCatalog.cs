using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using System.Globalization;

namespace LibRed.Catalog;

/// <summary>
/// Reads the system catalog (<c>MSysObjects</c>) to enumerate the tables in a database.
/// </summary>
/// <remarks>
/// Bootstrap: MSysObjects' own TDEF is the page page 0's catalog root pointer names
/// (<see cref="Formats.JetFormatBase.CatalogRootPointerOffset"/>), so we build a <see cref="TableDefinition"/> for it
/// from that page and read its rows like any other table. For a table object, the row's <c>Id</c> is its TDEF
/// page number.
/// </remarks>
public sealed class JetCatalog
{
    // The catalog column bits, and the attachment value bit, as ColumnSpec takes them (raw bytes).
    private const byte Sys = (byte)ColumnFlags.SystemCatalog;
    private const byte SysSid = (byte)(ColumnFlags.SystemCatalog | ColumnFlags.SecurityId);
    private const byte AttachmentValue = (byte)ColumnExtendedFlags.AttachmentValue;

    // MSysObjects — the system catalog. Declared in the real physical (alphabetical) descriptor order with
    // explicit canonical ColumnIds and system flags, exactly as an Access-created file stores it.
    private static readonly ColumnSpec[] MSysObjectsColumns =
    [
        new("Connect", JetDataType.Memo, 0, false, ColumnId: 9, SystemFlags: Sys),
        new("Database", JetDataType.Memo, 0, false, ColumnId: 8, SystemFlags: Sys),
        new("DateCreate", JetDataType.DateTime, 8, true, ColumnId: 4, SystemFlags: Sys),
        new("DateUpdate", JetDataType.DateTime, 8, true, ColumnId: 5, SystemFlags: Sys),
        new("Flags", JetDataType.Int32, 4, true, ColumnId: 7, SystemFlags: Sys),
        new("ForeignName", JetDataType.Text, 510, false, ColumnId: 10, SystemFlags: Sys),
        new("Id", JetDataType.Int32, 4, true, ColumnId: 0, SystemFlags: Sys),
        new("Lv", JetDataType.Ole, 0, false, ColumnId: 13, SystemFlags: Sys),
        new("LvExtra", JetDataType.Ole, 0, false, ColumnId: 16, SystemFlags: Sys),
        new("LvModule", JetDataType.Ole, 0, false, ColumnId: 15, SystemFlags: Sys),
        new("LvProp", JetDataType.Ole, 0, false, ColumnId: 14, SystemFlags: Sys),
        new("Name", JetDataType.Text, 510, false, ColumnId: 2, SystemFlags: Sys),
        new("Owner", JetDataType.Binary, 510, false, ColumnId: 6, SystemFlags: SysSid),
        new("ParentId", JetDataType.Int32, 4, true, ColumnId: 1, SystemFlags: Sys),
        new("RmtInfoLong", JetDataType.Ole, 0, false, ColumnId: 12, SystemFlags: Sys),
        new("RmtInfoShort", JetDataType.Binary, 510, false, ColumnId: 11, SystemFlags: Sys),
        new("Type", JetDataType.Int16, 2, true, ColumnId: 3, SystemFlags: Sys),
    ];

    // MSysACEs — per-object access-control rows. Real physical order + ColumnIds (ObjectId at fixed offset 0).
    private static readonly ColumnSpec[] MSysAcesColumns =
    [
        new("ACM", JetDataType.Int32, 4, true, ColumnId: 2, SystemFlags: Sys),
        new("FInheritable", JetDataType.Boolean, 1, true, ColumnId: 3, SystemFlags: Sys),
        new("ObjectId", JetDataType.Int32, 4, true, ColumnId: 0, SystemFlags: Sys),
        new("SID", JetDataType.Binary, 510, false, ColumnId: 1, SystemFlags: SysSid),
    ];

    // MSysQueries — stored query/view definitions (empty in a fresh database).
    private static readonly ColumnSpec[] MSysQueriesColumns =
    [
        new("Attribute", JetDataType.Byte, 1, true, ColumnId: 1, SystemFlags: Sys),
        new("Expression", JetDataType.Memo, 0, false, ColumnId: 5, SystemFlags: Sys),
        new("Flag", JetDataType.Int16, 2, true, ColumnId: 6, SystemFlags: Sys),
        new("LvExtra", JetDataType.Int32, 4, true, ColumnId: 7, SystemFlags: Sys),
        new("Name1", JetDataType.Text, 510, false, ColumnId: 3, SystemFlags: Sys),
        new("Name2", JetDataType.Text, 510, false, ColumnId: 4, SystemFlags: Sys),
        new("ObjectId", JetDataType.Int32, 4, true, ColumnId: 0, SystemFlags: Sys),
        new("Order", JetDataType.Binary, 510, false, ColumnId: 2, SystemFlags: Sys),
    ];

    // MSysRelationships — relationship (foreign key) definitions (empty in a fresh database).
    private static readonly ColumnSpec[] MSysRelationshipsColumns =
    [
        new("ccolumn", JetDataType.Int32, 4, true, ColumnId: 2, SystemFlags: Sys),
        new("grbit", JetDataType.Int32, 4, true, ColumnId: 1, SystemFlags: Sys),
        new("icolumn", JetDataType.Int32, 4, true, ColumnId: 3, SystemFlags: Sys),
        new("szColumn", JetDataType.Text, 510, false, ColumnId: 5, SystemFlags: Sys),
        new("szObject", JetDataType.Text, 510, false, ColumnId: 4, SystemFlags: Sys),
        new("szReferencedColumn", JetDataType.Text, 510, false, ColumnId: 7, SystemFlags: Sys),
        new("szReferencedObject", JetDataType.Text, 510, false, ColumnId: 6, SystemFlags: Sys),
        new("szRelationship", JetDataType.Text, 510, false, ColumnId: 0, SystemFlags: Sys),
    ];


    // ---- Complex-column system tables (ACE 12 / Access 2007 and later) --------------------------------------
    //
    // Complex columns are Access's multi-value and attachment columns. Their registry is MSysComplexColumns,
    // and each supported element type gets a flat storage table. Jet 4 (.mdb) has none of this — the feature
    // arrived with ACE 12 — so these are only created from version byte 0x02 up.
    //
    // MSysComplexColumns is not optional even for a database that never uses a complex column: ACE consults it
    // on every CREATE TABLE, and without it DDL through the OLE DB provider fails with "Cannot find table or
    // constraint" (isolated in AceDdlOnLibRedDatabaseProbeTest by dropping exactly this table from a working
    // DAO-created database). ACE never writes to it — it only has to resolve.
    //
    // Column ids are the ones the real engine assigns (creation order, which is not the alphabetical order the
    // descriptors are stored in), so a byte-comparison against a DAO-created file lines up.

    private static readonly ColumnSpec[] MSysComplexColumnsColumns =
    [
        new("ColumnName", JetDataType.Text, 510, false, ColumnId: 0, SystemFlags: Sys),
        new("ComplexID", JetDataType.Int32, 4, true, IsAutoNumber: true, ColumnId: 4, SystemFlags: Sys),
        new("ComplexTypeObjectID", JetDataType.Int32, 4, true, ColumnId: 1, SystemFlags: Sys),
        new("ConceptualTableID", JetDataType.Int32, 4, true, ColumnId: 3, SystemFlags: Sys),
        new("FlatTableID", JetDataType.Int32, 4, true, ColumnId: 2, SystemFlags: Sys),
    ];

    /// <summary>The flat storage tables, in the order the engine creates them. Each holds a single
    /// <c>Value</c> column of its element type; Attachment is the exception, carrying the file metadata.
    /// Engine tables, but not part of the catalog, so their columns carry no catalog flag.</summary>
    private static readonly (string Name, ColumnSpec[] Columns)[] MSysComplexTypeTables =
    [
        ("MSysComplexType_UnsignedByte", [new("Value", JetDataType.Byte, 1, true, ColumnId: 0, IsEngineColumn: true)]),
        ("MSysComplexType_Short", [new("Value", JetDataType.Int16, 2, true, ColumnId: 0, IsEngineColumn: true)]),
        ("MSysComplexType_Long", [new("Value", JetDataType.Int32, 4, true, ColumnId: 0, IsEngineColumn: true)]),
        ("MSysComplexType_IEEESingle", [new("Value", JetDataType.Single, 4, true, ColumnId: 0, IsEngineColumn: true)]),
        ("MSysComplexType_IEEEDouble", [new("Value", JetDataType.Double, 8, true, ColumnId: 0, IsEngineColumn: true)]),
        ("MSysComplexType_GUID", [new("Value", JetDataType.Guid, 16, true, ColumnId: 0, IsEngineColumn: true)]),
        ("MSysComplexType_Decimal", [new("Value", JetDataType.FixedPoint, 9, false, ColumnId: 0, IsEngineColumn: true)]),
        ("MSysComplexType_Text", [new("Value", JetDataType.Text, 510, false, ColumnId: 0, IsEngineColumn: true)]),
        ("MSysComplexType_Attachment",
        [
            new("FileData", JetDataType.Ole, 0, false, ColumnId: 3, IsEngineColumn: true, ExtendedFlags: AttachmentValue),
            new("FileFlags", JetDataType.Int32, 4, true, ColumnId: 5, IsEngineColumn: true, ExtendedFlags: AttachmentValue),
            new("FileName", JetDataType.Text, 510, false, ColumnId: 1, IsEngineColumn: true, ExtendedFlags: AttachmentValue),
            new("FileTimeStamp", JetDataType.DateTime, 8, true, ColumnId: 4, IsEngineColumn: true, ExtendedFlags: AttachmentValue),
            new("FileType", JetDataType.Text, 510, false, ColumnId: 2, IsEngineColumn: true, ExtendedFlags: AttachmentValue),
            new("FileURL", JetDataType.Memo, 0, false, ColumnId: 0, IsEngineColumn: true, ExtendedFlags: AttachmentValue),
        ]),
    ];

    /// <summary>Builds the initial catalog definitions and usage maps in their stored page order.</summary>
    internal static (List<byte[]?> Pages, int GlobalMapPage, int ObjectsPage, int AcesPage, int QueriesPage, int RelationshipsPage)
        BuildBootstrap(JetFormatBase format, Collation sortOrder)
    {
        var pages = new List<byte[]?> { null };
        int Place() { pages.Add(null); return pages.Count - 1; }

        int globalMapPage = Place();
        ColumnSpec[][] systemTables = [MSysObjectsColumns, MSysAcesColumns, MSysQueriesColumns, MSysRelationshipsColumns];
        // Every TDEF first, then every usage map, as ACE lays them out.
        var tdefPages = new int[systemTables.Length];
        for (int table = 0; table < systemTables.Length; table++)
            tdefPages[table] = Place();

        // The system tables take the database collation too: Access writes v1 descriptors on MSys* in a
        // General (v1) database, so anything else would be a mixed-collation file it never produces.
        for (int table = 0; table < systemTables.Length; table++)
        {
            int usageMapPage = Place();
            var (tdef, usageMap) = BuildSystemTable(format, systemTables[table], usageMapPage, sortOrder);
            pages[tdefPages[table]] = tdef;
            pages[usageMapPage] = usageMap;
        }
        pages[globalMapPage] = UsageMap.BuildGlobalMapPage(format, pages.Count);
        int objPage = tdefPages[0], acesPage = tdefPages[1], queriesPage = tdefPages[2], relPage = tdefPages[3];
        return (pages, globalMapPage, objPage, acesPage, queriesPage, relPage);
    }

    /// <summary>Registers and indexes the initial system catalog through the normal database writers.</summary>
    internal static void InitializeBootstrap(JetDatabase db, byte[] sidUsers, byte[] sidAdmin, byte[] sidEngine, byte[] sidCreator)
    {
        int objPage = db.DefinitionPage.CatalogRootPage;
        int acesPage = db.DefinitionPage.AcesRootPage;
        int queriesPage = db.DefinitionPage.QueriesRootPage;
        int relPage = db.DefinitionPage.RelationshipsRootPage;
        // The DAO catalog hierarchy Access navigates: a root (0x0F000000) parents the three containers
        // (Tables/Databases/Relationships); tables live under Tables, MSysDb under Databases. Access looks
        // objects up by (ParentId, Name), so these parents must be exact.
        Table msysObjects = db.OpenTableAt(objPage, "MSysObjects");
        TableDefinition aces = db.OpenTableAt(acesPage, "MSysACEs").Definition;
        void ObjectRow(int id, string name, ObjectType type, ObjectAttributes flags, int parentId, byte[]? owner) =>
            JetCatalog.InsertObjectRow(db.Channel, msysObjects.Definition, name, id, type, parentId,
                unchecked((int)flags), owner, properties: null);
        void Ace(int objectId, byte[] sid, int acm, bool inherit = false) =>
            JetCatalog.InsertAceRow(db.Channel, aces, objectId, sid, acm, inherit);

        // Catalog rows in the real stored order: DAO containers + MSysDb first, then the system tables, then
        // the DAO "SingleRecord" pseudo-object. Access's catalog bootstrap walks MSysObjects in this order.
        ObjectRow(JetCatalog.ObjectContainerParentId, "Tables", ObjectType.Container,
            ObjectAttributes.System, parentId: JetCatalog.ContainerParentId, owner: sidEngine);
        ObjectRow(JetCatalog.DatabaseContainerParentId, "Databases", ObjectType.Container,
            ObjectAttributes.System, parentId: JetCatalog.ContainerParentId, owner: sidEngine);
        ObjectRow(JetCatalog.RelationshipContainerParentId, "Relationships", ObjectType.Container,
            ObjectAttributes.System, parentId: JetCatalog.ContainerParentId, owner: sidEngine);
        ObjectRow(JetCatalog.DatabaseObjectId, "MSysDb", ObjectType.Database,
            ObjectAttributes.System, parentId: JetCatalog.DatabaseContainerParentId, owner: sidAdmin);
        ObjectRow(objPage, "MSysObjects", ObjectType.Table,
            ObjectAttributes.System, parentId: JetCatalog.ObjectContainerParentId, owner: sidEngine);
        ObjectRow(acesPage, "MSysACEs", ObjectType.Table,
            ObjectAttributes.System, parentId: JetCatalog.ObjectContainerParentId, owner: sidEngine);
        ObjectRow(queriesPage, "MSysQueries", ObjectType.Table,
            ObjectAttributes.System, parentId: JetCatalog.ObjectContainerParentId, owner: sidEngine);
        ObjectRow(relPage, "MSysRelationships", ObjectType.Table,
            ObjectAttributes.System, parentId: JetCatalog.ObjectContainerParentId, owner: sidEngine);
        ObjectRow(JetCatalog.FirstPagelessObjectId, "SingleRecord", ObjectType.SingleRecord,
            ObjectAttributes.QueryDef, parentId: JetCatalog.RelationshipContainerParentId, owner: null);

        // Access-control rows (verified per-object-class masks) in the same object order as the catalog rows.
        Ace(JetCatalog.ObjectContainerParentId, sidCreator, 0x0F00FE, inherit: true);
        Ace(JetCatalog.ObjectContainerParentId, sidAdmin, 0x060001);
        Ace(JetCatalog.ObjectContainerParentId, sidUsers, 0x0FFEFF, inherit: true);
        Ace(JetCatalog.DatabaseContainerParentId, sidAdmin, 0x060000);
        Ace(JetCatalog.RelationshipContainerParentId, sidCreator, 0x0F00FE, inherit: true);
        Ace(JetCatalog.RelationshipContainerParentId, sidAdmin, 0x060001);
        Ace(JetCatalog.RelationshipContainerParentId, sidUsers, 0x0FFFFF, inherit: true);
        Ace(JetCatalog.DatabaseObjectId, sidAdmin, 0x06000E); Ace(JetCatalog.DatabaseObjectId, sidUsers, 0x00000E);   // MSysDb
        Ace(objPage, sidAdmin, 0x060000); Ace(objPage, sidUsers, 0x000014);   // MSysObjects
        Ace(acesPage, sidAdmin, 0x060000);                                          // MSysACEs (admin only)
        Ace(queriesPage, sidAdmin, 0x060000); Ace(queriesPage, sidUsers, 0x000014); // MSysQueries
        Ace(relPage, sidAdmin, 0x0E0000); Ace(relPage, sidUsers, 0x000014);   // MSysRelationships

        // The system tables carry the indexes Access uses to navigate the catalog. ParentIdName is the first
        // real index, Id (the PK) second — matching real files.
        // Its first index must use the known definition: catalog name lookup needs this index populated.
        new SchemaEditor(msysObjects.Channel, db.Catalog, db.Collation).AddIndex(
            msysObjects.Definition, "ParentIdName", [("ParentId", false), ("Name", false)],
            isUnique: true, isPrimary: false, disallowNull: false, ignoreNulls: false);
        db.CreateIndex("MSysObjects", "Id", [("Id", false)], isUnique: true, isPrimary: true);
        db.CreateIndex("MSysACEs", "ObjectId", [("ObjectId", false)], disallowNull: true);
        db.CreateIndex("MSysQueries", "ObjectIdAttribute", [("ObjectId", false), ("Attribute", false), ("Order", false)], isUnique: true, isPrimary: true);
        db.CreateIndex("MSysRelationships", "szRelationship", [("szRelationship", false)]);
        db.CreateIndex("MSysRelationships", "szObject", [("szObject", false)]);
        db.CreateIndex("MSysRelationships", "szReferencedObject", [("szReferencedObject", false)]);

        // Complex-column system tables — ACE 12 and later only (see CreateComplexSystemTables).
        if (db.Format.IsAccdb) CreateComplexSystemTables(db, sidEngine);

        // Note: MSysAccessStorage and the MSysNavPane* tables are deliberately NOT created here. Verified across
        // ~135 pure-DAO reference files: none of them carry those tables — Access creates them (plus the nav-pane
        // long SID) itself on first open. Emitting them ourselves both diverged from real DAO output and produced
        // a table Access's compact rejected ("-1206 Unrecognized database format"). A faithful native file mirrors
        // DAO: core catalog only, and Access augments on first open.
    }

    /// <summary>
    /// Creates <c>MSysComplexColumns</c> and the <c>MSysComplexType_*</c> storage tables — the complex-column
    /// (multi-value / attachment) infrastructure Access 2007 / ACE 12 introduced. <b>Jet 4 has none of it</b>,
    /// so the caller gates this on version byte <c>0x02</c> or later.
    ///
    /// <para>The registry table is required even in a database that never uses a complex column: ACE consults
    /// it on every <c>CREATE TABLE</c>, and a database without it rejects DDL through the OLE DB provider with
    /// "Cannot find table or constraint". It stays empty — ACE reads it, never writes it (both facts isolated
    /// in <c>AceDdlOnLibRedDatabaseProbeTest</c>). The storage tables are created for completeness so a
    /// complex column added later has somewhere to live.</para>
    ///
    /// <para>These go through the ordinary writers, so each gets its TDEF, usage map, catalog row and index
    /// roots the same way a user table does; the rows are then corrected to the system flags and owner the
    /// real engine writes. Page numbers therefore follow LibRed's own allocation rather than matching a
    /// DAO-created file position for position — DAO's numbering is a consequence of how it lays out the core
    /// four tables' usage maps and index roots, which LibRed does differently.</para>
    /// </summary>
    private static void CreateComplexSystemTables(JetDatabase db, byte[] sidEngine)
    {
        db.CreateTable("MSysComplexColumns", MSysComplexColumnsColumns, tableType: TableType.System);
        // Index names, order and flags as the engine writes them: the ComplexID primary key first, then the
        // two non-unique lookups the engine uses to find a table's complex columns.
        db.CreateIndex("MSysComplexColumns", "IdxID", [("ComplexID", false)],
            isUnique: true, isPrimary: true, disallowNull: true, ignoreNulls: true);
        db.CreateIndex("MSysComplexColumns", "IdxConceptualTableID", [("ConceptualTableID", false)],
            disallowNull: true, ignoreNulls: true);
        db.CreateIndex("MSysComplexColumns", "IdxFlatTableID", [("FlatTableID", false)],
            disallowNull: true, ignoreNulls: true);
        // The registry carries the plain system flag, the flat storage tables ComplexStorage as well — as the real
        // engine writes them.
        MarkAsSystemTable(db, "MSysComplexColumns", ObjectAttributes.System, sidEngine);

        foreach ((string name, ColumnSpec[] columns) in MSysComplexTypeTables)
        {
            db.CreateTable(name, columns, tableType: TableType.System);
            MarkAsSystemTable(db, name, ObjectAttributes.System | ObjectAttributes.ComplexStorage, sidEngine);
        }
    }

    /// <summary>Corrects the MSysObjects row the ordinary writers gave a system table to what the engine writes:
    /// the system flags and the engine's owner.</summary>
    private static void MarkAsSystemTable(JetDatabase db, string name, ObjectAttributes flags, byte[] sidEngine)
    {
        TableDefinition definition = db.Catalog.FindTable(name)
            ?? throw new InvalidOperationException($"'{name}' was not found after creating it.");

        new SchemaEditor(db.Channel, db.Catalog, db.Collation).UpdateCatalogRows("MSysObjects", "Id",
            definition.DefinitionPage, required: true, ("Flags", unchecked((int)flags)), ("Owner", sidEngine));
        db.Catalog.Invalidate();
    }

    /// <summary>Builds a system table's TDEF page and its owned/free usage-map page. Long-value (Memo/Ole)
    /// columns each get an owned+free map on that page (rows 2 onward, after the two data-page maps), so a
    /// row that stores a long value — e.g. an <c>MSysObjects</c> catalog row carrying an <c>LvProp</c> blob —
    /// has somewhere to record its LVAL page.</summary>
    private static (byte[] Tdef, byte[] UsageMap) BuildSystemTable(
        JetFormatBase format, IReadOnlyList<ColumnSpec> columns, int usageMapPage, Collation collation)
    {
        var longValueCols = columns.Select((c, pos) => (c, id: c.ColumnId ?? pos))
            .Where(x => x.c.Type is JetDataType.Memo or JetDataType.Ole).ToList();
        var longValueSpecs = new List<LongValueColumnSpec>(longValueCols.Count);
        for (int j = 0; j < longValueCols.Count; j++)
            longValueSpecs.Add(new LongValueColumnSpec(longValueCols[j].id, UsedRow: 2 + 2 * j, FreeRow: 3 + 2 * j, MapPage: usageMapPage));

        byte[] tdef = TableDefinition.Build(format, TableType.System, columns, collation,
            longValueColumns: longValueSpecs, usageMapPage: usageMapPage).Page;
        var tdefPage = new byte[format.PageSize];
        Array.Copy(tdef, tdefPage, format.PageSize);   // these system TDEFs fit one page

        byte[] usageMap = UsageMap.NewMapPage(format, 2 + longValueCols.Count * 2);
        return (tdefPage, usageMap);
    }

    /// <summary>The <c>MSysObjects.ParentId</c> of a top-level user object — the database's object container,
    /// a constant <c>0x0F000001</c>. Used for both table and query/view objects (verified vs Northwind).</summary>
    internal const int ObjectContainerParentId = 0x0F000001;

    /// <summary>The <c>MSysObjects.ParentId</c> of a relationship object — the Relationships container,
    /// <c>0x0F000003</c> (verified vs ACE). A relationship may share its name with a table or a query but not with
    /// another relationship (verified vs ACE).</summary>
    internal const int RelationshipContainerParentId = 0x0F000003;

    /// <summary>The <c>MSysObjects.ParentId</c> of <c>MSysDb</c> — the Databases container, <c>0x0F000002</c>.</summary>
    internal const int DatabaseContainerParentId = 0x0F000002;

    /// <summary>The <c>MSysObjects.ParentId</c> of every container — <c>Tables</c>, <c>Forms</c>, <c>Reports</c> and the
    /// rest, each a row of its own whose <c>Id</c> its objects carry as their <c>ParentId</c>. <c>0x0F000000</c> itself
    /// has no row (system-catalog §11).</summary>
    internal const int ContainerParentId = 0x0F000000;

    /// <summary>The <c>MSysObjects.Id</c> of the database object, <c>MSysDb</c>.</summary>
    internal const int DatabaseObjectId = 0x10000000;

    /// <summary>The first <c>MSysObjects.Id</c> of an object with no TDEF page to take its id from — a query, a
    /// relationship, the <c>SingleRecord</c> pseudo-object. They count up from here, so they are negative.</summary>
    internal const int FirstPagelessObjectId = unchecked((int)0x80000000);


    // MSysObjects TDEF page — the catalog root, from the page-0 bootstrap pointer.
    private readonly int _catalogPage;

    private static int CatalogRoot(PageChannel channel)
    {
        var page0 = new DatabaseDefinitionPage();
        page0.Read(channel.ReadPage(0), channel.Format);
        return page0.CatalogRootPage;
    }

    private readonly PageChannel _channel;
    private IReadOnlyList<TableDefinition>? _tables;
    private TableDefinition? _catalogDef;
    // The objects of the Tables container looked up by name (FindObject) — a table's TableDefinition, a StoredQuery, null for
    // neither. Dropped whenever the catalog is invalidated.
    private readonly Dictionary<string, object?> _objects = [with(StringComparer.OrdinalIgnoreCase)];
    private IReadOnlyList<ForeignKey>? _relationships;
    private IReadOnlyList<ComplexColumn>? _complexColumns;
    private long _seenSchemaGeneration;

    internal JetCatalog(PageChannel channel)
    {
        _catalogPage = CatalogRoot(channel);
        _channel = channel;
        _seenSchemaGeneration = channel.SchemaGeneration;
    }

    /// <summary>MSysObjects' own definition, read from the TDEF page page 0's catalog root names — what every
    /// catalog lookup reads it by.</summary>
    private TableDefinition MSysObjects => _catalogDef ??= ReadTableDefinition(_catalogPage, "MSysObjects", isSystem: true);

    /// <summary>All tables in the database (user and system).</summary>
    public IReadOnlyList<TableDefinition> Tables
    {
        get
        {
            EnsureFresh();
            return _tables ??= ObjectNames(ObjectType.Table).Select(name => FindTable(name)
                ?? throw new InvalidDataException($"Catalog table '{name}' could not be resolved through its name index."))
                .ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// The account SIDs <b>as this database masks them</b>: the admin user, who owns an object LibRed creates
    /// (LibRed acts as the default workgroup's admin), the Users group, and the Creator placeholder a container's
    /// inheritable grant names in place of whoever creates an object in it. Every on-disk SID is a workgroup
    /// account SID XOR'd with a keystream that differs per file and is stored nowhere, but which page 0 determines
    /// (page-00 §2.3). An object written into a file has to carry that file's SIDs, or its owner decodes to no
    /// account at all.
    /// </summary>
    /// <remarks>From page 0 rather than from <c>MSysObjects</c>' own Engine-owned row, which gives the same mask
    /// away: an Access-encrypted <c>.accdb</c> can leave that row's owner un-re-masked, and only page 0 follows the
    /// key the file has now.</remarks>
    public (byte[] Admin, byte[] Users, byte[] Creator) SecuritySids
    {
        get
        {
            ReadOnlySpan<byte> page0 = _channel.ReadPageShared(0).Span;
            return (Crypto.SidKeystream.MaskAccount(page0, Crypto.SidKeystream.AdminAccount, _channel.Format),
                Crypto.SidKeystream.MaskAccount(page0, Crypto.SidKeystream.UsersAccount, _channel.Format),
                Crypto.SidKeystream.MaskAccount(page0, Crypto.SidKeystream.CreatorAccount, _channel.Format));
        }
    }

    /// <summary>Every stored query in the database, by name, each read by <see cref="FindQuery"/>.</summary>
    public IReadOnlyDictionary<string, StoredQuery> Queries
    {
        get
        {
            Dictionary<string, StoredQuery> queries = [with(StringComparer.OrdinalIgnoreCase)];
            foreach (string name in ObjectNames(ObjectType.Query))
                if (FindQuery(name) is { } query)
                    queries[name] = query;
            return queries;
        }
    }

    /// <summary>Drops the cached catalog so a freshly created table is picked up on next read.</summary>
    internal void Invalidate(bool markChanged = true)
    {
        _tables = null;
        _catalogDef = null;
        _objects.Clear();
        _relationships = null;
        _complexColumns = null;
        _seenSchemaGeneration = _channel.SchemaGeneration;
        if (markChanged) _channel.MarkSchemaChanged();
    }

    /// <summary>Every complex (multi-value / attachment) column in the database, wired to the table its
    /// values live in. Empty when the file has no <c>MSysComplexColumns</c> — a Jet 4 database has none.</summary>
    public IReadOnlyList<ComplexColumn> ComplexColumns
    {
        get { EnsureFresh(); return _complexColumns ??= LoadComplexColumns().AsReadOnly(); }
    }

    /// <summary>The complex column <paramref name="column"/> of <paramref name="table"/>, or null when that
    /// column is not a complex one.</summary>
    public ComplexColumn? FindComplexColumn(string table, string column) =>
        ComplexColumns.FirstOrDefault(c =>
            string.Equals(c.OwnerTable.Name, table, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.ColumnName, column, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads <c>MSysComplexColumns</c> and resolves each row to its owner table, its flat table and the flat
    /// table's two bookkeeping columns. A row whose tables are missing is skipped rather than throwing: the
    /// catalog must stay readable even where the complex wiring is incomplete.
    /// </summary>
    private List<ComplexColumn> LoadComplexColumns()
    {
        var resolved = new List<ComplexColumn>();
        if (FindTable("MSysComplexColumns") is not { } definition) return resolved;

        if (definition.FindColumn("ColumnName") is not { Index: var name } || definition.FindColumn("ComplexID") is not { Index: var id }
            || definition.FindColumn("ConceptualTableID") is not { Index: var owner }
            || definition.FindColumn("FlatTableID") is not { Index: var flat })
            return resolved;
        int? elementType = definition.FindColumn("ComplexTypeObjectID")?.Index;

        foreach (object?[] row in new Storage.Table(_channel, definition, this).Rows())
        {
            if (row[name] is not string columnName) continue;
            if (TableWithId(row[owner]) is not { } ownerTable || TableWithId(row[flat]) is not { } flatTable) continue;
            if (ownerTable.FindColumn(columnName) is null) continue;

            // Structural, never by name: the primary index names the per-value id, and the one non-unique
            // single-column index names the link back to the owning record.
            ColumnDef? valueId = flatTable.Indexes.FirstOrDefault(i => i.IsPrimaryKey)?.Columns is [{ Column: { } pk }] ? pk : null;
            ColumnDef? ownerLink = flatTable.Indexes
                .FirstOrDefault(i => !i.IsUnique && !i.IsPrimaryKey && i.Columns.Count == 1)?.Columns[0].Column;
            if (valueId is null || ownerLink is null || valueId == ownerLink) continue;

            resolved.Add(new ComplexColumn(
                columnName, row[id] is null ? 0 : Convert.ToInt32(row[id], CultureInfo.InvariantCulture),
                ownerTable, flatTable, ownerLink, valueId,
                elementType is { } element ? TableWithId(row[element])?.Name : null));
        }
        return resolved;
    }

    /// <summary>The table whose MSysObjects id (its TDEF page) is <paramref name="id"/>.</summary>
    internal TableDefinition? TableWithId(object? id) =>
        id is null ? null
        : Tables.FirstOrDefault(t => t.DefinitionPage == Convert.ToInt32(id, CultureInfo.InvariantCulture));

    private void EnsureFresh()
    {
        long generation = _channel.SchemaGeneration;
        if (generation == _seenSchemaGeneration) return;
        Invalidate(markChanged: false);
        // The file's format version is schema too, and another connection can raise it — adding a BIGINT or a
        // DATETIME2 column moves the byte on page 0. Without this, a handle open across that change goes on
        // reporting the version the file had when IT opened, and would refuse a column the file can now hold.
        _channel.ResyncFormatVersion();
        _seenSchemaGeneration = generation;
    }

    /// <summary>All relationships (foreign keys) defined in the database.</summary>
    public IReadOnlyList<ForeignKey> Relationships { get { EnsureFresh(); return _relationships ??= LoadRelationships().AsReadOnly(); } }

    /// <summary>Relationships for which <paramref name="table"/> is the referencing (child) table.</summary>
    public IEnumerable<ForeignKey> ForeignKeysOf(string table) =>
        Relationships.Where(r => string.Equals(r.Table, table, StringComparison.OrdinalIgnoreCase));

    /// <summary>User (non-system) tables only.</summary>
    public IEnumerable<TableDefinition> UserTables => Tables.Where(t => !t.IsSystem);

    /// <summary>The table called <paramref name="name"/>, or null when that name is not a table's.</summary>
    public TableDefinition? FindTable(string name) => FindObject(name) as TableDefinition;

    /// <summary>The stored query called <paramref name="name"/>, or null when that name is not a query's.</summary>
    public StoredQuery? FindQuery(string name) => FindObject(name) as StoredQuery;

    /// <summary>
    /// The object called <paramref name="name"/> in the Tables container, found by one seek in <c>MSysObjects</c> and
    /// returned as what its <c>Type</c> makes it: a table's <see cref="TableDefinition"/>, a <see cref="StoredQuery"/>, or null
    /// for any other kind of object, or none. Kept until the catalog is next invalidated.
    /// </summary>
    private object? FindObject(string name)
    {
        EnsureFresh();
        if (_objects.TryGetValue(name, out object? found)) return found;

        if (FindObjectRow(JetCatalog.ObjectContainerParentId, name) is not { } row) return _objects[name] = null;
        return _objects[name] = row[MSysObjects.RequireColumn("Type").Index] switch
        {
            short t when (ObjectType)t == ObjectType.Table => ReadTable(row),
            short t when (ObjectType)t == ObjectType.Query => ReadQuery(row),
            _ => null,
        };
    }

    /// <summary>A table's definition from its <c>MSysObjects</c> row: its TDEF — the row's <c>Id</c> is that page — with
    /// what the row adds, its flags and the column and table properties in its <c>LvProp</c>.</summary>
    private TableDefinition ReadTable(object?[] row)
    {
        TableDefinition mo = MSysObjects;
        int definitionPage = (int)row[mo.RequireColumn("Id").Index]!;
        string name = (string)row[mo.RequireColumn("Name").Index]!;
        var flags = (ObjectAttributes)unchecked((uint)(int)row[mo.RequireColumn("Flags").Index]!);

        // A table is "system" (excluded from the user-table list, as Access's own schema view
        // does) if it is flagged system or hidden, or is named as engine/temporary infrastructure:
        // MSys*, a leading '~' (temp), or a leading '#' (e.g. EFCore.Jet's hidden #Dual helper).
        bool isSystem = (flags & (ObjectAttributes.System | ObjectAttributes.SystemAttribute | ObjectAttributes.Hidden)) != 0
                        || name.StartsWith("MSys", StringComparison.Ordinal)
                        || name.StartsWith('~')
                        || name.StartsWith('#');

        TableDefinition definition = ReadTableDefinition(definitionPage, name, isSystem);
        definition.ObjectFlags = flags;
        // Attach column DefaultValue and table CHECK properties from the extended-properties (LvProp) blob.
        if (row[mo.RequireColumn("LvProp").Index] is byte[] { Length: > 0 } blob)
        {
            IReadOnlyList<PropertyBlob.Property> properties = PropertyBlob.Read(blob);
            var defaults = PropertyBlob.ReadColumnDefaults(properties);
            var required = PropertyBlob.ReadRequiredColumns(properties);
            foreach (ColumnDef column in definition.Columns)
            {
                if (defaults.TryGetValue(column.Name, out string? value))
                    column.DefaultValue = value;
                if (required.Contains(column.Name))
                    column.IsNullable = false;
                (column.ValidationRule, column.ValidationText) = PropertyBlob.ReadValidation(properties, column.Name);
                // A calculated column's expression and REAL result type live here, not in the descriptor
                // (§3.4a). Without them the stored payload can only be guessed from its width, which is
                // wrong whenever the declared and expression types differ.
                if (column.IsCalculated)
                    (column.CalculatedExpression, column.CalculatedResultType) =
                        PropertyBlob.ReadCalculated(properties, column.Name);
            }

            var checks = PropertyBlob.ReadCheckConstraints(properties);
            if (checks.Count > 0) definition.CheckConstraints = checks;

            (definition.ValidationRule, definition.ValidationText) = PropertyBlob.ReadValidation(properties, "");
        }
        return definition;
    }

    /// <summary>A stored query from its <c>MSysObjects</c> row: rebuilt from its own MSysQueries rows, keyed by the
    /// row's <c>Id</c>. Null when it has none to rebuild from.</summary>
    private StoredQuery? ReadQuery(object?[] row)
    {
        int id = (int)row[MSysObjects.RequireColumn("Id").Index]!;
        if (FindTable("MSysQueries") is not { } mq) return null;

        int oid = mq.RequireColumn("ObjectId").Index, attr = mq.RequireColumn("Attribute").Index,
            expr = mq.RequireColumn("Expression").Index, flag = mq.RequireColumn("Flag").Index,
            n1 = mq.RequireColumn("Name1").Index, n2 = mq.RequireColumn("Name2").Index,
            order = mq.RequireColumn("Order").Index, lvExtra = mq.RequireColumn("LvExtra").Index;
        List<object?[]> queryRows = [.. new Table(_channel, mq, this).RowsWhere([oid], r => r[oid] is int o && o == id)
            .Select(r => r.Values)];
        if (queryRows.Count == 0) return null;

        // Reconstruct it: an action query → its readback, otherwise a SELECT → its view SQL.
        // The Attribute=1 row is the OPERATION row and its Flag is the query KIND, of which SELECT (1) is one
        // value -- so the row's presence does not make a query an action query. Access writes it on plain
        // SELECTs as well as on action queries, and it writes no such row at all for others (both shapes are
        // common in the wild), which is why the kind has to be read rather than inferred from the row.
        object?[]? operation = queryRows.FirstOrDefault(r => IsAttribute(r[attr], QueryAttribute.Operation));
        QueryOperation kind = operation?[flag] is short k ? (QueryOperation)k : QueryOperation.Select;
        StoredQuery query = operation is not null && kind != QueryOperation.Select
            ? ReconstructAction(queryRows, attr, expr, flag, n1, n2, order, lvExtra)
            : new StoredQuery(Reconstruct(queryRows, attr, expr, flag, n1, n2, order, lvExtra), null, IsAction: false);

        // The declared parameters (Attribute=2 rows), in declaration order, for EXECUTE positional binding.
        // Each row's Flag is the parameter's Jet type code (0 for Access's untyped parameter).
        var parameters = RowsOf(queryRows, attr, order, QueryAttribute.Parameter)
            .Where(r => r[n1] is string)
            .Select(r =>
            {
                JetDataType? parameterType = r[flag] is short f and not 0 ? (JetDataType)(byte)f : null;
                // The declared facets ride in LvExtra: a length for text, precision and scale packed
                // together for a decimal.
                var (size, precision, scale) = parameterType is { } t
                    ? StoredQuery.UnpackParameterFacets(t, r[lvExtra] as int?)
                    : (null, null, null);
                return new StoredQueryParameter(DeclaredParameterName((string)r[n1]!), parameterType, size, precision, scale);
            })
            .ToList();
        return query with { Parameters = parameters };
    }

    /// <summary>The names of the Tables container's objects of <paramref name="type"/>, read from <c>MSysObjects</c> —
    /// what the whole-catalog lists (<see cref="Tables"/>, <see cref="Queries"/>) go through
    /// <see cref="FindObject"/> with.</summary>
    private List<string> ObjectNames(ObjectType type)
    {
        EnsureFresh();
        int nameIndex = MSysObjects.RequireColumn("Name").Index, typeIndex = MSysObjects.RequireColumn("Type").Index;
        var objects = new Table(_channel, MSysObjects, this);
        return [.. objects.Rows(objects.DecodeOnly([nameIndex, typeIndex]))
            .Where(r => r[typeIndex] is short t && (ObjectType)t == type)
            .Select(r => (string)r[nameIndex]!)];
    }

    /// <summary>The <c>MSysObjects</c> id of the object of <paramref name="type"/> named <paramref name="name"/>,
    /// or null when there is none.</summary>
    /// <remarks>Every object is the row of that name in its kind's container (system-catalog §11), so it is sought
    /// there (<see cref="FindObjectRow"/>). Tables and queries share the Tables container, relationships have the
    /// Relationships one and the database objects the Databases one, all constants; any other container is itself a
    /// row under the root, found by its name.</remarks>
    internal int? FindObjectId(string name, ObjectType type)
    {
        EnsureFresh();
        int idIdx = MSysObjects.RequireColumn("Id").Index, typeIdx = MSysObjects.RequireColumn("Type").Index;

        int? container = type switch
        {
            ObjectType.Table or ObjectType.Query or ObjectType.LinkedTable => JetCatalog.ObjectContainerParentId,
            ObjectType.Relationship => JetCatalog.RelationshipContainerParentId,
            ObjectType.Database or ObjectType.DatabaseDocument => JetCatalog.DatabaseContainerParentId,
            ObjectType.Container => JetCatalog.ContainerParentId,
            _ => (type switch
            {
                ObjectType.User => "SysRel",
                ObjectType.Module => "Modules",
                ObjectType.Report => "Reports",
                ObjectType.Macro => "Scripts",
                ObjectType.Form => "Forms",
                _ => null,
            }) is { } containerName
                && FindObjectRow(JetCatalog.ContainerParentId, containerName) is { } containerRow
                    ? Convert.ToInt32(containerRow[idIdx], CultureInfo.InvariantCulture)
                    : null,
        };
        return container is { } parent && FindObjectRow(parent, name) is { } row && row[typeIdx] is short t && (ObjectType)t == type
            ? Convert.ToInt32(row[idIdx], CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>The <c>MSysObjects</c> row named <paramref name="name"/> in <paramref name="container"/>, or null.
    /// Sought through MSysObjects' unique <c>(ParentId, Name)</c> index when its name collation can be encoded;
    /// otherwise scanned through the same parent/name predicate. The seek can over-return on a text key, so the
    /// name is checked again on what it finds.</summary>
    internal object?[]? FindObjectRow(int container, string name)
    {
        EnsureFresh();
        TableDefinition mo = MSysObjects;
        int parentIndex = mo.RequireColumn("ParentId").Index, nameIndex = mo.RequireColumn("Name").Index;
        IndexDef byName = mo.Indexes.FirstOrDefault(i =>
                i.Columns is [{ Column.Index: var parent }, { Column.Index: var named }]
                && parent == parentIndex && named == nameIndex)
            ?? throw new InvalidDataException("MSysObjects has no (ParentId, Name) index.");

        var key = new object?[mo.Columns.Count];
        key[parentIndex] = container;
        key[nameIndex] = name;
        var objects = new Table(_channel, mo, this);
        return mo.Columns[nameIndex].Collation.IsIndexKeyEncodable
            ? objects.SeekRows(byName, key).FirstOrDefault(Matches)
            : objects.RowsWhere([parentIndex, nameIndex], Matches).Select(r => r.Values).FirstOrDefault();

        bool Matches(object?[] row) => row[parentIndex] is int p && p == container
            && string.Equals(row[nameIndex] as string, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A catalog table the caller cannot proceed without — <c>MSysObjects</c>, <c>MSysACEs</c> and
    /// the rest. Their absence is a broken database rather than a case to handle, so this throws where
    /// <see cref="FindTable"/> returns null.</summary>
    public TableDefinition RequireTable(string name) =>
        FindTable(name) ?? throw new InvalidOperationException($"{name} catalog table was not found.");

    private List<ForeignKey> LoadRelationships()
    {
        TableDefinition? def = FindTable("MSysRelationships");
        if (def is null) return [];

        int nameIdx = def.RequireColumn("szRelationship").Index;
        int childTableIdx = def.RequireColumn("szObject").Index;
        int childColumnIdx = def.RequireColumn("szColumn").Index;
        int parentTableIdx = def.RequireColumn("szReferencedObject").Index;
        int parentColumnIdx = def.RequireColumn("szReferencedColumn").Index;
        int orderIdx = def.RequireColumn("icolumn").Index;
        int flagsIdx = def.RequireColumn("grbit").Index;

        // One row per column; group by relationship name and order columns by icolumn.
        var groups = new Dictionary<string, (string Child, string Parent, RelationshipFlags Flags,
            List<(int Order, string Column, string ReferencedColumn)> Columns)>();

        foreach (object?[] row in new Table(_channel, def, this).Rows())
        {
            string name = (string)row[nameIdx]!;
            if (!groups.TryGetValue(name, out var g))
            {
                g = ((string)row[childTableIdx]!, (string)row[parentTableIdx]!,
                     (RelationshipFlags)(int)row[flagsIdx]!, []);
                groups[name] = g;
            }
            g.Columns.Add(((int)row[orderIdx]!, (string)row[childColumnIdx]!, (string)row[parentColumnIdx]!));
        }

        return groups
            .Select(kvp =>
            {
                // The actions come from the child's relationship block in its TDEF (0x15/0x16), not from grbit:
                // made to disagree, ACE cascades by the block (measured against ACE). grbit answers only for a
                // relationship with no block — an unenforced one, which has nothing to cascade.
                LogicalIndexDef? block = FindTable(kvp.Value.Child)?.LogicalIndexes.FirstOrDefault(l =>
                    l.IsRelationship && !l.IsIncomingRelationship &&
                    string.Equals(l.Name, kvp.Key, StringComparison.OrdinalIgnoreCase));
                RelationshipFlags flags = kvp.Value.Flags;
                return new ForeignKey(
                    kvp.Key,
                    kvp.Value.Child,
                    kvp.Value.Parent,
                    kvp.Value.Columns.OrderBy(x => x.Order).Select(x => (x.Column, x.ReferencedColumn)).ToList().AsReadOnly(),
                    IsEnforced: !flags.HasFlag(RelationshipFlags.DontEnforce),
                    CascadeUpdate: block is null
                        ? flags.HasFlag(RelationshipFlags.UpdateCascade)
                        : block.UpdateAction == RelationshipAction.Cascade,
                    CascadeDelete: block is null
                        ? flags.HasFlag(RelationshipFlags.DeleteCascade)
                        : block.DeleteAction == RelationshipAction.Cascade,
                    DeleteSetNull: block is null
                        ? flags.HasFlag(RelationshipFlags.DeleteSetNull)
                        : block.DeleteAction == RelationshipAction.SetNull,
                    IsOneToOne: flags.HasFlag(RelationshipFlags.OneToOne));
            })
            .ToList();
    }

    /// <summary>Rebuilds a stored action query's executable SQL from its MSysQueries rows. Handles every kind
    /// whose statement LibRed's engine can run — CREATE/DROP TABLE, INSERT (from VALUES or from a SELECT),
    /// UPDATE, DELETE and make-table; the rest return an unsupported reason.</summary>
    private static StoredQuery ReconstructAction(
        List<object?[]> rows, int attr, int expr, int flag, int n1, int n2, int order, int lvExtra)
    {
        object?[]? action = rows.FirstOrDefault(r => IsAttribute(r[attr], QueryAttribute.Operation));
        if (action is null) return new StoredQuery(null, "The stored action query has no action row.");

        var kind = (QueryOperation)(action[flag] is short f ? f : (short)0);
        if (kind == QueryOperation.Ddl)
            // The whole DDL statement is in Expression (Access stored it with a leading space).
            return new StoredQuery((action[expr] as string)?.TrimStart(), null);

        // Every remaining kind keeps its sources, predicate and declared parameters where a SELECT keeps them,
        // so they are read the same way. A query with no FROM source at all is an INSERT … VALUES, which has
        // none by definition.
        var source = BuildFromClause(rows, attr, expr, flag, n1, n2, order);
        string? where = source is { } s ? WhereClause(rows, attr, expr, order, s.Extra) : null;
        string Where() => where is null ? "" : $" WHERE {where}";
        var columns = RowsOf(rows, attr, order, QueryAttribute.Column);
        string declared = ParametersClause(rows, attr, flag, n1, order, lvExtra);
        // The SELECT of a make-table or an append query keeps its DISTINCT, DISTINCTROW and TOP on the same option
        // row a view does, and reads back the same way; WITH OWNERACCESS OPTION ends every kind of statement, as
        // it ends a SELECT.
        (string selecting, string owner) = SelectOptions(rows, attr, flag, n1, order);

        switch (kind)
        {
            case QueryOperation.Append:
                {
                    string target = Quote(action[n1] as string ?? "");
                    // A literal-value column (Flag 0x8000) is an INSERT … VALUES; a Flag-0 one reads its value
                    // from the query's own FROM source, which makes it an INSERT … SELECT. Both name the target
                    // column in Name2 and hold the value's text in Expression.
                    if (columns.Count == 0)
                        return new StoredQuery(null, "An append query with no columns is not executed by LibRed.");

                    string targetColumns = string.Join(", ", columns.Select(r => Quote(r[n2] as string ?? "")));
                    string values = string.Join(", ", columns.Select(r => r[expr] as string ?? "NULL"));

                    if (columns.All(r => r[flag] is short cf && cf == StoredQuery.AppendValueFlag))
                        return source is null
                            ? new StoredQuery($"{declared}INSERT INTO {target} ({targetColumns}) VALUES ({values}){owner}", null)
                            : new StoredQuery(null, "An append query cannot take both literal values and a source.");

                    return source is { } appendSource
                        ? new StoredQuery(
                            $"{declared}INSERT INTO {target} ({targetColumns}) SELECT {selecting}{values} FROM {appendSource.From}{Where()}{owner}", null)
                        : new StoredQuery(null, "An append query with no values and no source is not executed by LibRed.");
                }

            case QueryOperation.Update when source is { } updateSource:
                {
                    // One column row per assignment: Name2 is the target column — qualified when the update runs
                    // over a join — and Expression is the new value.
                    if (columns.Count == 0)
                        return new StoredQuery(null, "An update query with no assignments is not executed by LibRed.");
                    string assignments = string.Join(", ",
                        columns.Select(r => $"{Qualified(r[n2] as string ?? "")} = {r[expr] as string ?? "NULL"}"));
                    return new StoredQuery($"{declared}UPDATE {updateSource.From} SET {assignments}{Where()}{owner}", null);
                }

            case QueryOperation.Delete when source is { } deleteSource:
                {
                    // Access writes `DELETE <table>.* FROM …` when the query names the table's columns and
                    // `DELETE * FROM …` when it doesn't; the column row holds that `<table>.*` verbatim.
                    string what = columns.Count > 0 ? columns[0][expr] as string ?? "*" : "*";
                    return new StoredQuery($"{declared}DELETE {what} FROM {deleteSource.From}{Where()}{owner}", null);
                }

            case QueryOperation.MakeTable when source is { } intoSource:
                {
                    // The target is on the action row; a target in ANOTHER database file (Name2) is a shape
                    // LibRed has no statement for.
                    if (action[n2] is string external && external.Length > 0)
                        return new StoredQuery(null, $"A make-table query writing into '{external}' is not executed by LibRed.");

                    string selected = columns.Count == 0
                        ? "*"
                        : string.Join(", ", columns.Select(r =>
                            (r[n1] as string) is { } alias ? $"{r[expr] as string} AS {Quote(alias)}" : r[expr] as string ?? ""));
                    return new StoredQuery(
                        $"{declared}SELECT {selecting}{selected} INTO {Quote(action[n1] as string ?? "")} FROM {intoSource.From}{Where()}{GroupingClauses(rows, attr, expr, order)}{owner}", null);
                }
        }

        // Everything else is stored but not executed. Name the kind: "not supported" that doesn't say what it
        // is leaves a caller no way to tell an unimplemented feature from an unreadable file.
        string reason = kind switch
        {
            QueryOperation.Update or QueryOperation.Delete or QueryOperation.MakeTable =>
                "The stored action query names no table.",
            QueryOperation.Crosstab => "Crosstab (TRANSFORM) stored queries are not executed by LibRed yet.",
            QueryOperation.PassThrough => "Pass-through stored queries are not executed by LibRed yet.",
            QueryOperation.Union => "UNION stored queries are not executed by LibRed yet.",
            _ => $"Kind-{(short)kind} stored queries are not executed by LibRed yet.",
        };
        return new StoredQuery(null, reason);
    }

    /// <summary>Whether an <c>MSysQueries.Attribute</c> value is <paramref name="attribute"/>.</summary>
    private static bool IsAttribute(object? value, QueryAttribute attribute) =>
        value is byte b && (QueryAttribute)b == attribute;

    /// <summary>A stored query's rows of one <paramref name="attribute"/>, in their stored order.</summary>
    private static List<object?[]> RowsOf(List<object?[]> rows, int attr, int order, QueryAttribute attribute) =>
        [.. rows.Where(r => IsAttribute(r[attr], attribute)).OrderBy(r => StoredQuery.UnpackOrder(r[order]))];

    /// <summary>The keywords a query's option rows put after SELECT — DISTINCTROW or DISTINCT, then TOP n
    /// [PERCENT] — and the <c>WITH OWNERACCESS OPTION</c> that ends the statement, or empty strings.</summary>
    private static (string Selecting, string OwnerAccess) SelectOptions(
        List<object?[]> rows, int attr, int flag, int n1, int order)
    {
        // The option bits are cumulative and can share one row, so test each as a bit across all of them.
        List<object?[]> optionRows = RowsOf(rows, attr, order, QueryAttribute.Option);
        var options = QueryOptions.None;
        foreach (object?[] r in optionRows)
            if (r[flag] is short o) options |= (QueryOptions)o;

        // DISTINCT and DISTINCTROW are separate bits and separate keywords: DISTINCT dedupes output rows,
        // DISTINCTROW dedupes by the underlying contributing rows. Emitting one for the other changes results.
        // TOP n: an option row with the TOP bit; the count is in Name1. The PERCENT bit rides alongside it (48 =
        // TOP PERCENT), and dropping it turns "TOP 10 PERCENT" into "TOP 10" -- silently wrong.
        string? top = optionRows.Where(r => r[flag] is short f && ((QueryOptions)f & QueryOptions.Top) != 0)
            .Select(r => r[n1] as string).FirstOrDefault();
        string selecting = ((options & QueryOptions.DistinctRow) != 0 ? "DISTINCTROW "
                : (options & QueryOptions.Distinct) != 0 ? "DISTINCT " : "")
            + (top is null ? "" : $"TOP {top} {((options & QueryOptions.Percent) != 0 ? "PERCENT " : "")}");
        return (selecting, (options & QueryOptions.OwnerAccess) != 0 ? " WITH OWNERACCESS OPTION" : "");
    }

    /// <summary>A query's <c> GROUP BY …</c> and <c> HAVING …</c> clauses, each empty when it has none. HAVING is a
    /// single row holding the predicate verbatim, carrying no flag of its own.</summary>
    private static string GroupingClauses(List<object?[]> rows, int attr, int expr, int order)
    {
        var groupBy = RowsOf(rows, attr, order, QueryAttribute.GroupBy).Select(r => r[expr] as string ?? "").ToList();
        string? having = RowsOf(rows, attr, order, QueryAttribute.Having)
            .Select(r => r[expr] as string).FirstOrDefault(s => !string.IsNullOrEmpty(s));
        return (groupBy.Count > 0 ? $" GROUP BY {string.Join(", ", groupBy)}" : "")
            + (having is null ? "" : $" HAVING {having}");
    }

    /// <summary>Bracket-quotes a stored name, so one with a space in it survives.</summary>
    private static string Quote(string name) => $"[{name}]";

    /// <summary>Bracket-quotes a name that may already be table-qualified, quoting each part — a stored
    /// assignment names its target as <c>Orders.ShipCountry</c> when the update runs over a join, and
    /// quoting that whole string would make it one nonexistent column.</summary>
    private static string Qualified(string name) =>
        name.Contains('.') ? string.Join(".", name.Split('.').Select(Quote)) : Quote(name);

    /// <summary>
    /// A stored query's FROM clause, and the join conditions that could not go in it. Access stores a query's
    /// sources the same way whatever its kind — one <c>0x05</c> row per table and one <c>0x07</c> per join
    /// condition, flat, with no grouping — so a SELECT, an UPDATE, a DELETE and an append-from-SELECT all
    /// rebuild their sources through here. Null when the query names no table at all.
    /// </summary>
    private static (string From, List<string> Extra)? BuildFromClause(
        List<object?[]> rows, int attr, int expr, int flag, int n1, int n2, int order)
    {
        // A derived-table source has its subquery SQL in Expression and no Name1; a named table uses Name1.
        var tables = RowsOf(rows, attr, order, QueryAttribute.Table)
            .Select(r => (Table: r[n1] as string ?? "", Alias: r[n2] as string, Sub: r[n1] is null ? r[expr] as string : null)).ToList();
        if (tables.Count == 0) return null;
        var joins = RowsOf(rows, attr, order, QueryAttribute.Join)
            .Select(r => (Cond: r[expr] as string ?? "", Kind: r[flag] is short f ? (ViewJoinType)f : ViewJoinType.Inner,
                          Left: r[n1] as string ?? "", Right: r[n2] as string ?? "")).ToList();

        static string Ident(string s) => $"[{s}]";
        static string Render((string Table, string? Alias, string? Sub) t) =>
            t.Sub is { } sub ? $"({sub}) AS {Ident(t.Alias!)}"
            : t.Alias is not null && !string.Equals(t.Alias, t.Table, StringComparison.OrdinalIgnoreCase)
                ? $"{Ident(t.Table)} AS {Ident(t.Alias)}" : Ident(t.Table);
        string Key((string Table, string? Alias, string? Sub) t) => t.Alias ?? t.Table;

        // Build a JOIN chain by a spanning walk: only add a join once one of its two tables is already in
        // scope, bringing the other into scope (so a flat set of joins from a nested source rebuilds a valid
        // left-deep tree). If the new table is the condition's *left* side, a LEFT/RIGHT join flips.
        var from = new System.Text.StringBuilder(Render(tables[0]));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Key(tables[0]) };
        var pending = joins.ToList();
        for (bool progress = true; progress;)
        {
            progress = false;
            for (int i = 0; i < pending.Count; i++)
            {
                var j = pending[i];
                bool leftIn = used.Contains(j.Left), rightIn = used.Contains(j.Right);
                if (leftIn == rightIn) continue; // both already joined (extra condition) or neither reachable yet
                string newKey = leftIn ? j.Right : j.Left;
                int ti = tables.FindIndex(t => string.Equals(Key(t), newKey, StringComparison.OrdinalIgnoreCase));
                if (ti < 0) continue;

                ViewJoinType kind = leftIn ? j.Kind : j.Kind switch // flip on reversed order
                {
                    ViewJoinType.Left => ViewJoinType.Right,
                    ViewJoinType.Right => ViewJoinType.Left,
                    _ => j.Kind,
                };
                string kw = kind switch { ViewJoinType.Left => "LEFT", ViewJoinType.Right => "RIGHT", _ => "INNER" };
                from.Append(CultureInfo.InvariantCulture, $" {kw} JOIN {Render(tables[ti])} ON {j.Cond}");
                used.Add(newKey);
                pending.RemoveAt(i);
                progress = true;
                break;
            }
        }
        // Any tables the joins didn't reach are comma (cross) joins.
        foreach (var t in tables.Where(t => !used.Contains(Key(t))))
            from.Append(CultureInfo.InvariantCulture, $", {Render(t)}");

        // Joins whose two tables were both already in scope become extra WHERE conditions (a cyclic graph).
        return (from.ToString(), pending.Select(j => j.Cond).ToList());
    }

    /// <summary>The name a stored parameter binds to, from the name ACE stored — which is the name as declared, its
    /// brackets included: <c>[@firstName]</c> binds as <c>@firstName</c>. A form control is declared as a chain,
    /// <c>[Forms]![frmSelector]![txtTo]</c>, and binds as its parts undelimited and joined by bangs,
    /// <c>Forms!frmSelector!txtTo</c>, a period counting as a bang; the parser names a reference to it the same
    /// way. A period or bang inside delimiters is part of the name.</summary>
    private static string DeclaredParameterName(string declared)
    {
        var parts = new List<string>();
        var part = new System.Text.StringBuilder();
        char close = '\0';
        foreach (char c in declared)
        {
            if (close != '\0')
            {
                if (c == close) close = '\0';
                else part.Append(c);
            }
            else if (c is '[' or '`') close = c == '[' ? ']' : '`';
            else if (c is '!' or '.') { parts.Add(part.ToString()); part.Clear(); }
            else part.Append(c);
        }
        parts.Add(part.ToString());
        return string.Join('!', parts);
    }

    /// <summary>The leading <c>PARAMETERS name Type, …;</c> clause a query with declared parameters (its
    /// <c>0x02</c> rows: Name1=name, Flag=Jet type code) is rebuilt with, so the parser lowers references to
    /// those names in the body into engine parameters; empty for a query declaring none. An action query
    /// declares them exactly as a SELECT does.</summary>
    private static string ParametersClause(List<object?[]> rows, int attr, int flag, int n1, int order, int lvExtra)
    {
        var parameters = RowsOf(rows, attr, order, QueryAttribute.Parameter)
            .Select(r => (Name: r[n1] as string, Code: r[flag] is short f ? (byte)f : (byte)0, Extra: r[lvExtra] as int?))
            .Where(p => p.Name is not null)
            // ACE stores the name as declared, its brackets included ('[@firstName]'), and renders it as it stands;
            // LibRed stores it bare. Bracket only a bare one.
            .Select(p => $"{(p.Name!.StartsWith('[') ? p.Name : $"[{p.Name}]")} {AccessTypeName(p.Code)}{Facets(p.Code, p.Extra)}")
            .ToList();

        return parameters.Count == 0 ? "" : $"PARAMETERS {string.Join(", ", parameters)}; ";
    }

    /// <summary>A declared parameter's facets as the type name's suffix — <c>(50)</c> for a text length,
    /// <c>(18,4)</c> for a decimal's precision and scale — so the rebuilt clause declares what was declared.
    /// Empty where the row records none.</summary>
    private static string Facets(byte code, int? lvExtra)
    {
        if (code == 0) return "";   // the untyped parameter, rendered as the keyword Value
        var (size, precision, scale) = StoredQuery.UnpackParameterFacets((JetDataType)code, lvExtra);
        return size is { } length ? $"({length})"
            : precision is { } p ? $"({p},{scale ?? 0})"
            : "";
    }

    /// <summary>The WHERE clause: the query's stored predicate (the <c>0x08</c> row) and any join condition
    /// <see cref="BuildFromClause"/> could not place, ANDed together. The stored predicate is only
    /// parenthesised when something is being ANDed onto it — wrapping a lone predicate adds a paren pair
    /// Access never wrote, which is a needless difference from its own SQL.</summary>
    private static string? WhereClause(List<object?[]> rows, int attr, int expr, int order, List<string> extra)
    {
        string? where = RowsOf(rows, attr, order, QueryAttribute.Where)
            .Select(r => r[expr] as string).FirstOrDefault();

        return where is null ? (extra.Count > 0 ? string.Join(" AND ", extra) : null)
            : extra.Count == 0 ? where
            : string.Join(" AND ", extra.Prepend($"({where})"));
    }

    /// <summary>Rebuilds a simple-SELECT view's SQL from its MSysQueries rows; null if it uses an
    /// attribute we don't reconstruct (a non-simple query).</summary>
    private static string? Reconstruct(
        List<object?[]> rows, int attr, int expr, int flag, int n1, int n2, int order, int lvExtra)
    {
        // Bail out if the query uses attributes beyond a simple SELECT: a pass-through connection string or
        // complex-type data mean this is not a shape we can render. The operation row is allowed because a SELECT
        // may carry one — the caller has already checked that its kind IS SELECT, so reaching here with any other
        // kind is impossible.
        if (rows.Any(r => r[attr] is byte b
                && ((QueryAttribute)b is QueryAttribute.Connect or QueryAttribute.Complex || !Enum.IsDefined((QueryAttribute)b))))
            return null;

        // A column row's Name1 (when present) is its output alias.
        var columns = RowsOf(rows, attr, order, QueryAttribute.Column)
            .Select(r => (r[n1] as string) is { } a ? $"{r[expr] as string} AS [{a}]" : r[expr] as string ?? "").ToList();
        // A query with no table rows has no FROM clause, which Access allows and stores this way (`SELECT 1
        // AS n`). One with neither tables nor columns says nothing at all, and is not a query we can rebuild.
        var source = BuildFromClause(rows, attr, expr, flag, n1, n2, order);
        if (source is null && columns.Count == 0) return null;
        // No column rows at all is Access's "SELECT *" -- the shape every auto-generated form/report
        // record-source query takes. Treating it as unreconstructable dropped those queries silently.
        if (columns.Count == 0) columns.Add("*");

        (string selecting, string owner) = SelectOptions(rows, attr, flag, n1, order);
        // ORDER BY: one row per key (Expression = column, Name1 = "d" for descending).
        var orderBy = RowsOf(rows, attr, order, QueryAttribute.OrderBy)
            .Select(r => (r[expr] as string ?? "") + (string.Equals(r[n1] as string, "d", StringComparison.OrdinalIgnoreCase) ? " DESC" : ""))
            .Where(s => s.Length > 0).ToList();
        string? whereClause = WhereClause(rows, attr, expr, order, source?.Extra ?? []);

        var sql = new System.Text.StringBuilder(ParametersClause(rows, attr, flag, n1, order, lvExtra));
        sql.Append("SELECT ").Append(selecting);
        sql.Append(string.Join(", ", columns));
        if (source is { } from) sql.Append(" FROM ").Append(from.From);
        if (whereClause is not null) sql.Append(" WHERE ").Append(whereClause);
        sql.Append(GroupingClauses(rows, attr, expr, order));
        if (orderBy.Count > 0) sql.Append(" ORDER BY ").Append(string.Join(", ", orderBy));
        sql.Append(owner);
        return sql.ToString();
    }

    /// <summary>The Access SQL type name for a stored parameter's Jet type code (inverse of the CREATE TABLE
    /// type mapper), used to render a read-back PARAMETERS clause. Code 0 is not a Jet type at all: it is
    /// Access's untyped parameter, which it renders as the keyword <c>Value</c>.</summary>
    private static string AccessTypeName(byte code) => code == 0 ? "Value" : (JetDataType)code switch
    {
        JetDataType.Boolean => "YESNO",
        JetDataType.Byte => "BYTE",
        JetDataType.Int16 => "SHORT",
        JetDataType.Int32 => "LONG",
        JetDataType.Int64 => "BIGINT",
        JetDataType.Single => "SINGLE",
        JetDataType.Double => "DOUBLE",
        JetDataType.Currency => "CURRENCY",
        JetDataType.DateTime => "DATETIME",
        JetDataType.Text => "TEXT",
        JetDataType.Memo => "MEMO",
        JetDataType.Guid => "GUID",
        JetDataType.Binary => "BINARY",
        JetDataType.FixedPoint => "DECIMAL",
        JetDataType.Ole => "OLEOBJECT",
        _ => "TEXT",
    };

    /// <summary>Builds a <see cref="TableDefinition"/> straight from a TDEF page, without its catalog row — what every
    /// catalog lookup builds on, and what database creation and an index back-fill use to read a table they
    /// already know the page of.</summary>
    internal TableDefinition ReadTableDefinition(int definitionPage, string name, bool isSystem)
    {
        var definition = new TableDefinition { Name = name, IsSystem = isSystem };
        definition.Read(_channel, definitionPage);
        return definition;
    }


    /// <summary>Inserts the object's <c>MSysObjects</c> row — owner admin, both dates now — maintaining the
    /// table's indexes, including the (ParentId, Name) one Access resolves names through.</summary>
    /// <param name="name">The object's name, unique within its container.</param>
    /// <param name="objectId">A table's is its TDEF page; every other object takes a synthetic negative id.</param>
    /// <param name="type">A table, a query or a relationship.</param>
    /// <param name="parentId">The container the object belongs to.</param>
    /// <param name="flags">Object-class specific; a query's encodes its kind.</param>
    /// <param name="properties">The object's extended properties, if it has any: a table's per-column
    /// DefaultValue/Required and its CHECK constraints all live in this one blob.</param>
    internal void AddObjectRow(
        string name, int objectId, ObjectType type, int parentId, int flags,
        IReadOnlyList<PropertyBlob.Property>? properties = null) =>
        InsertObjectRow(_channel, RequireTable("MSysObjects"), name, objectId, type, parentId, flags,
            SecuritySids.Admin, properties is { Count: > 0 } ? PropertyBlob.Write(properties) : null);

    /// <summary>Inserts one <c>MSysObjects</c> row into <paramref name="msysObjects"/> — both dates now, and
    /// <paramref name="properties"/>, when given, stored as any long value is: inline up to 64 bytes, else packed
    /// onto a shared LvProp page as Access does — maintaining whatever indexes the table has.</summary>
    internal static void InsertObjectRow(PageChannel channel, TableDefinition msysObjects, string name, int objectId,
        ObjectType type, int parentId, int flags, byte[]? owner, byte[]? properties)
    {
        DateTime now = DateTime.Now;
        var values = new object?[msysObjects.Columns.Count];
        Set(msysObjects, values, "Id", objectId);
        Set(msysObjects, values, "ParentId", parentId);
        Set(msysObjects, values, "Type", (short)type);
        Set(msysObjects, values, "Name", name);
        Set(msysObjects, values, "Flags", flags);
        if (owner is not null) Set(msysObjects, values, "Owner", owner);
        Set(msysObjects, values, "DateCreate", now);
        Set(msysObjects, values, "DateUpdate", now);

        var inserter = new RowInserter(channel, msysObjects);
        if (properties is not null)
            Set(msysObjects, values, "LvProp", inserter.StorePackedLongValue(
                msysObjects.RequireColumn("LvProp").ColumnId, properties));

        inserter.Insert(values, updateIndexes: true);
    }

    /// <summary>Inserts one <c>MSysACEs</c> row into <paramref name="msysAces"/>: <paramref name="sid"/>'s
    /// <paramref name="acm"/> on the object, inheritable by what is created in it or not, maintaining whatever
    /// indexes the table has.</summary>
    internal static void InsertAceRow(PageChannel channel, TableDefinition msysAces, int objectId, byte[] sid, int acm,
        bool inheritable)
    {
        var values = new object?[msysAces.Columns.Count];
        Set(msysAces, values, "ACM", acm);
        Set(msysAces, values, "FInheritable", inheritable);
        Set(msysAces, values, "ObjectId", objectId);
        Set(msysAces, values, "SID", sid);
        new RowInserter(channel, msysAces).Insert(values, updateIndexes: true);
    }

    /// <summary>Inserts the object's <c>MSysACEs</c> rows, maintaining the ObjectId index so Access's security
    /// check finds them — without them it warns about permissions on opening the object.</summary>
    /// <remarks>The grants are the ones the object's container passes down, derived as ACE derives them
    /// (verified): the Creator account's inheritable grant becomes the owner's — the admin user, as whom LibRed
    /// creates every object — and every other inheritable grant is copied for its own account, OR'd into the
    /// owner's row when it names the owner's account. The owner's row comes first, the rest in the container's
    /// order. So the masks follow the database: a table's owner gets 0xF00FE where the Tables container grants the
    /// Creator that alone, and 0xFFEFF where it also grants admin 0xFFEFF, as Northwind's does.</remarks>
    internal void AddPermissionRows(int objectId, int containerId)
    {
        TableDefinition msysAces = RequireTable("MSysACEs");
        int idIndex = msysAces.RequireColumn("ObjectId").Index, sidIndex = msysAces.RequireColumn("SID").Index;
        int acmIndex = msysAces.RequireColumn("ACM").Index, inheritIndex = msysAces.RequireColumn("FInheritable").Index;
        (byte[] owner, _, byte[] creator) = SecuritySids;

        var grants = new List<(byte[] Sid, int Acm)>();
        var aces = new Table(_channel, msysAces, this);
        foreach (object?[] row in aces.Rows(aces.DecodeOnly([idIndex, sidIndex, acmIndex, inheritIndex])))
        {
            if (row[idIndex] is not int id || id != containerId || row[inheritIndex] is not true) continue;
            byte[] sid = (byte[])row[sidIndex]!;
            if (sid.AsSpan().SequenceEqual(creator)) sid = owner;
            int at = grants.FindIndex(g => g.Sid.AsSpan().SequenceEqual(sid));
            if (at < 0) grants.Add((sid, (int)row[acmIndex]!));
            else grants[at] = (grants[at].Sid, grants[at].Acm | (int)row[acmIndex]!);
        }

        foreach ((byte[] sid, int acm) in grants.OrderBy(g => g.Sid.AsSpan().SequenceEqual(owner) ? 0 : 1))
            InsertAceRow(_channel, msysAces, objectId, sid, acm, inheritable: false);
    }

    /// <summary>Puts <paramref name="value"/> in the row slot <paramref name="column"/> occupies.</summary>
    internal static void Set(TableDefinition table, object?[] values, string column, object? value) =>
        values[table.RequireColumn(column).Index] = value;



    internal void CreateView(string name, ViewSpec spec)
    {
        int objectId = AllocateQueryObject(name, QueryDefType.Select);
        AddQueryRows(objectId, spec);
    }

    /// <summary>Persists a stored action query (a non-SELECT CREATE PROCEDURE body) byte-faithfully.</summary>
    internal void CreateActionQuery(string name, ActionQuerySpec spec)
    {
        QueryDefType type = spec.Kind switch
        {
            ActionQueryKind.DataDefinition => QueryDefType.DataDefinition,
            ActionQueryKind.Append => QueryDefType.Append,
            ActionQueryKind.Update => QueryDefType.Update,
            ActionQueryKind.Delete => QueryDefType.Delete,
            ActionQueryKind.MakeTable => QueryDefType.MakeTable,
            _ => throw new NotSupportedException($"Action query kind {spec.Kind} is not stored yet."),
        };
        int objectId = AllocateQueryObject(name, type);
        AddActionRows(objectId, spec);
    }

    /// <summary>
    /// Records a relationship as ACE does (verified): a type-8 <c>MSysObjects</c> object in the Relationships
    /// container, named after it, flags 0, with the next high-bit id — the sequence queries draw from, so the two
    /// interleave, and a dropped one's id is taken again — and its two <c>MSysACEs</c> rows. Refuses a name
    /// another relationship has, as ACE does; a table or query may share it.
    /// </summary>
    internal void CreateRelationshipObject(string name) =>
        AllocateObject(name, ObjectType.Relationship, JetCatalog.RelationshipContainerParentId, flags: 0);

    /// <summary>A stored query's object: its <c>MSysObjects.Flags</c> are <see cref="ObjectAttributes.QueryDef"/>
    /// plus its DAO <paramref name="type"/>.</summary>
    private int AllocateQueryObject(string name, QueryDefType type)
    {
        JetName.Validate(name, "query name");
        return AllocateObject(name, ObjectType.Query, JetCatalog.ObjectContainerParentId,
            (int)ObjectAttributes.QueryDef | (int)type);
    }

    /// <summary>Reserves the next free high-bit object id, checks the name is free, and writes
    /// the MSysObjects row and the MSysACEs rows its container grants it. For a query the <paramref name="flags"/>
    /// distinguish view / append / data-definition.</summary>
    private int AllocateObject(string name, ObjectType type, int parentId, int flags)
    {
        // A name must be free within the object's own container — the rule MSysObjects' unique (ParentId, Name)
        // index states, and the one ACE applies (measured): a query collides with a table, a query or a linked
        // table, all in the Tables container, and not with a form, report, macro, module, relationship, database
        // document or container; a relationship only with another relationship.
        if (FindObjectRow(parentId, name) is not null)
            throw parentId == JetCatalog.RelationshipContainerParentId
                ? SchemaEditor.RelationshipNameTaken(name)
                : new SchemaObjectExistsException($"Object '{name}' already exists.", name);

        // The next free negative id: one past the highest in use.
        TableDefinition msysObjects = RequireTable("MSysObjects");
        int idIndex = msysObjects.RequireColumn("Id").Index;
        int nextId = JetCatalog.FirstPagelessObjectId;
        var objects = new Table(_channel, msysObjects, this);
        foreach (object?[] row in objects.Rows(objects.DecodeOnly([idIndex])))
            if (row[idIndex] is int id && id < 0 && id >= nextId) nextId = id + 1;

        AddObjectRow(name, nextId, type, parentId, flags);
        AddPermissionRows(nextId, parentId);
        return nextId;
    }

    private void AddActionRows(int objectId, ActionQuerySpec spec)
    {
        TableDefinition mq = RequireTable("MSysQueries");

        AddHeaderRows(mq, objectId, spec.Parameters);

        if (spec.Kind == ActionQueryKind.DataDefinition)
        {
            // The whole DDL statement is stored verbatim in one row; Access records it with a leading space.
            // Nothing is decomposed: a data-definition query has no sources, columns or predicate.
            Row(mq, objectId, QueryAttribute.Operation, order: 1, flag: (short)QueryOperation.Ddl,
                expression: " " + spec.DdlSql);
            return;
        }

        // The action row carries the kind, and the target table for the two kinds that write into one.
        QueryOperation kind = spec.Kind switch
        {
            ActionQueryKind.Append => QueryOperation.Append,
            ActionQueryKind.Update => QueryOperation.Update,
            ActionQueryKind.Delete => QueryOperation.Delete,
            ActionQueryKind.MakeTable => QueryOperation.MakeTable,
            _ => throw new NotSupportedException($"Action query kind {spec.Kind} is not stored yet."),
        };
        Row(mq, objectId, QueryAttribute.Operation, order: 1, flag: (short)kind,
            name1: spec.Kind is ActionQueryKind.Append or ActionQueryKind.MakeTable ? spec.TargetTable : null);

        // Sources first: Access processes the rows in order, and a derived-table source defines an alias the
        // column expressions reference (the same reason the view path writes tables before columns).
        AddSourceRows(mq, objectId, spec.Body);

        var values = spec.Values ?? [];
        switch (spec.Kind)
        {
            case ActionQueryKind.Append:
                // Name2 = target column, Expression = the value; the 0x8000 flag marks an INSERT … VALUES,
                // where a column fed by the query's own source carries Flag 0.
                for (int i = 0; i < values.Count; i++)
                    Row(mq, objectId, QueryAttribute.Column, order: i + 1,
                        flag: spec.Body is null ? StoredQuery.AppendValueFlag : (short)0,
                        expression: values[i].ValueExpression, name2: values[i].Column);
                break;

            case ActionQueryKind.Update:
                // One row per SET assignment, stored exactly as an append's columns are.
                for (int i = 0; i < values.Count; i++)
                    Row(mq, objectId, QueryAttribute.Column, order: i + 1, flag: 0,
                        expression: values[i].ValueExpression, name2: values[i].Column);
                break;

            case ActionQueryKind.Delete:
                // `DELETE t.* FROM …` keeps that target verbatim in a single column row; `DELETE FROM …`
                // stores no column row at all, and Access renders it back as `DELETE * FROM …`.
                if (spec.DeleteTarget is { } target)
                    Row(mq, objectId, QueryAttribute.Column, order: 1, flag: 0, expression: target);
                break;

            case ActionQueryKind.MakeTable:
                AddColumnRows(mq, objectId, spec.Body?.Columns ?? []);
                break;
        }

        AddJoinAndWhereRows(mq, objectId, spec.Body);
        // The option row a view carries: a make-table or append query's DISTINCT and TOP, and any kind's WITH
        // OWNERACCESS OPTION.
        AddOptionRow(mq, objectId, spec.Body, spec.OwnerAccess);
    }

    /// <summary>
    /// The query's one <c>0x03</c> option row, when it has an option: DISTINCT, WITH OWNERACCESS OPTION, TOP and PERCENT
    /// are bits of the same row, the TOP count in its Name1 (verified vs ACE: DISTINCT TOP is 18, with the option 22,
    /// and with PERCENT too 54; the option alone is 4; a make-table query's DISTINCT is the same row as a view's).
    /// </summary>
    private void AddOptionRow(TableDefinition mq, int objectId, ViewSpec? body, bool ownerAccess)
    {
        QueryOptions options = (body?.Distinct == true ? QueryOptions.Distinct : QueryOptions.None)
            | (ownerAccess ? QueryOptions.OwnerAccess : QueryOptions.None)
            | (body?.Top is null ? QueryOptions.None
                : QueryOptions.Top | (body.TopPercent ? QueryOptions.Percent : QueryOptions.None));
        if (options != QueryOptions.None)
            Row(mq, objectId, QueryAttribute.Option, order: 1, flag: (short)options,
                name1: body?.Top?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The rows every stored query opens with — the type row, the end row, then its declared parameters
    /// in declaration order — written identically for a view and for an action query.</summary>
    private void AddHeaderRows(TableDefinition mq, int objectId, IReadOnlyList<ViewParameterSpec>? parameters)
    {
        Row(mq, objectId, QueryAttribute.Type, order: 1, flag: StoredQuery.QueryTypeSelect);
        Row(mq, objectId, QueryAttribute.End, order: 1);
        for (int i = 0; i < (parameters?.Count ?? 0); i++)
        {
            ViewParameterSpec p = parameters![i];
            Row(mq, objectId, QueryAttribute.Parameter, order: i + 1,
                flag: p.TypeCode, name1: p.Name,
                lvExtra: StoredQuery.PackParameterFacets((JetDataType)p.TypeCode, p.Size, p.Scale));
        }
    }

    /// <summary>An ordinary output list, a view's or a make-table query's: Expression = the column, Name1 = its
    /// alias.</summary>
    private void AddColumnRows(TableDefinition mq, int objectId, IReadOnlyList<ViewColumnSpec> columns)
    {
        for (int i = 0; i < columns.Count; i++)
            Row(mq, objectId, QueryAttribute.Column, order: i + 1, flag: 0,
                expression: columns[i].Expression, name1: columns[i].Alias);
    }

    /// <summary>The <c>0x05</c> FROM rows: a named table in Name1 (alias in Name2), or a derived table whose
    /// subquery SQL goes in Expression with Name1 empty.</summary>
    private void AddSourceRows(TableDefinition mq, int objectId, ViewSpec? body)
    {
        var tables = body?.Tables ?? [];
        for (int i = 0; i < tables.Count; i++)
        {
            ViewTableSpec t = tables[i];
            if (t.SubquerySql is { } sub)
                Row(mq, objectId, QueryAttribute.Table, order: i + 1, expression: sub, name2: t.Alias);
            else
                Row(mq, objectId, QueryAttribute.Table, order: i + 1, name1: t.Table, name2: t.Alias);
        }
    }

    /// <summary>The <c>0x07</c> join rows (condition, kind, and the two tables the condition names) and the
    /// single <c>0x08</c> WHERE row.</summary>
    private void AddJoinAndWhereRows(TableDefinition mq, int objectId, ViewSpec? body)
    {
        var joins = body?.Joins ?? [];
        for (int i = 0; i < joins.Count; i++)
        {
            ViewJoinSpec j = joins[i];
            Row(mq, objectId, QueryAttribute.Join, order: i + 1, flag: (short)j.Kind,
                expression: j.Condition, name1: j.LeftAlias, name2: j.RightAlias);
        }
        if (body?.Where is { } where)
            Row(mq, objectId, QueryAttribute.Where, order: 1, expression: where);
    }

    private void AddQueryRows(int objectId, ViewSpec spec)
    {
        TableDefinition mq = RequireTable("MSysQueries");

        // ACE's row order (verified against every Northwind view): type, end, distinct, TABLES, COLUMNS,
        // joins, where. Tables must precede columns — a derived-table source defines an alias that the
        // column expressions reference, and Access processes the rows in order, so columns-before-tables
        // makes it fail to run the view (it opens, but SELECT-from-view errors). Order fields are
        // per-attribute counters, independent of this insertion order.
        // Declared parameters (CREATE PROCEDURE) come right after the End row, before the tables.
        AddHeaderRows(mq, objectId, spec.Parameters);
        AddOptionRow(mq, objectId, spec, spec.OwnerAccess);
        AddSourceRows(mq, objectId, spec);
        AddColumnRows(mq, objectId, spec.Columns);
        AddJoinAndWhereRows(mq, objectId, spec);
        for (int i = 0; i < (spec.GroupBy?.Count ?? 0); i++)
            Row(mq, objectId, QueryAttribute.GroupBy, order: i + 1, flag: 0, expression: spec.GroupBy![i]);
        // The group filter carries no flag of its own, exactly as the WHERE row doesn't.
        if (spec.Having is { } having)
            Row(mq, objectId, QueryAttribute.Having, order: 1, expression: having);
        for (int i = 0; i < (spec.OrderBy?.Count ?? 0); i++)
            Row(mq, objectId, QueryAttribute.OrderBy, order: i + 1, expression: spec.OrderBy![i].Expression,
                name1: spec.OrderBy[i].Descending ? "d" : null);
    }

    private void Row(TableDefinition mq, int objectId, QueryAttribute attribute, int order,
        short? flag = null, string? expression = null, string? name1 = null, string? name2 = null,
        int? lvExtra = null)
    {
        var values = new object?[mq.Columns.Count];
        Set(mq, values, "ObjectId", objectId);
        Set(mq, values, "Attribute", (byte)attribute);
        Set(mq, values, "Order", StoredQuery.PackOrder(order));
        if (flag is { } f) Set(mq, values, "Flag", f);
        if (expression is not null) Set(mq, values, "Expression", expression);
        if (name1 is not null) Set(mq, values, "Name1", name1);
        if (name2 is not null) Set(mq, values, "Name2", name2);
        // A declared parameter's length; ACE writes it here and renders the PARAMETERS clause from it.
        if (lvExtra is { } extra) Set(mq, values, "LvExtra", extra);
        new RowInserter(_channel, mq).Insert(values, updateIndexes: true);
    }

}