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

    public sealed record Result(byte[] Page, IReadOnlyList<ColumnDef> Columns);

    public static Result Build(JetFormatBase format, TableType tableType, IReadOnlyList<ColumnSpec> specs)
    {
        var columns = ResolveColumns(format, specs);
        var page = new byte[format.PageSize];

        page[0] = PageTypeTableDefinition;
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefNextPageOffset, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefRowCountOffset, 4), 0);
        page[format.TdefTableTypeOffset] = (byte)tableType;
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefVariableColumnsOffset, 2),
            (ushort)columns.Count(c => !c.IsFixedLength));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefColumnCountOffset, 2), (ushort)columns.Count);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefRealIndexCountOffset, 4), 0); // logical
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefIndexCountOffset, 4), 0);      // real

        // No indexes, so column descriptors start right at the real-index block offset.
        int columnBlock = format.TdefRealIndexBlockOffset;
        WriteColumnDescriptors(page, format, columns, columnBlock);
        WriteColumnNames(page, format, columns, columnBlock + columns.Count * format.ColumnDescriptorSize);

        return new Result(page, columns);
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
            columns.Add(new ColumnDef
            {
                Name = s.Name,
                Type = s.Type,
                Index = i,
                ColumnId = i,
                Length = s.Length,
                FixedOffset = s.IsFixedLength ? fixedOffset : 0,
                VariableIndex = s.IsFixedLength ? -1 : variableRank[i],
                IsFixedLength = s.IsFixedLength,
                IsAutoNumber = s.IsAutoNumber,
                Precision = s.Precision,
                Scale = s.Scale,
            });
            if (s.IsFixedLength) fixedOffset += s.Length;
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

    private static void WriteColumnNames(byte[] page, JetFormatBase format, List<ColumnDef> columns, int namePos)
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
    }
}
