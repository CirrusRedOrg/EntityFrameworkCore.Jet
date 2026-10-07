using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace LibRed.Catalog;

/// <summary>
/// The table definition: its decoded metadata, raw layout, construction and page-chain read/write rules.
/// Catalog properties enrich this same object; storage and the engine consume it directly.
/// </summary>
public sealed class TableDefinition : Page
{
    internal TableDefinition() { }

    public string Name { get; init; } = string.Empty;

    /// <summary>Page number of the table's TDEF (definition) page.</summary>
    public int DefinitionPage
    {
        get => PageNumber;
        init => PageNumber = value;
    }

    /// <summary>CHECK constraints (name, expression), read from the table's extended-properties
    /// (<c>LvProp</c>) blob. Set by the catalog after the definition is decoded.</summary>
    public IReadOnlyList<(string Name, string Expression)> CheckConstraints
    {
        get => _checkConstraints;
        internal set => _checkConstraints = Array.AsReadOnly(value.ToArray());
    }

    private IReadOnlyList<(string Name, string Expression)> _checkConstraints = [];

    /// <summary>The table's <c>ValidationRule</c>/<c>ValidationText</c> designer properties, read from the
    /// extended-properties (<c>LvProp</c>) blob; null if none. Surfaced through
    /// <c>INFORMATION_SCHEMA.TABLES</c> (VALIDATION_RULE/VALIDATION_TEXT), matching EFCore.Jet's
    /// <c>AdoxSchema.GetTables</c> (<c>Jet OLEDB:Table Validation Rule/Text</c>).</summary>
    public string? ValidationRule { get; internal set; }

    /// <inheritdoc cref="ValidationRule"/>
    public string? ValidationText { get; internal set; }

    /// <summary>True for the MSys* system tables.</summary>
    public bool IsSystem { get; init; }

    /// <summary>The object's raw <c>MSysObjects.Flags</c>, kept as read so callers can classify an object the
    /// way Access does rather than by name — the system bit, the hidden bit and the bits that keep an object out
    /// of the schema rowsets altogether are all in here. Set by the catalog after the definition is decoded;
    /// none for a table built without one (a test's synthetic definition, say).</summary>
    public Formats.ObjectAttributes ObjectFlags { get; internal set; }

    /// <summary>
    /// One <see cref="IndexDef"/> per B-tree the table actually has: those with a root page, deduplicated by
    /// it. Several logical indexes can name one index-data block — a primary key that also backs a
    /// relationship is the everyday case — and they are the same tree, so anything that maintains index
    /// entries must visit it once. Doing it per <see cref="Indexes"/> entry writes the same key twice.
    /// </summary>
    public IEnumerable<IndexDef> RealIndexes =>
        Indexes.Where(i => i.RootPage > 0).GroupBy(i => i.RootPage).Select(g => g.First());

    public ColumnDef? FindColumn(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The column, or the error naming the table that is missing it — the partner of
    /// <see cref="JetCatalog.RequireTable"/>, for a column the caller's own correctness depends on rather than
    /// one the schema may or may not have.</summary>
    public ColumnDef RequireColumn(string name) =>
        FindColumn(name) ?? throw new InvalidOperationException($"'{Name}' is missing the '{name}' column.");
    private static readonly Encoding StrictUnicode = new UnicodeEncoding(
        bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private readonly List<ColumnDef> _columns = [];

    public override PageType Type => PageType.TableDefinition;

    /// <summary>Marks this definition released, preserving its header and definition bytes.</summary>
    internal static void MarkReleased(Span<byte> page) => PageHeader.WriteType(page, PageType.ReleasedTableDefinition);

    public int NextDefinitionPage { get; private set; }
    public int RowCount { get; internal set; }

    /// <summary>The complex-type AutoNumber high-water (header <c>0x1C</c>) — the next id for a complex
    /// (multi-value/attachment) column. Read and carried for faithful round-trip; 0 for every table without
    /// such a column (LibRed neither creates nor consumes complex columns).</summary>
    public int ComplexAutoNumber { get; internal set; }

    public TableType TableType { get; private set; }

    /// <summary>The column-id high-water (header <c>0x29</c>) — how many ids the table has handed out over its
    /// lifetime, which never decrements. It is what sizes a row's leading count and null bitmap, so it differs
    /// from <see cref="ColumnCount"/> for every table that has dropped a column or burned an id on a retype.</summary>
    public int ColumnIdHighWater { get; internal set; }

    public int VariableColumnCount { get; internal set; }
    public int ColumnCount { get; private set; }
    public int LogicalIndexCount { get; private set; }
    public int IndexCount { get; private set; }

    public IReadOnlyList<ColumnDef> Columns
    {
        get => _columnsView ??= _columns.AsReadOnly();
        init => _columns.AddRange(value);
    }

    private readonly List<IndexDef> _indexes = [];
    public IReadOnlyList<IndexDef> Indexes
    {
        get => _indexesView ??= _indexes.AsReadOnly();
        init => _indexes.AddRange(value);
    }

    private readonly List<LogicalIndexDef> _logicalIndexes = [];
    /// <summary>Every logical index, in the order the TDEF lists them — including the relationship names that
    /// share a real index with a named one, which <see cref="Indexes"/> keeps only one of.</summary>
    public IReadOnlyList<LogicalIndexDef> LogicalIndexes
    {
        get => _logicalIndexesView ??= _logicalIndexes.AsReadOnly();
        init => _logicalIndexes.AddRange(value);
    }

    private readonly Dictionary<int, (int Row, int Page)> _longValueOwnedMaps = [];
    /// <summary>Per long-value (memo/OLE) column id → its owned-pages usage-map pointer (record row +
    /// page), from the §3.3.2 list after the index names. Used to record a newly allocated LVAL page.</summary>
    public IReadOnlyDictionary<int, (int Row, int Page)> LongValueOwnedMaps =>
        _longValueOwnedView ??= new ReadOnlyDictionary<int, (int Row, int Page)>(_longValueOwnedMaps);

    private readonly Dictionary<int, (int Row, int Page)> _longValueFreeMaps = [];
    /// <summary>Per long-value column id → its free-pages usage-map pointer (LVAL pages with spare room).</summary>
    public IReadOnlyDictionary<int, (int Row, int Page)> LongValueFreeMaps =>
        _longValueFreeView ??= new ReadOnlyDictionary<int, (int Row, int Page)>(_longValueFreeMaps);

    private ReadOnlyCollection<ColumnDef>? _columnsView;
    private ReadOnlyCollection<IndexDef>? _indexesView;
    private ReadOnlyCollection<LogicalIndexDef>? _logicalIndexesView;
    private ReadOnlyDictionary<int, (int Row, int Page)>? _longValueOwnedView;
    private ReadOnlyDictionary<int, (int Row, int Page)>? _longValueFreeView;

    // Index structures follow the column names, in this order:
    //   IndexCount data blocks         : columns, flags, root page
    //   LogicalIndexCount info blocks  : links a name to a data block
    //   LogicalIndexCount names        : 2-byte length + UTF-16
    // A logical index may be a relationship (FK) sharing a data block with a real index. The block layouts are
    // the format's (JetFormatBase.IndexData* / IndexInfo*), shared with the writers.

    /// <summary>
    /// Reads a table definition starting at <paramref name="page"/>, transparently
    /// stitching continuation pages (wide tables whose definition spans multiple pages)
    /// into one contiguous buffer before parsing.
    /// </summary>
    internal void Read(PageChannel channel, int page)
    {
        (PageBuffer buffer, _) = ReadChain(channel, page);
        Read(buffer, channel.Format);
    }

    internal override void Read(PageBuffer buffer, JetFormatBase format)
    {
        if (buffer.Length < format.TdefRealIndexBlockOffset)
            throw new InvalidDataException(
                $"TDEF buffer is {buffer.Length} bytes; the fixed header requires {format.TdefRealIndexBlockOffset}.");
        int declaredLength = ReadLength(buffer.Span, format);
        if (declaredLength < format.TdefRealIndexBlockOffset || declaredLength > buffer.Length)
            throw new InvalidDataException(
                $"TDEF declares length {declaredLength}, outside the available {buffer.Length}-byte buffer.");
        if (declaredLength != buffer.Length)
            buffer = new PageBuffer(buffer.Data[..declaredLength], buffer.PageNumber);

        PageNumber = buffer.PageNumber;

        NextDefinitionPage = buffer.ReadInt32(format.TdefNextPageOffset);
        RowCount = ReadRowCount(buffer.Span, format);
        ComplexAutoNumber = ReadComplexAutoNumber(buffer.Span, format);
        TableType = (TableType)buffer.ReadByte(format.TdefTableTypeOffset);
        (ColumnIdHighWater, VariableColumnCount) = ReadHighWaters(buffer.Span, format);
        ColumnCount = buffer.ReadUInt16(format.TdefColumnCountOffset);
        LogicalIndexCount = buffer.ReadInt32(format.TdefLogicalIndexCountOffset);
        IndexCount = buffer.ReadInt32(format.TdefIndexCountOffset);

        int maxColumns = format.MaxColumnsPerTable, maxIndexes = format.MaxIndexesPerTable;
        if (ColumnCount > maxColumns)
            throw new InvalidDataException($"TDEF declares {ColumnCount} columns; a table can have at most {maxColumns}.");
        if (VariableColumnCount > maxColumns)
            throw new InvalidDataException(
                $"TDEF declares a variable-column high-water of {VariableColumnCount}; the maximum is {maxColumns}.");
        if (IndexCount < 0 || IndexCount > maxIndexes)
            throw new InvalidDataException($"TDEF declares {IndexCount} real indexes; the valid range is 0 through {maxIndexes}.");
        // Capped exactly as IndexCount is, and this is the check that matters: a table gains a logical block per
        // INCOMING relationship without gaining a data block, so it overruns here while 0x33 stays legal.
        // Previously only the sign was checked, which let a file written past the limit read back as sound - the
        // one shape where LibRed produces a database Access reports as an unrecognized format while seeing
        // nothing wrong with it itself.
        if (LogicalIndexCount < 0 || LogicalIndexCount > maxIndexes)
            throw new InvalidDataException(
                $"TDEF declares {LogicalIndexCount} logical indexes; the valid range is 0 through {maxIndexes}.");

        // The column descriptors follow a per-index block sized by the REAL index count at
        // 0x33 (IndexCount) — NOT the logical count at 0x2F (LogicalIndexCount). The two are
        // equal for MSysObjects but differ for user tables (e.g. logical=2, real=1).
        // The buffer here may already be a stitched multi-page definition (see Read(channel, page)).
        Regions regions = Regions.Of(buffer.Span, format);
        ReadColumns(buffer, format, regions.ColumnDescriptors);
        ReadIndexes(buffer, format, regions);
    }

    /// <summary>
    /// Parses the index structures following the column names into <see cref="IndexDef"/>s
    /// (one per index-data block): columns + sort order, unique/primary flags, root page,
    /// and the index name (resolved from the logical-index info blocks).
    /// </summary>
    private void ReadIndexes(PageBuffer buffer, JetFormatBase format, Regions regions)
    {
        _indexes.Clear();
        var byColumnId = _columns.ToDictionary(c => c.ColumnId);

        // 1. Index-data blocks (one IndexDef each): columns, unique flag, root page.
        for (int i = 0; i < IndexCount; i++)
        {
            int block = regions.DataBlocks + i * format.IndexDataBlockSize;

            // Per-index statistics live in the real-index entries at TdefRealIndexBlockOffset.
            int uniqueEntryCount = ReadIndexCounts(buffer.Span, format, i).Unique;

            var columns = new List<(ColumnDef Column, bool Ascending)>();
            for (int slot = 0; slot < format.IndexDataMaxColumns; slot++)
            {
                (short columnId, IndexColumnOrder order) =
                    ReadIndexSlot(buffer.Span.Slice(block, format.IndexDataBlockSize), format, slot);
                if (columnId == JetFormatBase.IndexDataColumnUnused) continue;
                // A used slot naming a column this table does not have is corruption, and a silent skip is the
                // worst answer: the index reads back over FEWER columns than it was built on, so its keys are
                // encoded differently from the ones on its pages and every seek quietly misses.
                if (!byColumnId.TryGetValue(columnId, out ColumnDef? column))
                    throw new InvalidDataException(
                        $"Index {i} of the table at page {buffer.PageNumber} names column id {columnId} in slot "
                        + $"{slot}, which the table does not have.");
                columns.Add((column, (order & IndexColumnOrder.Ascending) != 0));
            }

            var flags = (IndexAttributes)buffer.ReadUInt16(block + format.IndexDataFlagsOffset);
            _indexes.Add(new IndexDef
            {
                Name = string.Empty,
                Columns = columns,
                IsUnique = (flags & IndexAttributes.Unique) != 0,
                IgnoreNulls = (flags & IndexAttributes.IgnoreNulls) != 0,
                Required = (flags & IndexAttributes.Required) != 0,
                Flags = flags,
                IsPrimaryKey = false,
                UniqueEntryCount = uniqueEntryCount,
                RootPage = buffer.ReadInt32(block + format.IndexDataRootPageOffset),
                UsageMap = buffer.ReadRecordPointer(block + format.IndexDataUsageMapOffset),
                RealIndexOrdinal = i,
            });
        }

        int afterIndexNames = ResolveIndexNames(buffer, regions, format);
        ReadLongValueMaps(buffer, afterIndexNames, format);
    }

    /// <summary>Slot <paramref name="slot"/> of an index-data block: the column id it holds
    /// (<see cref="JetFormatBase.IndexDataColumnUnused"/> for none) and its order byte, as
    /// <see cref="WriteIndexSlot"/> writes them.</summary>
    internal static (short ColumnId, IndexColumnOrder Order) ReadIndexSlot(ReadOnlySpan<byte> block, JetFormatBase format, int slot)
    {
        int entry = format.IndexDataColumnsOffset + slot * format.IndexDataColumnSlotSize;
        return (BinaryPrimitives.ReadInt16LittleEndian(block.Slice(entry, sizeof(short))),
                (IndexColumnOrder)block[entry + format.IndexDataColumnOrderOffset]);
    }

    /// <summary>Where real index <paramref name="ordinal"/>'s entry starts in the real-index block that opens the
    /// definition.</summary>
    private static int RealIndexEntry(JetFormatBase format, int ordinal) =>
        format.TdefRealIndexBlockOffset + ordinal * format.RealIndexEntrySize;

    /// <summary>Real index <paramref name="ordinal"/>'s two statistics: its total entry count, and its unique count —
    /// cumulative, never decremented by Access on an insert.</summary>
    internal static (int Total, int Unique) ReadIndexCounts(ReadOnlySpan<byte> tdef, JetFormatBase format, int ordinal)
    {
        int entry = RealIndexEntry(format, ordinal);
        return (BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(entry + format.RealIndexRowCountOffset, sizeof(int))),
                BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(entry + format.RealIndexUniqueCountOffset, sizeof(int))));
    }

