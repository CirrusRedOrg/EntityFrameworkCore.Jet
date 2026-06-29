using System.Buffers.Binary;
using System.Text;
using LibRed.Formats;
using LibRed.Pages;

namespace LibRed.Catalog;

/// <summary>A column to create: its name, type, and (for fixed/text) byte length.</summary>
public sealed record ColumnSpec(
    string Name,
    JetDataType Type,
    int Length,
    bool IsFixedLength,
    bool IsAutoNumber = false,
    byte Precision = 0,
    byte Scale = 0);

/// <summary>An index to create over the named columns, anchored at an already-allocated root page.</summary>
public sealed record IndexSpec(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsPrimaryKey,
    bool IsUnique,
    int RootPage,
    int UsageMapRow = 0,
    int UsageMapPage = 0);

/// <summary>
/// Serializes a table schema into a Jet 4 / ACE table-definition (TDEF) page — the inverse of
/// <see cref="TableDefinitionPage"/>. This first cut builds a single-page definition with no
/// indexes; index-data blocks and continuation pages come later. Fixed columns are packed in
/// declaration order; variable columns are ranked by column id for the row var-offset table.
/// </summary>
public static class TdefBuilder
{
    private const byte PageTypeTableDefinition = 0x02;
    private const byte ColumnFlagUpdatable = 0x02;

    // TDEF header fields Access validates that the reader currently ignores (verified vs ACE).
    private const int TdefHeaderFlagsOffset = 0x01;   // observed 0x01
    private const int TdefFreeSpaceOffset = 0x02;     // bytes free in this page
    private const int TdefLengthOffset = 0x08;        // total definition length
    private const int TdefMarkerOffset = 0x0C;        // 0x00000659 record marker
    private const int TdefConstantOffset = 0x18;      // observed constant 0x00000001
    private const int TdefMaxColumnsOffset = 0x29;    // maximum column count
    private const uint TdefRecordMarker = 0x659;
    private const int TdefContinuationReserve = 8;    // free space excludes the 8-byte continuation header

    // Column-descriptor sub-offsets the reader doesn't consume but Access does.
    private const int ColumnRecordMarkerOffset = 0x01; // 0x0659
    private const int ColumnNumber2Offset = 0x09;      // duplicate column id
    private const int ColumnVariableIndexOffset = 0x07;
    private const int ColumnLocaleLowOffset = 0x0B;    // non-numeric: en-US locale 0x0409
    private const int ColumnLocaleHighOffset = 0x0C;
    private const byte LocaleLow = 0x09;
    private const byte LocaleHigh = 0x04;

    // Index-data block (52 bytes): a 0x783 marker, 10 column slots, root page, unique flag.
    private const int IndexBlockSize = 52;
    private const int IndexMaxColumns = 10;
    private const int IndexColumnSlotSize = 3;
    private const int IndexColumnsOffset = 0x04;
    private const int IndexUsageMapRowOffset = 0x22;  // 1-byte row + 3-byte page for the index's pages
    private const int IndexRootPageOffset = 0x26;
    private const int IndexFlagsOffset = 0x2E;
    private const short IndexColumnUnused = -1; // 0xFFFF
    private const byte IndexColumnAscending = 0x01;
    private const ushort IndexFlagUnique = 0x0001;
    private const ushort IndexFlagRequired = 0x0008;
    private const ushort IndexFlagAlwaysSet = 0x0080; // Access 2000+
    private const uint IndexDataMarker = 0x783;

    // Index-info block (28 bytes, one per logical index): links a name to a data block.
    private const int IndexInfoBlockSize = 28;
    private const int IndexInfoMarkerOffset = 0x00;       // 0x0659
    private const int IndexInfoNumberOffset = 0x04;
    private const int IndexInfoDataNumberOffset = 0x08;
    private const int IndexInfoFkNumberOffset = 0x0D;     // 0xFFFFFFFF = no foreign key
    private const int IndexInfoUpdateActionOffset = 0x15;
    private const int IndexInfoDeleteActionOffset = 0x16;
    private const byte IndexActionDefault = 0x04;         // observed on a plain PK (no relationship)
    private const int IndexInfoTypeOffset = 0x17;
    private const byte IndexTypePrimary = 0x01;
    private const byte IndexTypeRegular = 0x02;

