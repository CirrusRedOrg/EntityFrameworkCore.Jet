namespace LibRed.Formats;

/// <summary>
/// Version-specific layout description for a Jet/ACE database. Names every byte offset, size and limit of the
/// on-disk format; each format family sets its own values — <see cref="Jet4Format"/> for Jet 4 and, by
/// inheritance, every ACE version, and <see cref="Jet3Format"/> for Jet 3.
/// </summary>
/// <remarks>
/// Two kinds of value live here rather than in a format: what has to be read before a format is known — the
/// format identifier and version byte page 0 is recognised by, and the engine-version string an unknown ACE byte
/// is checked against — and the marker values written inside the structures (<see cref="TdefRecordMarker"/>,
/// <see cref="IndexDataMarker"/>, <see cref="IndexDataColumnUnused"/>, <see cref="IndexInfoNoForeignKey"/>,
/// <see cref="TdefLongValueMapTerminator"/>, <see cref="LongValuePageMarker"/>). The authoritative references for the layouts are the mdbtools source (<c>include/mdbtools.h</c>,
/// <c>src/libmdb/</c>) and Jackcess (<c>com.healthmarketscience.jackcess.impl.JetFormat</c>).
/// </remarks>
public abstract class JetFormatBase
{
    // --- Format detection (page 0, read before the format is known) ---

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

    /// <summary>Offset of the one-byte format version marker within page 0.</summary>
    public const int VersionOffset = 0x14;

    /// <summary>Offset of the cleartext ASCII engine-version string ("4.0", NUL-terminated) — past the masked
    /// header window, so readable directly. Present on both Jet 4 (<c>.mdb</c>) and ACE (<c>.accdb</c>), which
    /// are both the Jet-4.0 engine. Used to confirm an unknown version byte is still a 4.0-family database
    /// before falling back to the latest known ACE layout.</summary>
    public const int EngineVersionOffset = 0x9C;
    public const int EngineVersionLength = 4;
    public const string Jet40EngineVersion = "4.0";

    // --- File ---

    /// <summary>Page size in bytes.</summary>
    public abstract int PageSize { get; }

    /// <summary>The logical version this format describes.</summary>
    public abstract JetVersion Version { get; }

    /// <summary>True for the ACCDB (ACE 12+) family, which uses different encryption and layout details.</summary>
    public abstract bool IsAccdb { get; }

    // --- Page 0 ---

    /// <summary>Offset of the one-byte minor version that follows the version byte.</summary>
    public abstract int MinorVersionOffset { get; }

    // The header is XOR-obfuscated with a fixed mask: the RC4 keystream of a fixed key, the same for every file.
    // Field offsets and the mask are corroborated by mdbtools and Jackcess AND verified against real files here:
    // the mask reproduces the code page, collation LCID and creation date bytes we recovered independently by
    // known-plaintext, and it decodes every fixture's header to sensible values (see
    // docs/format/page-00-database.md §2.1).

    /// <summary>Start offset of the obfuscated page-0 header region (also the mask's first byte).</summary>
    public abstract int PageZeroHeaderMaskStart { get; }

    /// <summary>Length of the obfuscated page-0 header region, and so of the mask.</summary>
    public abstract int PageZeroHeaderMaskLength { get; }

    /// <summary>The fixed RC4 key whose keystream is the page-0 header mask.</summary>
    public abstract ReadOnlySpan<byte> PageZeroHeaderMaskKey { get; }

    /// <summary>The XOR mask applied to the page-0 header from <see cref="PageZeroHeaderMaskStart"/>: the first
    /// <see cref="PageZeroHeaderMaskLength"/> bytes of the RC4 keystream under <see cref="PageZeroHeaderMaskKey"/>.</summary>
    public ReadOnlySpan<byte> PageZeroHeaderMask
    {
        get
        {
            if (_pageZeroHeaderMask is null)
            {
                var mask = new byte[PageZeroHeaderMaskLength];
                Crypto.Rc4Cipher.Apply(PageZeroHeaderMaskKey, mask); // the keystream, XOR'd over zeros
                _pageZeroHeaderMask = mask;
            }
            return _pageZeroHeaderMask;
        }
    }

    private byte[]? _pageZeroHeaderMask;

