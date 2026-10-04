using LibRed.Catalog;
using LibRed.IO;
using LibRed.Formats;
using LibRed.Storage;
using System.Text;
using System.Buffers.Binary;

namespace LibRed.Pages;

/// <summary>
/// Page 0 — the database definition page. Carries the format version, code page,
/// collation, creation metadata and the encryption material needed to decrypt the
/// rest of the file.
/// </summary>
public sealed class DatabaseDefinitionPage : Page
{
    public override PageType Type => PageType.DatabaseDefinition;

    /// <summary>The ASCII format identifier, e.g. "Standard Jet DB" or "Standard ACE DB".</summary>
    public string FormatIdentifier { get; internal set; } = string.Empty;

    public byte JetVersion { get; internal set; }

    /// <summary>The 4-byte database (encryption) key; 0 when the database has no password.</summary>
    public int DatabaseKey { get; internal set; }

    /// <summary>The database's ANSI code page (e.g. 1252 Latin-1, 1250 Central European),
    /// decoded from the obfuscated field at <see cref="Formats.JetFormatBase.CodePageOffset"/>.</summary>
    public int CodePage { get; internal set; }

    /// <summary>The database's default collation LCID (e.g. 1033 = en-US), decoded from the
    /// obfuscated sort-order field at <see cref="Formats.JetFormatBase.CollationSortOrderOffset"/>.</summary>
    public int DefaultCollationLcid { get; internal set; }

    /// <summary>The database default sort-order version: 0 = the legacy compacted table, 1 = the Access-2010
    /// NLS order. Matches each column descriptor's byte <c>0x0E</c>.</summary>
    public byte DefaultCollationVersion { get; internal set; }

    /// <summary>The default collation's sort id — the LCID's high word, from <c>0x70</c>. Non-zero only for a
    /// Windows alternate sort order (German Phone Book, Hungarian Technical), which shares its LANGID with the
    /// base locale and is distinguishable by nothing else. Matches column descriptor byte <c>0x0D</c>.</summary>
    public byte DefaultCollationSortId { get; internal set; }

    /// <summary>The default text collating order, assembled from the three fields of the <c>0x6E</c> block.
    /// This is what a column created in the database inherits.</summary>
    public Collation Collation =>
        new((CollatingOrder)DefaultCollationLcid, DefaultCollationVersion, DefaultCollationSortId);

    /// <summary>Page number of the <c>MSysObjects</c> TDEF (the catalog root), read from the bootstrap
    /// pointer at <see cref="Formats.JetFormatBase.CatalogRootPointerOffset"/>. 2 where ACE creates it, but any page the pointer names.</summary>
    public int CatalogRootPage { get; internal set; }

    /// <summary>Page number of the <c>MSysACEs</c> TDEF, from the bootstrap pointer at
    /// <see cref="Formats.JetFormatBase.AcesRootPointerOffset"/>.</summary>
    public int AcesRootPage { get; internal set; }

    /// <summary>Page number of the <c>MSysQueries</c> TDEF, from the bootstrap pointer at
    /// <see cref="Formats.JetFormatBase.QueriesRootPointerOffset"/>.</summary>
    public int QueriesRootPage { get; internal set; }

    /// <summary>Page number of the <c>MSysRelationships</c> TDEF, from the bootstrap pointer at
    /// <see cref="Formats.JetFormatBase.RelationshipsRootPointerOffset"/>.</summary>
    public int RelationshipsRootPage { get; internal set; }

    /// <summary>Page number of the <c>MSysAccounts</c> TDEF, from the bootstrap pointer at
    /// <see cref="Formats.JetFormatBase.AccountsRootPointerOffset"/>; zero unless this is a workgroup file.</summary>
    public int AccountsRootPage { get; internal set; }

    /// <summary>Page number of the <c>MSysGroups</c> TDEF, from the bootstrap pointer at
    /// <see cref="Formats.JetFormatBase.GroupsRootPointerOffset"/>; zero unless this is a workgroup file.</summary>
    public int GroupsRootPage { get; internal set; }

