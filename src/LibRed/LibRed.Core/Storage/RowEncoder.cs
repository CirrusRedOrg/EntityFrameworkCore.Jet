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
/// (12-byte descriptor + payload, §8) when it is small enough; anything larger is stored on LVAL
/// pages by <see cref="RowInserter"/> before the row reaches here, so only the descriptor is encoded.
/// </summary>
public sealed class RowEncoder(IReadOnlyList<ColumnDef> columns, JetFormatBase format,
    int? fixedDataLength = null, int? variableColumnCount = null)
{
    private readonly IReadOnlyList<ColumnDef> _columns = columns;
    private readonly JetFormatBase _format = format;

    // How many variable slots a row carries. The TDEF's 0x2B when the caller has it — a high-water that
    // never decrements — else the tight maximum over the live columns, which is the same number until the
    // LAST variable column is dropped and is all a standalone encode can know.
    private readonly int? _variableColumnCount = variableColumnCount;

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

        // And the variable section is addressed the same way: by VariableIndex, NOT by position among the
        // live variable columns. DROP COLUMN leaves a hole in the index space — the TDEF's 0x2B count is a
        // high-water mark that never decrements, and a column added later takes the next index above it — so
        // packing the chunks densely puts every column after the hole one slot too low. The decoder reads
        // VarChunk(column.VariableIndex) and so does ACE, which is what makes it silent: the row is written
        // and read back happily by nothing at all.
        var varCols = _columns.Where(c => !c.IsFixedLength).ToList();
        int numVar = varCols.Count == 0 ? 0
            : Math.Max(_variableColumnCount ?? 0, varCols.Max(c => c.VariableIndex) + 1);

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
        Array.Fill(varChunks, []);          // a dropped column's slot stays present, and empty
        foreach (ColumnDef column in varCols)
        {
            object? v = values[column.Index];
            varChunks[column.VariableIndex] = v is null ? [] : JetTypeCodec.Encode(column, v);
        }

        return AssembleRow(maxColumnId, fixedRegion, varChunks, _columns, values);
    }

    /// <summary>Rejects a variable TEXT/BINARY value longer than its column's declared width, as ACE does
    /// (measured in <c>ColumnLengthAccessTests</c>); without it LibRed wrote rows Access will not read back.
    /// Memo/OLE are exempt — they encode to a long-value descriptor whose size is unrelated to
    /// <see cref="ColumnDef.Length"/>. Fixed columns are checked by <see cref="JetTypeCodec.EnsureFitsFixedWidth"/>
    /// before the codec pads them, since padding to width would otherwise hide an over-long value.</summary>
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
    /// <remarks>
    /// The declared-width check runs HERE rather than in <see cref="Encode"/>. It used to sit above this call,
    /// which meant the ALTER COLUMN re-lay — the other caller — never got it, and a narrowing retype could
    /// write rows Access refuses. A guard that both paths must pass through belongs on the shared path.
    /// </remarks>
    internal static byte[] AssembleRow(int maxColumnId, ReadOnlySpan<byte> fixedRegion,
        IReadOnlyList<byte[]> varChunks, IReadOnlyList<ColumnDef> columns, object?[] values)
    {
        // A calculated column's slot is not a value but an envelope holding ACE's cached result
        // (page-02b §3.4a), and only ACE can evaluate the expression that fills it. Encoding the decoded
        // value back would write a bare value into that slot, which ACE then reads as a corrupt envelope.
        // Refusing is the honest outcome until LibRed can evaluate the expression itself: LibRed reads
        // these tables, and until this guard existed reading them was impossible anyway.
        foreach (ColumnDef column in columns)
            if (column.IsCalculated)
                throw new NotSupportedException(
                    $"Table has calculated column '{column.Name}', whose stored value only ACE can compute. " +
                    "LibRed reads calculated columns but cannot write a row that contains one.");

        foreach (ColumnDef column in columns)
            if (!column.IsFixedLength && column.VariableIndex >= 0 && column.VariableIndex < varChunks.Count)
                EnsureFitsDeclaredLength(column, varChunks[column.VariableIndex]);

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
