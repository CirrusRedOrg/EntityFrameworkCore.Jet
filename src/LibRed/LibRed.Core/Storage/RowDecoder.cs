using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage.Types;

namespace LibRed.Storage;

/// <summary>
/// Decodes a single raw row record into CLR values. Implements the Jet 4 / ACE row
/// layout (verified against real data):
/// <code>
/// [colCount:2] [fixed data] [var data] [varOffsetTable:(numVar+1)x2] [numVar:2] [nullBitmap]
/// </code>
/// The null bitmap is indexed by column id (bit set = value present). Variable columns
/// are addressed via the trailing offset table in ascending column-id order.
/// </summary>
public sealed class RowDecoder(IReadOnlyList<ColumnDef> columns, JetFormatBase format)
{
    private readonly IReadOnlyList<ColumnDef> _columns = columns;
    private readonly JetFormatBase _format = format;

    /// <summary>Decodes the row into one value per column (aligned to <see cref="ColumnDef.Index"/>).</summary>
    public object?[] Decode(ReadOnlySpan<byte> row)
    {
        var values = new object?[_columns.Count];

        int nullBitmapSize = (_columns.Count + 7) / 8;

        // A real inline row is at least: column count + an empty var table (1 entry) +
        // var count + null bitmap. Anything shorter is an overflow/lookup pointer slot,
        // which the caller should have skipped.
        if (row.Length < _format.RowColumnCountSize + 2 + 2 + nullBitmapSize)
            throw new ArgumentException("Row is too short to be an inline record (overflow/lookup slot?).", nameof(row));

        ReadOnlySpan<byte> nullBitmap = row[^nullBitmapSize..];

        int numVarCols = BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(row.Length - nullBitmapSize - 2, 2));
        int varTableStart = row.Length - nullBitmapSize - 2 - (numVarCols + 1) * 2;

        // The 2-byte variable offset table assumed here is only valid for rows that do
        // not use the >256-byte jump-table encoding. Guard so we never silently misparse.
        if (numVarCols > 0 && row.Length > 256)
            throw new NotSupportedException(
                "Rows >= 256 bytes use the variable-offset jump table, which is not yet implemented.");

        foreach (ColumnDef column in _columns)
        {
            bool present = IsPresent(nullBitmap, column.ColumnId);

            // Jet stores Boolean (YesNo) columns with no fixed/variable data: the value
            // IS the null-bitmap bit (set = true). Booleans are never null.
            if (column.Type == JetDataType.Boolean)
            {
                values[column.Index] = present;
                continue;
            }

            if (!present)
            {
                values[column.Index] = null;
                continue;
            }

            ReadOnlySpan<byte> raw = column.IsFixedLength
                ? FixedSlice(row, column)
                : VariableSlice(row, varTableStart, numVarCols, column.VariableIndex);

            values[column.Index] = JetTypeCodec.Decode(column, raw);
        }

        return values;
    }

    private ReadOnlySpan<byte> FixedSlice(ReadOnlySpan<byte> row, ColumnDef column)
    {
        int start = _format.RowColumnCountSize + column.FixedOffset;
        return row.Slice(start, column.Length);
    }

    private static ReadOnlySpan<byte> VariableSlice(ReadOnlySpan<byte> row, int varTableStart, int numVarCols, int variableIndex)
    {
        // Offsets are stored end-first: variable column j starts at entry[numVar - j].
        int start = VarOffset(row, varTableStart, numVarCols - variableIndex);
        int end = VarOffset(row, varTableStart, numVarCols - variableIndex - 1);
        return row[start..end];
    }

    private static int VarOffset(ReadOnlySpan<byte> row, int varTableStart, int entry) =>
        BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(varTableStart + entry * 2, 2));

    private static bool IsPresent(ReadOnlySpan<byte> nullBitmap, int columnId) =>
        (nullBitmap[columnId >> 3] & (1 << (columnId & 7))) != 0;
}