    /// <summary>Where the global free-pages usage map lives, from the <c>[row:1][page:3]</c> pointer at
    /// <see cref="Formats.JetFormatBase.FreePagesMapPointerOffset"/>. Page 1 row 0 where ACE creates it.</summary>
    public (int Row, int Page) FreePagesMap { get; internal set; }

    /// <summary>Where the global released-pages usage map lives, from the <c>[row:1][page:3]</c> pointer at
    /// <see cref="Formats.JetFormatBase.ReleasedPagesMapPointerOffset"/>. Page 1 row 1 where ACE creates it.</summary>
    public (int Row, int Page) ReleasedPagesMap { get; internal set; }

    public DateTime DatabaseCreationDate { get; internal set; }

    internal override void Read(PageBuffer buffer, Formats.JetFormatBase format)
    {
        PageNumber = buffer.PageNumber;
        FormatIdentifier = Formats.JetFormatBase.ReadFormatIdentifier(buffer.Span);
        JetVersion = Formats.JetFormatBase.ReadVersionByte(buffer.Span);

        // The header from 0x18 is XOR-obfuscated with a fixed RC4 keystream; de-obfuscate the
        // whole region once, then read the fields out of the clear copy.
        Span<byte> clear = stackalloc byte[format.PageZeroHeaderMaskLength];
        int b = format.PageZeroHeaderMaskStart;
        ReadMasked(buffer.Span, b, clear, format);

        CodePage = BinaryPrimitives.ReadUInt16LittleEndian(clear.Slice(format.CodePageOffset - b, 2));
        DatabaseKey = ReadDatabaseKey(buffer.Span, format);
        DefaultCollationLcid = BinaryPrimitives.ReadUInt16LittleEndian(clear.Slice(format.CollationSortOrderOffset - b, 2));
        DefaultCollationSortId = clear[format.CollationSortIdOffset - b];
        DefaultCollationVersion = clear[format.CollationVersionOffset - b];
        CatalogRootPage = BinaryPrimitives.ReadInt32LittleEndian(clear.Slice(format.CatalogRootPointerOffset - b, 4));
        AcesRootPage = BinaryPrimitives.ReadInt32LittleEndian(clear.Slice(format.AcesRootPointerOffset - b, 4));
        QueriesRootPage = BinaryPrimitives.ReadInt32LittleEndian(clear.Slice(format.QueriesRootPointerOffset - b, 4));
        RelationshipsRootPage = BinaryPrimitives.ReadInt32LittleEndian(clear.Slice(format.RelationshipsRootPointerOffset - b, 4));
        AccountsRootPage = BinaryPrimitives.ReadInt32LittleEndian(clear.Slice(format.AccountsRootPointerOffset - b, 4));
        GroupsRootPage = BinaryPrimitives.ReadInt32LittleEndian(clear.Slice(format.GroupsRootPointerOffset - b, 4));
        FreePagesMap = ReadMapPointer(buffer.Span, format.FreePagesMapPointerOffset, format);
        ReleasedPagesMap = ReadMapPointer(buffer.Span, format.ReleasedPagesMapPointerOffset, format);
        // An OLE Automation date, so it is decoded by the OA function rather than by hand: the two disagree
        // below the epoch, where OA keeps the time fraction positive (-1.25 is 1899-12-29 06:00, not
        // 1899-12-28 18:00). And the value comes straight off page 0, so a NaN, an infinity or anything past
        // DateTime.MaxValue is corruption in the very first thing an open does — reported as such, rather than
        // escaping as ArgumentOutOfRangeException from inside AddDays.
        double days = BinaryPrimitives.ReadDoubleLittleEndian(clear.Slice(format.CreationDateOffset - b, sizeof(double)));
        DatabaseCreationDate = Storage.Types.JetTypeCodec.TryFromOaDate(days, out DateTime created)
            ? created
            : throw new InvalidDataException($"Page 0's creation date ({days}) is not a valid OLE Automation date.");
    }

