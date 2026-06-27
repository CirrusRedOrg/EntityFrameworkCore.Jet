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
    public TableType TableType { get; private set; }
    public int VariableColumnCount { get; private set; }
    public int ColumnCount { get; private set; }
    public int RealIndexCount { get; private set; }
    public int IndexCount { get; private set; }

    public IReadOnlyList<ColumnDef> Columns => _columns;

    private readonly List<IndexDef> _indexes = [];
    public IReadOnlyList<IndexDef> Indexes => _indexes;

    /// <summary>Bytes of a continuation TDEF page that precede the resumed definition data.</summary>
    private const int ContinuationHeaderSize = 8;

    // Index column block layout (Jet 4 / ACE), one per real index, following the column names.
    private const int IndexBlockSize = 52;
    private const int IndexMaxColumns = 10;
    private const int IndexColumnSlotSize = 3;   // 2-byte column id + 1-byte flags
    private const int IndexColumnsOffset = 0x04;
    private const int IndexRootPageOffset = 0x26;
    private const int IndexFlagsOffset = 0x2E;
    private const short IndexColumnUnused = -1;   // 0xFFFF
    private const byte IndexColumnAscending = 0x01;
    private const ushort IndexFlagUnique = 0x0001;
    private const ushort IndexFlagRequired = 0x0008;

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
            assembled.AddRange(continuation.Span[ContinuationHeaderSize..]);
        }

        return new PageBuffer(assembled.ToArray(), page);
    }

    public override void Read(PageBuffer buffer, JetFormatBase format)
    {
        PageNumber = buffer.PageNumber;

        NextDefinitionPage = buffer.ReadInt32(format.TdefNextPageOffset);
        RowCount = buffer.ReadInt32(format.TdefRowCountOffset);
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
        ReadIndexes(buffer, afterNames);
    }

    /// <summary>
    /// Parses the per-index column blocks (52 bytes each) that follow the column names:
    /// indexed columns + sort order, the unique/required flags, and the B-tree root page.
    /// </summary>
    private void ReadIndexes(PageBuffer buffer, int blockStart)
    {
        _indexes.Clear();
        var byColumnId = _columns.ToDictionary(c => c.ColumnId);

        for (int i = 0; i < IndexCount; i++)
        {
            int block = blockStart + i * IndexBlockSize;

            var columns = new List<(ColumnDef Column, bool Ascending)>();
            for (int slot = 0; slot < IndexMaxColumns; slot++)
            {
                int entry = block + IndexColumnsOffset + slot * IndexColumnSlotSize;
                short columnId = buffer.ReadInt16(entry);
                if (columnId == IndexColumnUnused) continue;
                if (byColumnId.TryGetValue(columnId, out ColumnDef? column))
                    columns.Add((column, (buffer.ReadByte(entry + 2) & IndexColumnAscending) != 0));
            }

            ushort flags = buffer.ReadUInt16(block + IndexFlagsOffset);
            bool unique = (flags & IndexFlagUnique) != 0;
            bool required = (flags & IndexFlagRequired) != 0;

            _indexes.Add(new IndexDef
            {
                Name = string.Empty, // index names live further on, after the logical-index entries (TODO)
                Columns = columns,
                IsUnique = unique,
                IsPrimaryKey = unique && required, // heuristic; the precise flag is in the logical-index entry
                RootPage = buffer.ReadInt32(block + IndexRootPageOffset),
            });
        }
    }

    private int ReadColumns(PageBuffer buffer, JetFormatBase format, int columnBlock)
    {
        _columns.Clear();

        // Pass 1: fixed-size column descriptors.
        var descriptors = new (JetDataType Type, int ColumnId, byte Flags, int FixedOffset, int Length)[ColumnCount];
        for (int i = 0; i < ColumnCount; i++)
        {
            int entry = columnBlock + i * format.ColumnDescriptorSize;
            descriptors[i] = (
                (JetDataType)buffer.ReadByte(entry + format.ColumnTypeOffset),
                buffer.ReadUInt16(entry + format.ColumnNumberOffset),
                buffer.ReadByte(entry + format.ColumnFlagsOffset),
                buffer.ReadUInt16(entry + format.ColumnFixedOffsetOffset),
                buffer.ReadUInt16(entry + format.ColumnLengthOffset));
        }

        // Variable columns are addressed (in the row's var-offset table) in ascending
        // column-id order, so assign each variable column its rank in that ordering.
        var variableIndex = new Dictionary<int, int>();
        int rank = 0;
        foreach (int columnId in descriptors
                     .Where(d => (d.Flags & JetFormatBase.ColumnFlagFixedLength) == 0)
                     .Select(d => d.ColumnId)
                     .OrderBy(id => id))
        {
            variableIndex[columnId] = rank++;
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
                VariableIndex = isFixed ? -1 : variableIndex[d.ColumnId],
                IsFixedLength = isFixed,
                IsAutoNumber = (d.Flags & JetFormatBase.ColumnFlagAutoNumber) != 0,
            });
        }

        return namePos;
    }
}
