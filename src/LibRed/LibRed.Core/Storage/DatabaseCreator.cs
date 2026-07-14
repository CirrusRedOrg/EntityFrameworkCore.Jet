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
    /// <param name="creationDate">Database creation timestamp.</param>
    public static byte[] BuildDefinitionPage(
        byte version, bool isAccdb, int codePage, int collationLcid, byte collationVersion, DateTime creationDate)
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
        double days = (creationDate - OleEpoch).TotalDays;
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

    // MSysObjects — the system catalog. 17 columns (see the format docs); LibRed reads it by scan.
    private static readonly ColumnSpec[] MSysObjectsColumns =
    [
        new("Id", JetDataType.Int32, 4, true),
        new("ParentId", JetDataType.Int32, 4, true),
        new("Name", JetDataType.Text, 510, false),
        new("Type", JetDataType.Int16, 2, true),
        new("Flags", JetDataType.Int32, 4, true),
        new("Owner", JetDataType.Binary, 510, false),
        new("Connect", JetDataType.Memo, 0, false),
        new("Database", JetDataType.Memo, 0, false),
        new("ForeignName", JetDataType.Text, 510, false),
        new("DateCreate", JetDataType.DateTime, 8, true),
        new("DateUpdate", JetDataType.DateTime, 8, true),
        new("Lv", JetDataType.Ole, 0, false),
        new("LvExtra", JetDataType.Ole, 0, false),
        new("LvModule", JetDataType.Ole, 0, false),
        new("LvProp", JetDataType.Ole, 0, false),
        new("RmtInfoLong", JetDataType.Ole, 0, false),
        new("RmtInfoShort", JetDataType.Binary, 510, false),
    ];

    // MSysACEs — per-object access-control rows. TableCreator inserts into it when a table is created.
    private static readonly ColumnSpec[] MSysAcesColumns =
    [
        new("ACM", JetDataType.Int32, 4, true),
        new("FInheritable", JetDataType.Boolean, 1, true),
        new("ObjectId", JetDataType.Int32, 4, true),
        new("SID", JetDataType.Binary, 510, false),
    ];

    private const int SystemFlag = unchecked((int)0x80000000);

    /// <summary>
    /// Creates a new, empty database at <paramref name="path"/> from scratch — no DAO/ADOX. Hand-builds the
    /// bootstrap (page 0, the page-1 free map, and the <c>MSysObjects</c>/<c>MSysACEs</c> TDEFs with their
    /// usage maps + self-registering catalog rows), then the file is a normal LibRed database: further tables
    /// are added through the ordinary writers. Produces a LibRed-openable, round-trippable file (Access-level
    /// fidelity — the remaining system tables and the 0xE00 map — is a follow-up).
    /// </summary>
    public static void CreateEmpty(string path, byte version = 0x02)
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(version);
        const int msysObjPage = 2, msysObjMapPage = 3, msysAcesPage = 4, msysAcesMapPage = 5;

        var (msysObjTdef, msysObjMap) = BuildSystemTable(format, MSysObjectsColumns, msysObjMapPage);
        var (msysAcesTdef, msysAcesMap) = BuildSystemTable(format, MSysAcesColumns, msysAcesMapPage);
        byte[][] seed =
        [
            BuildDefinitionPage(version, isAccdb: true, 1252, 1033, 0, DateTime.Now),
            BuildEmptyDataPage(format),   // page 1: free map (empty)
            msysObjTdef,                  // page 2
            msysObjMap,                   // page 3
            msysAcesTdef,                 // page 4
            msysAcesMap,                  // page 5
        ];
        using (var fs = File.Create(path))
            foreach (byte[] p in seed) fs.Write(p, 0, format.PageSize);

        // Reopen through the normal stack and self-register the two bootstrap tables, so the catalog then
        // finds them and every other table can go through TableCreator.
        using var db = JetDatabase.Open(path, readOnly: false);
        Table msysObjects = db.OpenTableAt(msysObjPage, "MSysObjects");
        InsertCatalogRow(msysObjects, msysObjPage, "MSysObjects", SystemFlag);
        InsertCatalogRow(msysObjects, msysAcesPage, "MSysACEs", SystemFlag);
    }

    private static void InsertCatalogRow(Table msysObjects, int id, string name, int flags)
    {
        var values = new object?[msysObjects.Definition.Columns.Count];
        void Set(string col, object? v) => values[msysObjects.Definition.FindColumn(col)!.Index] = v;
        Set("Id", id);
        Set("ParentId", 0);
        Set("Name", name);
        Set("Type", (short)1);      // table object
        Set("Flags", flags);
        Set("DateCreate", DateTime.Now);
        Set("DateUpdate", DateTime.Now);
        msysObjects.Insert(values);
    }

    private static byte[] BuildEmptyDataPage(JetFormatBase format)
    {
        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        page[1] = 0x01;
        return page; // rowCount 0 → the allocator grows the file on demand
    }

    /// <summary>Builds a system table's TDEF page and its owned/free usage-map page. Long-value (Memo/Ole)
    /// columns each get an owned+free map on that page (rows 2 onward, after the two data-page maps), so a
    /// row that stores a long value — e.g. an <c>MSysObjects</c> catalog row carrying an <c>LvProp</c> blob —
    /// has somewhere to record its LVAL page.</summary>
    private static (byte[] Tdef, byte[] UsageMap) BuildSystemTable(
        JetFormatBase format, IReadOnlyList<ColumnSpec> columns, int usageMapPage)
    {
        var longValueCols = columns.Select((c, id) => (c, id))
            .Where(x => x.c.Type is JetDataType.Memo or JetDataType.Ole).ToList();
        var longValueSpecs = new List<LongValueColumnSpec>(longValueCols.Count);
        for (int j = 0; j < longValueCols.Count; j++)
            longValueSpecs.Add(new LongValueColumnSpec(longValueCols[j].id, UsedRow: 2 + 2 * j, FreeRow: 3 + 2 * j, MapPage: usageMapPage));

        byte[] tdef = TdefBuilder.Build(format, TableType.System, columns, longValueColumns: longValueSpecs).Page;
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
