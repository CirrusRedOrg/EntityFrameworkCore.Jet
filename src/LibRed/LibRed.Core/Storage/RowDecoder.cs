using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage.Types;
using System.Buffers.Binary;

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
/// <remarks>
/// <para><b>Decoding a long value needs <paramref name="longValues"/>, and says so.</b> A memo or OLE column
/// holds a 12-byte descriptor rather than its data, so without page access the decode can only hand back the
/// pointer — a <c>byte[]</c> where the caller expects a string, which is an error nowhere and arrives at an
/// index key encoder as one. Rather than return it, <see cref="Decode"/> refuses. The reader stays optional
/// because a row with no long value in it needs no pages, which is what lets the row codec be tested without
/// a file; it is only the value that cannot be faked.</para>
/// <para>The two operations that want the stored descriptors rather than the values —
/// <c>LongValueDescriptors</c> and <c>CalculatedSlots</c> — are static. They need no reader, so
/// they are not reached through an instance that might lack one, and no caller has to decide what an
/// instance "mode" means. Each takes either the row alone or a <see cref="RowLayout"/> the caller has already
/// parsed, so a caller wanting both off one row derives the trailer arithmetic once.</para>
/// </remarks>
/// <param name="columns">The table's columns.</param>
/// <param name="format">The file's format.</param>
/// <param name="longValues">Page access for memo/OLE values; see the remarks.</param>
/// <param name="decode">Which columns to decode, by <see cref="ColumnDef.Index"/>, or null for all of them. A
/// column left out is null in every row, whatever is stored — so this is only for a reader that provably never
/// looks at it (a query that does not name it), never for a row that is going to be written back.</param>
public sealed class RowDecoder(
    IReadOnlyList<ColumnDef> columns, JetFormatBase format, LongValueReader? longValues = null, bool[]? decode = null)
{
    private readonly bool[]? _decode = decode;

    private readonly JetFormatBase _format = format;
    private readonly LongValueReader? _longValues = longValues;

    // Decode runs once per row, so it walks an array rather than enumerating the interface (a boxed
    // enumerator per row), and answers RowLayout.HasVariableSection from the lowest variable column id
    // instead of re-scanning the columns: a row has a variable section exactly when its stored count
    // reaches past that id.
    private readonly ColumnDef[] _columnArray = [.. columns];
    private readonly int _lowestVariableId = LowestVariableId(columns);

    private static readonly object BoxedTrue = true;
    private static readonly object BoxedFalse = false;

    private static int LowestVariableId(IReadOnlyList<ColumnDef> columns)
    {
        int lowest = int.MaxValue;
        foreach (ColumnDef column in columns)
            if (!column.IsFixedLength && column.ColumnId < lowest)
                lowest = column.ColumnId;
        return lowest;
    }

    /// <summary>Decodes the row into one value per column (aligned to <see cref="ColumnDef.Index"/>).</summary>
    public object?[] Decode(ReadOnlySpan<byte> row)
    {
        var values = new object?[_columnArray.Length];

        // The null-bitmap width comes from the row's own leading count (= max id + 1), NOT the live column
        // count — they differ once ids have a gap (a burned type-change id / DROP COLUMN gap). Reading the
        // stored count is exactly how ACE sizes it, and is robust to any id scheme (spec §5). A real inline
        // row is at least: column count + an empty var table (1 entry) + var count + null bitmap; anything
        // shorter is an overflow/lookup pointer slot the caller should have skipped.
        if (row.Length < _format.RowColumnCountSize)
            throw new InvalidDataException("Row is too short to be an inline record.");
        bool hasVar = _lowestVariableId < BinaryPrimitives.ReadUInt16LittleEndian(row);
        RowLayout layout = RowLayout.Parse(row, _format.RowColumnCountSize, hasVar);
        int nullBitmapSize = layout.NullBitmapSize;

        ReadOnlySpan<byte> nullBitmap = row[^nullBitmapSize..];

        foreach (ColumnDef column in _columnArray)
        {
            if (_decode is not null && !_decode[column.Index])
                continue;

            bool present = IsPresent(nullBitmap, layout.ColumnCount, column.ColumnId);

            // Jet stores Boolean (YesNo) columns with no fixed/variable data: the value
            // IS the null-bitmap bit (set = true). Booleans are never null.
            //
            // A CALCULATED Boolean is the exception, and a silent one: it carries a real variable-length
            // slot, and its bitmap bit means "present" like every other calculated column, so it is set
            // for a stored False too. Reading the bit here would report True for every row.
            if (column.Type == JetDataType.Boolean && !column.IsCalculated)
            {
                values[column.Index] = present ? BoxedTrue : BoxedFalse;
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

            // A calculated column stores ACE's cached result wrapped in an envelope, routed through a
            // long-value descriptor when the column owns a long-value map (which is how a calculated Memo
            // arrives — ACE declares it Text, so the Memo branch below would never fire for it).
            if (column.IsCalculated)
            {
                ReadOnlySpan<byte> envelope = column.HasLongValueMap
                    ? Reader(column).Resolve(raw)
                    : raw;
                values[column.Index] = CalculatedValue.Decode(column, envelope);
                continue;
            }

            // Memo / OLE columns store a long-value descriptor, not the data itself.
            if (column.Type is JetDataType.Memo or JetDataType.Ole)
            {
                byte[] data = Reader(column).Resolve(raw);
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
    public static Dictionary<int, byte[]> LongValueDescriptors(
        IReadOnlyList<ColumnDef> columns, JetFormatBase format, ReadOnlySpan<byte> row) =>
        LongValueDescriptors(columns, ParseLayout(columns, format, row), row);

    /// <inheritdoc cref="LongValueDescriptors(IReadOnlyList{ColumnDef}, JetFormatBase, ReadOnlySpan{byte})"/>
    /// <remarks>Takes a layout the caller has already parsed — an UPDATE wants this and
    /// <see cref="CalculatedSlots(IReadOnlyList{ColumnDef}, RowLayout, ReadOnlySpan{byte})"/> off one row, and
    /// <see cref="RowLayout"/> exists so that arithmetic is done once.</remarks>
    internal static Dictionary<int, byte[]> LongValueDescriptors(
        IReadOnlyList<ColumnDef> columns, RowLayout layout, ReadOnlySpan<byte> row)
    {
        var result = new Dictionary<int, byte[]>();
        int nullBitmapSize = layout.NullBitmapSize;

        ReadOnlySpan<byte> nullBitmap = row[^nullBitmapSize..];

        // Keyed on owning a long-value map rather than on the declared type: a calculated Memo is declared
        // Text and still stores a descriptor, so a type test alone walks past its pages and orphans them.
        foreach (ColumnDef column in columns)
            if ((column.Type is JetDataType.Memo or JetDataType.Ole || column.HasLongValueMap)
                && IsPresent(nullBitmap, layout.ColumnCount, column.ColumnId))
                result[column.Index] = layout.VarChunk(column.VariableIndex).ToArray();

        return result;
    }

    /// <summary>Returns each calculated column's stored slot verbatim (keyed by <see cref="ColumnDef.Index"/>)
    /// — the envelope, or the long-value descriptor wrapping it — WITHOUT decoding or resolving it. An UPDATE
    /// that touches nothing the expression reads writes these back unchanged, which is what ACE does.</summary>
    public static Dictionary<int, byte[]> CalculatedSlots(
        IReadOnlyList<ColumnDef> columns, JetFormatBase format, ReadOnlySpan<byte> row) =>
        CalculatedSlots(columns, ParseLayout(columns, format, row), row);

    /// <inheritdoc cref="CalculatedSlots(IReadOnlyList{ColumnDef}, JetFormatBase, ReadOnlySpan{byte})"/>
    /// <remarks>Takes a layout the caller has already parsed; see the
    /// <see cref="LongValueDescriptors(IReadOnlyList{ColumnDef}, RowLayout, ReadOnlySpan{byte})"/> overload.</remarks>
    internal static Dictionary<int, byte[]> CalculatedSlots(
        IReadOnlyList<ColumnDef> columns, RowLayout layout, ReadOnlySpan<byte> row)
    {
        var result = new Dictionary<int, byte[]>();
        ReadOnlySpan<byte> nullBitmap = row[^layout.NullBitmapSize..];

        foreach (ColumnDef column in columns)
            if (column.IsCalculated && !column.IsFixedLength
                && IsPresent(nullBitmap, layout.ColumnCount, column.ColumnId)
                && column.VariableIndex >= 0 && column.VariableIndex < layout.NumVar)
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

    /// <summary>The long-value reader, or a refusal naming the column that needed it. Silently returning the
    /// descriptor instead is the failure this exists to prevent: it is a <c>byte[]</c> that looks like a
    /// value, and it travels — into an index key, into a comparison, into another row — before anything
    /// notices.</summary>
    private LongValueReader Reader(ColumnDef column) =>
        _longValues ?? throw new InvalidOperationException(
            $"Column '{column.Name}' stores its value on long-value pages, so decoding it needs a "
            + $"{nameof(LongValueReader)}; this {nameof(RowDecoder)} was constructed without one. "
            + $"Use {nameof(LongValueDescriptors)} to read the stored descriptors instead.");

    internal static RowLayout ParseLayout(IReadOnlyList<ColumnDef> columns, JetFormatBase format, ReadOnlySpan<byte> row)
    {
        if (row.Length < format.RowColumnCountSize)
            throw new InvalidDataException("Row is too short to be an inline record.");
        return RowLayout.Parse(row, format.RowColumnCountSize, RowLayout.HasVariableSection(row, columns));
    }

    private static bool IsPresent(ReadOnlySpan<byte> nullBitmap, int storedColumnCount, int columnId) =>
        columnId >= 0 && columnId < storedColumnCount
        && (nullBitmap[columnId >> 3] & (1 << (columnId & 7))) != 0;
}