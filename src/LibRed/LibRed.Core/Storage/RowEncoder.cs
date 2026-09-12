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
/// A calculated column is the exception, because its value is derived here rather than supplied: an
/// oversized result is spilled through the <c>spillCalculated</c> callback the writer passes in.
/// </summary>
public sealed class RowEncoder(IReadOnlyList<ColumnDef> columns, JetFormatBase format,
    int? fixedDataLength = null, int? variableColumnCount = null,
    Func<ColumnDef, byte[], byte[]>? spillCalculated = null)
{
    private readonly IReadOnlyList<ColumnDef> _columns = columns;
    private readonly JetFormatBase _format = format;

    // How an oversized calculated result reaches an LVAL page, returning the in-row descriptor. A calculated
    // column is the one value the encoder DERIVES rather than receives, so it cannot have been materialised
    // before the row arrived here the way a plain memo is; the writer that owns the pages supplies this
    // instead. Null for a standalone encode, which has no channel to write to and so must refuse.
    private readonly Func<ColumnDef, byte[], byte[]>? _spillCalculated = spillCalculated;

    // How many variable slots a row carries. The TDEF's 0x2B when the caller has it — a high-water that
    // never decrements — else the tight maximum over the live columns, which is the same number until the
    // LAST variable column is dropped and is all a standalone encode can know.
    private readonly int? _variableColumnCount = variableColumnCount;

    // Fixed (non-boolean) columns occupy a contiguous region; its length is defined by the
    // table definition. Default to the tight max so a standalone encode round-trips; INSERT
    // passes the TDEF's actual fixed-row size so the on-disk layout matches Access.
    private readonly int _fixedDataLength = fixedDataLength ?? ComputeFixedDataLength(columns);

    public byte[] Encode(object?[] values) => Encode(values, null);

    /// <summary>Encodes a row, recomputing its calculated columns.</summary>
    /// <param name="preservedCalculated">Envelopes to carry over verbatim, keyed by
    /// <see cref="ColumnDef.Index"/>. An UPDATE that touches nothing a calculated column reads must leave
    /// the cached value exactly as it was: ACE recomputes only when a referenced column is written, so
    /// recomputing unconditionally would write bytes ACE would not have (§3.4a). Null on INSERT, where every
    /// calculated column is computed fresh.</param>
    public byte[] Encode(object?[] values, IReadOnlyDictionary<int, byte[]>? preservedCalculated)
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
            if (column.IsCalculated)
            {
                varChunks[column.VariableIndex] = EncodeCalculated(column, values, preservedCalculated);
                continue;
            }
            object? v = values[column.Index];
            varChunks[column.VariableIndex] = v is null ? [] : JetTypeCodec.Encode(column, v);
        }

        return AssembleRow(maxColumnId, fixedRegion, varChunks, _columns, values);
    }

    /// <summary>The slot for a calculated column: the envelope holding the result, wrapped in a long-value
    /// descriptor when the column owns a long-value map (which is how ACE stores a calculated Memo).</summary>
    private byte[] EncodeCalculated(ColumnDef column, object?[] values,
        IReadOnlyDictionary<int, byte[]>? preservedCalculated)
    {
        if (preservedCalculated is not null && preservedCalculated.TryGetValue(column.Index, out byte[]? kept))
            return kept;

        byte[] envelope = CalculatedValue.Encode(column, CalculatedValue.Evaluate(column, _columns, values));
        if (!column.HasLongValueMap) return envelope;

        // A memo-backed result inlines while it fits and spills to an LVAL page once it does not — the same
        // 64-byte boundary a plain memo uses, and for the same reason: Access reads an inlined long value
        // back but refuses one that should have been on a page. What goes on the page is the WHOLE envelope,
        // header and padding included, because that is what the in-row slot would otherwise have held.
        if (envelope.Length <= LongValueFormat.MaxInlineValue)
            return JetTypeCodec.EncodeInlineLongValue(envelope);

        return _spillCalculated is not null
            ? _spillCalculated(column, envelope)
            : throw new NotSupportedException(
                $"Calculated column '{column.Name}' produced a {envelope.Length}-byte value, too large to "
                + "store inline. Encoding it needs a writer that can allocate long-value pages, which a "
                + "standalone RowEncoder has no channel for — insert or update through RowInserter.");
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
        // A calculated column is exempt from the declared-width check: its length field is a constant ACE
        // writes (39 for a value type, 509 for any Text, whatever size was asked for), not a limit — a
        // Text(5) calculated column stores a far longer result and ACE reads it back in full (§3.4a).
        foreach (ColumnDef column in columns)
            if (!column.IsFixedLength && !column.IsCalculated
                && column.VariableIndex >= 0 && column.VariableIndex < varChunks.Count)
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
            // A calculated column is always present, even when its expression evaluated to Null — ACE marks
            // the bit and stores a zero-length payload, so here the bit means "has an envelope" and says
            // nothing about the value (§3.4a). It is also why a calculated Boolean cannot use the bit as its
            // value the way a real one does.
            bool present = column.IsCalculated
                || (column.Type == JetDataType.Boolean ? IsTruthy(values[column.Index]) : values[column.Index] is not null);
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