    /// <summary>Decodes one of page 0's global usage-map pointers: a record pointer under the header mask.</summary>
    internal static (int Row, int Page) ReadMapPointer(ReadOnlySpan<byte> page, int offset, Formats.JetFormatBase format)
    {
        Span<byte> pointer = stackalloc byte[PageBuffer.RecordPointerSize];
        ReadMasked(page, offset, pointer, format);
        return PageBuffer.ReadRecordPointer(pointer, 0);
    }

    /// <summary>Reads <paramref name="clear"/>'s length in bytes of the masked header from page offset
    /// <paramref name="offset"/>, removing the fixed header mask
    /// (<see cref="Formats.JetFormatBase.PageZeroHeaderMask"/>).</summary>
    internal static void ReadMasked(ReadOnlySpan<byte> page0, int offset, Span<byte> clear, Formats.JetFormatBase format)
    {
        ReadOnlySpan<byte> mask = format.PageZeroHeaderMask[(offset - format.PageZeroHeaderMaskStart)..];
        for (int i = 0; i < clear.Length; i++)
            clear[i] = (byte)(page0[offset + i] ^ mask[i]);
    }

    /// <summary>Writes <paramref name="clear"/> into the masked header at page offset <paramref name="offset"/>
    /// under the fixed header mask — the inverse of <see cref="ReadMasked"/>.</summary>
    internal static void WriteMasked(Span<byte> page0, int offset, ReadOnlySpan<byte> clear, Formats.JetFormatBase format)
    {
        ReadOnlySpan<byte> mask = format.PageZeroHeaderMask[(offset - format.PageZeroHeaderMaskStart)..];
        for (int i = 0; i < clear.Length; i++)
            page0[offset + i] = (byte)(clear[i] ^ mask[i]);
    }

    /// <summary>The database (encryption) key at <see cref="Formats.JetFormatBase.DatabaseKeyOffset"/>; nonzero
    /// when the pages are encrypted.</summary>
    internal static int ReadDatabaseKey(ReadOnlySpan<byte> page0, Formats.JetFormatBase format)
    {
        Span<byte> key = stackalloc byte[sizeof(int)];
        ReadMasked(page0, format.DatabaseKeyOffset, key, format);
        return BinaryPrimitives.ReadInt32LittleEndian(key);
    }

    /// <summary>The declared length of the EncryptionInfo descriptor at
    /// <see cref="Formats.JetFormatBase.EncryptionInfoOffset"/>, from the cleartext field at
    /// <see cref="Formats.JetFormatBase.EncryptionInfoLengthOffset"/>; 0 when there is none. Unbounded — it comes out of
    /// the file, and each caller checks it against what it is about to touch.</summary>
    internal static int ReadEncryptionInfoLength(ReadOnlySpan<byte> page0, Formats.JetFormatBase format) =>
        BinaryPrimitives.ReadUInt16LittleEndian(page0.Slice(format.EncryptionInfoLengthOffset, sizeof(ushort)));

    /// <summary>Writes the database key — the inverse of <see cref="ReadDatabaseKey"/>.</summary>
    internal static void WriteDatabaseKey(Span<byte> page0, int key, Formats.JetFormatBase format)
    {
        Span<byte> clear = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(clear, key);
        WriteMasked(page0, format.DatabaseKeyOffset, clear, format);
    }

    /// <summary>Writes the password field: <paramref name="value"/> zero-padded to the field's size, XOR'd with its
    /// creation-date mask (<see cref="XorPasswordDateMask"/>, so the creation date must already be in place), under
    /// the header mask. An <c>.mdb</c>'s value is its Jet password, UTF-16LE — empty when it has none; an
    /// <c>.accdb</c>'s is the low byte of its database key, repeated across the field (zero when unencrypted).</summary>
    internal static void WritePassword(Span<byte> page0, ReadOnlySpan<byte> value, Formats.JetFormatBase format)
    {
        Span<byte> creationDate = stackalloc byte[sizeof(double)];
        ReadMasked(page0, format.CreationDateOffset, creationDate, format);

        Span<byte> field = stackalloc byte[format.PasswordSize];
        field.Clear();
        value.CopyTo(field);
        XorPasswordDateMask(field, creationDate);
        WriteMasked(page0, format.PasswordOffset, field, format);
    }

