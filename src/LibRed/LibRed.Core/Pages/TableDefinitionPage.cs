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
        // NOTE: assumes a single-page TDEF. A multi-page TDEF (NextDefinitionPage != 0)
        // must have its pages stitched into one contiguous buffer first. TODO.
        int columnBlock = format.TdefRealIndexBlockOffset + IndexCount * format.RealIndexEntrySize;
        ReadColumns(buffer, format, columnBlock);
    }

    private void ReadColumns(PageBuffer buffer, JetFormatBase format, int columnBlock)
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
    }
}
