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

        // The leading count and the null-bitmap width are driven by the highest column id + 1, NOT the live
        // column count — the two coincide only while ids are contiguous (fresh table / ADD COLUMN), and diverge
        // once ids have a gap (a burned type-change id, or a DROP COLUMN gap). Verified vs ACE (spec §5).
        // AssembleRow derives both from this.
        int maxColumnId = _columns.Count == 0 ? -1 : _columns.Max(c => c.ColumnId);

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
            ColumnDef column = varCols[j];
            object? v = values[column.Index];
            varChunks[j] = v is null ? [] : JetTypeCodec.Encode(column, v);
            EnsureFitsDeclaredLength(column, varChunks[j]);
        }

        return AssembleRow(maxColumnId, fixedRegion, varChunks, _columns, values);
    }

    /// <summary>Rejects a variable TEXT/BINARY value longer than its column's declared width, as ACE does
    /// (measured in <c>ColumnLengthAccessTests</c>); without it LibRed wrote rows Access will not read back.
    /// Memo/OLE are exempt — they encode to a long-value descriptor whose size is unrelated to
    /// <see cref="ColumnDef.Length"/>. Fixed columns need no equivalent: the codec pads or truncates them to
    /// width, and the caller then checks for exactly that width.</summary>
    private static void EnsureFitsDeclaredLength(ColumnDef column, byte[] encoded)
    {
        if (column.Type is not (JetDataType.Text or JetDataType.Binary)) return;
        if (column.Length <= 0 || encoded.Length <= column.Length) return;

        // Report in the column's own units: TEXT declares characters and stores UTF-16, BINARY declares bytes.
        bool text = column.Type == JetDataType.Text;
        int declared = text ? column.Length / 2 : column.Length;
        int actual = text ? encoded.Length / 2 : encoded.Length;
        throw new InvalidOperationException(
            $"The field '{column.Name}' is too small to accept the amount of data you attempted to add: "
            + $"{actual} {(text ? "characters" : "bytes")} into a column declared to hold {declared}.");
    }

    /// <summary>Assembles the on-disk row bytes from a prepared fixed region and the ordered variable chunks:
    /// <c>[count][fixed][var data][var-offset table][numVar]</c> (the variable section is omitted entirely when
    /// there are none) then <c>[null bitmap]</c>. The count and bitmap width are <c>maxColumnId + 1</c>; a
    /// column's bit is set when present (Boolean = its truthy value), and dead ids (gaps below the max, from a
    /// burned/dropped id) are set present too — all verified vs ACE (§5). Shared by <see cref="Encode"/> and
    /// the ALTER COLUMN row re-lay so the two can never drift.</summary>
    internal static byte[] AssembleRow(int maxColumnId, ReadOnlySpan<byte> fixedRegion,
        IReadOnlyList<byte[]> varChunks, IReadOnlyList<ColumnDef> columns, object?[] values)
    {
        const int countSize = 2;
        int count = maxColumnId + 1;
        int nullBitmapSize = (count + 7) / 8;
        int numVar = varChunks.Count;
        int varDataLength = 0;
        for (int j = 0; j < numVar; j++) varDataLength += varChunks[j].Length;
        int varSectionLen = numVar > 0 ? varDataLength + (numVar + 1) * 2 + 2 : 0;

        var row = new byte[countSize + fixedRegion.Length + varSectionLen + nullBitmapSize];
        BinaryPrimitives.WriteUInt16LittleEndian(row, (ushort)count);
        fixedRegion.CopyTo(row.AsSpan(countSize));

        int bitmapPos;
        if (numVar > 0)
        {
            int varDataStart = countSize + fixedRegion.Length;
            int pos = varDataStart;
            for (int j = 0; j < numVar; j++) { varChunks[j].CopyTo(row.AsSpan(pos)); pos += varChunks[j].Length; }

            // End-first offset table: entry[numVar] = var-data start, entry[numVar-j-1] = end of var col j.
            int tableStart = pos;
            BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(tableStart + numVar * 2, 2), (ushort)varDataStart);
            int running = varDataStart;
            for (int j = 0; j < numVar; j++)
            {
                running += varChunks[j].Length;
                BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(tableStart + (numVar - j - 1) * 2, 2), (ushort)running);
            }
            int numVarPos = tableStart + (numVar + 1) * 2;
            BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(numVarPos, 2), (ushort)numVar);
            bitmapPos = numVarPos + 2;
        }
        else bitmapPos = countSize + fixedRegion.Length;

        var liveIds = new HashSet<int>();
        foreach (ColumnDef column in columns)
        {
            liveIds.Add(column.ColumnId);
            bool present = column.Type == JetDataType.Boolean ? IsTruthy(values[column.Index]) : values[column.Index] is not null;
            if (present) row[bitmapPos + (column.ColumnId >> 3)] |= (byte)(1 << (column.ColumnId & 7));
        }
        for (int id = 0; id <= maxColumnId; id++)   // dead ids read present in ACE
            if (!liveIds.Contains(id))
                row[bitmapPos + (id >> 3)] |= (byte)(1 << (id & 7));
        return row;
    }

    /// <summary>Access truthiness for a Boolean (bit) value being stored: a bool is itself, any non-zero
    /// number is true, 0 / null is false.</summary>
    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        _ => Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture),
    };

    private static int ComputeFixedDataLength(IReadOnlyList<ColumnDef> columns)
    {
        int length = 0;
        foreach (ColumnDef c in columns)
            if (c.IsFixedLength && c.Type != JetDataType.Boolean)
                length = Math.Max(length, c.FixedOffset + c.Length);
        return length;
    }
}
