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
    int RootPage);

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

    // Column-descriptor sub-offsets the reader doesn't consume but Access does.
    private const int ColumnVariableIndexOffset = 0x07;

    // Index-data block (52 bytes): a 0x783 marker, 10 column slots, root page, unique flag.
    private const int IndexBlockSize = 52;
    private const int IndexMaxColumns = 10;
    private const int IndexColumnSlotSize = 3;
    private const int IndexColumnsOffset = 0x04;
    private const int IndexRootPageOffset = 0x26;
    private const int IndexFlagsOffset = 0x2E;
    private const short IndexColumnUnused = -1; // 0xFFFF
    private const byte IndexColumnAscending = 0x01;
    private const ushort IndexFlagUnique = 0x0001;
    private const uint IndexDataMarker = 0x783;

    // Index-info block (28 bytes, one per logical index): links a name to a data block.
    private const int IndexInfoBlockSize = 28;
    private const int IndexInfoNumberOffset = 0x04;
    private const int IndexInfoDataNumberOffset = 0x08;
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
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefNextPageOffset, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefRowCountOffset, 4), 0);
        page[format.TdefTableTypeOffset] = (byte)tableType;
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

        WriteIndexes(page, format, columns, indexes, afterNames);
        return new Result(page, columns);
    }

    private static void WriteIndexes(byte[] page, JetFormatBase format, List<ColumnDef> columns, IReadOnlyList<IndexSpec> indexes, int dataBlockStart)
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
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexRootPageOffset, 4), index.RootPage);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(block + IndexFlagsOffset, 2),
                (ushort)(index.IsUnique ? IndexFlagUnique : 0));
        }

        // 2. Index-info blocks: link each name to its data block.
        int infoStart = dataBlockStart + indexes.Count * IndexBlockSize;
        for (int i = 0; i < indexes.Count; i++)
        {
            int block = infoStart + i * IndexInfoBlockSize;
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexInfoNumberOffset, 4), i);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexInfoDataNumberOffset, 4), i);
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
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(entry + format.ColumnNumberOffset, 2), (ushort)c.ColumnId);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(entry + ColumnVariableIndexOffset, 2),
                (ushort)(c.IsFixedLength ? 0 : c.VariableIndex));
            if (c.Type == JetDataType.FixedPoint)
            {
                page[entry + format.ColumnPrecisionOffset] = c.Precision;
                page[entry + format.ColumnScaleOffset] = c.Scale;
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
