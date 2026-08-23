namespace LibRed.Formats;

/// <summary>
/// Version-specific layout description for a Jet/ACE database. Holds every byte
/// offset, size and limit that differs between format versions, so the page
/// parsers can read named constants instead of hard-coded magic numbers.
/// </summary>
/// <remarks>
/// The authoritative references for these values are the mdbtools source
/// (<c>include/mdbtools.h</c>, <c>src/libmdb/</c>) and Jackcess
/// (<c>com.healthmarketscience.jackcess.impl.JetFormat</c>).
/// </remarks>
public abstract class JetFormatBase
{
    /// <summary>Offset of the one-byte format version marker within page 0.</summary>
    public const int VersionOffset = 0x14;

    /// <summary>Offset of the cleartext ASCII engine-version string ("4.0", NUL-terminated) — past the masked
    /// header window, so readable directly. Present on both Jet 4 (<c>.mdb</c>) and ACE (<c>.accdb</c>), which
    /// are both the Jet-4.0 engine. Used to confirm an unknown version byte is still a 4.0-family database
    /// before falling back to the latest known ACE layout.</summary>
    public const int EngineVersionOffset = 0x9C;
    public const int EngineVersionLength = 4;
    private const string Jet40EngineVersion = "4.0";

    /// <summary>Offset of the ASCII format identifier string within page 0.</summary>
    public const int FormatIdentifierOffset = 0x04;

    /// <summary>Length of the format identifier string (excluding its NUL terminator).</summary>
    public const int FormatIdentifierLength = 15;

    /// <summary>Identifier for the MDB (Jet 3/4) family.</summary>
    public const string JetIdentifier = "Standard Jet DB";

    /// <summary>Identifier for the ACCDB (ACE 12+) family.</summary>
    public const string AceIdentifier = "Standard ACE DB";

    /// <summary>Identifier for a Jet workgroup / system database (<c>System.mdw</c>). Same Jet 4 binary format
    /// as <see cref="JetIdentifier"/>, but with this signature and always engine-encrypted (see
    /// <see cref="JetLegacyEncryption"/>).</summary>
    public const string JetSystemIdentifier = "Jet System DB";

    // --- Page 0 obfuscated header (0x18..0x98) ---
    // The header is XOR-obfuscated with a fixed 128-byte mask (below). Field offsets and the mask
    // are corroborated by mdbtools and Jackcess AND verified against real files here: the mask
    // reproduces the code page (0x3C), collation LCID (0x6E) and creation date (0x72) bytes we
    // recovered independently by known-plaintext, and it decodes every fixture's header to sensible
    // values (see docs/format/page-00-database.md §2.1).

    /// <summary>Start offset of the obfuscated page-0 header region (also the mask's first byte).</summary>
    public const int PageZeroHeaderMaskStart = 0x18;

    /// <summary>Offset of the 4-byte page number of the <c>MSysObjects</c> TDEF — the catalog root, the
    /// bootstrap pointer that lets the engine find the system catalog before it can read any table. It is
    /// the first of four system-table pointers (<c>MSysObjects</c>/<c>MSysACEs</c>/<c>MSysQueries</c>/
    /// <c>MSysRelationships</c> at <c>0x20</c>/<c>0x24</c>/<c>0x28</c>/<c>0x2C</c>, values 2/3/4/5 in every
    /// file); the others are reachable via the catalog itself. Verified: each value equals the object's
    /// <c>MSysObjects.Id</c> and the page it names is a TDEF.</summary>
    public const int CatalogRootPointerOffset = 0x20;

    /// <summary>Offset of the 2-byte ANSI code page (LE): <c>0x04E4</c> = 1252, <c>0x04E2</c> = 1250.</summary>
    public const int CodePageOffset = 0x3C;

    /// <summary>Offset of the 4-byte database (encryption) key; 0 when the database has no password.</summary>
    public const int DatabaseKeyOffset = 0x3E;

    /// <summary>Offset of the database password (Jet 4: 40 bytes; additionally masked by a
    /// creation-date-derived value, so an empty password does not read as zeroes).</summary>
    public const int PasswordOffset = 0x42;

