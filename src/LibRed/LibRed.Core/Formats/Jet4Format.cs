namespace LibRed.Formats;

/// <summary>Access 2000–2003 (Jet 4.x) — 4 KB pages, MDB. Every ACE format derives from it: the layout is
/// the same (verified against real MDB and ACCDB files), and each ACE version overrides only what it changes.</summary>
internal class Jet4Format : JetFormatBase
{
    // --- File ---
    public override int PageSize => 4096;
    public override JetVersion Version => JetVersion.Version4;
    public override bool IsAccdb => false;

    // --- Page 0 ---
    public override int MinorVersionOffset => 0x15;
    public override int PageZeroHeaderMaskStart => 0x18;
    public override int PageZeroHeaderMaskLength => 128;
    public override ReadOnlySpan<byte> PageZeroHeaderMaskKey => [0xC7, 0xDA, 0x39, 0x6B];
    public override int FreePagesMapPointerOffset => 0x18;
    public override int ReleasedPagesMapPointerOffset => 0x1C;
    public override int CatalogRootPointerOffset => 0x20;
    public override int AcesRootPointerOffset => 0x24;
    public override int QueriesRootPointerOffset => 0x28;
    public override int RelationshipsRootPointerOffset => 0x2C;
    public override int AccountsRootPointerOffset => 0x30;
    public override int GroupsRootPointerOffset => 0x34;
    public override int CodePageOffset => 0x3C;
    public override int DatabaseKeyOffset => 0x3E;
    public override int PasswordOffset => 0x42;
    public override int PasswordSize => 40; // 20 UTF-16LE chars
    public override int EngineBuildOffset => 0x6A;
    public override int CollationSortOrderOffset => 0x6E;
    public override int CollationSortIdOffset => 0x70;
    public override int CollationVersionOffset => 0x71;
    public override int CreationDateOffset => 0x72;
    public override int PageZeroConstantOffset => 0x98;
    public override int PageZeroConstant => 0x00000654;
    public override int EncryptionInfoLengthOffset => 0x299;
    public override int EncryptionInfoOffset => 0x29B;
    public override int CommitByteTableUsers => 256;
    public override int CommitByteSlotSize => 2;
    public override int CommitByteTableOffset => PageSize - CommitByteTableUsers * CommitByteSlotSize; // 0xE00

    // --- Data page layout ---
    public override int DataFreeSpaceOffset => 0x02;
    public override int DataOwnerOffset => 0x04;
    public override int DataChainStampOffset => 0x08;
    public override int DataRowCountOffset => 0x0C;
    public override int DataRowDirectoryOffset => 0x0E;
    public override int DataRowDirectoryEntrySize => 2;
    public override int DataRowOffsetMask => 0x1FFF;

    // ACE itself writes at most 255 rows, one below what a byte can name, and LibRed matches ACE rather than the
    // pointer's maximum so its pages are shapes Access also produces. Only narrow rows reach it: a 4 KB page fits
    // this many once they are under about 14 bytes each. ACE's cap is not about space — a page it had filled to 255
    // still held 2,297 of its 4,096 bytes free — and on reaching it ACE drops the page from the table's free-pages
    // map exactly as if it were full. Exceeding it loses rows silently: every row past slot 255 is unaddressable by
    // any index, its entry aliasing a different row on another page, and ACE cannot see those rows at all — it
    // reads the full 16-bit row count but caps at 256 slots per page. Measured: LibRed wrote 900 rows as
    // 400/400/100 and read all 900 back, while ACE counted 612, exactly 256 + 256 + 100.
    public override int MaxRowsPerPage => 255;

    public override int RowColumnCountSize => 2;
    public override int RowVariableOffsetSize => 2;
    public override int RowVariableCountSize => 2;

