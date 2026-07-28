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
/// Jet 4 / ACE uses 2-byte variable offsets at any row size — there is no Jet 3-style
/// jump table (1-byte offsets), so rows larger than 256 bytes decode the same way.
/// </summary>
public sealed class RowDecoder(IReadOnlyList<ColumnDef> columns, JetFormatBase format, LongValueReader? longValues = null)
{
    private readonly IReadOnlyList<ColumnDef> _columns = columns;
    private readonly JetFormatBase _format = format;
    private readonly LongValueReader? _longValues = longValues;

    /// <summary>Decodes the row into one value per column (aligned to <see cref="ColumnDef.Index"/>).</summary>
    public object?[] Decode(ReadOnlySpan<byte> row)
    {
        var values = new object?[_columns.Count];

        // The null-bitmap width comes from the row's own leading count (= max id + 1), NOT the live column
        // count — they differ once ids have a gap (a burned type-change id / DROP COLUMN gap). Reading the
        // stored count is exactly how ACE sizes it, and is robust to any id scheme (spec §5). A real inline
        // row is at least: column count + an empty var table (1 entry) + var count + null bitmap; anything
        // shorter is an overflow/lookup pointer slot the caller should have skipped.
        RowLayout layout = ParseLayout(row);
        int nullBitmapSize = layout.NullBitmapSize;

        ReadOnlySpan<byte> nullBitmap = row[^nullBitmapSize..];

        foreach (ColumnDef column in _columns)
        {
            bool present = IsPresent(nullBitmap, layout.ColumnCount, column.ColumnId);

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
                ? FixedSlice(row, layout, column)
                : layout.VarChunk(column.VariableIndex);

            // Memo / OLE columns store a long-value descriptor, not the data itself.
            if (_longValues is not null && column.Type is JetDataType.Memo or JetDataType.Ole)
            {
                byte[] data = _longValues.Resolve(raw);
                values[column.Index] = column.Type == JetDataType.Memo
                    ? JetTypeCodec.DecodeText(data)
                    : data;
                continue;
            }

            values[column.Index] = JetTypeCodec.Decode(column, raw);
        }

        return values;
    }

    /// <summary>Returns the raw in-row long-value descriptor bytes for each present memo/OLE column (keyed by
    /// <see cref="ColumnDef.Index"/>), WITHOUT resolving the value. Used by UPDATE/DELETE to preserve an
    /// unchanged column's descriptor verbatim (avoiding a needless re-materialise) and to free a replaced or
    /// deleted value's LVAL pages.</summary>
    public Dictionary<int, byte[]> LongValueRaw(ReadOnlySpan<byte> row)
    {
        var result = new Dictionary<int, byte[]>();
        RowLayout layout = ParseLayout(row);
        int nullBitmapSize = layout.NullBitmapSize;

        ReadOnlySpan<byte> nullBitmap = row[^nullBitmapSize..];

        foreach (ColumnDef column in _columns)
            if (column.Type is JetDataType.Memo or JetDataType.Ole
                && IsPresent(nullBitmap, layout.ColumnCount, column.ColumnId))
                result[column.Index] = layout.VarChunk(column.VariableIndex).ToArray();

        return result;
    }

    private ReadOnlySpan<byte> FixedSlice(ReadOnlySpan<byte> row, RowLayout layout, ColumnDef column)
    {
        int start = _format.RowColumnCountSize + column.FixedOffset;
        long end = (long)start + column.Length;
        if (column.FixedOffset < 0 || column.Length < 0
            || end > _format.RowColumnCountSize + (long)layout.FixedRegionLength)
            throw new InvalidDataException(
                $"Row fixed value for column '{column.Name}' extends outside the fixed-data region.");
        return row.Slice(start, column.Length);
    }

    private RowLayout ParseLayout(ReadOnlySpan<byte> row)
    {
        if (row.Length < _format.RowColumnCountSize)
            throw new InvalidDataException("Row is too short to be an inline record.");
        int storedCount = BinaryPrimitives.ReadUInt16LittleEndian(row[.._format.RowColumnCountSize]);
        bool hasVar = _columns.Any(c => !c.IsFixedLength && c.ColumnId < storedCount);
        return RowLayout.Parse(row, _format.RowColumnCountSize, hasVar);
    }

    private static bool IsPresent(ReadOnlySpan<byte> nullBitmap, int storedColumnCount, int columnId) =>
        columnId >= 0 && columnId < storedColumnCount
        && (nullBitmap[columnId >> 3] & (1 << (columnId & 7))) != 0;
}