    /// <summary>Offset of the 4-byte default text collating sort order — a 32-bit Windows LCID whose
    /// otherwise-unused top byte carries the sort-order version. Byte for byte it mirrors a column
    /// descriptor's <c>0x0B</c>..<c>0x0E</c>: LANGID at <c>0x6E</c> (2 bytes LE, <c>0x0409</c> = 1033 en-US),
    /// sort id at <c>0x70</c>, version at <c>0x71</c>.</summary>
    public const int CollationSortOrderOffset = 0x6E;

    /// <summary>Offset of the collation's 1-byte sort id — the LCID's high word, which is what distinguishes
    /// an alternate sort order from its base locale (German Phone Book <c>0x00010407</c> vs German
    /// <c>0x00000407</c>; Hungarian Technical <c>0x0001040E</c> vs Hungarian <c>0x0000040E</c>).</summary>
    public const int CollationSortIdOffset = 0x70;

    /// <summary>Offset of the 1-byte collation sort-order version within the sort-order field (0/1).</summary>
    public const int CollationVersionOffset = 0x71;

    /// <summary>Offset of the 8-byte database creation timestamp: an OLE automation date
    /// (IEEE <c>double</c>, days from the 1899-12-30 epoch).</summary>
    public const int CreationDateOffset = 0x72;

    /// <summary>
    /// The fixed 128-byte XOR mask applied to the page-0 header from <see cref="PageZeroHeaderMaskStart"/>
    /// (Jet 4 / ACE; Jet 3 uses 126 bytes). This is Jackcess's <c>BASE_HEADER_MASK</c>, verified here to
    /// reproduce the header bytes LibRed recovered from first principles and to decode real fixtures.
    /// </summary>
    public static ReadOnlySpan<byte> PageZeroHeaderMask =>
    [
        0xB5, 0x6F, 0x03, 0x62, 0x61, 0x08, 0xC2, 0x55, 0xEB, 0xA9, 0x67, 0x72, 0x43, 0x3F, 0x00, 0x9C,
        0x7A, 0x9F, 0x90, 0xFF, 0x80, 0x9A, 0x31, 0xC5, 0x79, 0xBA, 0xED, 0x30, 0xBC, 0xDF, 0xCC, 0x9D,
        0x63, 0xD9, 0xE4, 0xC3, 0x7B, 0x42, 0xFB, 0x8A, 0xBC, 0x4E, 0x86, 0xFB, 0xEC, 0x37, 0x5D, 0x44,
        0x9C, 0xFA, 0xC6, 0x5E, 0x28, 0xE6, 0x13, 0xB6, 0x8A, 0x60, 0x54, 0x94, 0x7B, 0x36, 0xF5, 0x72,
        0xDF, 0xB1, 0x77, 0xF4, 0x13, 0x43, 0xCF, 0xAF, 0xB1, 0x33, 0x34, 0x61, 0x79, 0x5B, 0x92, 0xB5,
        0x7C, 0x2A, 0x05, 0xF1, 0x7C, 0x99, 0x01, 0x1B, 0x98, 0xFD, 0x12, 0x4F, 0x4A, 0x94, 0x6C, 0x3E,
        0x60, 0x26, 0x5F, 0x95, 0xF8, 0xD0, 0x89, 0x24, 0x85, 0x67, 0xC6, 0x1F, 0x27, 0x44, 0xD2, 0xEE,
        0xCF, 0x65, 0xED, 0xFF, 0x07, 0xC7, 0x46, 0xA1, 0x78, 0x16, 0x0C, 0xED, 0xE9, 0x2D, 0x62, 0xD4,
    ];

    // --- Table definition (TDEF) page layout ---
    // Defaults below are for Jet 4 / ACE (verified against a real ACCDB). Jet 3 differs
    // (18-byte column entries, 1-byte ASCII name lengths) and will override these.

    /// <summary>Offset of the 1-byte TDEF header flags (observed 0x01).</summary>
    public virtual int TdefHeaderFlagsOffset => 0x01;

    /// <summary>Offset of the 2-byte free-space-remaining-in-this-page field.</summary>
    public virtual int TdefFreeSpaceOffset => 0x02;

    /// <summary>Offset of the 4-byte pointer to the next TDEF page (0 if the definition fits one page).</summary>
    public virtual int TdefNextPageOffset => 0x04;