    /// <summary>Writes real index <paramref name="ordinal"/>'s two statistics — the inverse of
    /// <see cref="ReadIndexCounts"/>.</summary>
    internal static void WriteIndexCounts(Span<byte> tdef, JetFormatBase format, int ordinal, int total, int unique)
    {
        int entry = RealIndexEntry(format, ordinal);
        BinaryPrimitives.WriteInt32LittleEndian(tdef.Slice(entry + format.RealIndexRowCountOffset, sizeof(int)), total);
        BinaryPrimitives.WriteInt32LittleEndian(tdef.Slice(entry + format.RealIndexUniqueCountOffset, sizeof(int)), unique);
    }

    /// <summary>The definition's declared length (<see cref="JetFormatBase.TdefLengthOffset"/>), continuation pages
    /// included; written by <see cref="WriteCounts"/>. Unbounded — it comes out of the file.</summary>
    internal static int ReadLength(ReadOnlySpan<byte> tdef, JetFormatBase format) =>
        BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(format.TdefLengthOffset, sizeof(int)));

    /// <summary>The header's two id high-waters, neither of which ever decrements: column ids handed out
    /// (<c>0x29</c>, <see cref="ColumnIdHighWater"/>) and variable-table slots handed out (<c>0x2B</c>,
    /// <see cref="VariableColumnCount"/>).</summary>
    internal static (int ColumnIds, int VariableColumns) ReadHighWaters(ReadOnlySpan<byte> tdef, JetFormatBase format) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(tdef.Slice(format.TdefMaxColumnsOffset, sizeof(ushort))),
         BinaryPrimitives.ReadUInt16LittleEndian(tdef.Slice(format.TdefVariableColumnsOffset, sizeof(ushort))));

    /// <summary>Writes the header's two id high-waters — the inverse of <see cref="ReadHighWaters"/>.</summary>
    internal static void WriteHighWaters(Span<byte> tdef, JetFormatBase format, int columnIds, int variableColumns)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(tdef.Slice(format.TdefMaxColumnsOffset, sizeof(ushort)), (ushort)columnIds);
        BinaryPrimitives.WriteUInt16LittleEndian(tdef.Slice(format.TdefVariableColumnsOffset, sizeof(ushort)), (ushort)variableColumns);
    }

    /// <summary>The header's row count (<c>0x10</c>).</summary>
    internal static int ReadRowCount(ReadOnlySpan<byte> tdef, JetFormatBase format) =>
        BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(format.TdefRowCountOffset, sizeof(int)));

    /// <summary>Writes the header's row count — the inverse of <see cref="ReadRowCount"/>.</summary>
    internal static void WriteRowCount(Span<byte> tdef, JetFormatBase format, int count) =>
        BinaryPrimitives.WriteInt32LittleEndian(tdef.Slice(format.TdefRowCountOffset, sizeof(int)), count);

    /// <summary>The header's last-assigned AutoNumber (<c>0x14</c>): the next id is this plus the increment.</summary>
    internal static int ReadLastAutoNumber(ReadOnlySpan<byte> tdef, JetFormatBase format) =>
        BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(format.TdefLastAutoNumberOffset, sizeof(int)));

    /// <summary>Writes the header's last-assigned AutoNumber — the inverse of <see cref="ReadLastAutoNumber"/>.</summary>
    internal static void WriteLastAutoNumber(Span<byte> tdef, JetFormatBase format, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(tdef.Slice(format.TdefLastAutoNumberOffset, sizeof(int)), value);

    /// <summary>The header's complex-type AutoNumber high-water (<c>0x1C</c>).</summary>
    internal static int ReadComplexAutoNumber(ReadOnlySpan<byte> tdef, JetFormatBase format) =>
        BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(format.TdefComplexAutoNumberOffset, sizeof(int)));

    /// <summary>Writes the header's complex-type AutoNumber high-water — the inverse of
    /// <see cref="ReadComplexAutoNumber"/>.</summary>
    internal static void WriteComplexAutoNumber(Span<byte> tdef, JetFormatBase format, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(tdef.Slice(format.TdefComplexAutoNumberOffset, sizeof(int)), value);

    /// <summary>Bounds one variable-length TDEF region against the assembled definition, in <c>long</c> so a
    /// file-sourced count cannot overflow the multiply back into range. <see cref="Regions.Of"/> bounds every
    /// region with it, so a walk that reaches an index-data block to write it cannot land on the wrong block.</summary>
    internal static int CheckedRegionEnd(
        int start, int count, int itemSize, int bufferLength, string section)
    {
        long end = (long)start + (long)count * itemSize;
        if (start < 0 || count < 0 || end < start || end > bufferLength)
            throw new InvalidDataException(
                $"TDEF {section} extend past the assembled definition ({start} + {count} * {itemSize} > {bufferLength}).");
        return (int)end;
    }

    /// <summary>Parses the §3.3.2 long-value column usage-map list (after the index names): one entry
    /// {col_num:2, used_ptr:4, free_ptr:4} per memo/OLE column, then the terminator. Each pointer is a 1-byte
    /// record row + 3-byte page. Captures the owned- (used-pages) map pointer.</summary>
    private void ReadLongValueMaps(PageBuffer buffer, int pos, JetFormatBase format)
    {
        _longValueOwnedMaps.Clear();
        _longValueFreeMaps.Clear();
        var seen = new HashSet<int>();
        while (true)
        {
            EnsureAvailable(buffer, pos, sizeof(ushort), "long-value map terminator");
            int colNum = buffer.ReadUInt16(pos);
            if (colNum == JetFormatBase.TdefLongValueMapTerminator)
            {
                pos += sizeof(ushort);
                if (pos != buffer.Length)
                    throw new InvalidDataException(
                        $"TDEF has {buffer.Length - pos} trailing bytes after the long-value map terminator.");
                return;
            }

            EnsureAvailable(buffer, pos, format.TdefLongValueMapEntrySize, "long-value map entry");
            ColumnDef? column = _columns.FirstOrDefault(c => c.ColumnId == colNum);
            if (column is null)
                throw new InvalidDataException($"TDEF long-value map references unknown column id {colNum}.");
            // A calculated column also gets a map, whatever its declared type: its cached result is stored
            // as an envelope in the variable section and spills to an LVAL page when it outgrows the row.
            // ACE writes one for a calculated Memo declared as Text, which this guard used to reject —
            // and because the catalog loads every TDEF, that made the whole database unopenable.
            if (column.Type is not (JetDataType.Memo or JetDataType.Ole) && !column.IsCalculated)
                throw new InvalidDataException(
                    $"TDEF long-value map references non-long-value column '{column.Name}' ({column.Type}).");
            if (!seen.Add(colNum))
                throw new InvalidDataException($"TDEF contains duplicate long-value map entries for column id {colNum}.");

            column.HasLongValueMap = true;
            _longValueOwnedMaps[colNum] = buffer.ReadRecordPointer(pos + format.TdefLongValueMapOwnedOffset);
            _longValueFreeMaps[colNum] = buffer.ReadRecordPointer(pos + format.TdefLongValueMapFreeOffset);
            pos += format.TdefLongValueMapEntrySize;
        }
    }

    /// <summary>
    /// Reads the logical-index info blocks and their names, then attaches each name (and the
    /// primary-key flag) to the index-data block it references. A data block may be referenced
    /// by several logical indexes (e.g. a relationship plus the real index); the real index's
    /// name wins over a foreign-key relationship's.
    /// </summary>
    private int ResolveIndexNames(PageBuffer buffer, Regions regions, JetFormatBase format)
    {
        int namePos = regions.IndexNames;
        var priority = new int[_indexes.Count];
        _logicalIndexes.Clear();
        for (int i = 0; i < LogicalIndexCount; i++) // 0x2F — the logical-index (slot) count
        {
            (string name, namePos) = ReadName(buffer, namePos, "logical index", format);
            LogicalIndexSpec info = ReadInfoBlock(
                buffer.Span.Slice(regions.InfoBlocks + i * format.IndexInfoBlockSize, format.IndexInfoBlockSize), format, name);

            int dataNumber = info.DataOrdinal;
            bool isRelationship = info.FkTablePage != 0;
            // Every logical index names the data block that holds its columns and root page. One that names a
            // block the table does not have is corruption; skipping it used to make the index vanish from the
            // table's schema while its B-tree stayed on disk, maintained by nobody.
            if (dataNumber < 0 || dataNumber >= _indexes.Count)
                throw new InvalidDataException(
                    $"Logical index '{name}' of the table at page {buffer.PageNumber} names index-data block "
                    + $"{dataNumber}, and the table has {_indexes.Count}.");

            _logicalIndexes.Add(new LogicalIndexDef(name, dataNumber, isRelationship,
                !isRelationship && info.Type == IndexInfoType.Primary, info.FkType, info.UpdateAction, info.DeleteAction));

            // Prefer a real index name over a relationship's; prefer the primary among real ones.
            int p = isRelationship ? 1 : info.Type == IndexInfoType.Primary ? 3 : 2;
            if (p > priority[dataNumber])
            {
                priority[dataNumber] = p;
                _indexes[dataNumber] = _indexes[dataNumber] with
                {
                    Name = name,
                    IsPrimaryKey = !isRelationship && info.Type == IndexInfoType.Primary,
                };
            }
        }

        return namePos;
    }

    /// <summary>One index-info block (§3.6), the logical index called <paramref name="name"/> — every field
    /// <see cref="WriteInfoBlock"/> writes, so an edit reads a block here and writes it back there.</summary>
    internal static LogicalIndexSpec ReadInfoBlock(ReadOnlySpan<byte> block, JetFormatBase format, string name) =>
        new(Number: BinaryPrimitives.ReadInt32LittleEndian(block.Slice(format.IndexInfoNumberOffset, sizeof(int))),
            DataOrdinal: BinaryPrimitives.ReadInt32LittleEndian(block.Slice(format.IndexInfoDataNumberOffset, sizeof(int))),
            FkType: (ForeignKeyType)block[format.IndexInfoFkTypeOffset],
            FkNumber: BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(format.IndexInfoFkNumberOffset, sizeof(uint))),
            FkTablePage: BinaryPrimitives.ReadInt32LittleEndian(block.Slice(format.IndexInfoFkTablePageOffset, sizeof(int))),
            UpdateAction: (RelationshipAction)block[format.IndexInfoUpdateActionOffset],
            DeleteAction: (RelationshipAction)block[format.IndexInfoDeleteActionOffset],
            Type: (IndexInfoType)block[format.IndexInfoTypeOffset],
            Name: name);

    private void ReadColumns(PageBuffer buffer, JetFormatBase format, int columnBlock)
    {
        _columns.Clear();

        // Pass 1: fixed-size column descriptors.
        var descriptors = new (JetDataType Type, int ColumnId, ColumnFlags Flags, ColumnExtendedFlags ExtFlags, int FixedOffset, int Length, byte Precision, byte Scale, int VariableIndex, Collation Collation)[ColumnCount];
        var columnIds = new HashSet<int>();
        for (int i = 0; i < ColumnCount; i++)
        {
            int entry = columnBlock + i * format.ColumnDescriptorSize;
            var type = (JetDataType)buffer.ReadByte(entry + format.ColumnTypeOffset);
            int columnId = buffer.ReadUInt16(entry + format.ColumnNumberOffset);
            if (!Enum.IsDefined(type))
                throw new InvalidDataException($"TDEF column {i} has unknown type code 0x{(byte)type:X2}.");
            if (columnId >= format.MaxColumnsPerTable)
                throw new InvalidDataException(
                    $"TDEF column {i} has id {columnId}; valid ids are 0 through {format.MaxColumnsPerTable - 1}.");
            if (!columnIds.Add(columnId))
                throw new InvalidDataException($"TDEF contains duplicate column id {columnId}.");

            // Bytes 0x0B/0x0C are precision/scale for a Decimal/Numeric column, the MSysComplexColumns key for a
            // Complex one (page-02b §3.4), and the text-collation LANGID for everything else; 0x0D is the
            // collation's sort id and 0x0E its sort-order version. Read whichever applies — a complex column has
            // no collation to read.
            bool numeric = type == JetDataType.FixedPoint;
            bool complex = type == JetDataType.Complex;
            // The variable-table index is **stored** in the descriptor (0x07), not derived. Reading it
            // (rather than ranking column ids) is what lets a table with a **dropped column** decode:
            // ACE's DROP COLUMN removes a descriptor but does NOT renumber the survivors or rewrite
            // rows, so a survivor keeps its original variable index even though ranking would shift it.
            // 0x0B..0x0E are one 32-bit LCID with the sort-order version in the top byte: LANGID,
            // then the sort id at 0x0D (non-zero only for a Windows alternate sort order, e.g.
            // Hungarian Technical), then the version at 0x0E (0 = legacy table, 1 = Access-2010).
            descriptors[i] = (
                type,
                columnId,
                (ColumnFlags)buffer.ReadByte(entry + format.ColumnFlagsOffset),
                (ColumnExtendedFlags)buffer.ReadByte(entry + format.ColumnExtendedFlagsOffset),
                buffer.ReadUInt16(entry + format.ColumnFixedOffsetOffset),
                buffer.ReadUInt16(entry + format.ColumnLengthOffset),
                numeric ? buffer.ReadByte(entry + format.ColumnPrecisionOffset) : (byte)0,
                numeric ? buffer.ReadByte(entry + format.ColumnScaleOffset) : (byte)0,
                buffer.ReadUInt16(entry + format.ColumnVariableIndexOffset),
                numeric || complex ? Collation.GeneralLegacy
                    : new Collation((CollatingOrder)buffer.ReadUInt16(entry + format.ColumnLocaleOffset),
                        buffer.ReadByte(entry + format.ColumnCollationVersionOffset),
                        buffer.ReadByte(entry + format.ColumnCollationSortIdOffset)));
        }

        // Pass 2: column names, in the same order, immediately after the descriptor block.
        // Each name is a 2-byte (little-endian) byte length followed by UTF-16LE text.
        int namePos = columnBlock + ColumnCount * format.ColumnDescriptorSize;
        for (int i = 0; i < ColumnCount; i++)
        {
            (string name, namePos) = ReadName(buffer, namePos, "column", format);

            var d = descriptors[i];
            bool isFixed = (d.Flags & ColumnFlags.FixedLength) != 0;
            if ((!isFixed && d.VariableIndex >= VariableColumnCount)
                || (isFixed && d.VariableIndex > VariableColumnCount))
                throw new InvalidDataException(
                    $"TDEF column '{name}' has variable-table index {d.VariableIndex}, " +
                    $"outside high-water {VariableColumnCount}.");
            _columns.Add(new ColumnDef
            {
                Name = name,
                Type = d.Type,
                Index = i,
                ColumnId = d.ColumnId,
                Length = d.Length,
                FixedOffset = d.FixedOffset,
                VariableIndex = isFixed ? -1 : d.VariableIndex,
                // Byte 7 is stored on fixed columns too (the running count of preceding variable columns).
                VariableTableIndex = d.VariableIndex,
                IsFixedLength = isFixed,
                IsAutoNumber = (d.Flags & ColumnFlags.AutoNumber) != 0,
                // The user-column flag bits (0x0F: updatable/GUID-autonumber/hyperlink; 0x10: compressed-Unicode /
                // calculated). The catalog and attachment bits are only ever written, from a created column's spec.
                IsUpdatable = (d.Flags & ColumnFlags.Updatable) != 0,
                IsGuidAutoNumber = (d.Flags & ColumnFlags.GuidAutoNumber) != 0,
                IsHyperlink = (d.Flags & ColumnFlags.Hyperlink) != 0,
                SupportsCompressedUnicode = (d.ExtFlags & ColumnExtendedFlags.CompressedUnicode) != 0,
                IsCalculated = (d.ExtFlags & ColumnExtendedFlags.Calculated) != 0,
                Precision = d.Precision,
                Scale = d.Scale,
                Collation = d.Collation,
                // The descriptor's bytes as read, fields LibRed does not model included.
                RawDescriptor = buffer.Slice(columnBlock + i * format.ColumnDescriptorSize, format.ColumnDescriptorSize).ToArray(),
            });
        }

        // AutoNumber seed/increment from the TDEF header: 0x18 = increment, 0x14 = last-assigned value. On a
        // freshly created table the last value is Seed-Increment, so Seed = last + increment (matching what a
        // no-insert scaffold reports).
        //
        // At most one column draws on THAT pair — but it is not the only counter a table has. A complex column
        // carries the very same 0x04 flag and is allocated from 0x1C (ComplexAutoNumber), so a table can hold an
        // ordinary counter and any number of complex columns all reading IsAutoNumber (complex1.accdb's Table1
        // has five). Applying the header pair to those too reports a seed and increment that describe a
        // different counter, so skip them: their high-water is the table's ComplexAutoNumber.
        int increment = buffer.ReadInt32(format.TdefAutoNumberIncrementOffset);
        int lastAuto = ReadLastAutoNumber(buffer.Span, format);
        foreach (ColumnDef column in _columns)
            if (column.IsAutoNumber && column.Type != JetDataType.Complex)
            {
                // Zero is not a counter ACE will write: its own DDL refuses COUNTER(seed, 0) with "Invalid
                // argument", because every row would take the same id. A table carrying one is damaged, and
                // reading it as 1 — which this did — hides that behind a counter that looks ordinary.
                if (increment == 0)
                    throw new InvalidDataException(
                        $"The table at page {buffer.PageNumber} has AutoNumber column '{column.Name}' and an "
                        + "increment of 0, which would hand every row the same id.");
                column.Increment = increment;
                column.Seed = lastAuto + increment;
            }
    }

    /// <summary>Where the name entry at <paramref name="pos"/> ends: its byte length
    /// (<see cref="JetFormatBase.TdefNameLengthSize"/>), then that many bytes of UTF-16LE text. Bounded against
    /// <paramref name="tdef"/>, since the length comes out of the file.</summary>
    internal static int NameEntryEnd(ReadOnlySpan<byte> tdef, int pos, JetFormatBase format, string kind)
    {
        int text = CheckedRegionEnd(pos, 1, format.TdefNameLengthSize, tdef.Length, kind);
        return CheckedRegionEnd(text, 1,
            BinaryPrimitives.ReadUInt16LittleEndian(tdef.Slice(pos, format.TdefNameLengthSize)), tdef.Length, kind);
    }

    /// <summary>Reads the name entry at <paramref name="pos"/> and returns its text and where the entry
    /// ends.</summary>
    internal static (string Name, int Next) ReadName(PageBuffer buffer, int pos, string kind, JetFormatBase format)
    {
        int end = NameEntryEnd(buffer.Span, pos, format, kind);
        pos += format.TdefNameLengthSize;
        int byteLength = end - pos;
        if (byteLength == 0 || byteLength > format.MaxNameBytes || (byteLength & 1) != 0)
            throw new InvalidDataException(
                $"TDEF {kind} name has invalid UTF-16 byte length {byteLength}; expected an even value from 2 through {format.MaxNameBytes}.");
        try
        {
            return (StrictUnicode.GetString(buffer.Slice(pos, byteLength)), end);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException($"TDEF {kind} name is not valid UTF-16LE.", ex);
        }
    }

    private static void EnsureAvailable(PageBuffer buffer, int pos, int length, string section)
    {
        long end = (long)pos + length;
        if (pos < 0 || length < 0 || end > buffer.Length)
            throw new InvalidDataException(
                $"TDEF {section} extends past the declared definition ({pos} + {length} > {buffer.Length}).");
    }

    /// <summary>
    /// One logical index-info block (§3.6). Several logical indexes may share a data block: a plain
    /// index has one, and a relationship adds one that reuses this table's side of the foreign key.
    /// <paramref name="DataOrdinal"/> is the data-block index (<c>index_num2</c>); <paramref name="Number"/>
    /// is the logical id (<c>index_num</c>). For a relationship, <paramref name="FkType"/> says which end this
    /// is, <paramref name="FkNumber"/> is the other end's <c>index_num</c>, and <paramref name="FkTablePage"/>
    /// is the other table's TDEF page.
    /// </summary>
    internal sealed record LogicalIndexSpec(
        int Number,
        int DataOrdinal,
        ForeignKeyType FkType,
        uint FkNumber,
        int FkTablePage,
        RelationshipAction UpdateAction,
        RelationshipAction DeleteAction,
        IndexInfoType Type,
        string Name)
    {
        /// <summary>The block of a plain index — one that is not a relationship's.</summary>
        public static LogicalIndexSpec Plain(int number, int dataOrdinal, bool isPrimary, string name) =>
            new(number, dataOrdinal, ForeignKeyType.None, JetFormatBase.IndexInfoNoForeignKey, FkTablePage: 0,
                RelationshipAction.NotRelationship, RelationshipAction.NotRelationship,
                isPrimary ? IndexInfoType.Primary : IndexInfoType.Secondary, name);
    }

    internal sealed record Result(byte[] Page, IReadOnlyList<ColumnDef> Columns);

    /// <param name="collation">The <b>database's</b> collating order, which every non-numeric column this
    /// builds inherits — it is what decides how their index keys are encoded. Required, and deliberately not
    /// defaulted: the old <c>?? GeneralLegacy</c> fallback was correct only for a database that happens to use
    /// General-Legacy, and silently wrote v0 columns into a v1 file for every caller that forgot it.</param>
    /// <param name="format">The on-disk format to build for.</param>
    /// <param name="tableType">User or system.</param>
    /// <param name="specs">The columns, in creation order.</param>
    /// <param name="indexes">The table's indexes, or null for none.</param>
    /// <param name="longValueColumns">The memo/OLE columns' usage-map pointers.</param>
    /// <param name="logicalIndexes">Explicit logical-index blocks, or null to derive them from the indexes.</param>
    /// <param name="usageMapPage">The page holding the table's own usage maps — owned pages at row 0, free pages
    /// at row 1 — or null to leave the header's two map pointers zero.</param>
    internal static Result Build(
        JetFormatBase format,
        TableType tableType,
        IReadOnlyList<ColumnSpec> specs,
        Collation collation,
        IReadOnlyList<IndexSpec>? indexes = null,
        IReadOnlyList<LongValueColumnSpec>? longValueColumns = null,
        IReadOnlyList<LogicalIndexSpec>? logicalIndexes = null,
        int? usageMapPage = null)
    {
        indexes ??= [];
        longValueColumns ??= [];
        ValidateColumnSpecs(format, specs);
        // Jet/ACE caps a table at 32 indexes, counting those backing primary keys, unique constraints
        // and relationships (§3.5 index-data blocks, the 0x33 count). Reject rather than write a bad TDEF.
        if (indexes.Count > format.MaxIndexesPerTable)
            throw new NotSupportedException(
                $"Table has {indexes.Count} indexes; a table can have at most {format.MaxIndexesPerTable} (including those backing keys and relationships).");
        var columns = ResolveColumns(format, specs, collation);
        IReadOnlyList<LogicalIndexSpec> logical = logicalIndexes
            ?? indexes.Select((ix, i) => LogicalIndexSpec.Plain(i, i, ix.IsPrimaryKey, ix.Name)).ToList();
        ValidateIndexAndLongValueSpecs(format, columns, indexes, logical, longValueColumns);

        int definitionSize = DefinitionSize(format, columns, indexes, logical, longValueColumns);
        var page = new byte[Math.Max(format.PageSize, definitionSize)];

        PageHeader.WriteType(page, PageType.TableDefinition);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(format.TdefRecordMarkerOffset, 4), JetFormatBase.TdefRecordMarker);
        // AutoNumber (COUNTER) config lives in the TDEF header: 0x18 = increment (default 1), and 0x14 =
        // the last-assigned value initialized to Seed-Increment so the first insert yields Seed. A table has
        // at most one AutoNumber column; with none, these stay at the plain-counter defaults (increment 1,
        // last 0). Verified vs ACE (COUNTER(1000, 7) → 0x18=7, 0x14=993).
        // "At most one" is enforced, not assumed: the header holds a single seed/increment pair, so a second
        // AutoNumber column would be written with the 0x04 flag set and no counter configuration of its own —
        // two columns then claiming one header counter at insert. ALTER's promote path already refuses this;
        // CREATE silently took the first and ignored the rest.
        //
        // Complex columns are exempt, and are not a second claimant: they carry the same 0x04 flag but are
        // allocated from 0x1C, not from this pair. Counting them would refuse a table that has an ordinary
        // counter beside a complex column — complex1.accdb's Table1 has one of each kind — and could hand
        // `counter` a complex spec whose seed/increment describe nothing.
        var counters = specs.Where(s => s.IsAutoNumber && s.Type != JetDataType.Complex).ToList();
        if (counters.Count > 1)
            throw new NotSupportedException(
                $"A table may have only one AutoNumber column; {string.Join(", ", counters.Select(c => $"'{c.Name}'"))} are all declared as one.");
        ColumnSpec? counter = counters.FirstOrDefault();
        if (counter is { Increment: 0 }) throw ZeroIncrement(counter.Name);
        WriteCounter(page, format, counter?.Seed ?? 1, counter?.Increment ?? 1);
        // Complex-type AutoNumber high-water (0x1C): 0 for a new table, which has no complex column.
        WriteComplexAutoNumber(page, format, 0);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefNextPageOffset, 4), 0);
        WriteRowCount(page, format, 0);
        page[format.TdefTableTypeOffset] = (byte)tableType;
        if (usageMapPage is int maps)
        {
            PageBuffer.WriteRecordPointer(page, format.TdefOwnedPagesOffset, row: 0, maps);
            PageBuffer.WriteRecordPointer(page, format.TdefFreePagesOffset, row: 1, maps);
        }
        // The 0x29 high-water is the next column id to hand out = max existing id + 1. For contiguous ids this
        // equals the column count.
        int maxColumnId = columns.Select(c => c.ColumnId).DefaultIfEmpty(-1).Max();
        WriteHighWaters(page, format, maxColumnId + 1, columns.Count(c => !c.IsFixedLength));

        // The per-index statistics blocks (12 bytes each, one per data block) precede the columns.
        int columnBlock = format.TdefRealIndexBlockOffset + indexes.Count * format.RealIndexEntrySize;
        WriteColumnDescriptors(page, format, columns, columnBlock);
        int afterNames = WriteColumnNames(page, format, columns, columnBlock + columns.Count * format.ColumnDescriptorSize);

        int definitionEnd = WriteIndexes(page, format, columns, indexes, logical, longValueColumns, afterNames);
        if (definitionEnd != definitionSize)
            throw new InvalidOperationException(
                $"TDEF sizing preflight calculated {definitionSize} bytes but serialization wrote {definitionEnd}.");

        WriteCounts(page, format, columns.Count, indexes.Count, logical.Count, definitionEnd);
        // Remaining free space (Access reserves an 8-byte continuation header). For a multi-page definition the
        // caller recomputes the first page's free space, so clamp at 0 here.
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefFreeSpaceOffset, 2),
            (ushort)Math.Max(0, format.PageSize - definitionEnd - format.TdefContinuationHeaderSize));

        return new Result(page, columns);
    }

    /// <summary>The precision written to the descriptor. A declared 0 resolves to <b>18</b> — ACE's default for
    /// a bare <c>DECIMAL</c>, and <c>AccessTypeMapper</c>'s — because a 0 on disk leaves the value with no
    /// declared shape and ACE cannot materialise such a column (<c>AceCoreApiShapeProbeTests</c>,
    /// <c>decimal-precision-zero</c>). FixedPoint only: elsewhere those bytes are the LANGID.</summary>
    internal static byte EffectivePrecision(ColumnSpec spec) =>
        spec.Type == JetDataType.FixedPoint && spec.Precision == 0
            ? DefaultNumericPrecision
            : spec.Precision;

    /// <summary>What ACE declares for a bare <c>DECIMAL</c> or <c>NUMERIC</c> — measured, not assumed
    /// (<c>AceDecimalDeclarationProbeTest</c>), and the SQL front end's default too. ACE never writes precision
    /// 0: none of the 127 FixedPoint columns across 36 fixtures carries one.</summary>
    private const byte DefaultNumericPrecision = 18;

    /// <summary>A NUMERIC/DECIMAL column's declared precision and scale, which nothing else checked — the width
    /// check covers only the fixed 17 bytes. <c>AccessTypeMapper</c> enforces this on the SQL path; a direct
    /// Core caller bypasses it. Precision 0 means "unspecified" (see <see cref="EffectivePrecision"/>), not zero
    /// digits, so only a positively wrong declaration is refused.</summary>
    internal static void ValidateNumericPrecision(ColumnSpec spec)
    {
        if (spec.Type is not JetDataType.FixedPoint || spec.Precision == 0) return;

        if (spec.Precision > Storage.Types.JetTypeCodec.MaxNumericPrecision)
            throw new NotSupportedException(
                $"Column '{spec.Name}' declares NUMERIC precision {spec.Precision}; the maximum precision is "
                + $"{Storage.Types.JetTypeCodec.MaxNumericPrecision}.");

        if (spec.Scale > spec.Precision)
            throw new NotSupportedException(
                $"Column '{spec.Name}' declares NUMERIC scale {spec.Scale} under precision {spec.Precision}; "
                + "the scale cannot exceed the precision — that would be more digits after the decimal point "
                + "than the column has in total.");
    }

    private static void ValidateColumnSpecs(JetFormatBase format, IReadOnlyList<ColumnSpec> specs)
    {
        if (specs.Count > format.MaxColumnsPerTable)
            throw new NotSupportedException(
                $"Table has {specs.Count} columns; a table can have at most {format.MaxColumnsPerTable}.");

        var ids = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long fixedBytes = 0;
        int variableColumns = 0, highWater = -1;
        for (int i = 0; i < specs.Count; i++)
        {
            ColumnSpec spec = specs[i];
            ValidateNameLength(spec.Name, "Column", format);
            if (!names.Add(spec.Name))
                throw new NotSupportedException($"Column name '{spec.Name}' is used more than once.");
            int id = spec.ColumnId ?? i;
            if (id < 0 || id >= format.MaxColumnsPerTable)
                throw new NotSupportedException(
                    $"Column '{spec.Name}' has id {id}; column ids range from 0 through {format.MaxColumnsPerTable - 1}.");
            if (!ids.Add(id))
                throw new NotSupportedException($"Column id {id} is used more than once.");
            if (spec.Length is < 0 or > ushort.MaxValue)
                throw new NotSupportedException(
                    $"Column '{spec.Name}' has byte length {spec.Length}, which does not fit the TDEF field.");
            RowCodec.ValidateFieldWidth(spec.Name, spec.Type, spec.Length);
            JetDataTypeVersions.EnsureStorable(spec.Type, format.Version, spec.Name);
            ValidateNumericPrecision(spec);
            if (spec.IsFixedLength && spec.Type != JetDataType.Boolean)
                fixedBytes += spec.Length;
            if (!spec.IsFixedLength) variableColumns++;
            highWater = Math.Max(highWater, id);
        }

        // The fixed region used to be checked only against the TDEF's 2-byte offset fields (65535). ACE's real
        // limit is far tighter — the widest record the declaration allows must still be storable — and it
        // subsumes that one, since no column may now exceed 510 bytes. Without this a plain CreateTable of
        // 252 GUID columns writes a database Access will not open at all.
        RowCodec.ValidateRecordFits(null, (int)fixedBytes, variableColumns, highWater + 1, format);
    }

    private static void ValidateIndexAndLongValueSpecs(
        JetFormatBase format,
        IReadOnlyList<ColumnDef> columns,
        IReadOnlyList<IndexSpec> indexes,
        IReadOnlyList<LogicalIndexSpec> logical,
        IReadOnlyList<LongValueColumnSpec> longValueColumns)
    {
        if (logical.Count > format.MaxIndexesPerTable)
            throw new NotSupportedException(
                $"Table has {logical.Count} logical indexes; a table can have at most {format.MaxIndexesPerTable}.");

        var columnByName = columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (IndexSpec index in indexes)
        {
            ValidateNameLength(index.Name, "Index", format);
            // The index-data block (§3.5) has a fixed array of column slots and no count field, so an index —
            // hence any PRIMARY KEY / UNIQUE / FOREIGN KEY — spans at most that many columns. Reject an
            // over-wide index rather than silently truncating it.
            if (index.Columns.Count > format.IndexDataMaxColumns)
                throw new NotSupportedException(
                    $"Index '{index.Name}' spans {index.Columns.Count} columns; an index, and a key built on one, can span at most {format.IndexDataMaxColumns}.");
            foreach (string column in index.Columns)
                if (!columnByName.ContainsKey(column))
                    throw new NotSupportedException($"Index '{index.Name}' refers to unknown column '{column}'.");
            ValidateUsageMapPointer(index.UsageMapRow, index.UsageMapPage, $"index '{index.Name}'", allowNull: true);
        }
        // Duplicate index names, on the create path. ACE rejects them, and every downstream lookup resolves an
        // index by name, so two blocks sharing one would make DROP INDEX remove an arbitrary one of them. The
        // column list has had this check all along (see below); the index list had only a length check.
        var indexNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LogicalIndexSpec index in logical)
        {
            ValidateNameLength(index.Name, "Logical index", format);
            if (!indexNames.Add(index.Name))
                throw new NotSupportedException($"Index name '{index.Name}' is used more than once.");
        }

        var columnById = columns.ToDictionary(c => c.ColumnId);
        var seen = new HashSet<int>();
        foreach (LongValueColumnSpec value in longValueColumns)
        {
            if (!seen.Add(value.ColumnId))
                throw new NotSupportedException($"Long-value column id {value.ColumnId} has more than one usage-map entry.");
            // A calculated column with a Memo result is the exception: ACE declares it Text with length 0 and
            // still gives it long-value maps, so the declared type does not decide this. The reader had to
            // drop the same assumption — a guard that kept it rejected the TDEF, and since the catalog loads
            // every TDEF that made the whole database unopenable (§3.4a).
            if (!columnById.TryGetValue(value.ColumnId, out ColumnDef? column)
                || (column.Type is not (JetDataType.Memo or JetDataType.Ole) && !column.IsCalculated))
                throw new NotSupportedException(
                    $"Long-value usage-map entry {value.ColumnId} does not identify a Memo/OLE column.");
            ValidateUsageMapPointer(value.UsedRow, value.MapPage, $"long-value column '{column.Name}' owned map", allowNull: false);
            ValidateUsageMapPointer(value.FreeRow, value.MapPage, $"long-value column '{column.Name}' free map", allowNull: false);
        }
    }

    private static void ValidateUsageMapPointer(int row, int page, string owner, bool allowNull)
    {
        if (allowNull && row == 0 && page == 0) return;
        if (row is < 0 or > byte.MaxValue || page is <= 0 or > 0xFFFFFF)
            throw new NotSupportedException(
                $"The {owner} usage-map pointer ({row}, {page}) does not fit its 1-byte row / 3-byte page fields.");
    }

    private static void ValidateNameLength(string name, string kind, JetFormatBase format)
    {
        int length = Encoding.Unicode.GetByteCount(name);
        if (length == 0 || length > format.MaxNameBytes)
            throw new NotSupportedException(
                $"{kind} name is {length} UTF-16 bytes; a name must be 1 through {format.MaxNameBytes / 2} characters.");
    }

    private static int DefinitionSize(
        JetFormatBase format,
        List<ColumnDef> columns,
        IReadOnlyList<IndexSpec> indexes,
        IReadOnlyList<LogicalIndexSpec> logical,
        IReadOnlyList<LongValueColumnSpec> longValueColumns)
    {
        long size = format.TdefRealIndexBlockOffset
            + (long)indexes.Count * format.RealIndexEntrySize
            + (long)columns.Count * format.ColumnDescriptorSize
            + columns.Sum(c => (long)format.TdefNameLengthSize + Encoding.Unicode.GetByteCount(c.Name))
            + (long)indexes.Count * format.IndexDataBlockSize
            + (long)logical.Count * format.IndexInfoBlockSize
            + logical.Sum(i => (long)format.TdefNameLengthSize + Encoding.Unicode.GetByteCount(i.Name))
            + (long)longValueColumns.Count * format.TdefLongValueMapEntrySize
            + sizeof(ushort); // the terminator
        if (size > MaxDefinitionLength)
            throw new NotSupportedException(
                $"The serialized table definition requires {size} bytes; LibRed's validated TDEF budget is {MaxDefinitionLength}.");
        return (int)size;
    }

    /// <summary>Writes the index structures and returns the offset just past them (the definition end).</summary>
    private static int WriteIndexes(byte[] page, JetFormatBase format, List<ColumnDef> columns, IReadOnlyList<IndexSpec> indexes, IReadOnlyList<LogicalIndexSpec> logical, IReadOnlyList<LongValueColumnSpec> longValueColumns, int dataBlockStart)
    {
        var columnIdByName = columns.ToDictionary(c => c.Name, c => c.ColumnId, StringComparer.OrdinalIgnoreCase);

        // 1. Index-data blocks: columns, root page, unique flag.
        for (int i = 0; i < indexes.Count; i++)
        {
            IndexSpec index = indexes[i];
            BuildDataBlock(format, [.. index.Columns.Select(c => (columnIdByName[c], Ascending: true))],
                index.RootPage, index.UsageMapRow, index.UsageMapPage,
                index.IsUnique, ignoreNulls: false, required: index.IsPrimaryKey, complexColumn: false)
                .CopyTo(page, dataBlockStart + i * format.IndexDataBlockSize);
        }

        // 2. Index-info blocks (one per logical index) and 3. their names. Without explicit logical
        // specs each data block maps 1:1 to a plain info block (back-compat); with them, relationship
        // blocks are included and stored name-sorted (matching Access).
        int infoStart = dataBlockStart + indexes.Count * format.IndexDataBlockSize;
        for (int i = 0; i < logical.Count; i++)
            BuildInfoBlock(format, logical[i]).CopyTo(page, infoStart + i * format.IndexInfoBlockSize);

        int namePos = infoStart + logical.Count * format.IndexInfoBlockSize;
        foreach (LogicalIndexSpec li in logical)
        {
            byte[] entry = NameEntry(li.Name, format);
            entry.CopyTo(page, namePos);
            namePos += entry.Length;
        }

        // After the index names comes a per-long-value-column (memo/OLE) usage-map list (spec §3.3.2):
        // one entry {col_num:2, used_pages:4, free_pages:4} per column, in ascending column order, then the
        // terminator. Each pointer is a 1-byte usage-map row + 3-byte page.
        foreach (LongValueColumnSpec lv in longValueColumns.OrderBy(l => l.ColumnId))
        {
            LongValueMapEntry(format, lv.ColumnId, lv.UsedRow, lv.FreeRow, lv.MapPage).CopyTo(page, namePos);
            namePos += format.TdefLongValueMapEntrySize;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), JetFormatBase.TdefLongValueMapTerminator);
        namePos += sizeof(ushort);
        return namePos;
    }

    /// <summary>A fresh index-data block (§3.5): the marker, a slot per column with its order and the rest unused,
    /// the index's B-tree (<see cref="WriteIndexTree"/>), and its flags — always <see cref="IndexAttributes.AlwaysSet"/>,
    /// and the bit for each of <paramref name="unique"/>, <paramref name="ignoreNulls"/>, <paramref name="required"/>
    /// and <paramref name="complexColumn"/> (an index over a complex column carries it; every one Access writes does).
    /// </summary>
    internal static byte[] BuildDataBlock(JetFormatBase format, IReadOnlyList<(int Id, bool Ascending)> columns,
        int rootPage, int usageRow, int usagePage, bool unique, bool ignoreNulls, bool required, bool complexColumn)
    {
        var block = new byte[format.IndexDataBlockSize];
        BinaryPrimitives.WriteUInt32LittleEndian(block, JetFormatBase.IndexDataMarker);
        for (int slot = 0; slot < format.IndexDataMaxColumns; slot++)
        {
            if (slot < columns.Count)
                WriteIndexSlot(block, format, slot, columns[slot].Id,
                    columns[slot].Ascending ? IndexColumnOrder.Ascending : IndexColumnOrder.Descending);
            else
                WriteIndexSlot(block, format, slot, JetFormatBase.IndexDataColumnUnused, order: 0);
        }
        WriteIndexTree(block, format, rootPage, usageRow, usagePage);
        IndexAttributes flags = IndexAttributes.AlwaysSet
            | (unique ? IndexAttributes.Unique : IndexAttributes.None)
            | (ignoreNulls ? IndexAttributes.IgnoreNulls : IndexAttributes.None)
            | (required ? IndexAttributes.Required : IndexAttributes.None)
            | (complexColumn ? IndexAttributes.ComplexColumn : IndexAttributes.None);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(format.IndexDataFlagsOffset, sizeof(ushort)), (ushort)flags);
        return block;
    }

    /// <summary>Writes slot <paramref name="slot"/> of an index-data block: the column it holds
    /// (<see cref="JetFormatBase.IndexDataColumnUnused"/> for none) and its order byte — what
    /// <see cref="ReadIndexSlot"/> reads.</summary>
    internal static void WriteIndexSlot(Span<byte> block, JetFormatBase format, int slot, int columnId, IndexColumnOrder order)
    {
        int entry = format.IndexDataColumnsOffset + slot * format.IndexDataColumnSlotSize;
        BinaryPrimitives.WriteInt16LittleEndian(block.Slice(entry, sizeof(short)), (short)columnId);
        block[entry + format.IndexDataColumnOrderOffset] = (byte)order;
    }

    /// <summary>Writes where an index-data block's B-tree is: its root page, and the (row, page) pointer to the index's
    /// own pages usage map — in a fresh block, or over an existing one when an index is rebuilt onto a fresh root.</summary>
    internal static void WriteIndexTree(Span<byte> block, JetFormatBase format, int rootPage, int usageRow, int usagePage)
    {
        PageBuffer.WriteRecordPointer(block, format.IndexDataUsageMapOffset, usageRow, usagePage);
        BinaryPrimitives.WriteInt32LittleEndian(block.Slice(format.IndexDataRootPageOffset, sizeof(int)), rootPage);
    }

    /// <summary>A fresh index-info block (§3.6): the record marker, then every field of <paramref name="info"/>.</summary>
    internal static byte[] BuildInfoBlock(JetFormatBase format, LogicalIndexSpec info)
    {
        var block = new byte[format.IndexInfoBlockSize];
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(format.IndexInfoMarkerOffset, sizeof(uint)), JetFormatBase.TdefRecordMarker);
        WriteInfoBlock(block, format, info);
        return block;
    }

    /// <summary>Writes every field of <paramref name="info"/> into an index-info block but its name, which the TDEF
    /// stores apart, and the marker, which a file may carry as <c>0</c> (page-02d) and an edit must leave as it
    /// found it — as it does every byte the fields do not cover. <see cref="ReadInfoBlock"/>
    /// reads them back.</summary>
    internal static void WriteInfoBlock(Span<byte> block, JetFormatBase format, LogicalIndexSpec info)
    {
        BinaryPrimitives.WriteInt32LittleEndian(block.Slice(format.IndexInfoNumberOffset, sizeof(int)), info.Number);
        BinaryPrimitives.WriteInt32LittleEndian(block.Slice(format.IndexInfoDataNumberOffset, sizeof(int)), info.DataOrdinal);
        block[format.IndexInfoFkTypeOffset] = (byte)info.FkType;
        BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(format.IndexInfoFkNumberOffset, sizeof(uint)), info.FkNumber);
        BinaryPrimitives.WriteInt32LittleEndian(block.Slice(format.IndexInfoFkTablePageOffset, sizeof(int)), info.FkTablePage);
        block[format.IndexInfoUpdateActionOffset] = (byte)info.UpdateAction;
        block[format.IndexInfoDeleteActionOffset] = (byte)info.DeleteAction;
        block[format.IndexInfoTypeOffset] = (byte)info.Type;
    }


    private static List<ColumnDef> ResolveColumns(JetFormatBase format, IReadOnlyList<ColumnSpec> specs, Collation collation)
    {
        // A column's id is its declaration position unless the spec pins one explicitly (the catalog tables
        // JetDatabase writes). Variable columns are addressed in ascending column-id order.
        int EffectiveId(int i) => specs[i].ColumnId ?? i;

        var variableRank = new Dictionary<int, int>();
        int rank = 0;
        foreach (int i in Enumerable.Range(0, specs.Count).Where(i => !specs[i].IsFixedLength).OrderBy(EffectiveId))
            variableRank[i] = rank++;

        // Descriptor offset 7 ("variable-table index") = number of variable columns with a smaller column-id.
        // Access stores this on EVERY column, fixed ones included, where it is NOT 0 — measured on ACE's own
        // ADD COLUMN, which writes 2 for a LONG added to (K LONG, A TEXT, B TEXT). ACE does still read a file
        // that has 0 there, so this is parity rather than a hard requirement.
        int VarTableIndex(int i) =>
            Enumerable.Range(0, specs.Count).Count(j => !specs[j].IsFixedLength && EffectiveId(j) < EffectiveId(i));

        // Fixed-data offsets are assigned in ascending column-id order, NOT declaration order — Access lays the
        // fixed columns out by column id (e.g. MSysACEs stores ObjectId(id0) at offset 0 even though its
        // descriptor is written after ACM). Only differs from declaration order when ids are reordered (MSys*).
        bool Occupies(int i) => specs[i].IsFixedLength && specs[i].Type != JetDataType.Boolean;
        var fixedOffsets = new Dictionary<int, int>();
        int running = 0;
        foreach (int i in Enumerable.Range(0, specs.Count).Where(Occupies).OrderBy(EffectiveId))
        {
            fixedOffsets[i] = running;
            running += specs[i].Length;
        }

        var columns = new List<ColumnDef>(specs.Count);
        for (int i = 0; i < specs.Count; i++)
        {
            ColumnSpec s = specs[i];
            // Booleans live in the null bitmap and occupy no fixed-data bytes, so they don't
            // advance the fixed offset (matching how the row codec skips them).
            bool occupiesFixedData = s.IsFixedLength && s.Type != JetDataType.Boolean;
            columns.Add(new ColumnDef
            {
                Name = s.Name,
                Type = s.Type,
                Index = i,
                ColumnId = EffectiveId(i),
                Length = s.Length,
                FixedOffset = occupiesFixedData ? fixedOffsets[i] : 0,
                VariableIndex = s.IsFixedLength ? -1 : variableRank[i],
                VariableTableIndex = VarTableIndex(i),
                IsFixedLength = s.IsFixedLength,
                IsAutoNumber = s.IsAutoNumber,
                SupportsCompressedUnicode = s.SupportsCompressedUnicode,
                IsCalculated = s.CalculatedExpression is not null,
                CalculatedExpression = s.CalculatedExpression,
                CalculatedResultType = s.CalculatedResultType,
                SystemFlags = s.SystemFlags,
                IsEngineColumn = s.IsEngineColumn || s.SystemFlags != 0,
                ExtendedFlags = s.ExtendedFlags,
                Precision = EffectivePrecision(s),
                Scale = s.Scale,
                // Numeric columns carry no collation (their 0x0B/0x0C bytes are precision/scale); every
                // other column inherits the database's collating order.
                Collation = s.Type == JetDataType.FixedPoint ? Collation.GeneralLegacy : collation,
            });
        }
        return columns;
    }

    private static void WriteColumnDescriptors(byte[] page, JetFormatBase format, List<ColumnDef> columns, int columnBlock)
    {
        for (int i = 0; i < columns.Count; i++)
            BuildColumnDescriptor(columns[i], format).CopyTo(page.AsSpan(columnBlock + i * format.ColumnDescriptorSize));
    }

    /// <summary>Writes the TDEF header's counts of what the definition holds — columns (<c>0x2D</c>), index-data
    /// blocks (<c>0x33</c>) and logical indexes (<c>0x2F</c>) — and its length. The logical count may exceed the
    /// data-block count: a relationship adds a logical block that shares a data block. The one writer of these, for
    /// a definition built whole and for one reassembled after an ALTER. The <c>0x29</c> and <c>0x2B</c> high-waters
    /// are not counts — they never go down — and stay with whatever hands out ids.</summary>
    internal static void WriteCounts(Span<byte> tdef, JetFormatBase format, int columns, int dataIndexes,
        int logicalIndexes, int length)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(tdef.Slice(format.TdefColumnCountOffset, sizeof(ushort)), (ushort)columns);
        BinaryPrimitives.WriteInt32LittleEndian(tdef.Slice(format.TdefIndexCountOffset, sizeof(int)), dataIndexes);
        BinaryPrimitives.WriteInt32LittleEndian(tdef.Slice(format.TdefLogicalIndexCountOffset, sizeof(int)), logicalIndexes);
        BinaryPrimitives.WriteInt32LittleEndian(tdef.Slice(format.TdefLengthOffset, sizeof(int)), length);
    }

    /// <summary>Writes the TDEF header's AutoNumber pair so the next id assigned is <paramref name="seed"/>: the
    /// last-assigned value (<c>0x14</c>) as <paramref name="seed"/> − <paramref name="increment"/>, and the increment
    /// (<c>0x18</c>). A table with no AutoNumber column carries seed 1, increment 1 — last 0. The one writer of the
    /// pair, on create and on every ALTER that changes a counter.</summary>
    internal static void WriteCounter(Span<byte> tdef, JetFormatBase format, int seed, int increment)
    {
        WriteLastAutoNumber(tdef, format, seed - increment);
        BinaryPrimitives.WriteInt32LittleEndian(tdef.Slice(format.TdefAutoNumberIncrementOffset, sizeof(int)), increment);
    }

    /// <summary>An AutoNumber counting by zero would hand every row the same id, and ACE's own DDL refuses it
    /// ("Invalid argument" to <c>COUNTER(1, 0)</c>). An omitted increment is 1, all the way from the SQL
    /// layer's <c>IdentitySpec</c>, so a zero here is one the caller asked for.</summary>
    internal static NotSupportedException ZeroIncrement(string column) =>
        new($"AutoNumber column '{column}' cannot have an increment of 0: every row would take the same id. "
            + "ACE refuses the same COUNTER, and an omitted increment is 1.");

    /// <summary>Builds one column's fixed-size (25-byte Jet4) descriptor. Shared by CREATE TABLE and
    /// ALTER TABLE ADD COLUMN.</summary>
    internal static byte[] BuildColumnDescriptor(ColumnDef c, JetFormatBase format)
    {
        byte[] d = new byte[format.ColumnDescriptorSize];
        d[format.ColumnTypeOffset] = (byte)c.Type;
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnRecordMarkerOffset, 2), (ushort)JetFormatBase.TdefRecordMarker);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnNumberOffset, 2), (ushort)c.ColumnId);
        // Offset 0x09 repeats the id on a user column. Every creator does it — ACE's SQL DDL, DAO's object
        // model and DAO-executed SQL — and every user table in every fixture carries it, while only the
        // engine's own bootstrap tables (MSysObjects and friends) leave it zero, which is what a system
        // column keeps here. An earlier comment claimed real files store zero; that had been read off the
        // system tables alone.
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnSecondaryNumberOffset, 2),
            (ushort)(c.IsEngineColumn ? 0 : c.ColumnId));
        // Offset 7 = variable-table index (count of variable columns with a smaller id), stored on fixed columns
        // too. Prefer the precomputed value; fall back to the legacy rule (0 for fixed) when unset (ADD COLUMN).
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnVariableIndexOffset, 2),
            (ushort)(c.VariableTableIndex >= 0 ? c.VariableTableIndex : (c.IsFixedLength ? 0 : c.VariableIndex)));
        WriteLocaleUnion(d, c.Type, c.Precision, c.Scale, c.Collation, format);
        // Compose the flag byte (0x0F) from every bit a user column models, plus the catalog bits a created system
        // column asks for; likewise the extended-flag byte (0x10), plus the unmodelled bits an attachment's
        // value columns ask for.
        ColumnFlags flags =
            (c.IsUpdatable ? ColumnFlags.Updatable : ColumnFlags.None)
            | (c.IsFixedLength ? ColumnFlags.FixedLength : ColumnFlags.None)
            | (c.IsAutoNumber ? ColumnFlags.AutoNumber : ColumnFlags.None)
            | (c.IsGuidAutoNumber ? ColumnFlags.GuidAutoNumber : ColumnFlags.None)
            | (c.IsHyperlink ? ColumnFlags.Hyperlink : ColumnFlags.None)
            | (ColumnFlags)c.SystemFlags;
        d[format.ColumnFlagsOffset] = (byte)flags;

        ColumnExtendedFlags extFlags =
            (c.SupportsCompressedUnicode ? ColumnExtendedFlags.CompressedUnicode : ColumnExtendedFlags.None)
            | (c.IsCalculated ? ColumnExtendedFlags.Calculated : ColumnExtendedFlags.None)
            | (ColumnExtendedFlags)c.ExtendedFlags;
        d[format.ColumnExtendedFlagsOffset] = (byte)extFlags;

        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnFixedOffsetOffset, 2), (ushort)c.FixedOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnLengthOffset, 2), (ushort)c.Length);
        return d;
    }

    /// <summary>Writes descriptor bytes <c>0x0B</c>–<c>0x0E</c>, which are a union keyed by column type.
    /// Shared with the in-place ALTER (<c>SchemaEditor.EditTargetDescriptor</c>), which edits an existing
    /// descriptor in place: it must rewrite the whole union on every retype, or a column that stops being
    /// <c>DECIMAL</c> keeps its precision and scale sitting where the LANGID belongs — read back as a
    /// nonexistent collating order on what is now a text column.</summary>
    internal static void WriteLocaleUnion(
        Span<byte> d, JetDataType type, byte precision, byte scale, Collation collation, JetFormatBase format)
    {
        if (type == JetDataType.FixedPoint)
        {
            // Decimal/Numeric: precision and scale take the LANGID bytes. 0x0D/0x0E are left as they stand —
            // ACE does not clear them on a retype INTO decimal, so neither does LibRed.
            d[format.ColumnPrecisionOffset] = precision;
            d[format.ColumnScaleOffset] = scale;
        }
        else if (type == JetDataType.Complex)
        {
            // A complex (multi-value / attachment) column stores its MSysComplexColumns key here (page-02b
            // §3.4) — there is nothing to collate, since the values are rows of the flat table and carry their
            // own collations. Writing a LANGID over it severs the column from its values, so the bytes stand.
        }
        else if (type == JetDataType.DateTimeExtended)
        {
            // Date/Time Extended is 42 bytes of ASCII with nothing to collate, and ACE writes only the LOW
            // byte of the LANGID here, clearing the sublanguage half — the primary language id on its own,
            // with sort id and version zero. Measured across five collating orders: 0x0409 and 0x0809 both
            // give 0x0009, 0x0407 gives 0x0007, 0x040E 0x000E, 0x041D 0x001D, while a Text column in the
            // same table carries the full LANGID each time. (On an en-US database this looks like a
            // constant 0x0009, which is how it was first mis-read.)
            BinaryPrimitives.WriteUInt16LittleEndian(
                d.Slice(format.ColumnLocaleOffset, 2), (ushort)((ushort)collation.Order & 0x00FF));
            d[format.ColumnCollationSortIdOffset] = 0;
            d[format.ColumnCollationVersionOffset] = 0;
        }
        else
        {
            // Non-numeric columns use the precision/scale bytes (0x0B/0x0C) onward for the text collation:
            // 0x0B/0x0C LANGID, 0x0D sort id, 0x0E sort-order version. Together a 32-bit LCID with the version
            // in its unused top byte. General legacy is LANGID 1033 (0x0409), sort id 0, version 0.
            BinaryPrimitives.WriteUInt16LittleEndian(d.Slice(format.ColumnLocaleOffset, 2), (ushort)collation.Order);
            d[format.ColumnCollationSortIdOffset] = collation.SortId;
            d[format.ColumnCollationVersionOffset] = collation.Version;
        }
    }

    private static int WriteColumnNames(byte[] page, JetFormatBase format, List<ColumnDef> columns, int namePos)
    {
        foreach (ColumnDef c in columns)
        {
            byte[] entry = NameEntry(c.Name, format);
            entry.CopyTo(page, namePos);
            namePos += entry.Length;
        }
        return namePos;
    }

    /// <summary>One entry of the long-value usage-map list (§3.3.2): the column id, then the pointers to its owned-
    /// and free-pages maps, both on <paramref name="mapPage"/>. Read back by
    /// <see cref="LongValueOwnedMaps"/> and <see cref="LongValueFreeMaps"/>.</summary>
    internal static byte[] LongValueMapEntry(JetFormatBase format, int columnId, int usedRow, int freeRow, int mapPage)
    {
        var entry = new byte[format.TdefLongValueMapEntrySize];
        BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)columnId);
        PageBuffer.WriteRecordPointer(entry, format.TdefLongValueMapOwnedOffset, usedRow, mapPage);
        PageBuffer.WriteRecordPointer(entry, format.TdefLongValueMapFreeOffset, freeRow, mapPage);
        return entry;
    }

    /// <summary>One TDEF name entry, a column's or an index's: the text's byte length
    /// (<see cref="JetFormatBase.TdefNameLengthSize"/>), then the UTF-16LE text. Read back by
    /// <see cref="ReadName"/>.</summary>
    internal static byte[] NameEntry(string name, JetFormatBase format)
    {
        int size = format.TdefNameLengthSize;
        int length = Encoding.Unicode.GetByteCount(name);
        var entry = new byte[size + length];
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(0, size), (ushort)length);
        Encoding.Unicode.GetBytes(name, entry.AsSpan(size));
        return entry;
    }
    // A valid Jet/ACE table is constrained to 255 columns, 32 real indexes, and 64-character names.
    // One MiB is deliberately generous while still preventing a hostile 32-bit length from driving
    // process-scale allocation. This is an implementation safety budget, not an on-disk field width.
    internal const int MaxDefinitionLength = 1024 * 1024;

    internal static (PageBuffer Buffer, IReadOnlyList<int> ContinuationPages) ReadChain(
        PageChannel channel, int firstPage)
    {
        JetFormatBase format = channel.Format;
        ValidatePageNumber(channel, firstPage, "TDEF root");
        // Shared reads: each page is only checked and copied into the assembled buffer below, never written into or kept.
        PageBuffer first = channel.ReadPageShared(firstPage);
        if (PageHeader.ReadType(first.Span) != PageType.TableDefinition)
            throw new InvalidDataException(
                $"TDEF root page {firstPage} is type 0x{(ushort)PageHeader.ReadType(first.Span):X4}, expected 0x0102.");

        int definitionLength = ReadLength(first.Span, format);
        if (definitionLength < format.TdefRealIndexBlockOffset || definitionLength > MaxDefinitionLength)
            throw new InvalidDataException(
                $"TDEF page {firstPage} declares length {definitionLength}; supported validated range is " +
                $"{format.TdefRealIndexBlockOffset} through {MaxDefinitionLength} bytes.");

        // The chain holds the definition AND its 8-byte trailing reserve, which follows the last definition byte
        // and spills onto a page of its own when it does not fit — so a continuation page can carry no definition
        // bytes at all (verified vs ACE: a 4,090-byte definition has a continuation holding two reserve bytes).
        int pageSize = format.PageSize;
        int headerSize = format.TdefContinuationHeaderSize;
        int bodySize = pageSize - headerSize;
        int stored = definitionLength + headerSize;
        int continuationCount = stored <= pageSize
            ? 0
            : (stored - pageSize + bodySize - 1) / bodySize;

        int next = first.ReadInt32(format.TdefNextPageOffset);
        var continuationPages = new List<int>(continuationCount);
        var continuationBuffers = new List<PageBuffer>(continuationCount);
        var visited = new HashSet<int> { firstPage };

        for (int i = 0; i < continuationCount; i++)
        {
            if (next == 0)
                throw new InvalidDataException(
                    $"TDEF page {firstPage} ends after {i} continuation pages but its declared length requires {continuationCount}.");
            ValidatePageNumber(channel, next, "TDEF continuation");
            if (!visited.Add(next))
                throw new InvalidDataException($"TDEF page {firstPage} contains a continuation cycle at page {next}.");

            PageBuffer continuation = channel.ReadPageShared(next);
            if (PageHeader.ReadType(continuation.Span) != PageType.TableDefinition)
                throw new InvalidDataException(
                    $"TDEF continuation page {next} is type 0x{(ushort)PageHeader.ReadType(continuation.Span):X4}, expected 0x0102.");

            continuationPages.Add(next);
            continuationBuffers.Add(continuation);
            next = continuation.ReadInt32(format.TdefNextPageOffset);
        }

        if (next != 0)
            throw new InvalidDataException(
                $"TDEF page {firstPage} has more continuation pages than its declared length permits.");

        var assembled = new byte[definitionLength];
        int written = Math.Min(pageSize, definitionLength);
        first.Span[..written].CopyTo(assembled);
        foreach (PageBuffer continuation in continuationBuffers)
        {
            int take = Math.Min(bodySize, definitionLength - written);
            continuation.Span.Slice(headerSize, take)
                .CopyTo(assembled.AsSpan(written));
            written += take;
        }

        return (new PageBuffer(assembled, firstPage), continuationPages);
    }

    private static void ValidatePageNumber(PageChannel channel, int pageNumber, string role)
    {
        if (pageNumber <= 0 || pageNumber >= channel.PageCount)
            throw new InvalidDataException(
                $"{role} page {pageNumber} is outside the database's {channel.PageCount} pages.");
    }
    /// <summary>
    /// Where each variable-length region of a table definition begins (page-02a §3.3). Only the first has a fixed
    /// offset; every later one is found by stepping over all of its predecessors, including the column names,
    /// whose lengths come out of the file. Anything reaching into a TDEF it did not itself parse — adding an index
    /// or a relationship block, repointing a B-tree root, splitting the definition apart to rewrite it — needs the
    /// same walk, so they share this one.
    /// </summary>
    /// <param name="ColumnCount">Header <c>0x2D</c>: descriptors and names alike.</param>
    /// <param name="DataCount">Header <c>0x33</c>: the real index count, sizing both the statistics and the
    /// index-data blocks.</param>
    /// <param name="LogicalCount">Header <c>0x2F</c>: info blocks and index names.</param>
    /// <param name="Stats">Start of the per-index statistics — the one fixed offset, <c>0x3F</c>.</param>
    /// <param name="ColumnDescriptors">Start of the column descriptors; the names follow them.</param>
    /// <param name="DataBlocks">Start of the index-data blocks, past the column names.</param>
    /// <param name="InfoBlocks">Start of the logical index-info blocks.</param>
    /// <param name="IndexNames">Start of the index names, and so the end of the info blocks.</param>
    internal readonly record struct Regions(
        int ColumnCount,
        int DataCount,
        int LogicalCount,
        int Stats,
        int ColumnDescriptors,
        int DataBlocks,
        int InfoBlocks,
        int IndexNames)
    {
        /// <summary>Walks <paramref name="tdef"/> — the whole definition, continuation pages already stitched in,
        /// since the regions run straight across a page boundary.</summary>
        /// <remarks>
        /// Every region is bounded as it is crossed. The counts and name lengths are all file-sourced, and
        /// unchecked they carry the walk past the buffer — or, when a multiply overflows, back inside it at the
        /// wrong place, which lands a write on some other index's block with no error raised at all.
        /// </remarks>
        public static Regions Of(ReadOnlySpan<byte> tdef, JetFormatBase format)
        {
            int columnCount = BinaryPrimitives.ReadUInt16LittleEndian(tdef.Slice(format.TdefColumnCountOffset, 2));
            int dataCount = BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(format.TdefIndexCountOffset, 4));
            int logicalCount = BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(format.TdefLogicalIndexCountOffset, 4));

            int stats = format.TdefRealIndexBlockOffset;
            int descriptors = CheckedRegionEnd(
                stats, dataCount, format.RealIndexEntrySize, tdef.Length, "index statistics");
            int pos = CheckedRegionEnd(
                descriptors, columnCount, format.ColumnDescriptorSize, tdef.Length, "column descriptors");

            for (int i = 0; i < columnCount; i++)
                pos = NameEntryEnd(tdef, pos, format, "column");

            int dataBlocks = pos;
            int infoBlocks = CheckedRegionEnd(
                dataBlocks, dataCount, format.IndexDataBlockSize, tdef.Length, "index-data blocks");
            int indexNames = CheckedRegionEnd(
                infoBlocks, logicalCount, format.IndexInfoBlockSize, tdef.Length, "logical-index blocks");

            return new Regions(
                columnCount, dataCount, logicalCount, stats, descriptors, dataBlocks, infoBlocks, indexNames);
        }
    }
    internal sealed class Parts
    {
        public required byte[] Header;                          // [0, TdefRealIndexBlockOffset)
        public required List<byte[]> Stats;                     // one statistics entry per data index
        public required List<(byte[] Descriptor, byte[] Name)> Columns; // descriptor + its name, in descriptor order
        public required List<byte[]> DataBlocks;                // one index-data block per data index
        public required List<(byte[] Info, byte[] Name)> Logical; // info block + its name, name-sorted
        public required byte[] Lval;                            // §3.3.2 list + terminator
        public IReadOnlyList<int> Continuations = [];           // continuation-page numbers (multi-page TDEF)
    }

    internal static Parts ReadParts(PageChannel channel, int tdefPage)
    {
        JetFormatBase format = channel.Format;
        // Stitch any continuation pages into one contiguous buffer (offsets are absolute from page 1), so the
        // surgery below works the same for single- and multi-page definitions.
        (PageBuffer buf, IReadOnlyList<int> continuations) = ReadChain(channel, tdefPage);

        Regions regions = Regions.Of(buf.Span, format);
        int defEnd = ReadLength(buf.Span, format);

        var stats = new List<byte[]>(regions.DataCount);
        for (int i = 0; i < regions.DataCount; i++)
            stats.Add(buf.Slice(regions.Stats + i * format.RealIndexEntrySize, format.RealIndexEntrySize).ToArray());

        // Each descriptor with its name: the names follow the descriptors in the same order.
        var columns = new List<(byte[], byte[])>(regions.ColumnCount);
        int np = regions.ColumnDescriptors + regions.ColumnCount * format.ColumnDescriptorSize;
        for (int i = 0; i < regions.ColumnCount; i++)
        {
            byte[] descriptor = buf.Slice(regions.ColumnDescriptors + i * format.ColumnDescriptorSize, format.ColumnDescriptorSize).ToArray();
            int end = NameEntryEnd(buf.Span, np, format, "column");
            columns.Add((descriptor, buf.Slice(np, end - np).ToArray()));
            np = end;
        }

        var dataBlocks = new List<byte[]>(regions.DataCount);
        for (int i = 0; i < regions.DataCount; i++)
            dataBlocks.Add(buf.Slice(regions.DataBlocks + i * format.IndexDataBlockSize, format.IndexDataBlockSize).ToArray());

        // Each info block with its name, likewise.
        var logical = new List<(byte[], byte[])>(regions.LogicalCount);
        np = regions.IndexNames;
        for (int i = 0; i < regions.LogicalCount; i++)
        {
            byte[] info = buf.Slice(regions.InfoBlocks + i * format.IndexInfoBlockSize, format.IndexInfoBlockSize).ToArray();
            int end = NameEntryEnd(buf.Span, np, format, "logical index");
            logical.Add((info, buf.Slice(np, end - np).ToArray()));
            np = end;
        }

        return new Parts
        {
            Header = buf.Slice(0, regions.Stats).ToArray(),
            Stats = stats,
            Columns = columns,
            DataBlocks = dataBlocks,
            Logical = logical,
            Lval = buf.Slice(np, defEnd - np).ToArray(),
            Continuations = continuations,
        };
    }
    internal static void WriteParts(PageChannel channel, PageAllocator allocator, int tdefPage, Parts parts)
    {
        JetFormatBase format = channel.Format;
        var body = new List<byte>(format.PageSize);
        body.AddRange(parts.Header);
        foreach (byte[] s in parts.Stats) body.AddRange(s);
        foreach ((byte[] descriptor, _) in parts.Columns) body.AddRange(descriptor);
        foreach ((_, byte[] name) in parts.Columns) body.AddRange(name);
        foreach (byte[] d in parts.DataBlocks) body.AddRange(d);
        foreach ((byte[] info, _) in parts.Logical) body.AddRange(info);
        foreach ((_, byte[] nm) in parts.Logical) body.AddRange(nm);
        body.AddRange(parts.Lval);
        byte[] def = [.. body];
        int defEnd = def.Length;

        // The live column count, like the two index counts, is whatever the parts now hold.
        WriteCounts(def, format, parts.Columns.Count, parts.DataBlocks.Count, parts.Logical.Count, defEnd);

        // Write across the first page and continuation pages as needed (fresh ones, the old released) — handles a
        // definition that shrinks to one page, stays multi-page, or grows past a page (e.g. ADD COLUMN).
        WriteChain(channel, allocator, tdefPage, def, parts.Continuations, rewrite: true);

    }

    /// <summary>
    /// Writes a definition buffer across the first page and, if it overflows, continuation pages (each
    /// <c>[0x02][0x01][free:2][next:4]</c> then data). The first page carries the whole definition in its
    /// coordinate space; each continuation contributes <see cref="JetFormatBase.TdefContinuationHeaderSize"/>-offset data.
    /// <para>Rewriting an existing definition (<paramref name="rewrite"/>) is done as ACE does it (verified by
    /// whole-file diff, growing and shrinking): the first page is rewritten in place — alone, only the 8-byte
    /// reserve past the new end is zeroed and older bytes beyond it are left — and continuation data always goes
    /// to freshly allocated pages, while <paramref name="oldContinuations"/> are released untouched.</para>
    /// </summary>
    internal static void WriteChain(PageChannel channel, PageAllocator allocator, int firstPage, byte[] def,
        IReadOnlyList<int> oldContinuations, bool rewrite)
    {
        JetFormatBase format = channel.Format;
        int ps = format.PageSize;
        int nextOffset = format.TdefNextPageOffset;
        int headerSize = format.TdefContinuationHeaderSize;

        foreach (int old in oldContinuations)
            allocator.Release(old);   // reusable only after this handle closes, as ACE holds them

        if (def.Length + headerSize <= ps)
        {
            byte[] only = rewrite ? channel.ReadPage(firstPage).Span.ToArray() : new byte[ps];
            def.CopyTo(only, 0);
            only.AsSpan(def.Length, headerSize).Clear(); // the reserve
            BinaryPrimitives.WriteInt32LittleEndian(only.AsSpan(nextOffset, 4), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(only.AsSpan(format.TdefFreeSpaceOffset, 2), (ushort)(ps - def.Length - headerSize));
            channel.WritePage(firstPage, only);
            return;
        }

        // The chain holds the definition and then its 8-byte trailing reserve, as ACE lays it out (verified): every
        // page is filled before the next begins, and the reserve follows the last definition byte, spilling onto a
        // page of its own when it does not fit — so a continuation can hold reserve bytes and no definition. A
        // 4,090-byte definition fills page 1 with 4,090 bytes and six of the reserve, and its continuation holds the
        // other two, free 4,086. Each page's free space is what it has left once both are placed.
        int maxMiddle = ps - headerSize;
        int stored = def.Length + headerSize;
        var chunks = new List<(int Offset, int Length, int Free)>();
        for (int consumed = ps; consumed < stored;)
        {
            int placed = Math.Min(maxMiddle, stored - consumed);
            chunks.Add((consumed, Math.Clamp(def.Length - consumed, 0, placed), maxMiddle - placed));
            consumed += placed;
        }

        // ACE allocates the last continuation first (verified: from free pages 354.. a two-page continuation became
        // first → 355 → 354, and from the end of a file first → n+1 → n).
        var pageNumbers = new int[chunks.Count];
        for (int i = chunks.Count - 1; i >= 0; i--)
            pageNumbers[i] = allocator.Allocate();

        var page1 = new byte[ps];
        Array.Copy(def, 0, page1, 0, Math.Min(ps, def.Length)); // page 1 is completely full in a multi-page definition
        BinaryPrimitives.WriteInt32LittleEndian(page1.AsSpan(nextOffset, 4), pageNumbers[0]);
        BinaryPrimitives.WriteUInt16LittleEndian(page1.AsSpan(format.TdefFreeSpaceOffset, 2), 0);
        channel.WritePage(firstPage, page1);

        for (int i = 0; i < chunks.Count; i++)
        {
            var (offset, length, free) = chunks[i];
            var page = new byte[ps];
            PageHeader.WriteType(page, PageType.TableDefinition);
            if (length > 0) // a page holding only the reserve starts past the definition's end
                Array.Copy(def, offset, page, headerSize, length);
            int next = i + 1 < pageNumbers.Length ? pageNumbers[i + 1] : 0;
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(nextOffset, 4), next);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefFreeSpaceOffset, 2), (ushort)free);
            channel.WritePage(pageNumbers[i], page);
        }
    }

    /// <summary>Inserts a long-value (memo/OLE) column's §3.3.2 usage-map entry
    /// (<c>{col_num:2}{used row+page:4}{free row+page:4}</c>) just before the list's terminator.
    /// The new column has the highest id, so appending keeps the list in ascending column order.</summary>
    internal static void AddLongValueMapEntry(JetFormatBase format, Parts parts, int columnId, int usedRow, int freeRow, int mapPage)
    {
        int entrySize = format.TdefLongValueMapEntrySize;
        byte[] lval = parts.Lval;
        int at = lval.Length - sizeof(ushort); // before the terminator

        byte[] entry = LongValueMapEntry(format, columnId, usedRow, freeRow, mapPage);

        var result = new byte[lval.Length + entrySize];
        Array.Copy(lval, 0, result, 0, at);
        entry.CopyTo(result, at);
        Array.Copy(lval, at, result, at + entrySize, sizeof(ushort)); // the terminator
        parts.Lval = result;
    }

    /// <summary>Removes a long-value column's §3.3.2 usage-map entry from the list, keeping the other entries
    /// and the terminator. A no-op for a column without one.</summary>
    internal static void RemoveLongValueMapEntry(JetFormatBase format, Parts parts, int columnId)
    {
        int entrySize = format.TdefLongValueMapEntrySize;
        byte[] lval = parts.Lval;
        for (int at = 0; at + sizeof(ushort) < lval.Length; at += entrySize)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(lval.AsSpan(at, 2)) != columnId) continue;
            var result = new byte[lval.Length - entrySize];
            Array.Copy(lval, 0, result, 0, at);
            Array.Copy(lval, at + entrySize, result, at, lval.Length - at - entrySize);
            parts.Lval = result;
            return;
        }
    }

    /// <summary>Applies ACE's in-place column retype to the target descriptor within <paramref name="parts"/>
    /// (no page write — the caller writes the TDEF once): the target becomes a NEW column with a fresh id from the
    /// <c>0x29</c> high-water and its fixed data appended to the END of the current fixed region (its old slot left
    /// as dead space — ACE does not compact); <c>0x29</c> bumps, and <c>0x2B</c> too for a variable retype. Only
    /// the target descriptor changes; every other descriptor stays byte-identical. Returns the burned new id.</summary>
    internal static int EditTargetDescriptor(Parts parts, ColumnDef target, ColumnSpec newSpec, int fixedEnd,
        Collation collation, JetFormatBase format)
    {
        // ACE-only probe: with 254 columns one retype succeeds, the next fails; with 255
        // columns the first retype fails. A same-type ALTER does not reach this id-burning path.
        (int columnId, int varCount) = TakeColumnId(parts, newSpec.IsFixedLength,
            $"Cannot change the type of '{target.Name}'", format);

        Span<byte> d = parts.Columns[target.Index].Descriptor;
        d[format.ColumnTypeOffset] = (byte)newSpec.Type;
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnNumberOffset..], (ushort)columnId); // +0x05 id burned
        // The target's var-index (+0x07) becomes the old variable-column count — the next var slot — for BOTH a
        // fixed and a variable retype (verified vs ACE); a variable retype also bumps the 0x2B var-column count.
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnVariableIndexOffset..], (ushort)varCount);
        // +0x09 is deliberately left unchanged: it is the column's ordinal position, which a retype does not
        // move (verified: ACE does not update it).
        var flags = (ColumnFlags)d[format.ColumnFlagsOffset];
        flags = newSpec.IsFixedLength ? flags | ColumnFlags.FixedLength : flags & ~ColumnFlags.FixedLength;
        flags = newSpec.IsAutoNumber ? flags | ColumnFlags.AutoNumber : flags & ~ColumnFlags.AutoNumber;
        d[format.ColumnFlagsOffset] = (byte)flags;
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnFixedOffsetOffset..], (ushort)(newSpec.IsFixedLength ? fixedEnd : 0)); // +0x15
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnLengthOffset..], (ushort)newSpec.Length); // +0x17
        // 0x0B–0x0E is a union keyed by type, so the WHOLE union is rewritten, not just the decimal arm.
        // Writing precision/scale on the way in but nothing on the way out left a former DECIMAL(12,3) with
        // 0x0C 0x03 in its LANGID bytes, which reads back as collating order 0x030C on a text column.
        WriteLocaleUnion(d, newSpec.Type, EffectivePrecision(newSpec), newSpec.Scale, collation, format);
        return columnId;
    }

    /// <summary>Hands out the next column id from the <c>0x29</c> high-water and the next variable slot from the
    /// <c>0x2B</c> one, bumping <c>0x29</c> — and <c>0x2B</c> too for a variable column — in
    /// <paramref name="parts"/>' header; returns both as they were. Neither ever decrements on DROP COLUMN, so once
    /// every id has been handed out no column can be added or retyped, even when the live count is lower, until the
    /// database is compacted (which renumbers and reclaims dropped ids). ACE refuses exactly this, "Too many fields
    /// defined", rather than write an id it can't represent (verified); <paramref name="refusal"/> says what was
    /// refused.</summary>
    internal static (int ColumnId, int VariableIndex) TakeColumnId(Parts parts, bool isFixedLength, string refusal,
        JetFormatBase format)
    {
        (int columnId, int variableIndex) = ReadHighWaters(parts.Header, format);
        if (columnId >= format.MaxColumnsPerTable)
            throw new NotSupportedException(
                $"{refusal}: too many fields defined — {format.MaxColumnsPerTable} column ids have been used over this "
                + "table's lifetime, and dropped ids are only reclaimed by compacting the database.");

        WriteHighWaters(parts.Header, format, columnId + 1, isFixedLength ? variableIndex : variableIndex + 1);
        return (columnId, variableIndex);
    }

    /// <summary>Removes a data index (its stats + data block at <paramref name="removeDataOrdinal"/>,
    /// decrementing the data-ordinal reference of every remaining info block that pointed past it)
    /// and every logical block matching <paramref name="removeLogical"/> (with its name).</summary>
    internal static void RemoveIndexBlocks(JetFormatBase format, Parts parts, int? removeDataOrdinal, Func<(byte[] Info, byte[] Name), bool> removeLogical)
    {
        // The logical blocks go first, so what is left is exactly what has to survive the renumbering below.
        parts.Logical.RemoveAll(b => removeLogical(b));

        if (removeDataOrdinal is int ord)
        {
            // One data block can be named by more than one logical block — a primary key that also backs a
            // relationship is the everyday case — and the renumbering below only moves references PAST the
            // block being removed. A surviving reference EQUAL to it would silently come to name whichever
            // index slid into the slot: same table, same file, an index quietly pointing at another index's
            // B-tree. The callers guard against this by refusing to drop an index a relationship uses; this is
            // the structural check behind that, so a route that ever gets here says so instead of writing it.
            List<(byte[] Block, LogicalIndexSpec Info)> survivors =
                [.. parts.Logical.Select(b => (b.Info, ReadInfoBlock(b.Info, format, ReadName(new PageBuffer(b.Name, 0), 0, "logical index", format).Name)))];
            foreach ((_, LogicalIndexSpec info) in survivors)
                if (info.DataOrdinal == ord)
                    throw new InvalidOperationException(
                        $"Cannot remove index-data block {ord}: logical index '{info.Name}' still refers to it.");

            parts.Stats.RemoveAt(ord);
            parts.DataBlocks.RemoveAt(ord);
            foreach ((byte[] block, LogicalIndexSpec info) in survivors)
                if (info.DataOrdinal > ord)
                    WriteInfoBlock(block, format, info with { DataOrdinal = info.DataOrdinal - 1 });
        }
    }

    /// <summary>Maps an absolute definition offset to the page holding it and the offset within that page.</summary>
    private (int Page, int Offset) MapDefinitionOffset(PageChannel channel, IReadOnlyList<int> continuations, int offset)
    {
        int pageSize = channel.Format.PageSize;
        if (offset < pageSize) return (DefinitionPage, offset);

        int headerSize = channel.Format.TdefContinuationHeaderSize;
        int body = pageSize - headerSize;
        int relative = offset - pageSize;
        int index = relative / body;
        if (index >= continuations.Count)
            throw new InvalidOperationException(
                $"Definition offset {offset} lies past the end of table '{Name}'s definition chain.");
        return (continuations[index], headerSize + relative % body);
    }


    /// <summary>Writes <paramref name="bytes"/> at an absolute definition offset, splitting the write where it
    /// straddles a continuation-page boundary.</summary>
    internal void WriteIntoDefinition(PageChannel channel, IReadOnlyList<int> continuations, int offset, ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length;)
        {
            int pageNumber = MapDefinitionOffset(channel, continuations, offset + i).Page;
            byte[] page = channel.ReadPage(pageNumber).Span.ToArray();

            int j = i;
            for (; j < bytes.Length; j++)
            {
                (int target, int within) = MapDefinitionOffset(channel, continuations, offset + j);
                if (target != pageNumber) break;
                page[within] = bytes[j];
            }

            channel.WritePage(pageNumber, page);
            i = j;
        }
    }


}