    /// <summary>Offset of the 4-byte <c>[row:1][page:3]</c> pointer to the global free-pages usage map — the
    /// map every allocation takes a page from. Page 1 row 0 in every file ACE writes, but ACE follows the
    /// pointer, row included, so a reader must too (docs/format/page-05-usage-maps.md §9.1).</summary>
    public abstract int FreePagesMapPointerOffset { get; }

    /// <summary>Offset of the 4-byte <c>[row:1][page:3]</c> pointer to the global released-pages usage map:
    /// pages released but not yet reusable, which ACE never allocates. Page 1 row 1 in every file ACE writes.</summary>
    public abstract int ReleasedPagesMapPointerOffset { get; }

    /// <summary>Offset of the 4-byte page number of the <c>MSysObjects</c> TDEF — the catalog root, the
    /// bootstrap pointer that lets the engine find the system catalog before it can read any table. It is
    /// the first of six system-table pointers (<c>MSysObjects</c>/<c>MSysACEs</c>/<c>MSysQueries</c>/
    /// <c>MSysRelationships</c>, then <c>MSysAccounts</c>/<c>MSysGroups</c> in a workgroup file); the others
    /// are reachable via the catalog itself. Verified: each value equals the object's <c>MSysObjects.Id</c>
    /// and the page it names is a TDEF.</summary>
    public abstract int CatalogRootPointerOffset { get; }

    /// <summary>Offset of the 4-byte page number of the <c>MSysACEs</c> TDEF.</summary>
    public abstract int AcesRootPointerOffset { get; }

    /// <summary>Offset of the 4-byte page number of the <c>MSysQueries</c> TDEF.</summary>
    public abstract int QueriesRootPointerOffset { get; }

    /// <summary>Offset of the 4-byte page number of the <c>MSysRelationships</c> TDEF.</summary>
    public abstract int RelationshipsRootPointerOffset { get; }

    /// <summary>Offset of the 4-byte page number of the <c>MSysAccounts</c> TDEF in a workgroup file; zero in an
    /// ordinary database, which has no such table.</summary>
    public abstract int AccountsRootPointerOffset { get; }

    /// <summary>Offset of the 4-byte page number of the <c>MSysGroups</c> TDEF in a workgroup file; zero in an
    /// ordinary database, which has no such table.</summary>
    public abstract int GroupsRootPointerOffset { get; }

    /// <summary>Offset of the 2-byte ANSI code page (LE): <c>0x04E4</c> = 1252, <c>0x04E2</c> = 1250.</summary>
    public abstract int CodePageOffset { get; }

    /// <summary>Offset of the 4-byte database (encryption) key; 0 when the database has no password.</summary>
    public abstract int DatabaseKeyOffset { get; }

    /// <summary>Offset of the database password field, additionally masked by a creation-date-derived value,
    /// so an empty password does not read as zeroes.</summary>
    public abstract int PasswordOffset { get; }

    /// <summary>Size of the database password field, UTF-16LE.</summary>
    public abstract int PasswordSize { get; }

    /// <summary>Offset of the 4-byte build number of the engine that created the file — stamped once and
    /// preserved across copy and compact, so it is not a constant (docs/format/page-00-database.md).</summary>
    public abstract int EngineBuildOffset { get; }

    /// <summary>Offset of the 4-byte default text collating sort order — a 32-bit Windows LCID whose
    /// otherwise-unused top byte carries the sort-order version. Byte for byte it mirrors a column
    /// descriptor's <see cref="ColumnLocaleOffset"/> block: LANGID (2 bytes LE, <c>0x0409</c> = 1033 en-US),
    /// then <see cref="CollationSortIdOffset"/>, then <see cref="CollationVersionOffset"/>.</summary>
    public abstract int CollationSortOrderOffset { get; }

    /// <summary>Offset of the collation's 1-byte sort id — the LCID's high word, which is what distinguishes
    /// an alternate sort order from its base locale (German Phone Book <c>0x00010407</c> vs German
    /// <c>0x00000407</c>; Hungarian Technical <c>0x0001040E</c> vs Hungarian <c>0x0000040E</c>).</summary>
    public abstract int CollationSortIdOffset { get; }

    /// <summary>Offset of the 1-byte collation sort-order version within the sort-order field (0/1).</summary>
    public abstract int CollationVersionOffset { get; }

    /// <summary>Offset of the 8-byte database creation timestamp: an OLE automation date
    /// (IEEE <c>double</c>, days from the 1899-12-30 epoch).</summary>
    public abstract int CreationDateOffset { get; }