    /// <summary>Offset of the 4-byte total TDEF definition length.</summary>
    public virtual int TdefLengthOffset => 0x08;

    /// <summary>Offset of the 4-byte TDEF record marker (0x00000659); see <see cref="TdefRecordMarker"/>.</summary>
    public virtual int TdefRecordMarkerOffset => 0x0C;

    /// <summary>Offset of the 2-byte maximum-column-count high-water (the next column id to assign).</summary>
    public virtual int TdefMaxColumnsOffset => 0x29;

    /// <summary>The 0x00000659 record marker written at the TDEF header (<see cref="TdefRecordMarkerOffset"/>),
    /// each column descriptor (+0x01) and each index-info block (+0x00). Access validates it; the reader ignores it.</summary>
    public const uint TdefRecordMarker = 0x00000659;

    /// <summary>Size of the 8-byte continuation header that prefixes each TDEF continuation page's payload
    /// (also the free-space reserve the first page leaves for it).</summary>
    public const int TdefContinuationHeaderSize = 8;

    /// <summary>Offset of the 4-byte row count.</summary>
    public virtual int TdefRowCountOffset => 0x10;

    /// <summary>Offset of the 4-byte highest-AutoNumber-assigned value (the last id used; next = +increment).</summary>
    public virtual int TdefLastAutoNumberOffset => 0x14;

    /// <summary>Offset of the 4-byte AutoNumber increment (default 1; a custom COUNTER sets it).</summary>
    public virtual int TdefAutoNumberIncrementOffset => 0x18;

    /// <summary>Offset of the 4-byte complex-type AutoNumber high-water (mdbtools <c>ct_autonum</c>) — the
    /// next id for a complex (multi-value/attachment) column. 0 for every table without such a column.</summary>
    public virtual int TdefComplexAutoNumberOffset => 0x1C;

    /// <summary>Offset of the 1-byte table type (0x4E 'N' user, 0x53 'S' system).</summary>
    public virtual int TdefTableTypeOffset => 0x28;

    /// <summary>Offset of the 2-byte variable-length column count.</summary>
    public virtual int TdefVariableColumnsOffset => 0x2B;

    /// <summary>Offset of the 2-byte total column count.</summary>
    public virtual int TdefColumnCountOffset => 0x2D;

    /// <summary>Offset of the 4-byte real-index (slot) count, used to size the index block before columns.</summary>
    public virtual int TdefRealIndexCountOffset => 0x2F;

    /// <summary>Offset of the 4-byte logical index count.</summary>
    public virtual int TdefIndexCountOffset => 0x33;

    /// <summary>Offset where the real-index block begins; column descriptors follow it.</summary>
    public virtual int TdefRealIndexBlockOffset => 0x3F;

    /// <summary>Size in bytes of each real-index entry in the block before the column descriptors.</summary>
    public virtual int RealIndexEntrySize => 12;

    /// <summary>Size in bytes of one column descriptor.</summary>
    public virtual int ColumnDescriptorSize => 25;

    // --- Column descriptor layout (offsets within a single descriptor) ---
    public virtual int ColumnTypeOffset => 0x00;
    public virtual int ColumnNumberOffset => 0x05;
    public virtual int ColumnVariableIndexOffset => 0x07; // position among variable columns (0 for fixed)
    public virtual int ColumnPrecisionOffset => 0x0B; // Decimal/Numeric columns only
    public virtual int ColumnScaleOffset => 0x0C;     // Decimal/Numeric columns only
    // Non-numeric columns instead use 0x0B..0x0E for the text collation, and the four bytes together are a
    // 32-bit Windows LCID with the sort-order version in its otherwise-unused top byte:
    //   0x0B/0x0C  LANGID, little-endian (0x0409 = General/en-US)
    //   0x0D       sort id — the high word of the LCID, which is what separates an alternate sort order from
    //              its base locale (German Phone Book = 0x00010407, Hungarian Technical = 0x0001040E)
    //   0x0E       sort-order version (0 = the legacy compacted table, 1 = the Access 2010 NLS order)
    public virtual int ColumnLocaleOffset => 0x0B;
    public virtual int ColumnCollationSortIdOffset => 0x0D;
    public virtual int ColumnCollationVersionOffset => 0x0E;
    public virtual int ColumnFlagsOffset => 0x0F;
    /// <summary>Extended column flags (0x10): bit 0x01 = compressed-Unicode capable, 0xC0 = calculated column.</summary>
    public virtual int ColumnExtendedFlagsOffset => 0x10;
    public virtual int ColumnFixedOffsetOffset => 0x15;
    public virtual int ColumnLengthOffset => 0x17;