    /// <summary>XORs the password field's own mask over <paramref name="field"/>, in either direction: the
    /// integer part of the creation date (<paramref name="creationDate"/>, the clear 8-byte double), as a
    /// 4-byte little-endian value, cycled across the field. It lies under the header mask as well.</summary>
    internal static void XorPasswordDateMask(Span<byte> field, ReadOnlySpan<byte> creationDate)
    {
        Span<byte> dateMask = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(dateMask, (int)BinaryPrimitives.ReadDoubleLittleEndian(creationDate));
        for (int i = 0; i < field.Length; i++)
            field[i] ^= dateMask[i % dateMask.Length];
    }
    // The engine build LibRed stamps at page-0 EngineBuildOffset. A file keeps whatever its creator wrote, so this
    // is the creator's choice, not part of the format: a Jet 4 .mdb gets Jet 4.0.9801, what the current msjet40.dll
    // (x86 only) stamps on anything created through the Jet OLE DB 4.0 provider, and an ACCDB gets ACE 12.0.4518
    // (Office 2007 RTM), which every ACE version stamps.
    private const int Jet4EngineBuild = 0x2649;
    private const int AceEngineBuild = 0x11A6;

    /// <summary>The minor byte ACE writes when it CREATES a database of <paramref name="version"/>: <c>0x01</c>
    /// for the 2010 format, <c>0x00</c> for every other. Like the build, the creator's choice rather than the
    /// format's: a version raise writes <c>0x00</c> whatever the target, so a 2007 file raised to 2010 does not
    /// carry the <c>0x01</c> a created one does.</summary>
    private static byte CreatedMinorVersion(byte version) =>
        (byte)(version == (byte)Formats.JetVersion.Version14_2010 ? 0x01 : 0x00);

    /// <summary>
    /// Synthesises page 0 (the database definition page) — the exact inverse of
    /// <see cref="Pages.DatabaseDefinitionPage.Read"/>. Byte-for-byte identical to a real empty file's
    /// page 0 for the same parameters (verified against Access-created files).
    /// </summary>
    /// <param name="version">Format version byte (e.g. 0x02 = ACE 12 / Access 2007).</param>
    /// <param name="isAccdb">true for the ACCDB identifier, false for the MDB (Jet) identifier.</param>
    /// <param name="codePage">ANSI code page of the collation's language (1252 for en-US, 0 for a language
    /// with none) — see <see cref="JetCodePages"/>.</param>
    /// <param name="collation">The database's default collation — its LCID and sort-order version
    /// (1033 / version 0 is General Legacy en-US).</param>
    /// <param name="creationDays">Creation timestamp as an OLE-automation date (days since 1899-12-30), passed
    /// as the raw double so the exact millisecond-precise bit pattern is preserved — the file's SIDs are masked
    /// with a keystream folded from those bits and the rest of the header (page-00 §2.3).</param>
    /// <param name="globalMapPage">The page holding the global usage maps: free pages in row 0, released pages in
    /// row 1.</param>
    /// <param name="objectsPage">The <c>MSysObjects</c> TDEF page.</param>
    /// <param name="acesPage">The <c>MSysACEs</c> TDEF page.</param>
    /// <param name="queriesPage">The <c>MSysQueries</c> TDEF page.</param>
    /// <param name="relationshipsPage">The <c>MSysRelationships</c> TDEF page.</param>
    /// <param name="accountsPage">The <c>MSysAccounts</c> TDEF page of a workgroup file; zero for a database,
    /// which has no such table.</param>
    /// <param name="groupsPage">The <c>MSysGroups</c> TDEF page of a workgroup file; zero for a database.</param>
    public static byte[] Build(
        byte version, bool isAccdb, int codePage, Collation collation, double creationDays,
        int globalMapPage, int objectsPage, int acesPage, int queriesPage, int relationshipsPage,
        int accountsPage, int groupsPage)
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(version);
        var page = new byte[format.PageSize];