    /// <summary>Offset of a 4-byte cleartext constant just past the masked window; undecoded.</summary>
    public abstract int PageZeroConstantOffset { get; }

    /// <summary>The value every file carries at <see cref="PageZeroConstantOffset"/>.</summary>
    public abstract int PageZeroConstant { get; }

    /// <summary>Offset of the 2-byte <c>EncryptionInfo</c> blob length (LE) — Access's "is this file
    /// encrypted?" signal, 0 when unencrypted. Only meaningful for an ACCDB.</summary>
    public abstract int EncryptionInfoLengthOffset { get; }

    /// <summary>Offset of the <c>EncryptionInfo</c> descriptor, <see cref="EncryptionInfoLengthOffset"/> bytes
    /// long: a binary Office "Standard" header or an XML Agile descriptor.</summary>
    public abstract int EncryptionInfoOffset { get; }

    /// <summary>Number of slots in the user commit-byte table: the exclusive-mode slot, then one per shared-mode
    /// user (docs/format/page-00-database.md §2.2).</summary>
    public abstract int CommitByteTableUsers { get; }

    /// <summary>Size of one commit-byte table slot.</summary>
    public abstract int CommitByteSlotSize { get; }

    /// <summary>Offset of the user commit-byte table, which runs to the end of page 0:
    /// <see cref="CommitByteTableUsers"/> slots of <see cref="CommitByteSlotSize"/> bytes, the first the
    /// exclusive-mode commit state. Nothing else may be written over it — an <c>EncryptionInfo</c> descriptor has
    /// to end before it.</summary>
    public abstract int CommitByteTableOffset { get; }

    // --- Data page layout ---

    /// <summary>Offset of the 2-byte free-space count on a data page.</summary>
    public abstract int DataFreeSpaceOffset { get; }

    /// <summary>Offset of the 4-byte owning-table TDEF page (or the "LVAL" marker on long-value pages).</summary>
    public abstract int DataOwnerOffset { get; }

    /// <summary>
    /// Offset of the 4-byte <b>chain stamp</b> on a data page. Zero on every page but the <b>first</b> of a
    /// multi-page long-value chain, where it must equal the pointing descriptor's own stamp or ACE refuses to
    /// materialise the value — see <c>docs/format/long-values.md</c>. Jet 3 has the row count where Jet 4 has
    /// this (which is why Jet 4's row count sits four bytes later), so a Jet 3 file has nowhere to put one.
    /// </summary>
    public abstract int DataChainStampOffset { get; }

    /// <summary>Offset of the 2-byte row count on a data page.</summary>
    public abstract int DataRowCountOffset { get; }

    /// <summary>Offset of the row-offset slot directory, <see cref="DataRowDirectoryEntrySize"/> bytes per row.</summary>
    public abstract int DataRowDirectoryOffset { get; }

    /// <summary>Size of one entry in the row-offset slot directory.</summary>
    public abstract int DataRowDirectoryEntrySize { get; }

    /// <summary>The row slot directory entry's offset bits; the bits above are its <see cref="RowSlotFlags"/>.</summary>
    public abstract int DataRowOffsetMask { get; }

    /// <summary>
    /// The most rows a data page may hold. Not a space limit — an index entry addresses a row as
    /// <c>page &lt;&lt; 8 | row</c> (page-03-04-index-btree.md §10.2), and a long-value descriptor its row in one
    /// byte too, so the slot number has to fit one byte.
    /// </summary>
    public abstract int MaxRowsPerPage { get; }

    /// <summary>Size of the column-count field at the start of a row record. The count (the highest column id ever
    /// handed out, plus one) also sets the width of the row's null bitmap: one bit per column id.</summary>
    public abstract int RowColumnCountSize { get; }

    /// <summary>Size of one entry of a row's variable-offset table — <c>numVar + 1</c> entries, end-first, the last
    /// the variable-data start.</summary>
    public abstract int RowVariableOffsetSize { get; }

    /// <summary>Size of a row's variable-column count (<c>numVar</c>), which follows its offset table.</summary>
    public abstract int RowVariableCountSize { get; }

    /// <summary>The largest record the engine will store, excluding anything that lives on LVAL pages.</summary>
    public abstract int MaxRecordSize { get; }

    // --- Table definition (TDEF) page header ---

    /// <summary>Offset of the 2-byte free-space count: the bytes still free in this page. (Before it, at 0x00,
    /// the 2-byte page type — <see cref="Pages.PageHeader"/>.)</summary>
    public abstract int TdefFreeSpaceOffset { get; }