    // 4060 bytes, where ACE raises "Record is too large." Measured across three table shapes whose row overhead
    // differs by 23 bytes (9, 12 and 20 text columns), and the total lands on 4060 every time, so it is a flat
    // cap rather than something derived from the row's layout (RecordSizeAccessTests). It is 20 bytes below
    // what the page could hold — 4096 less the 14-byte header and a 2-byte slot leaves 4080 — and that reserve
    // is not explained. Enforcing it is not optional politeness: a record between 4061 and 4080 fits the page
    // and LibRed used to write it happily, but ACE then cannot read the row, failing with an unrelated "another
    // user are attempting to change the same data" error.
    public override int MaxRecordSize => 4060;

    // --- Table definition (TDEF) page header (verified against a real ACCDB) ---
    public override int TdefFreeSpaceOffset => 0x02;
    public override int TdefNextPageOffset => 0x04;
    public override int TdefLengthOffset => 0x08;
    public override int TdefRecordMarkerOffset => 0x0C;
    public override int TdefRowCountOffset => 0x10;
    public override int TdefLastAutoNumberOffset => 0x14;
    public override int TdefAutoNumberIncrementOffset => 0x18;
    public override int TdefComplexAutoNumberOffset => 0x1C;
    public override int TdefTableTypeOffset => 0x28;
    public override int TdefMaxColumnsOffset => 0x29;
    public override int TdefVariableColumnsOffset => 0x2B;
    public override int TdefColumnCountOffset => 0x2D;
    public override int TdefLogicalIndexCountOffset => 0x2F;
    public override int TdefIndexCountOffset => 0x33;
    public override int TdefOwnedPagesOffset => 0x37;
    public override int TdefFreePagesOffset => 0x3B;
    public override int TdefRealIndexBlockOffset => 0x3F;
    public override int TdefContinuationHeaderSize => 8;

    // --- TDEF limits ---
    public override int MaxColumnsPerTable => 255;
    public override int MaxIndexesPerTable => 32;
    public override int MaxNameBytes => Catalog.JetName.MaxLength * 2; // 64 UTF-16 code units (verified Access limit)
    public override int TdefNameLengthSize => 2;

    // --- TDEF real-index block ---
    public override int RealIndexEntrySize => 12; // [+0] total entries, [+4] unique entries, [+8] reserved
    public override int RealIndexRowCountOffset => 0;
    public override int RealIndexUniqueCountOffset => 4;

    // --- TDEF column descriptor ---
    public override int ColumnDescriptorSize => 25;
    public override int ColumnTypeOffset => 0x00;
    public override int ColumnRecordMarkerOffset => 0x01;
    public override int ColumnNumberOffset => 0x05;
    public override int ColumnVariableIndexOffset => 0x07;
    public override int ColumnSecondaryNumberOffset => 0x09;
    public override int ColumnPrecisionOffset => 0x0B;
    public override int ColumnScaleOffset => 0x0C;
    public override int ColumnLocaleOffset => 0x0B;
    public override int ColumnCollationSortIdOffset => 0x0D;
    public override int ColumnCollationVersionOffset => 0x0E;
    public override int ColumnFlagsOffset => 0x0F;
    public override int ColumnExtendedFlagsOffset => 0x10;
    public override int ColumnFixedOffsetOffset => 0x15;
    public override int ColumnLengthOffset => 0x17;

    // --- TDEF index-data block ---
    public override int IndexDataBlockSize => 52;
    public override int IndexDataColumnsOffset => 0x04;
    public override int IndexDataMaxColumns => 10;
    public override int IndexDataColumnSlotSize => 3;
    public override int IndexDataColumnOrderOffset => 2;
    public override int IndexDataUsageMapOffset => 0x22;
    public override int IndexDataRootPageOffset => 0x26;
    public override int IndexDataFlagsOffset => 0x2E;

    // --- TDEF index-info block ---
    public override int IndexInfoBlockSize => 28;
    public override int IndexInfoMarkerOffset => 0x00;
    public override int IndexInfoNumberOffset => 0x04;
    public override int IndexInfoDataNumberOffset => 0x08;
    public override int IndexInfoFkTypeOffset => 0x0C;
    public override int IndexInfoFkNumberOffset => 0x0D;
    public override int IndexInfoFkTablePageOffset => 0x11;
    public override int IndexInfoUpdateActionOffset => 0x15;
    public override int IndexInfoDeleteActionOffset => 0x16;
    public override int IndexInfoTypeOffset => 0x17;