    // Column flag byte (0x0F). Every documented bit is modelled (read into ColumnDef, written from it); the
    // undocumented bits (0x08/0x10/0x20, zero in every file observed) are the only ones carried through raw.
    /// <summary>Column flag: the column is fixed-length.</summary>
    public const byte ColumnFlagFixedLength = 0x01;
    /// <summary>Column flag: the column is updatable (set on essentially every column).</summary>
    public const byte ColumnFlagUpdatable = 0x02;
    /// <summary>Column flag: the column is an AutoNumber.</summary>
    public const byte ColumnFlagAutoNumber = 0x04;
    /// <summary>Column flag: an AutoNumber column that generates GUIDs (Replication ID) rather than Longs.</summary>
    public const byte ColumnFlagGuidAutoNumber = 0x40;
    /// <summary>Column flag: a hyperlink (a Memo column presented as a hyperlink).</summary>
    public const byte ColumnFlagHyperlink = 0x80;
    /// <summary>Mask of the documented flag bits — the complement is undocumented and preserved from raw.</summary>
    public const byte ColumnFlagsDocumented =
        ColumnFlagFixedLength | ColumnFlagUpdatable | ColumnFlagAutoNumber | ColumnFlagGuidAutoNumber | ColumnFlagHyperlink;

    // Extended flag byte (0x10).
    /// <summary>Extended flag: the column can store compressed Unicode text (§7).</summary>
    public const byte ColumnExtFlagCompressedUnicode = 0x01;
    /// <summary>Extended flag: a calculated (computed) column (ACE 14+); the 0xC0 pair.</summary>
    public const byte ColumnExtFlagCalculated = 0xC0;
    /// <summary>Mask of the documented extended-flag bits — the complement is preserved from raw.</summary>
    public const byte ColumnExtFlagsDocumented = ColumnExtFlagCompressedUnicode | ColumnExtFlagCalculated;
    // Note: nullability is NOT in the column flag byte (bit 0x02 is set on every column). A NOT NULL column
    // is marked by a boolean `Required` property in the LvProp blob instead — see PropertyBlob / §11.

    // --- Data page layout (Jet 4 / ACE) ---

    /// <summary>Offset of the 2-byte free-space count on a data page.</summary>
    public virtual int DataFreeSpaceOffset => 0x02;

    /// <summary>Offset of the 4-byte owning-table TDEF page (or the "LVAL" marker on long-value pages).</summary>
    public virtual int DataOwnerOffset => 0x04;

    /// <summary>Offset of the 2-byte row count on a data page.</summary>
    public virtual int DataRowCountOffset => 0x0C;

    /// <summary>Offset of the row-offset slot directory (2 bytes per row).</summary>
    public virtual int DataRowDirectoryOffset => 0x0E;

    /// <summary>Size of the column-count field at the start of a row record (2 bytes in Jet 4 / ACE, 1 in Jet 3).</summary>
    public virtual int RowColumnCountSize => 2;

    /// <summary>Page number of the system catalog table MSysObjects (its TDEF page).</summary>
    public virtual int CatalogPage => 2;

    /// <summary>Offset in a TDEF of the owned-pages usage-map pointer: 1 byte row, then a 3-byte page.</summary>
    public virtual int TdefOwnedPagesOffset => 0x37;

    /// <summary>Offset in a TDEF of the free-pages usage-map pointer (same 1-byte row + 3-byte page shape):
    /// the subset of the table's owned data pages that still have room for a row. Once earlier pages fill,
    /// Access leaves only the page it is currently appending to marked here.</summary>
    public virtual int TdefFreePagesOffset => 0x3B;

    /// <summary>Page size in bytes (2048 for Jet 3, 4096 for Jet 4 and all ACE versions).</summary>
    public int PageSize { get; protected set; } = 4096;