    /// <summary>Offset of the 4-byte pointer to the next TDEF page (0 if the definition fits one page).</summary>
    public abstract int TdefNextPageOffset { get; }

    /// <summary>Offset of the 4-byte total TDEF definition length.</summary>
    public abstract int TdefLengthOffset { get; }

    /// <summary>Offset of the 4-byte TDEF record marker; see <see cref="TdefRecordMarker"/>.</summary>
    public abstract int TdefRecordMarkerOffset { get; }

    /// <summary>The 0x00000659 record marker written at the TDEF header (<see cref="TdefRecordMarkerOffset"/>),
    /// each column descriptor (+0x01) and each index-info block (+0x00). Access validates it; the reader ignores it.</summary>
    public const uint TdefRecordMarker = 0x00000659;

    /// <summary>Offset of the 4-byte row count.</summary>
    public abstract int TdefRowCountOffset { get; }

    /// <summary>Offset of the 4-byte highest-AutoNumber-assigned value (the last id used; next = +increment).</summary>
    public abstract int TdefLastAutoNumberOffset { get; }

    /// <summary>Offset of the 4-byte AutoNumber increment (default 1; a custom COUNTER sets it).</summary>
    public abstract int TdefAutoNumberIncrementOffset { get; }

    /// <summary>Offset of the 4-byte complex-type AutoNumber high-water (mdbtools <c>ct_autonum</c>) — the
    /// next id for a complex (multi-value/attachment) column. 0 for every table without such a column.</summary>
    public abstract int TdefComplexAutoNumberOffset { get; }

    /// <summary>Offset of the 1-byte table type (0x4E 'N' user, 0x53 'S' system).</summary>
    public abstract int TdefTableTypeOffset { get; }

    /// <summary>Offset of the 2-byte maximum-column-count high-water (the next column id to assign).</summary>
    public abstract int TdefMaxColumnsOffset { get; }

    /// <summary>Offset of the 2-byte variable-length column count.</summary>
    public abstract int TdefVariableColumnsOffset { get; }

    /// <summary>Offset of the 2-byte total column count.</summary>
    public abstract int TdefColumnCountOffset { get; }

    /// <summary>
    ///     Offset of the 4-byte <b>logical</b> index count — the §3.6 index-info blocks, one per named index.
    ///     Several may share a single data block: a relationship adds a logical block pointing at an index that
    ///     already exists, so a table many others reference accumulates these without gaining any B-tree.
    ///     <b>It does not size the statistics or index-data regions</b> — <see cref="TdefIndexCountOffset"/>
    ///     does. Both counts are capped at 32 (see <c>page-02d-constraints.md</c>).
    /// </summary>
    /// <remarks>
    ///     Named for the meaning rather than the offset's history, because the history is a trap: this was
    ///     previously <c>TdefRealIndexCountOffset</c>, which inverts the reference vocabulary. mdbtools calls
    ///     it <c>num_idx</c> ("number of logical indexes") and <see cref="TdefIndexCountOffset"/>
    ///     <c>num_real_idx</c>; Jackcess calls them <c>OFFSET_NUM_INDEX_SLOTS</c> and <c>OFFSET_NUM_INDEXES</c>.
    ///     "Real" belongs to the other one, not here.
    /// </remarks>
    public abstract int TdefLogicalIndexCountOffset { get; }

    /// <summary>
    ///     Offset of the 4-byte <b>real</b> index count — the §3.5 index-data blocks, one per B-tree actually
    ///     on disk. This is the count that sizes both the statistics block at
    ///     <see cref="TdefRealIndexBlockOffset"/> and the index-data blocks that follow the column names.
    ///     mdbtools calls it <c>num_real_idx</c>; Jackcess calls it <c>OFFSET_NUM_INDEXES</c>.
    /// </summary>
    public abstract int TdefIndexCountOffset { get; }

    /// <summary>Offset in a TDEF of the owned-pages usage-map pointer: 1 byte row, then a 3-byte page.</summary>
    public abstract int TdefOwnedPagesOffset { get; }

    /// <summary>Offset in a TDEF of the free-pages usage-map pointer (same 1-byte row + 3-byte page shape):
    /// the subset of the table's owned data pages that still have room for a row. Once earlier pages fill,
    /// Access leaves only the page it is currently appending to marked here.</summary>
    public abstract int TdefFreePagesOffset { get; }

