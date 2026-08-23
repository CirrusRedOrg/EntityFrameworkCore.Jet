using System.Buffers.Binary;
using System.Text;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Builds the pages of a brand-new, empty Jet/ACE database from scratch — the native, cross-platform
/// replacement for the DAO/ADOX file creator. Currently synthesises page 0 (the database definition
/// page); the system catalog follows.
/// </summary>
public static class DatabaseCreator
{
    private static readonly DateTime OleEpoch = new(1899, 12, 30);

    /// <summary>
    /// Synthesises page 0 (the database definition page) — the exact inverse of
    /// <see cref="Pages.DatabaseDefinitionPage.Read"/>. Byte-for-byte identical to a real empty file's
    /// page 0 for the same parameters (verified against Access-created files).
    /// </summary>
    /// <param name="version">Format version byte (e.g. 0x02 = ACE 12 / Access 2007).</param>
    /// <param name="isAccdb">true for the ACCDB identifier, false for the MDB (Jet) identifier.</param>
    /// <param name="codePage">ANSI code page (1252 for en-US).</param>
    /// <param name="collationLcid">Default collation LCID (1033 = en-US).</param>
    /// <param name="collationVersion">Sort-order version (0 = General Legacy, 1 = General).</param>
    /// <param name="creationDays">Creation timestamp as an OLE-automation date (days since 1899-12-30), passed
    /// as the raw double so the exact millisecond-precise bit pattern is preserved — the page-0 SID mask is bound
    /// to those exact bits (see <see cref="SeedCreationDateBits"/>).</param>
    public static byte[] BuildDefinitionPage(
        byte version, bool isAccdb, int codePage, int collationLcid, byte collationVersion, double creationDays)
    {
        var page = new byte[4096];

        // --- Pre-mask region (0x00..0x17, cleartext) ---
        page[0x00] = 0x00;              // page type
        page[0x01] = 0x01;              // observed constant 01 00 00
        string id = isAccdb ? JetFormatBase.AceIdentifier : JetFormatBase.JetIdentifier;
        Encoding.ASCII.GetBytes(id).CopyTo(page, JetFormatBase.FormatIdentifierOffset); // 0x04, 15 bytes; 0x13 stays NUL
        page[JetFormatBase.VersionOffset] = version;                                     // 0x14
        page[0x15] = (byte)(version == 0x03 ? 0x01 : 0x00);                              // 2010-format minor byte

        // --- Masked header (0x18..0x97): build the clear image, then XOR the fixed mask over it. ---
        int b = JetFormatBase.PageZeroHeaderMaskStart;
        Span<byte> clear = stackalloc byte[JetFormatBase.PageZeroHeaderMask.Length];

        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x18 - b)..], 0x00000100);        // 0x18 fixed constant
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x1C - b)..], 0x00000101);        // 0x1C fixed constant
        // 0x20..0x2C: system-catalog bootstrap pointers = MSysObjects/ACEs/Queries/Relationships pages.
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x20 - b)..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x24 - b)..], 3);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x28 - b)..], 4);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x2C - b)..], 5);
        BinaryPrimitives.WriteUInt16LittleEndian(clear[(JetFormatBase.CodePageOffset - b)..], (ushort)codePage); // 0x3C
        // 0x3E database key = 0 (unencrypted); leave clear zero.
        // 0x42..0x69 password (empty): on disk the field is additionally masked by a 4-byte value derived
        // from the creation date, so the clear image (pre-header-mask) of an empty password is that value
        // repeated — reproduce it exactly.
        double days = creationDays;
        Span<byte> dateMask = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(dateMask, (int)days);
        for (int i = 0; i < 40; i++)
            clear[JetFormatBase.PasswordOffset - b + i] = dateMask[i % 4];
        // 0x6A fixed sentinel constant.
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x6A - b)..], 0x000011A6);
        // 0x6E..0x71 collating sort order: LCID + version byte at 0x71.
        BinaryPrimitives.WriteUInt16LittleEndian(clear[(JetFormatBase.CollationSortOrderOffset - b)..], (ushort)collationLcid);
        clear[JetFormatBase.CollationVersionOffset - b] = collationVersion;
        // 0x72..0x79 creation date (OLE double).
        BinaryPrimitives.WriteDoubleLittleEndian(clear[(JetFormatBase.CreationDateOffset - b)..], days);

        ReadOnlySpan<byte> mask = JetFormatBase.PageZeroHeaderMask;
        for (int i = 0; i < mask.Length; i++)
            page[b + i] = (byte)(clear[i] ^ mask[i]);

        // --- Post-mask tail (cleartext) ---
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(0x98), 0x00000654);           // fixed constant
        Encoding.ASCII.GetBytes("4.0").CopyTo(page, 0x9C);                                 // engine version string

        // User commit-byte table (0xE00–0xFFF): 256 users × 2 bytes at the end of the header page — Jet 3.x's
        // 0x600 commit region relocated to the end of the 4 KB page (see the Jet locking white paper). Each
        // pair is a per-user commit/lock status. A fresh file must seed every slot to the neutral idle value
        // 00 01 — NOT 00 00, which Jet reads as "mid-write to disk"; with no matching .ldb user lock that
        // reads as a suspect/corrupt database and forces a repair before Access will open it.
        for (int i = 0xE00; i < 0x1000; i++) page[i] = (byte)(i & 1);

        return page;
    }

    // System-column flag bits: Sys = 0x10 (system-catalog column), SysSid = 0x10|0x20 (also a security id).
    private const byte Sys = 0x10, SysSid = 0x30;

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
    /// <c>Value</c> column of its element type; Attachment is the exception, carrying the file metadata.</summary>
    private static readonly (string Name, ColumnSpec[] Columns)[] MSysComplexTypeTables =
    [
        ("MSysComplexType_UnsignedByte", [new("Value", JetDataType.Byte, 1, true, ColumnId: 0, SystemFlags: Sys)]),
        ("MSysComplexType_Short", [new("Value", JetDataType.Int16, 2, true, ColumnId: 0, SystemFlags: Sys)]),
        ("MSysComplexType_Long", [new("Value", JetDataType.Int32, 4, true, ColumnId: 0, SystemFlags: Sys)]),
        ("MSysComplexType_IEEESingle", [new("Value", JetDataType.Single, 4, true, ColumnId: 0, SystemFlags: Sys)]),
        ("MSysComplexType_IEEEDouble", [new("Value", JetDataType.Double, 8, true, ColumnId: 0, SystemFlags: Sys)]),
        ("MSysComplexType_GUID", [new("Value", JetDataType.Guid, 16, true, ColumnId: 0, SystemFlags: Sys)]),
        ("MSysComplexType_Decimal", [new("Value", JetDataType.FixedPoint, 9, false, ColumnId: 0, SystemFlags: Sys)]),
        ("MSysComplexType_Text", [new("Value", JetDataType.Text, 510, false, ColumnId: 0, SystemFlags: Sys)]),
        ("MSysComplexType_Attachment",
        [
            new("FileData", JetDataType.Ole, 0, false, ColumnId: 3, SystemFlags: Sys),
            new("FileFlags", JetDataType.Int32, 4, true, ColumnId: 5, SystemFlags: Sys),
            new("FileName", JetDataType.Text, 510, false, ColumnId: 1, SystemFlags: Sys),
            new("FileTimeStamp", JetDataType.DateTime, 8, true, ColumnId: 4, SystemFlags: Sys),
            new("FileType", JetDataType.Text, 510, false, ColumnId: 2, SystemFlags: Sys),
            new("FileURL", JetDataType.Memo, 0, false, ColumnId: 0, SystemFlags: Sys),
        ]),
    ];

    /// <summary>MSysObjects.Flags for the complex tables, as the real engine writes them: the registry carries
    /// the plain system flag, the flat storage tables an extra <c>0x00030000</c>.</summary>
    private const int ComplexStorageFlags = unchecked((int)0x80030000);

    private const int SystemFlag = unchecked((int)0x80000000);

    // Per-file SID cluster. A database's on-disk 2-byte SIDs are the DEFAULT WORKGROUP's account SIDs XOR'd with
    // a per-file 2-byte mask. The account SIDs were read verbatim from a real System.mdw (the file Access opens
    // first to authenticate): admin(user)=03-01, Users(group)=02-01, Engine=02-03, Creator=02-04; the Admins
    // group alone has a long per-workgroup SID (which Access materialises as a 98-byte SID on first open, so we
    // don't emit it). Object ownership uses the "user" form (byte0 0x03) of Engine/Creator, matching real DAO
    // files. Verified against WideTable with mask 24-CC: Users 02-01^24-CC = 26-CD, admin 03-01^24-CC = 27-CD,
    // Engine-as-user 03-03^24-CC = 27-CF (system-object owner), Creator-as-user 03-04^24-CC = 27-C8.
    //
    // The mask is bound to the millisecond-precise creation date: Access derives both from a shared PRNG state
    // at create time, so there is NO closed-form date->mask function (144 files, no checksum/PRNG fit) and the
    // two MUST travel together. We bake one verified, self-consistent (creation-date, mask) pair — the from-
    // scratch analogue of the account-SID constants — giving an Access-openable file with no template/graft.
    // TODO: per-file-random dates (and custom/secured workgroups) need the date<->mask coupling cracked.
    internal const long SeedCreationDateBits = 0x40E68F1E8943D217L; // 2026-06-27 22:54:07.716 (WideTable) — pairs with SidMask
    private static readonly byte[] SidMask = [0x24, 0xCC];           // WideTable's per-file mask (pairs with SeedCreationDateBits)
    private static byte[] Masked(byte b0, byte b1) => [(byte)(b0 ^ SidMask[0]), (byte)(b1 ^ SidMask[1])];
    internal static readonly byte[] SidUsers = Masked(0x02, 0x01);   // Users group — read grantee / owner of user tables
    internal static readonly byte[] SidAdmin = Masked(0x03, 0x01);   // admin user  — full grantee
    private static readonly byte[] SidEngine = Masked(0x03, 0x03);   // Engine (user form) — owner of system tables + DAO containers
    private static readonly byte[] SidCreator = Masked(0x03, 0x04);  // Creator (user form) — inheritable container grant

    /// <summary>
    /// Creates a new, empty database at <paramref name="path"/> from scratch — no DAO/ADOX. Hand-builds the
    /// bootstrap (page 0, the page-1 free map, and the <c>MSysObjects</c>/<c>MSysACEs</c> TDEFs with their
    /// usage maps + self-registering catalog rows), then the file is a normal LibRed database: further tables
    /// are added through the ordinary writers. Produces a LibRed-openable, round-trippable file (Access-level
    /// fidelity — the remaining system tables and the 0xE00 map — is a follow-up).
    /// </summary>
    /// <param name="collation">The database's default text collating order, written to page 0 and inherited
    /// by every column created in it. Defaults to General-Legacy (LCID 1033, version 0), which is what the
    /// engine writes; pass <see cref="Collation.General"/> for the order Access 2010+ offers as "General".
    /// Only the two General orders can have their index keys encoded — see <c>IndexKeyEncoder</c>.</param>
    public static void CreateEmpty(string path, byte version = 0x02, Collation? collation = null)
    {
        Collation sortOrder = collation ?? Collation.GeneralLegacy;
        JetFormatBase format = JetFormatBase.FromVersionByte(version);
        // The four core system tables live at the exact pages the page-0 bootstrap pointers name (2/3/4/5);
        // their usage maps follow at 6..9. Access uses those pointers to find the catalog.
        const int objPage = 2, acesPage = 3, queriesPage = 4, relPage = 5;

        // The system tables take the database collation too: Access writes v1 descriptors on MSys* in a
        // General (v1) database, so anything else would be a mixed-collation file it never produces.
        var (objTdef, objMap) = BuildSystemTable(format, MSysObjectsColumns, usageMapPage: 6, sortOrder);
        var (acesTdef, acesMap) = BuildSystemTable(format, MSysAcesColumns, usageMapPage: 7, sortOrder);
        var (queriesTdef, queriesMap) = BuildSystemTable(format, MSysQueriesColumns, usageMapPage: 8, sortOrder);
        var (relTdef, relMap) = BuildSystemTable(format, MSysRelationshipsColumns, usageMapPage: 9, sortOrder);
        const int seedPages = 10;   // page 0, page 1, 4 core TDEFs (2..5), 4 usage maps (6..9)
        byte[][] seed =
        [
            BuildDefinitionPage(version, isAccdb: true, 1252, (int)sortOrder.Order, sortOrder.Version,
                BitConverter.Int64BitsToDouble(SeedCreationDateBits)),
            BuildFreeMapPage(format, seedPages),       // page 1: global free-pages map
            objTdef, acesTdef, queriesTdef, relTdef,   // pages 2..5: core TDEFs
            objMap, acesMap, queriesMap, relMap,       // pages 6..9: their usage maps
        ];
        // Creation is an explicit create-new operation. FileMode.Create would silently truncate an existing
        // database before any of the format bootstrap work could validate or fail.
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            foreach (byte[] p in seed) fs.Write(p, 0, format.PageSize);

        // Reopen through the normal stack and self-register the four core tables, so the catalog then finds
        // them and every other table can go through TableCreator.
        using var db = JetDatabase.Open(path, readOnly: false);
        // The DAO catalog hierarchy Access navigates: a root (0x0F000000) parents the three containers
        // (Tables/Databases/Relationships); tables live under Tables, MSysDb under Databases. Access looks
        // objects up by (ParentId, Name), so these parents must be exact.
        const int root = 0x0F000000, tablesC = 0x0F000001, databasesC = 0x0F000002, relationshipsC = 0x0F000003;

        Table msysObjects = db.OpenTableAt(objPage, "MSysObjects");
        Table msysAces = db.OpenTableAt(acesPage, "MSysACEs");
        void Ace(int objectId, byte[] sid, int acm, bool inherit = false)
        {
            var v = new object?[msysAces.Definition.Columns.Count];
            v[msysAces.Definition.FindColumn("ObjectId")!.Index] = objectId;
            v[msysAces.Definition.FindColumn("SID")!.Index] = sid;
            v[msysAces.Definition.FindColumn("ACM")!.Index] = acm;
            v[msysAces.Definition.FindColumn("FInheritable")!.Index] = inherit;
            msysAces.Insert(v);
        }

        // Catalog rows in the real stored order: DAO containers + MSysDb first, then the system tables, then
        // the DAO "SingleRecord" pseudo-object. Access's catalog bootstrap walks MSysObjects in this order.
        InsertCatalogRow(msysObjects, tablesC, "Tables", type: 3, SystemFlag, parentId: root, owner: SidEngine);
        InsertCatalogRow(msysObjects, databasesC, "Databases", type: 3, SystemFlag, parentId: root, owner: SidEngine);
        InsertCatalogRow(msysObjects, relationshipsC, "Relationships", type: 3, SystemFlag, parentId: root, owner: SidEngine);
        InsertCatalogRow(msysObjects, 0x10000000, "MSysDb", type: 2, SystemFlag, parentId: databasesC, owner: SidUsers);
        InsertCatalogRow(msysObjects, objPage, "MSysObjects", type: 1, SystemFlag, parentId: tablesC, owner: SidEngine);
        InsertCatalogRow(msysObjects, acesPage, "MSysACEs", type: 1, SystemFlag, parentId: tablesC, owner: SidEngine);
        InsertCatalogRow(msysObjects, queriesPage, "MSysQueries", type: 1, SystemFlag, parentId: tablesC, owner: SidEngine);
        InsertCatalogRow(msysObjects, relPage, "MSysRelationships", type: 1, SystemFlag, parentId: tablesC, owner: SidEngine);
        InsertCatalogRow(msysObjects, unchecked((int)0x80000000), "SingleRecord", type: 9, flags: 0x10000000, parentId: relationshipsC);

        // Access-control rows (verified per-object-class masks) in the same object order as the catalog rows.
        Ace(tablesC, SidCreator, 0x0F00FE, inherit: true); Ace(tablesC, SidUsers, 0x060001); Ace(tablesC, SidAdmin, 0x0FFEFF, inherit: true);
        Ace(databasesC, SidUsers, 0x060000);
        Ace(relationshipsC, SidCreator, 0x0F00FE, inherit: true); Ace(relationshipsC, SidUsers, 0x060001); Ace(relationshipsC, SidAdmin, 0x0FFFFF, inherit: true);
        Ace(0x10000000, SidUsers, 0x06000E);    Ace(0x10000000, SidAdmin, 0x00000E);   // MSysDb
        Ace(objPage, SidUsers, 0x060000);       Ace(objPage, SidAdmin, 0x000014);   // MSysObjects
        Ace(acesPage, SidUsers, 0x060000);                                          // MSysACEs (Users only)
        Ace(queriesPage, SidUsers, 0x060000);   Ace(queriesPage, SidAdmin, 0x000014); // MSysQueries
        Ace(relPage, SidUsers, 0x0E0000);       Ace(relPage, SidAdmin, 0x000014);   // MSysRelationships

        // The system tables carry the indexes Access uses to navigate the catalog. ParentIdName is the first
        // real index, Id (the PK) second — matching real files.
        db.CreateIndex("MSysObjects", "ParentIdName", [("ParentId", false), ("Name", false)], isUnique: true);
        db.CreateIndex("MSysObjects", "Id", [("Id", false)], isUnique: true, isPrimary: true);
        db.CreateIndex("MSysACEs", "ObjectId", [("ObjectId", false)], disallowNull: true);
        db.CreateIndex("MSysQueries", "ObjectIdAttribute", [("ObjectId", false), ("Attribute", false), ("Order", false)], isUnique: true, isPrimary: true);
        db.CreateIndex("MSysRelationships", "szRelationship", [("szRelationship", false)]);
        db.CreateIndex("MSysRelationships", "szObject", [("szObject", false)]);
        db.CreateIndex("MSysRelationships", "szReferencedObject", [("szReferencedObject", false)]);

        // Complex-column system tables — ACE 12 and later only (see CreateComplexSystemTables).
        if (version >= 0x02) CreateComplexSystemTables(db);

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
    private static void CreateComplexSystemTables(JetDatabase db)
    {
        db.CreateTable("MSysComplexColumns", MSysComplexColumnsColumns);
        // Index names, order and flags as the engine writes them: the ComplexID primary key first, then the
        // two non-unique lookups the engine uses to find a table's complex columns.
        db.CreateIndex("MSysComplexColumns", "IdxID", [("ComplexID", false)],
            isUnique: true, isPrimary: true, disallowNull: true, ignoreNulls: true);
        db.CreateIndex("MSysComplexColumns", "IdxConceptualTableID", [("ConceptualTableID", false)],
            disallowNull: true, ignoreNulls: true);
        db.CreateIndex("MSysComplexColumns", "IdxFlatTableID", [("FlatTableID", false)],
            disallowNull: true, ignoreNulls: true);
        MarkAsSystemTable(db, "MSysComplexColumns", SystemFlag);

        foreach ((string name, ColumnSpec[] columns) in MSysComplexTypeTables)
        {
            db.CreateTable(name, columns);
            MarkAsSystemTable(db, name, ComplexStorageFlags);
        }
    }

    /// <summary>Turns a table the ordinary writers just created into a system object: the MSysObjects row gets
    /// the engine's flags and owner, and the TDEF's table-type byte becomes 'S'. Creating it as a user table
    /// first and correcting it reuses all the allocation, usage-map and index machinery.</summary>
    private static void MarkAsSystemTable(JetDatabase db, string name, int flags)
    {
        TableDef definition = db.Catalog.FindTable(name)
            ?? throw new InvalidOperationException($"'{name}' was not found after creating it.");

        Table msysObjects = db.OpenTable("MSysObjects");
        TableDef objectsDef = msysObjects.Definition;
        int idIndex = objectsDef.FindColumn("Id")!.Index;
        int flagsIndex = objectsDef.FindColumn("Flags")!.Index;
        int ownerIndex = objectsDef.FindColumn("Owner")!.Index;

        foreach ((RowId rowId, object?[] values) in msysObjects.Rows().WithIds())
        {
            if (values[idIndex] is not { } id || Convert.ToInt32(id) != definition.DefinitionPage) continue;
            values[flagsIndex] = flags;
            values[ownerIndex] = SidEngine;
            msysObjects.Update(rowId, values, new HashSet<int> { flagsIndex, ownerIndex });
            break;
        }

        // TDEF table type: 'N' user -> 'S' system.
        IO.PageChannel channel = msysObjects.Channel;
        byte[] tdef = channel.ReadPageShared(definition.DefinitionPage).Span.ToArray();
        tdef[channel.Format.TdefTableTypeOffset] = (byte)TableType.System;
        channel.WritePage(definition.DefinitionPage, tdef);
        db.Catalog.Invalidate();
    }

    private static void InsertCatalogRow(Table msysObjects, int id, string name, short type, int flags, int parentId = 0, byte[]? owner = null)
    {
        var values = new object?[msysObjects.Definition.Columns.Count];
        void Set(string col, object? v) => values[msysObjects.Definition.FindColumn(col)!.Index] = v;
        Set("Id", id);
        Set("ParentId", parentId);
        Set("Name", name);
        Set("Type", type);          // 1 = table, 2 = database (MSysDb), 3 = DAO container, 9 = SingleRecord
        Set("Flags", flags);
        if (owner != null) Set("Owner", owner);
        Set("DateCreate", DateTime.Now);
        Set("DateUpdate", DateTime.Now);
        msysObjects.Insert(values);
    }

    /// <summary>Builds the page-1 global free-pages map: a data page with two 69-byte inline maps. Row 0 is
    /// the free map — pages &lt; <paramref name="usedPages"/> are used (bit 0); pages from there to the map's
    /// 512-page reach are marked <b>free</b> (bit 1), pre-declaring space beyond the file end so an allocator
    /// (LibRed's or Access's) grabs a "free" page and grows the file. Row 1 is an all-zeros companion, as real
    /// files carry.</summary>
    private static byte[] BuildFreeMapPage(JetFormatBase format, int usedPages)
    {
        const int mapLen = 1 + 4 + 64;   // inline map: type + start page + 64-byte bitmap (512 pages)
        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        page[1] = 0x01;
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.DataOwnerOffset, 4), 1);  // global-map owner (observed)

        int row0 = format.PageSize - mapLen;        // free map (highest offset — the one the allocator reads)
        int row1 = row0 - mapLen;                    // all-zeros companion
        for (int p = usedPages; p < 64 * 8; p++)     // mark pages >= usedPages free
            page[row0 + 5 + (p >> 3)] |= (byte)(1 << (p & 7));

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset, 2), (ushort)row0);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + 2, 2), (ushort)row1);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2), (ushort)(row1 - format.DataRowDirectoryOffset - 4));
        return page;
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

        byte[] tdef = TdefBuilder.Build(format, TableType.System, columns, longValueColumns: longValueSpecs,
            collation: collation).Page;
        tdef[format.TdefOwnedPagesOffset] = 0; WriteInt24(tdef, format.TdefOwnedPagesOffset + 1, usageMapPage);
        tdef[format.TdefFreePagesOffset] = 1; WriteInt24(tdef, format.TdefFreePagesOffset + 1, usageMapPage);
        var tdefPage = new byte[format.PageSize];
        Array.Copy(tdef, tdefPage, format.PageSize);   // these system TDEFs fit one page

        byte[] usageMap = BuildUsageMapPage(format, 2 + longValueCols.Count * 2);
        return (tdefPage, usageMap);
    }

    private static byte[] BuildUsageMapPage(JetFormatBase format, int mapCount)
    {
        const int mapLen = 1 + 4 + 64;   // inline map record: type + start page + 64-byte bitmap
        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        page[1] = 0x01;
        int offset = format.PageSize;
        for (int row = 0; row < mapCount; row++)
        {
            offset -= mapLen;
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + row * 2, 2), (ushort)offset);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)mapCount);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(offset - format.DataRowDirectoryOffset - mapCount * 2));
        return page;
    }

    private static void WriteInt24(byte[] b, int o, int v)
    {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16);
    }
}
