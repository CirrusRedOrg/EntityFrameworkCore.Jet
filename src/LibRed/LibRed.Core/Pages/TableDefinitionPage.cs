using System.Text;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;

namespace LibRed.Pages;

/// <summary>
/// A table definition (TDEF) page: row count, table type, and the column descriptors
/// and names. Verified against the Jet 4 / ACE layout. May be continued across pages
/// for wide tables (see <see cref="NextDefinitionPage"/>).
/// </summary>
public sealed class TableDefinitionPage : Page
{
    private const int MaxColumnsPerTable = 255;
    private const int MaxIndexesPerTable = 32;
    private const int MaxNameBytes = JetName.MaxLength * 2;
    private static readonly Encoding StrictUnicode = new UnicodeEncoding(
        bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private readonly List<ColumnDef> _columns = [];

    public override PageType Type => PageType.TableDefinition;

    public int NextDefinitionPage { get; private set; }
    public int RowCount { get; private set; }

    /// <summary>The complex-type AutoNumber high-water (header <c>0x1C</c>) — the next id for a complex
    /// (multi-value/attachment) column. Read and carried for faithful round-trip; 0 for every table without
    /// such a column (LibRed neither creates nor consumes complex columns).</summary>
    public int ComplexAutoNumber { get; private set; }

    public TableType TableType { get; private set; }
    public int VariableColumnCount { get; private set; }
    public int ColumnCount { get; private set; }
    public int LogicalIndexCount { get; private set; }
    public int IndexCount { get; private set; }

    public IReadOnlyList<ColumnDef> Columns => _columns;

    private readonly List<IndexDef> _indexes = [];
    public IReadOnlyList<IndexDef> Indexes => _indexes;

    private readonly Dictionary<int, (int Row, int Page)> _longValueOwnedMaps = [];
    /// <summary>Per long-value (memo/OLE) column id → its owned-pages usage-map pointer (record row +
    /// page), from the §3.3.2 list after the index names. Used to record a newly allocated LVAL page.</summary>
    public IReadOnlyDictionary<int, (int Row, int Page)> LongValueOwnedMaps => _longValueOwnedMaps;

    private readonly Dictionary<int, (int Row, int Page)> _longValueFreeMaps = [];
    /// <summary>Per long-value column id → its free-pages usage-map pointer (LVAL pages with spare room).</summary>
    public IReadOnlyDictionary<int, (int Row, int Page)> LongValueFreeMaps => _longValueFreeMaps;

    // Index structures (Jet 4 / ACE) follow the column names, in this order:
    //   IndexCount  (0x33) data blocks  : 52 bytes each — columns, flags, root page
    //   LogicalIndexCount (0x2F) info blocks : 28 bytes each — links a name to a data block
    //   LogicalIndexCount names         : 2-byte length + UTF-16
    // A logical index may be a relationship (FK) sharing a data block with a real index.
    // Index-block layout and flag values are shared with the writers via IndexBlockFormat / IndexFlags.

    /// <summary>
    /// Reads a table definition starting at <paramref name="page"/>, transparently
    /// stitching continuation pages (wide tables whose definition spans multiple pages)
    /// into one contiguous buffer before parsing.
    /// </summary>
    public void Read(PageChannel channel, int page)
    {
        (PageBuffer buffer, _) = TdefChainReader.Read(channel, page);
        Read(buffer, channel.Format);
    }

    public override void Read(PageBuffer buffer, JetFormatBase format)
    {
        if (buffer.Length < format.TdefRealIndexBlockOffset)
            throw new InvalidDataException(
                $"TDEF buffer is {buffer.Length} bytes; the fixed header requires {format.TdefRealIndexBlockOffset}.");
        int declaredLength = buffer.ReadInt32(format.TdefLengthOffset);
        if (declaredLength < format.TdefRealIndexBlockOffset || declaredLength > buffer.Length)
            throw new InvalidDataException(
                $"TDEF declares length {declaredLength}, outside the available {buffer.Length}-byte buffer.");
        if (declaredLength != buffer.Length)
            buffer = new PageBuffer(buffer.Data[..declaredLength], buffer.PageNumber);

        PageNumber = buffer.PageNumber;

        NextDefinitionPage = buffer.ReadInt32(format.TdefNextPageOffset);
        RowCount = buffer.ReadInt32(format.TdefRowCountOffset);
        ComplexAutoNumber = buffer.ReadInt32(format.TdefComplexAutoNumberOffset);
        TableType = (TableType)buffer.ReadByte(format.TdefTableTypeOffset);
        VariableColumnCount = buffer.ReadUInt16(format.TdefVariableColumnsOffset);
        ColumnCount = buffer.ReadUInt16(format.TdefColumnCountOffset);
        LogicalIndexCount = buffer.ReadInt32(format.TdefLogicalIndexCountOffset);
        IndexCount = buffer.ReadInt32(format.TdefIndexCountOffset);

        if (ColumnCount > MaxColumnsPerTable)
            throw new InvalidDataException($"TDEF declares {ColumnCount} columns; Jet/ACE permits at most {MaxColumnsPerTable}.");
        if (VariableColumnCount > MaxColumnsPerTable)
            throw new InvalidDataException(
                $"TDEF declares a variable-column high-water of {VariableColumnCount}; Jet/ACE permits at most {MaxColumnsPerTable}.");
        if (IndexCount is < 0 or > MaxIndexesPerTable)
            throw new InvalidDataException($"TDEF declares {IndexCount} real indexes; Jet/ACE permits 0 through {MaxIndexesPerTable}.");
        // Capped at 32 exactly as IndexCount is, and this is the check that matters: a table gains a logical
        // block per INCOMING relationship without gaining a data block, so it overruns here while 0x33 stays
        // legal. Previously only the sign was checked, which let a file written past the limit read back as
        // sound - the one shape where LibRed produces a database Access reports as an unrecognized format
        // while seeing nothing wrong with it itself.
        if (LogicalIndexCount is < 0 or > MaxIndexesPerTable)
            throw new InvalidDataException(
                $"TDEF declares {LogicalIndexCount} logical indexes; Jet/ACE permits 0 through {MaxIndexesPerTable}.");

        // The column descriptors follow a per-index block sized by the REAL index count at
        // 0x33 (IndexCount) — NOT the logical count at 0x2F (LogicalIndexCount). The two are
        // equal for MSysObjects but differ for user tables (e.g. logical=2, real=1).
        // The buffer here may already be a stitched multi-page definition (see Read(channel, page)).
        int columnBlock = CheckedRegionEnd(
            format.TdefRealIndexBlockOffset, IndexCount, format.RealIndexEntrySize, buffer.Span.Length, "index statistics");
        _ = CheckedRegionEnd(
            columnBlock, ColumnCount, format.ColumnDescriptorSize, buffer.Span.Length, "column descriptors");
        int afterNames = ReadColumns(buffer, format, columnBlock);
        ReadIndexes(buffer, format, afterNames);
    }

    /// <summary>
    /// Parses the index structures following the column names into <see cref="IndexDef"/>s
    /// (one per index-data block): columns + sort order, unique/primary flags, root page,
    /// and the index name (resolved from the logical-index info blocks).
    /// </summary>
    private void ReadIndexes(PageBuffer buffer, JetFormatBase format, int blockStart)
    {
        _indexes.Clear();
        var byColumnId = _columns.ToDictionary(c => c.ColumnId);
        int infoStart = CheckedRegionEnd(
            blockStart, IndexCount, IndexBlockFormat.DataBlockSize, buffer.Span.Length, "index-data blocks");
        _ = CheckedRegionEnd(
            infoStart, LogicalIndexCount, IndexBlockFormat.InfoBlockSize, buffer.Span.Length, "logical-index blocks");

        // 1. Index-data blocks (one IndexDef each): columns, unique flag, root page.
        for (int i = 0; i < IndexCount; i++)
        {
            int block = blockStart + i * IndexBlockFormat.DataBlockSize;

            // Per-index statistics live in the 12-byte block at TdefRealIndexBlockOffset:
            // [+0] total entries (= row count), [+4] unique entry count (cumulative, never
            // decremented by Access), [+8] reserved.
            int statsBlock = format.TdefRealIndexBlockOffset + i * format.RealIndexEntrySize;
            int uniqueEntryCount = buffer.ReadInt32(statsBlock + 4);

            var columns = new List<(ColumnDef Column, bool Ascending)>();
            for (int slot = 0; slot < IndexBlockFormat.MaxColumns; slot++)
            {
                int entry = block + IndexBlockFormat.ColumnsOffset + slot * IndexBlockFormat.ColumnSlotSize;
                short columnId = buffer.ReadInt16(entry);
                if (columnId == IndexBlockFormat.ColumnUnused) continue;
                if (byColumnId.TryGetValue(columnId, out ColumnDef? column))
                    columns.Add((column, (buffer.ReadByte(entry + 2) & IndexBlockFormat.ColumnAscending) != 0));
            }

            _indexes.Add(new IndexDef
            {
                Name = string.Empty,
                Columns = columns,
                IsUnique = (buffer.ReadUInt16(block + IndexBlockFormat.FlagsOffset) & IndexFlags.Unique) != 0,
                IgnoreNulls = (buffer.ReadUInt16(block + IndexBlockFormat.FlagsOffset) & IndexFlags.IgnoreNulls) != 0,
                Required = (buffer.ReadUInt16(block + IndexBlockFormat.FlagsOffset) & IndexFlags.Required) != 0,
                IsPrimaryKey = false,
                UniqueEntryCount = uniqueEntryCount,
                RootPage = buffer.ReadInt32(block + IndexBlockFormat.RootPageOffset),
                RealIndexOrdinal = i,
            });
        }

        int afterIndexNames = ResolveIndexNames(buffer, infoStart);
        ReadLongValueMaps(buffer, afterIndexNames);
    }

    private static int CheckedRegionEnd(
        int start, int count, int itemSize, int bufferLength, string section)
    {
        long end = (long)start + (long)count * itemSize;
        if (start < 0 || count < 0 || end < start || end > bufferLength)
            throw new InvalidDataException(
                $"TDEF {section} extend past the assembled definition ({start} + {count} * {itemSize} > {bufferLength}).");
        return (int)end;
    }

    /// <summary>Parses the §3.3.2 long-value column usage-map list (after the index names): one 10-byte
    /// entry {col_num:2, used_ptr:4, free_ptr:4} per memo/OLE column, terminated by col_num 0xFFFF. Each
    /// pointer is a 1-byte record row + 3-byte page. Captures the owned- (used-pages) map pointer.</summary>
    private void ReadLongValueMaps(PageBuffer buffer, int pos)
    {
        _longValueOwnedMaps.Clear();
        _longValueFreeMaps.Clear();
        var seen = new HashSet<int>();
        while (true)
        {
            EnsureAvailable(buffer, pos, 2, "long-value map terminator");
            int colNum = buffer.ReadUInt16(pos);
            if (colNum == 0xFFFF)
            {
                pos += 2;
                if (pos != buffer.Length)
                    throw new InvalidDataException(
                        $"TDEF has {buffer.Length - pos} trailing bytes after the long-value map terminator.");
                return;
            }

            EnsureAvailable(buffer, pos, 10, "long-value map entry");
            ColumnDef? column = _columns.FirstOrDefault(c => c.ColumnId == colNum);
            if (column is null)
                throw new InvalidDataException($"TDEF long-value map references unknown column id {colNum}.");
            if (column.Type is not (JetDataType.Memo or JetDataType.Ole))
                throw new InvalidDataException(
                    $"TDEF long-value map references non-long-value column '{column.Name}' ({column.Type}).");
            if (!seen.Add(colNum))
                throw new InvalidDataException($"TDEF contains duplicate long-value map entries for column id {colNum}.");

            _longValueOwnedMaps[colNum] = (buffer.ReadByte(pos + 2), buffer.ReadInt24(pos + 3));
            _longValueFreeMaps[colNum] = (buffer.ReadByte(pos + 6), buffer.ReadInt24(pos + 7));
            pos += 10;
        }
    }

    /// <summary>
    /// Reads the logical-index info blocks and their names, then attaches each name (and the
    /// primary-key flag) to the index-data block it references. A data block may be referenced
    /// by several logical indexes (e.g. a relationship plus the real index); the real index's
    /// name wins over a foreign-key relationship's.
    /// </summary>
    private int ResolveIndexNames(PageBuffer buffer, int infoStart)
    {
        int logicalCount = LogicalIndexCount; // 0x2F — the logical-index (slot) count
        var info = new (int DataNumber, bool IsRelationship, byte Type)[logicalCount];
        for (int i = 0; i < logicalCount; i++)
        {
            int block = infoStart + i * IndexBlockFormat.InfoBlockSize;
            info[i] = (
                buffer.ReadInt32(block + IndexBlockFormat.InfoDataNumberOffset),
                buffer.ReadInt32(block + IndexBlockFormat.InfoFkTablePageOffset) != 0,
                buffer.ReadByte(block + IndexBlockFormat.InfoTypeOffset));
        }

        int namePos = infoStart + logicalCount * IndexBlockFormat.InfoBlockSize;
        var priority = new int[_indexes.Count];
        for (int i = 0; i < logicalCount; i++)
        {
            (string name, namePos) = ReadName(buffer, namePos, $"logical index {i}");

            (int dataNumber, bool isRelationship, byte type) = info[i];
            if (dataNumber < 0 || dataNumber >= _indexes.Count) continue;

            // Prefer a real index name over a relationship's; prefer the primary among real ones.
            int p = isRelationship ? 1 : type == IndexBlockFormat.TypePrimary ? 3 : 2;
            if (p > priority[dataNumber])
            {
                priority[dataNumber] = p;
                _indexes[dataNumber] = _indexes[dataNumber] with
                {
                    Name = name,
                    IsPrimaryKey = !isRelationship && type == IndexBlockFormat.TypePrimary,
                };
            }
        }

        return namePos;
    }

    private int ReadColumns(PageBuffer buffer, JetFormatBase format, int columnBlock)
    {
        _columns.Clear();

        // Pass 1: fixed-size column descriptors.
        var descriptors = new (JetDataType Type, int ColumnId, byte Flags, byte ExtFlags, int FixedOffset, int Length, byte Precision, byte Scale, int VariableIndex, Collation Collation)[ColumnCount];
        var columnIds = new HashSet<int>();
        for (int i = 0; i < ColumnCount; i++)
        {
            int entry = columnBlock + i * format.ColumnDescriptorSize;
            var type = (JetDataType)buffer.ReadByte(entry + format.ColumnTypeOffset);
            int columnId = buffer.ReadUInt16(entry + format.ColumnNumberOffset);
            if (!Enum.IsDefined(type))
                throw new InvalidDataException($"TDEF column {i} has unknown type code 0x{(byte)type:X2}.");
            if (columnId >= MaxColumnsPerTable)
                throw new InvalidDataException(
                    $"TDEF column {i} has id {columnId}; valid ids are 0 through {MaxColumnsPerTable - 1}.");
            if (!columnIds.Add(columnId))
                throw new InvalidDataException($"TDEF contains duplicate column id {columnId}.");

            // Bytes 0x0B/0x0C are precision/scale for a Decimal/Numeric column and the text-collation LCID
            // for everything else; 0x0D is the collation's sort-order version. Read whichever applies.
            bool numeric = type == JetDataType.FixedPoint;
            descriptors[i] = (
                type,
                columnId,
                buffer.ReadByte(entry + format.ColumnFlagsOffset),
                buffer.ReadByte(entry + format.ColumnExtendedFlagsOffset),
                buffer.ReadUInt16(entry + format.ColumnFixedOffsetOffset),
                buffer.ReadUInt16(entry + format.ColumnLengthOffset),
                numeric ? buffer.ReadByte(entry + format.ColumnPrecisionOffset) : (byte)0,
                numeric ? buffer.ReadByte(entry + format.ColumnScaleOffset) : (byte)0,
                // The variable-table index is **stored** in the descriptor (0x07), not derived. Reading it
                // (rather than ranking column ids) is what lets a table with a **dropped column** decode:
                // ACE's DROP COLUMN removes a descriptor but does NOT renumber the survivors or rewrite
                // rows, so a survivor keeps its original variable index even though ranking would shift it.
                buffer.ReadUInt16(entry + format.ColumnVariableIndexOffset),
                numeric ? Collation.GeneralLegacy
                    // 0x0B..0x0E are one 32-bit LCID with the sort-order version in the top byte: LANGID,
                    // then the sort id at 0x0D (non-zero only for a Windows alternate sort order, e.g.
                    // Hungarian Technical), then the version at 0x0E (0 = legacy table, 1 = Access-2010).
                    : new Collation((CollatingOrder)buffer.ReadUInt16(entry + format.ColumnLocaleOffset),
                        buffer.ReadByte(entry + format.ColumnCollationVersionOffset),
                        buffer.ReadByte(entry + format.ColumnCollationSortIdOffset)));
        }

        // Pass 2: column names, in the same order, immediately after the descriptor block.
        // Each name is a 2-byte (little-endian) byte length followed by UTF-16LE text.
        int namePos = columnBlock + ColumnCount * format.ColumnDescriptorSize;
        for (int i = 0; i < ColumnCount; i++)
        {
            (string name, namePos) = ReadName(buffer, namePos, $"column {i}");

            var d = descriptors[i];
            bool isFixed = (d.Flags & JetFormatBase.ColumnFlagFixedLength) != 0;
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
                // Byte 7 is stored on fixed columns too (the running count of preceding variable columns); keep
                // it so a faithful rebuild re-emits the exact value instead of clobbering fixed columns to 0.
                VariableTableIndex = d.VariableIndex,
                IsFixedLength = isFixed,
                IsAutoNumber = (d.Flags & JetFormatBase.ColumnFlagAutoNumber) != 0,
                // Every documented flag bit is modelled (0x0F: updatable/GUID-autonumber/hyperlink; 0x10:
                // compressed-Unicode / calculated) so it round-trips explicitly, not via RawDescriptor.
                IsUpdatable = (d.Flags & JetFormatBase.ColumnFlagUpdatable) != 0,
                IsGuidAutoNumber = (d.Flags & JetFormatBase.ColumnFlagGuidAutoNumber) != 0,
                IsHyperlink = (d.Flags & JetFormatBase.ColumnFlagHyperlink) != 0,
                SupportsCompressedUnicode = (d.ExtFlags & JetFormatBase.ColumnExtFlagCompressedUnicode) != 0,
                IsCalculated = (d.ExtFlags & JetFormatBase.ColumnExtFlagCalculated) != 0,
                Precision = d.Precision,
                Scale = d.Scale,
                Collation = d.Collation,
                // Keep the original 25 bytes so a rewrite preserves fields we don't model (faithful round-trip).
                RawDescriptor = buffer.Slice(columnBlock + i * format.ColumnDescriptorSize, format.ColumnDescriptorSize).ToArray(),
            });
        }