    /// <summary>Offset where the real-index block begins; column descriptors follow it.</summary>
    public abstract int TdefRealIndexBlockOffset { get; }

    /// <summary>Size of the header that prefixes each TDEF continuation page's payload (also the free-space
    /// reserve the first page leaves for it).</summary>
    public abstract int TdefContinuationHeaderSize { get; }

    // --- TDEF limits ---

    /// <summary>The most columns a table can have, and one past the highest column id. The count and id fields are
    /// wider, so more could be written, but Access would refuse to open the table.</summary>
    public abstract int MaxColumnsPerTable { get; }

    /// <summary>
    /// The most indexes a table can have — and the cap applies to BOTH TDEF counts, the index-data blocks
    /// (<see cref="TdefIndexCountOffset"/>) and the logical index-info blocks
    /// (<see cref="TdefLogicalIndexCountOffset"/>). Microsoft states it against the logical one: "Number of
    /// indexes in a table: 32, including indexes created internally to maintain table relationships,
    /// single-field and composite indexes."
    /// </summary>
    /// <remarks>
    /// The logical count is the one that binds, because a data block must be named by a logical block, so the
    /// data count never exceeds it. A table many others reference gains a logical block per incoming
    /// relationship and no data block, so it overruns on the logical count while the data count still looks
    /// healthy. Building EF Core's <c>ComplexNavigationsSharedType</c> model, <c>Level1</c> reached 46 logical
    /// against 31 data, and the resulting file was unreadable by Access ("Unrecognized database format", the
    /// table missing entirely). Measured in <c>IndexCountLimitAccessTests</c>; see
    /// <c>docs/format/page-02d-constraints.md</c>.
    /// </remarks>
    public abstract int MaxIndexesPerTable { get; }

    /// <summary>The longest name a TDEF stores — a column's or an index's — in bytes as stored.</summary>
    public abstract int MaxNameBytes { get; }

    /// <summary>Size of the byte-length prefix on each TDEF name entry (column and index names alike); the text
    /// follows it.</summary>
    public abstract int TdefNameLengthSize { get; }

    // --- TDEF real-index block: one entry per real index, at TdefRealIndexBlockOffset ---

    /// <summary>Size in bytes of each real-index entry in the block before the column descriptors.</summary>
    public abstract int RealIndexEntrySize { get; }

    /// <summary>Offset in a real-index entry of its 4-byte total entry count (the row count).</summary>
    public abstract int RealIndexRowCountOffset { get; }

    /// <summary>Offset in a real-index entry of its 4-byte unique entry count — cumulative, never decremented by
    /// Access.</summary>
    public abstract int RealIndexUniqueCountOffset { get; }

    // --- TDEF column descriptor: one per column, after the real-index block (offsets within a descriptor) ---

    /// <summary>Size in bytes of one column descriptor.</summary>
    public abstract int ColumnDescriptorSize { get; }

    public abstract int ColumnTypeOffset { get; }

    /// <summary>Offset of the 2-byte low half of <see cref="TdefRecordMarker"/> in a column descriptor.
    /// Access needs it; the reader ignores it.</summary>
    public abstract int ColumnRecordMarkerOffset { get; }

    public abstract int ColumnNumberOffset { get; }

    /// <summary>Position among variable columns (0 for fixed).</summary>
    public abstract int ColumnVariableIndexOffset { get; }

    /// <summary>A second copy of the column id. Every creator writes it on a user table — ACE's SQL DDL, DAO's
    /// object model and DAO-executed SQL alike — while the engine's own bootstrap tables (MSysObjects and
    /// friends, and the f_&lt;GUID&gt; complex-column tables) leave it zero. It stops tracking
    /// <see cref="ColumnNumberOffset"/> after an ALTER COLUMN type change, which burns a new id there and leaves
    /// this at the old one (§3.8).</summary>
    public abstract int ColumnSecondaryNumberOffset { get; }

    /// <summary>Decimal/Numeric columns only.</summary>
    public abstract int ColumnPrecisionOffset { get; }

    /// <summary>Decimal/Numeric columns only.</summary>
    public abstract int ColumnScaleOffset { get; }