        // --- Pre-mask region (0x00..0x17, cleartext) ---
        PageHeader.WriteType(page, PageType.DatabaseDefinition);
        string id = isAccdb ? JetFormatBase.AceIdentifier : JetFormatBase.JetIdentifier;
        Encoding.ASCII.GetBytes(id).CopyTo(page, JetFormatBase.FormatIdentifierOffset); // 0x04, 15 bytes; 0x13 stays NUL
        format.WriteVersion(page, version, CreatedMinorVersion(version));

        // --- Masked header (0x18..0x97): build the clear image, then XOR the fixed mask over it. ---
        int b = format.PageZeroHeaderMaskStart;
        Span<byte> clear = stackalloc byte[format.PageZeroHeaderMaskLength];

        // [row][page] pointers to the global usage maps — free pages (row 0), released pages (row 1).
        PageBuffer.WriteRecordPointer(clear, format.FreePagesMapPointerOffset - b, row: 0, globalMapPage);
        PageBuffer.WriteRecordPointer(clear, format.ReleasedPagesMapPointerOffset - b, row: 1, globalMapPage);
        // System-table bootstrap pointers = MSysObjects/ACEs/Queries/Relationships/Accounts/Groups pages.
        BinaryPrimitives.WriteInt32LittleEndian(clear[(format.CatalogRootPointerOffset - b)..], objectsPage);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(format.AcesRootPointerOffset - b)..], acesPage);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(format.QueriesRootPointerOffset - b)..], queriesPage);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(format.RelationshipsRootPointerOffset - b)..], relationshipsPage);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(format.AccountsRootPointerOffset - b)..], accountsPage);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(format.GroupsRootPointerOffset - b)..], groupsPage);
        BinaryPrimitives.WriteUInt16LittleEndian(clear[(format.CodePageOffset - b)..], (ushort)codePage); // 0x3C
        // 0x3E database key = 0 (unencrypted); leave clear zero.
        // 0x72..0x79 creation date (OLE double).
        BinaryPrimitives.WriteDoubleLittleEndian(clear.Slice(format.CreationDateOffset - b, sizeof(double)), creationDays);
        // The creating engine's build number.
        BinaryPrimitives.WriteInt32LittleEndian(clear[(format.EngineBuildOffset - b)..],
            format.IsAccdb ? AceEngineBuild : Jet4EngineBuild);
        // 0x6E..0x71 collating sort order: LANGID, sort id at 0x70, version at 0x71 — a 32-bit LCID carrying
        // the sort-order version in its unused top byte. Mirrors a column descriptor's 0x0B..0x0E.
        BinaryPrimitives.WriteUInt16LittleEndian(clear[(format.CollationSortOrderOffset - b)..], (ushort)collation.Order);
        clear[format.CollationSortIdOffset - b] = collation.SortId;
        clear[format.CollationVersionOffset - b] = collation.Version;

        WriteMasked(page, b, clear, format);
        // 0x42..0x69 password, empty: masked by its creation date as any password is, so even the empty one is
        // not zero on disk.
        WritePassword(page, [], format);

        // --- Post-mask tail (cleartext) ---
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.PageZeroConstantOffset), format.PageZeroConstant);
        Encoding.ASCII.GetBytes(JetFormatBase.Jet40EngineVersion).CopyTo(page, JetFormatBase.EngineVersionOffset);

        // User commit-byte table: 2 bytes per user to the end of the header page — Jet 3.x's 0x600 commit region
        // relocated to the end of the 4 KB page (see the Jet locking white paper). Each pair is a per-user
        // commit/lock status. A fresh file must seed every slot to the neutral idle value 00 01 — NOT 00 00,
        // which Jet reads as "mid-write to disk"; with no matching .ldb user lock that reads as a
        // suspect/corrupt database and forces a repair before Access will open it.
        for (int i = format.CommitByteTableOffset; i < page.Length; i++) page[i] = (byte)(i & 1);

        return page;
    }

}