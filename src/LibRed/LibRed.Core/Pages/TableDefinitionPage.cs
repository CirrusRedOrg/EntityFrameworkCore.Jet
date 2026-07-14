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
    public int RealIndexCount { get; private set; }
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
        => Read(AssembleDefinition(channel, page), channel.Format);

    private static PageBuffer AssembleDefinition(PageChannel channel, int page)
    {
        PageBuffer first = channel.ReadPage(page);
        int next = first.ReadInt32(channel.Format.TdefNextPageOffset);
        if (next == 0)
            return first;

        // The column offsets are absolute from the first page's start, so the first page
        // is taken whole and each continuation contributes its data after the 8-byte header.
        var assembled = new List<byte>(first.Span.Length * 2);
        assembled.AddRange(first.Span);

        while (next != 0)
        {
            PageBuffer continuation = channel.ReadPage(next);
            next = continuation.ReadInt32(channel.Format.TdefNextPageOffset);
            assembled.AddRange(continuation.Span[JetFormatBase.TdefContinuationHeaderSize..]);
        }

        return new PageBuffer(assembled.ToArray(), page);
    }

    public override void Read(PageBuffer buffer, JetFormatBase format)
    {
        PageNumber = buffer.PageNumber;

        NextDefinitionPage = buffer.ReadInt32(format.TdefNextPageOffset);
        RowCount = buffer.ReadInt32(format.TdefRowCountOffset);
        ComplexAutoNumber = buffer.ReadInt32(format.TdefComplexAutoNumberOffset);
        TableType = (TableType)buffer.ReadByte(format.TdefTableTypeOffset);
        VariableColumnCount = buffer.ReadUInt16(format.TdefVariableColumnsOffset);
        ColumnCount = buffer.ReadUInt16(format.TdefColumnCountOffset);
        RealIndexCount = buffer.ReadInt32(format.TdefRealIndexCountOffset);
        IndexCount = buffer.ReadInt32(format.TdefIndexCountOffset);

        // The column descriptors follow a per-index block sized by the index count at
        // 0x33 (IndexCount) — NOT the index-slot count at 0x2F. The two are equal for
        // MSysObjects but differ for user tables (e.g. slots=2, indexes=1).
        // The buffer here may already be a stitched multi-page definition (see Read(channel, page)).
        int columnBlock = format.TdefRealIndexBlockOffset + IndexCount * format.RealIndexEntrySize;
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

        int afterIndexNames = ResolveIndexNames(buffer, blockStart + IndexCount * IndexBlockFormat.DataBlockSize);
        ReadLongValueMaps(buffer, afterIndexNames);
    }

    /// <summary>Parses the §3.3.2 long-value column usage-map list (after the index names): one 10-byte
    /// entry {col_num:2, used_ptr:4, free_ptr:4} per memo/OLE column, terminated by col_num 0xFFFF. Each
    /// pointer is a 1-byte record row + 3-byte page. Captures the owned- (used-pages) map pointer.</summary>
    private void ReadLongValueMaps(PageBuffer buffer, int pos)
    {
        _longValueOwnedMaps.Clear();
        _longValueFreeMaps.Clear();
        while (buffer.ReadUInt16(pos) is var colNum && colNum != 0xFFFF)
        {
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
        int logicalCount = RealIndexCount; // 0x2F — the logical-index (slot) count
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
            int byteLength = buffer.ReadUInt16(namePos);
            namePos += 2;
            string name = Encoding.Unicode.GetString(buffer.Slice(namePos, byteLength));
            namePos += byteLength;

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
        for (int i = 0; i < ColumnCount; i++)
        {
            int entry = columnBlock + i * format.ColumnDescriptorSize;
            var type = (JetDataType)buffer.ReadByte(entry + format.ColumnTypeOffset);

            // Bytes 0x0B/0x0C are precision/scale for a Decimal/Numeric column and the text-collation LCID
            // for everything else; 0x0D is the collation's sort-order version. Read whichever applies.
            bool numeric = type == JetDataType.FixedPoint;
            descriptors[i] = (
                type,
                buffer.ReadUInt16(entry + format.ColumnNumberOffset),
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
                    : new Collation((CollatingOrder)buffer.ReadUInt16(entry + format.ColumnLocaleOffset),
                        // The sort-order version is the HIGH byte of the 2-byte field at 0x0D — i.e. the byte at
                        // 0x0E (0 = General legacy, 1 = Access-2010 General). The low byte 0x0D is 0 in every file
                        // seen; reading it alone (as LibRed used to) hid v1. Watch 0x0D — see Collation.Version.
                        buffer.ReadByte(entry + format.ColumnCollationVersionOffset + 1)));
        }

        // Pass 2: column names, in the same order, immediately after the descriptor block.
        // Each name is a 2-byte (little-endian) byte length followed by UTF-16LE text.
        int namePos = columnBlock + ColumnCount * format.ColumnDescriptorSize;
        for (int i = 0; i < ColumnCount; i++)
        {
            int byteLength = buffer.ReadUInt16(namePos);
            namePos += 2;
            string name = Encoding.Unicode.GetString(buffer.Slice(namePos, byteLength));
            namePos += byteLength;

            var d = descriptors[i];
            bool isFixed = (d.Flags & JetFormatBase.ColumnFlagFixedLength) != 0;
            _columns.Add(new ColumnDef
            {
                Name = name,
                Type = d.Type,
                Index = i,
                ColumnId = d.ColumnId,
                Length = d.Length,
                FixedOffset = d.FixedOffset,
                VariableIndex = isFixed ? -1 : d.VariableIndex,
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
}