    /// <summary>
    /// Non-numeric columns use the precision/scale bytes and the two after them for the text collation, and
    /// the four bytes together are a 32-bit Windows LCID with the sort-order version in its otherwise-unused
    /// top byte: the LANGID, little-endian (0x0409 = General/en-US), at this offset; then
    /// <see cref="ColumnCollationSortIdOffset"/>; then <see cref="ColumnCollationVersionOffset"/>.
    /// </summary>
    public abstract int ColumnLocaleOffset { get; }

    /// <summary>The sort id — the high word of the LCID, which is what separates an alternate sort order from
    /// its base locale (German Phone Book = 0x00010407, Hungarian Technical = 0x0001040E).</summary>
    public abstract int ColumnCollationSortIdOffset { get; }

    /// <summary>The sort-order version (0 = the legacy compacted table, 1 = the Access 2010 NLS order).</summary>
    public abstract int ColumnCollationVersionOffset { get; }

    /// <summary>The column flag byte — <see cref="ColumnFlags"/>. Nullability is NOT in it (updatable is set
    /// on every column): a NOT NULL column is marked by a boolean <c>Required</c> property in the LvProp blob
    /// instead — see PropertyBlob / §11.</summary>
    public abstract int ColumnFlagsOffset { get; }

    /// <summary>The extended column flag byte — <see cref="ColumnExtendedFlags"/>.</summary>
    public abstract int ColumnExtendedFlagsOffset { get; }

    public abstract int ColumnFixedOffsetOffset { get; }
    public abstract int ColumnLengthOffset { get; }

    // --- TDEF index-data block (§3.5): one per real index, after the column names ---

    /// <summary>Size of one index-data block.</summary>
    public abstract int IndexDataBlockSize { get; }

    /// <summary>The 4-byte marker that opens an index-data block.</summary>
    public const uint IndexDataMarker = 0x783;

    /// <summary>Offset of the block's column slots — a fixed array of <see cref="IndexDataMaxColumns"/>, with no
    /// count field, so an index spans at most that many columns.</summary>
    public abstract int IndexDataColumnsOffset { get; }

    /// <summary>Number of column slots in an index-data block: the most columns an index can span.</summary>
    public abstract int IndexDataMaxColumns { get; }

    /// <summary>Size of one column slot: the 2-byte column id, then its <see cref="IndexColumnOrder"/> byte.</summary>
    public abstract int IndexDataColumnSlotSize { get; }

    /// <summary>Offset in a column slot of its <see cref="IndexColumnOrder"/> byte.</summary>
    public abstract int IndexDataColumnOrderOffset { get; }

    /// <summary>The column id in an unused slot (<c>0xFFFF</c>).</summary>
    public const short IndexDataColumnUnused = -1;

    /// <summary>Offset of the usage-map pointer for the index's own pages.</summary>
    public abstract int IndexDataUsageMapOffset { get; }

    /// <summary>Offset of the 4-byte B-tree root page.</summary>
    public abstract int IndexDataRootPageOffset { get; }

    /// <summary>Offset of the 2-byte <see cref="IndexAttributes"/>.</summary>
    public abstract int IndexDataFlagsOffset { get; }

    // --- TDEF index-info block (§3.6): one per logical index, linking a name to a data block ---

    /// <summary>Size of one index-info block.</summary>
    public abstract int IndexInfoBlockSize { get; }

    /// <summary>Offset of the block's <see cref="TdefRecordMarker"/>.</summary>
    public abstract int IndexInfoMarkerOffset { get; }

    /// <summary>Offset of the 4-byte logical index number (<c>index_num</c>).</summary>
    public abstract int IndexInfoNumberOffset { get; }

    /// <summary>Offset of the 4-byte number of the index-data block it uses (<c>index_num2</c>).</summary>
    public abstract int IndexInfoDataNumberOffset { get; }

    /// <summary>Offset of the <see cref="ForeignKeyType"/> byte.</summary>
    public abstract int IndexInfoFkTypeOffset { get; }

    /// <summary>Offset of the 4-byte <c>index_num</c> of the relationship's other end;
    /// <see cref="IndexInfoNoForeignKey"/> when there is none.</summary>
    public abstract int IndexInfoFkNumberOffset { get; }

    /// <summary>The <see cref="IndexInfoFkNumberOffset"/> value of an index that is not a relationship's.</summary>
    public const uint IndexInfoNoForeignKey = 0xFFFFFFFF;

    /// <summary>Offset of the 4-byte TDEF page of the relationship's other table; 0 when there is none.</summary>
    public abstract int IndexInfoFkTablePageOffset { get; }

