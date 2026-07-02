using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage.Types;

namespace LibRed.Storage;

/// <summary>
/// Encodes a row of CLR values into the Jet 4 / ACE inline row layout — the inverse of
/// <see cref="RowDecoder"/>:
/// <code>
/// [colCount:2] [fixed data] [var data] [varOffsetTable:(numVar+1)x2] [numVar:2] [nullBitmap]
/// </code>
/// The null bitmap marks present (non-null) columns; a Boolean column has no data and its
/// bit carries the value. Variable columns are laid out in ascending VariableIndex order with
/// an end-first offset table. A memo/OLE column's value is written as an *inline* long-value
/// (12-byte descriptor + payload, §8); values too large to inline (chained LVAL pages) are not
/// written yet.
/// </summary>
public sealed class RowEncoder(IReadOnlyList<ColumnDef> columns, JetFormatBase format, int? fixedDataLength = null)
{
    private readonly IReadOnlyList<ColumnDef> _columns = columns;
    private readonly JetFormatBase _format = format;

    // Fixed (non-boolean) columns occupy a contiguous region; its length is defined by the
    // table definition. Default to the tight max so a standalone encode round-trips; INSERT
    // passes the TDEF's actual fixed-row size so the on-disk layout matches Access.
    private readonly int _fixedDataLength = fixedDataLength ?? ComputeFixedDataLength(columns);

    public byte[] Encode(object?[] values)
    {
        if (values.Length != _columns.Count)
            throw new ArgumentException($"Expected {_columns.Count} values, got {values.Length}.", nameof(values));

        int colCountSize = _format.RowColumnCountSize;
        int nullBitmapSize = (_columns.Count + 7) / 8;

        var varCols = _columns.Where(c => !c.IsFixedLength).OrderBy(c => c.VariableIndex).ToList();
        int numVar = varCols.Count;

        // Encode each region's payload first so we can size the row exactly.
        var fixedRegion = new byte[_fixedDataLength];
        foreach (ColumnDef column in _columns)
        {
            if (column.Type == JetDataType.Boolean || !column.IsFixedLength) continue;
            object? v = values[column.Index];
            if (v is null) continue; // null fixed value: leave its slot zeroed, clear the bit below
            byte[] encoded = JetTypeCodec.Encode(column, v);
            if (encoded.Length != column.Length)
                throw new InvalidOperationException($"Column '{column.Name}' encoded to {encoded.Length} bytes, expected {column.Length}.");
            encoded.CopyTo(fixedRegion.AsSpan(column.FixedOffset));
        }

        var varChunks = new byte[numVar][];
        for (int j = 0; j < numVar; j++)
        {
            object? v = values[varCols[j].Index];
            varChunks[j] = v is null ? [] : JetTypeCodec.Encode(varCols[j], v);
        }
        int varDataLength = varChunks.Sum(b => b.Length);

        int total = colCountSize + _fixedDataLength + varDataLength + (numVar + 1) * 2 + 2 + nullBitmapSize;
        var row = new byte[total];

        // Leading column count.
        BinaryPrimitives.WriteUInt16LittleEndian(row, (ushort)_columns.Count);

        // Fixed region.
        fixedRegion.CopyTo(row.AsSpan(colCountSize));

        // Variable data, immediately after the fixed region.
        int varDataStart = colCountSize + _fixedDataLength;
        int pos = varDataStart;
        foreach (byte[] chunk in varChunks)
        {
            chunk.CopyTo(row.AsSpan(pos));
            pos += chunk.Length;
        }

        // End-first offset table: entry[numVar] = var-data start, entry[numVar-j-1] = end of var col j.
        int tableStart = pos; // == varDataStart + varDataLength
        var offsets = new int[numVar + 1];
        offsets[numVar] = varDataStart;
        int running = varDataStart;
        for (int j = 0; j < numVar; j++)
        {
            running += varChunks[j].Length;
            offsets[numVar - j - 1] = running;
        }
        for (int e = 0; e <= numVar; e++)
            BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(tableStart + e * 2, 2), (ushort)offsets[e]);

        // Variable column count, then the null bitmap.
        int numVarPos = tableStart + (numVar + 1) * 2;
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(numVarPos, 2), (ushort)numVar);

        int bitmapPos = numVarPos + 2;
        foreach (ColumnDef column in _columns)
        {
            // 1 = present; for Boolean the bit IS the value (and booleans are never null).
            bool bit = column.Type == JetDataType.Boolean
                ? values[column.Index] is true
                : values[column.Index] is not null;
            if (bit)
                row[bitmapPos + (column.ColumnId >> 3)] |= (byte)(1 << (column.ColumnId & 7));
        }

        return row;
    }

    private static int ComputeFixedDataLength(IReadOnlyList<ColumnDef> columns)
    {
        int length = 0;
        foreach (ColumnDef c in columns)
            if (c.IsFixedLength && c.Type != JetDataType.Boolean)
                length = Math.Max(length, c.FixedOffset + c.Length);
        return length;
    }
}