    public sealed record Result(byte[] Page, IReadOnlyList<ColumnDef> Columns);

    public static Result Build(
        JetFormatBase format,
        TableType tableType,
        IReadOnlyList<ColumnSpec> specs,
        IReadOnlyList<IndexSpec>? indexes = null)
    {
        indexes ??= [];
        var columns = ResolveColumns(format, specs);
        var page = new byte[format.PageSize];

        page[0] = PageTypeTableDefinition;
        page[TdefHeaderFlagsOffset] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(TdefMarkerOffset, 4), TdefRecordMarker);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(TdefConstantOffset, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefNextPageOffset, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefRowCountOffset, 4), 0);
        page[format.TdefTableTypeOffset] = (byte)tableType;
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(TdefMaxColumnsOffset, 2), (ushort)columns.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefVariableColumnsOffset, 2),
            (ushort)columns.Count(c => !c.IsFixedLength));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefColumnCountOffset, 2), (ushort)columns.Count);
        // Logical (0x2F) and real (0x33) index counts — equal here (no shared relationship indexes).
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefRealIndexCountOffset, 4), indexes.Count);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefIndexCountOffset, 4), indexes.Count);

        // The per-index statistics blocks (12 bytes each) precede the columns; entry counts start 0.
        int columnBlock = format.TdefRealIndexBlockOffset + indexes.Count * format.RealIndexEntrySize;
        WriteColumnDescriptors(page, format, columns, columnBlock);
        int afterNames = WriteColumnNames(page, format, columns, columnBlock + columns.Count * format.ColumnDescriptorSize);

        int definitionEnd = WriteIndexes(page, format, columns, indexes, afterNames);

        // Definition length and remaining free space (Access reserves an 8-byte continuation header).
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(TdefLengthOffset, 4), definitionEnd);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(TdefFreeSpaceOffset, 2),
            (ushort)(format.PageSize - definitionEnd - TdefContinuationReserve));

        return new Result(page, columns);
    }

    /// <summary>Writes the index structures and returns the offset just past them (the definition end).</summary>
    private static int WriteIndexes(byte[] page, JetFormatBase format, List<ColumnDef> columns, IReadOnlyList<IndexSpec> indexes, int dataBlockStart)
    {
        var columnIdByName = columns.ToDictionary(c => c.Name, c => c.ColumnId, StringComparer.OrdinalIgnoreCase);

        // 1. Index-data blocks: columns, root page, unique flag.
        for (int i = 0; i < indexes.Count; i++)
        {
            IndexSpec index = indexes[i];
            int block = dataBlockStart + i * IndexBlockSize;

            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(block, 4), IndexDataMarker);
            for (int slot = 0; slot < IndexMaxColumns; slot++)
            {
                int entry = block + IndexColumnsOffset + slot * IndexColumnSlotSize;
                if (slot < index.Columns.Count)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(page.AsSpan(entry, 2), (short)columnIdByName[index.Columns[slot]]);
                    page[entry + 2] = IndexColumnAscending;
                }
                else
                {
                    BinaryPrimitives.WriteInt16LittleEndian(page.AsSpan(entry, 2), IndexColumnUnused);
                }
            }
            page[block + IndexUsageMapRowOffset] = (byte)index.UsageMapRow;
            page[block + IndexUsageMapRowOffset + 1] = (byte)index.UsageMapPage;
            page[block + IndexUsageMapRowOffset + 2] = (byte)(index.UsageMapPage >> 8);
            page[block + IndexUsageMapRowOffset + 3] = (byte)(index.UsageMapPage >> 16);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexRootPageOffset, 4), index.RootPage);
            ushort flags = IndexFlagAlwaysSet;
            if (index.IsUnique) flags |= IndexFlagUnique;
            if (index.IsPrimaryKey) flags |= IndexFlagRequired;
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(block + IndexFlagsOffset, 2), flags);
        }

        // 2. Index-info blocks: link each name to its data block.
        int infoStart = dataBlockStart + indexes.Count * IndexBlockSize;
        for (int i = 0; i < indexes.Count; i++)
        {
            int block = infoStart + i * IndexInfoBlockSize;
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(block + IndexInfoMarkerOffset, 4), TdefRecordMarker);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexInfoNumberOffset, 4), i);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexInfoDataNumberOffset, 4), i);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexInfoFkNumberOffset, 4), -1); // no foreign key
            page[block + IndexInfoUpdateActionOffset] = IndexActionDefault;
            page[block + IndexInfoDeleteActionOffset] = IndexActionDefault;
            page[block + IndexInfoTypeOffset] = indexes[i].IsPrimaryKey ? IndexTypePrimary : IndexTypeRegular;
        }

        // 3. Index names.
        int namePos = infoStart + indexes.Count * IndexInfoBlockSize;
        foreach (IndexSpec index in indexes)
        {
            byte[] name = System.Text.Encoding.Unicode.GetBytes(index.Name);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), (ushort)name.Length);
            namePos += 2;
            name.CopyTo(page.AsSpan(namePos));
            namePos += name.Length;
        }

        // Trailing terminator: Access closes the index-name list with a 2-byte 0xFFFF, and the
        // definition length includes it. Without it Access rejects the table ("Unrecognized
        // database format"). Verified byte-for-byte against an ACE-created single-index table.
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), 0xFFFF);
        namePos += 2;
        return namePos;
    }

    private static List<ColumnDef> ResolveColumns(JetFormatBase format, IReadOnlyList<ColumnSpec> specs)
    {
        _ = format;
        // Variable columns are addressed in ascending column-id order; column id = declaration order.
        var variableRank = new Dictionary<int, int>();
        int rank = 0;
        for (int id = 0; id < specs.Count; id++)
            if (!specs[id].IsFixedLength)
                variableRank[id] = rank++;

        var columns = new List<ColumnDef>(specs.Count);
        int fixedOffset = 0;
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
                ColumnId = i,
                Length = s.Length,
                FixedOffset = occupiesFixedData ? fixedOffset : 0,
                VariableIndex = s.IsFixedLength ? -1 : variableRank[i],
                IsFixedLength = s.IsFixedLength,
                IsAutoNumber = s.IsAutoNumber,
                Precision = s.Precision,
                Scale = s.Scale,
            });
            if (occupiesFixedData) fixedOffset += s.Length;
        }
        return columns;
    }

    private static void WriteColumnDescriptors(byte[] page, JetFormatBase format, List<ColumnDef> columns, int columnBlock)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            ColumnDef c = columns[i];
            int entry = columnBlock + i * format.ColumnDescriptorSize;

            page[entry + format.ColumnTypeOffset] = (byte)c.Type;
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(entry + ColumnRecordMarkerOffset, 2), (ushort)TdefRecordMarker);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(entry + format.ColumnNumberOffset, 2), (ushort)c.ColumnId);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(entry + ColumnNumber2Offset, 2), (ushort)c.ColumnId);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(entry + ColumnVariableIndexOffset, 2),
                (ushort)(c.IsFixedLength ? 0 : c.VariableIndex));
            if (c.Type == JetDataType.FixedPoint)
            {
                page[entry + format.ColumnPrecisionOffset] = c.Precision;
                page[entry + format.ColumnScaleOffset] = c.Scale;
            }
            else
            {
                // Non-numeric columns store the en-US locale (0x0409) in the precision/scale bytes.
                page[entry + ColumnLocaleLowOffset] = LocaleLow;
                page[entry + ColumnLocaleHighOffset] = LocaleHigh;
            }
            page[entry + format.ColumnFlagsOffset] = (byte)(
                ColumnFlagUpdatable
                | (c.IsFixedLength ? JetFormatBase.ColumnFlagFixedLength : 0)
                | (c.IsAutoNumber ? JetFormatBase.ColumnFlagAutoNumber : 0));
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(entry + format.ColumnFixedOffsetOffset, 2), (ushort)c.FixedOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(entry + format.ColumnLengthOffset, 2), (ushort)c.Length);
        }
    }

    private static int WriteColumnNames(byte[] page, JetFormatBase format, List<ColumnDef> columns, int namePos)
    {
        _ = format;
        foreach (ColumnDef c in columns)
        {
            byte[] name = Encoding.Unicode.GetBytes(c.Name);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), (ushort)name.Length);
            namePos += 2;
            name.CopyTo(page.AsSpan(namePos));
            namePos += name.Length;
        }
        return namePos;
    }
}