    /// <summary>Offset of the update <see cref="RelationshipAction"/> byte.</summary>
    public abstract int IndexInfoUpdateActionOffset { get; }

    /// <summary>Offset of the delete <see cref="RelationshipAction"/> byte.</summary>
    public abstract int IndexInfoDeleteActionOffset { get; }

    /// <summary>Offset of the <see cref="IndexInfoType"/> byte.</summary>
    public abstract int IndexInfoTypeOffset { get; }

    // --- TDEF long-value map list: ends the definition, after the index names (§3.3.2) ---

    /// <summary>Size of one entry in the long-value column usage-map list that ends the definition, after the
    /// index names: the 2-byte column id, then the owned- and free-pages map pointers (§3.3.2).</summary>
    public abstract int TdefLongValueMapEntrySize { get; }

    /// <summary>Offset in a long-value map entry of its owned-pages map pointer.</summary>
    public abstract int TdefLongValueMapOwnedOffset { get; }

    /// <summary>Offset in a long-value map entry of its free-pages map pointer.</summary>
    public abstract int TdefLongValueMapFreeOffset { get; }

    /// <summary>The column id that ends the long-value map list — written even when the list is empty, and
    /// counted in the definition length.</summary>
    public const ushort TdefLongValueMapTerminator = 0xFFFF;

    // --- Long values (Memo / OLE / long binary): an in-row descriptor, and LVAL pages ---

    /// <summary>The owner field of a long-value page (<see cref="DataOwnerOffset"/>): ASCII "LVAL".</summary>
    public const uint LongValuePageMarker = 0x4C41564C;

    /// <summary>Bytes in an in-row long-value descriptor. Every consumer must have all of them before reading
    /// any field — the descriptor arrives as a row's variable chunk, so its width is whatever the offset table
    /// declared, not something the column guarantees. An inline value's payload follows it.</summary>
    public abstract int LongValueDescriptorSize { get; }

    /// <summary>The bits of the descriptor's first 4 bytes holding the value's length; the bits above are its
    /// <see cref="LongValueStore.StorageKind"/>.</summary>
    public abstract int LongValueLengthMask { get; }

    /// <summary>Offset in the descriptor of the record pointer to a single-page value's row, or to a chained
    /// value's first chunk.</summary>
    public abstract int LongValueDescriptorPointerOffset { get; }

    /// <summary>
    /// Offset in the descriptor of the 4-byte <b>chain stamp</b>, which must equal the
    /// <see cref="DataChainStampOffset"/> stamp of the first page of the chain it points at. Non-zero only on the
    /// chained form; ACE leaves it zero on inline and single-page values and checks it on neither. The value
    /// itself is arbitrary — ACE writes <c>GetTickCount()</c> and so does LibRed — because what it proves is that
    /// the descriptor and the chain came from the same write, not when. Patching either copy alone makes ACE
    /// refuse to materialise the value.
    /// </summary>
    public abstract int LongValueDescriptorChainStampOffset { get; }

    /// <summary>The largest payload kept inline in the row instead of being written to a long-value page.</summary>
    public abstract int LongValueMaxInline { get; }

    /// <summary>The largest value kept on a <b>single</b> LVAL page; anything longer is chained. The decision is
    /// made on the <b>uncompressed</b> length, as are the inline and chained ones; compression is applied to
    /// whatever form results, and a chained value is never compressed.</summary>
    public abstract int LongValueMaxSinglePage { get; }

    /// <summary>The largest LVAL page row: a single-page value up to this fits one row, and a chained value's
    /// chunk row is this size — a record pointer to the next chunk, then data.</summary>
    public abstract int LongValueMaxRowSize { get; }

    // --- Usage maps (§9): a record on a usage-map data page, inline or reference (UsageMapType) ---

    /// <summary>Offset in an inline record of its 4-byte start page — the page its bitmap's bit 0 stands for.</summary>
    public abstract int UsageMapStartPageOffset { get; }

    /// <summary>Size of an inline record's header (the type byte and the start page); the bitmap follows it.</summary>
    public abstract int UsageMapInlineHeaderSize { get; }

    /// <summary>Bitmap size of a full-width inline record: what a fresh map is written with, and the fixed
    /// window a free-pages map slides.</summary>
    public abstract int UsageMapInlineBitmapSize { get; }

    /// <summary>Size of a full-width inline record: its header and <see cref="UsageMapInlineBitmapSize"/>.</summary>
    public abstract int UsageMapInlineRecordSize { get; }