    // --- TDEF long-value map list ---
    public override int TdefLongValueMapEntrySize => 10;
    public override int TdefLongValueMapOwnedOffset => 2;
    public override int TdefLongValueMapFreeOffset => 6;

    // --- Long values ---
    public override int LongValueDescriptorSize => 12;

    // ACE accepted and fully read back 0x3FFFFFFF binary bytes, then rejected 0x40000000. The length carries
    // into the descriptor's byte 3; its storage bits must remain separate.
    public override int LongValueLengthMask => 0x3FFFFFFF;
    public override int LongValueDescriptorPointerOffset => 4;
    public override int LongValueDescriptorChainStampOffset => 8;
    public override int LongValueMaxInline => 64;

    // Measured (LongTextStorageAccessTests): 3816 bytes stays single-page, 3818 chains, for a plain and a WITH
    // COMPRESSION column alike. Distinct from LongValueMaxRowSize — conflating the two made LibRed keep 3817–4076
    // byte values on one page where ACE chains them. What fixes the boundary at 3816 is not established.
    public override int LongValueMaxSinglePage => 3816;

    // MAX_LONG_VALUE_ROW_SIZE (Jackcess): 4 bytes short of the page's usable space. Verified against ACE's own
    // chained OLE values (Northwind Employee photos: 4076, 4076, 2606-byte chunk rows).
    public override int LongValueMaxRowSize => 4076;

    // --- Usage maps ---
    public override int UsageMapStartPageOffset => 1;
    public override int UsageMapInlineHeaderSize => 5;

    // 512 pages. A movable window is exactly this, aligned to a 512-page boundary — verified against ACE
    // free-pages maps: the sole set bit at page 852/1227/1852/2852 sat in a 64-byte record whose startPage was
    // 512/1024/1536/2560, i.e. floor(page / 512) * 512.
    public override int UsageMapInlineBitmapSize => 64;
    public override int UsageMapInlineRecordSize => UsageMapInlineHeaderSize + UsageMapInlineBitmapSize;

    // Verified against owned-map record lengths on a 255-column ACE table whose data pages start at 353:
    // 8,000 rows → 1053, 12,000 → 1553, 30,000 → 3801, i.e. 5 + roundUp(ceil((353 + rows) / 8), 4) exactly.
    // (A 32-byte chunk would give 1056 / 1568 / 3808.) Overshooting spends the usage-map page's remaining room
    // and converts the map to reference type earlier than Access would.
    public override int UsageMapInlineGrowthSize => 4;

    // Measured against ACE (GlobalMapGrowthTests).
    public override int UsageMapHolderReserve => 4;

    public override int UsageMapReferencePointersOffset => 1;

    // Not arbitrary: each bitmap page covers (pageSize - 4) * 8 = 32,736 pages, so 17 slots span ~2.28 GB —
    // just past Jet's 2 GB file ceiling. Verified against an ACE-built 134 MB table.
    public override int UsageMapReferenceSlots => 17;
    public override int UsageMapReferenceRecordSize => UsageMapReferencePointersOffset + UsageMapReferenceSlots * sizeof(int);
    public override int UsageMapBitmapPageHeaderSize => 4; // type + flags + 2 unused
    public override int UsageMapPagesPerBitmapPage => (PageSize - UsageMapBitmapPageHeaderSize) * 8;

    // --- Index page layout ---
    public override int IndexFreeSpaceOffset => 0x02;
    public override int IndexOwnerOffset => 0x04;
    public override int IndexPrevPageOffset => 0x0C;
    public override int IndexNextPageOffset => 0x10;
    public override int IndexChildTailOffset => 0x14;
    public override int IndexCompressedByteCountOffset => 0x18;
    public override int IndexLevelOffset => 0x1A;
    public override int IndexEntryMaskOffset => 0x1B;
    public override int IndexEntryDataOffset => 0x1E0;
    public override int IndexEntryTrailerSize => 4;
}