        // AutoNumber seed/increment from the TDEF header: 0x18 = increment, 0x14 = last-assigned value. On a
        // freshly created table the last value is Seed-Increment, so Seed = last + increment (matching what a
        // no-insert scaffold reports). A table has at most one AutoNumber column; apply to it.
        int increment = buffer.ReadInt32(format.TdefAutoNumberIncrementOffset);
        if (increment == 0) increment = 1;
        int lastAuto = buffer.ReadInt32(format.TdefLastAutoNumberOffset);
        foreach (ColumnDef column in _columns)
            if (column.IsAutoNumber)
            {
                column.Increment = increment;
                column.Seed = lastAuto + increment;
            }

        return namePos;
    }

    private static (string Name, int Next) ReadName(PageBuffer buffer, int pos, string kind)
    {
        EnsureAvailable(buffer, pos, 2, $"{kind} name length");
        int byteLength = buffer.ReadUInt16(pos);
        pos += 2;
        if (byteLength == 0 || byteLength > MaxNameBytes || (byteLength & 1) != 0)
            throw new InvalidDataException(
                $"TDEF {kind} name has invalid UTF-16 byte length {byteLength}; expected an even value from 2 through {MaxNameBytes}.");
        EnsureAvailable(buffer, pos, byteLength, $"{kind} name");
        try
        {
            return (StrictUnicode.GetString(buffer.Slice(pos, byteLength)), pos + byteLength);
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
}