    /// <summary>The step an inline bitmap grows in.</summary>
    public abstract int UsageMapInlineGrowthSize { get; }

    /// <summary>Bytes the global maps' holder page keeps free: an inline global map that would leave less
    /// converts to reference form instead.</summary>
    public abstract int UsageMapHolderReserve { get; }

    /// <summary>Offset in a reference record of its 4-byte bitmap-page pointers.</summary>
    public abstract int UsageMapReferencePointersOffset { get; }

    /// <summary>Number of bitmap-page pointers in a reference record.</summary>
    public abstract int UsageMapReferenceSlots { get; }

    /// <summary>Size of a reference record: the type byte and <see cref="UsageMapReferenceSlots"/> pointers.</summary>
    public abstract int UsageMapReferenceRecordSize { get; }

    /// <summary>Bytes preceding the bitmap on a dedicated usage-bitmap page (type 0x0105).</summary>
    public abstract int UsageMapBitmapPageHeaderSize { get; }

    /// <summary>Number of pages one dedicated bitmap page covers; reference pointer <c>k</c> covers the range
    /// starting at <c>k</c> times this.</summary>
    public abstract int UsageMapPagesPerBitmapPage { get; }

    // --- Index page layout: a node (0x0103) or a leaf (0x0104), §10 ---

    /// <summary>Offset of the 2-byte free-space count on an index page.</summary>
    public abstract int IndexFreeSpaceOffset { get; }

    /// <summary>Offset of the 4-byte owning-table TDEF page on an index page.</summary>
    public abstract int IndexOwnerOffset { get; }

    /// <summary>Offset of a leaf's 4-byte link to the previous (lower-key) leaf.</summary>
    public abstract int IndexPrevPageOffset { get; }

    /// <summary>Offset of a leaf's 4-byte link to the next (higher-key) leaf — Access walks this for COUNT/scan.</summary>
    public abstract int IndexNextPageOffset { get; }

    /// <summary>Offset of a node's 4-byte child-tail page.</summary>
    public abstract int IndexChildTailOffset { get; }

    /// <summary>Offset of the 2-byte count of leading bytes every later entry shares with the first.</summary>
    public abstract int IndexCompressedByteCountOffset { get; }

    /// <summary>Offset of the level byte: 0 on a leaf, its height above the leaves on a node.</summary>
    public abstract int IndexLevelOffset { get; }

    /// <summary>Offset of the entry-position bitmask, whose set bits give the end of each entry within the
    /// entry-data region; it runs up to <see cref="IndexEntryDataOffset"/>.</summary>
    public abstract int IndexEntryMaskOffset { get; }

    /// <summary>Offset of the entry-data region.</summary>
    public abstract int IndexEntryDataOffset { get; }

    /// <summary>Size of the big-endian trailer ending every index entry: a leaf entry's row (page, then row) or a
    /// node entry's child page.</summary>
    public abstract int IndexEntryTrailerSize { get; }

    /// <summary>
    /// Sniffs the format version byte from page 0 of <paramref name="stream"/> and
    /// returns the matching format description. Restores the stream position.
    /// </summary>
    public static JetFormatBase Detect(Stream stream)
    {
        Span<byte> header = stackalloc byte[EngineVersionOffset + EngineVersionLength]; // through the 0x9C engine string
        // An empty or truncated file is not a database, and saying so here keeps the whole open path on one
        // exception contract — without this ReadExactly raises EndOfStreamException, which derives from
        // IOException and so shares no catchable base with the InvalidDataException everything else throws.
        if (stream.Length < header.Length)
            throw new InvalidDataException(
                $"The file is {stream.Length} bytes, too short to contain a Jet/ACE page-0 header ({header.Length} bytes).");

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

        byte version = ReadVersionByte(header);
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

    /// <summary>Reads the version byte at <see cref="VersionOffset"/> from a page-0 header.</summary>
    public static byte ReadVersionByte(ReadOnlySpan<byte> header) => header[VersionOffset];

    /// <summary>Writes page 0's version byte and the minor byte beside it — the inverse of
    /// <see cref="ReadVersionByte"/>, for a created file and for a version raise alike.</summary>
    internal void WriteVersion(Span<byte> page0, byte version, byte minor)
    {
        page0[VersionOffset] = version;
        page0[MinorVersionOffset] = minor;
    }

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