    /// <summary>The logical version this format describes.</summary>
    public abstract JetVersion Version { get; }

    /// <summary>True for the ACCDB (ACE 12+) family, which uses different encryption and layout details.</summary>
    public virtual bool IsAccdb => false;

    /// <summary>
    /// Sniffs the format version byte from page 0 of <paramref name="stream"/> and
    /// returns the matching format description. Restores the stream position.
    /// </summary>
    public static JetFormatBase Detect(Stream stream)
    {
        Span<byte> header = stackalloc byte[EngineVersionOffset + EngineVersionLength]; // through the 0x9C engine string
        long original = stream.Position;
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            stream.ReadExactly(header);
        }
        finally
        {
            stream.Seek(original, SeekOrigin.Begin);
        }

        string identifier = ReadFormatIdentifier(header);
        if (identifier is not (JetIdentifier or AceIdentifier or JetSystemIdentifier))
            throw new NotSupportedException(
                $"Not a Jet/ACE database: expected \"{JetIdentifier}\", \"{AceIdentifier}\" or \"{JetSystemIdentifier}\" at offset 0x{FormatIdentifierOffset:X2}, found \"{identifier}\".");

        byte version = header[VersionOffset];
        bool identifierMatchesVersion = identifier == AceIdentifier
            ? version >= 0x02
            : version <= 0x01;
        if (!identifierMatchesVersion)
            throw new NotSupportedException(
                $"Jet/ACE format identifier \"{identifier}\" does not match version byte 0x{version:X2}.");

        try
        {
            return FromVersionByte(version);
        }
        catch (NotSupportedException)
        {
            // Unknown version byte on an ACCDB that still carries the Jet-4.0 engine string is almost certainly a
            // newer 4KB ACE variant (the format grows conservatively, adding a byte per engine release). Read it
            // with the latest known ACE layout rather than failing. The 4.0 guard is essential: it stops us from
            // mis-reading a genuinely different future engine (e.g. a "5.0" string) as ACE.
            if (identifier == AceIdentifier && ReadEngineVersion(header) == Jet40EngineVersion)
                return new Jet17Format();
            throw;
        }
    }

    /// <summary>Reads the cleartext engine-version string ("4.0") at <see cref="EngineVersionOffset"/>.</summary>
    private static string ReadEngineVersion(ReadOnlySpan<byte> header) =>
        System.Text.Encoding.ASCII.GetString(header.Slice(EngineVersionOffset, EngineVersionLength)).TrimEnd('\0');

    /// <summary>Reads the ASCII format identifier ("Standard Jet DB"/"Standard ACE DB") from a page-0 header.</summary>
    public static string ReadFormatIdentifier(ReadOnlySpan<byte> header) =>
        // Trim trailing NULs and spaces: the ACE/Jet identifiers fill the field exactly, but "Jet System DB"
        // (a workgroup file) is padded with spaces to the field width.
        System.Text.Encoding.ASCII.GetString(header.Slice(FormatIdentifierOffset, FormatIdentifierLength)).TrimEnd('\0', ' ');

    /// <summary>Maps the raw version byte at <see cref="VersionOffset"/> to a format instance.</summary>
    public static JetFormatBase FromVersionByte(byte versionByte) => versionByte switch
    {
        0x00 => throw new NotSupportedException(
            "Jet 3 / Access 97 databases are not supported because their 2 KB page, TDEF, column, and row layouts are not yet implemented."),
        0x01 => new Jet4Format(),
        0x02 => new Jet12Format(),
        0x03 => new Jet14Format(),
        // 0x04 = ACE 15 (Access 2013)'s reserved engine byte. 2013 added no format-forcing data type (Large Number
        // is 0x05, Date/Time Extended is 0x06), so this is byte-identical to the 0x03 (2010) format and is never
        // actually emitted — 2013 defaults to 0x03. Read it with the 2010 layout rather than inventing a clone
        // Jet15Format (verified: a real db2013 reads 0x03; jackcess ships no 2013 fixture).
        0x04 => new Jet14Format(),
        0x05 => new Jet16Format(),
        0x06 => new Jet17Format(),
        _ => throw new NotSupportedException($"Unknown Jet/ACE format version byte 0x{versionByte:X2}."),
    };
}
