using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage.Types;
using System.Buffers.Binary;

namespace LibRed.Storage;

/// <summary>
/// Encodes and decodes CLR values and owns the Jet 4 / ACE inline row layout and declaration limits.
/// The null bitmap marks present (non-null) columns; a Boolean column has no data and its
/// bit carries the value. Variable columns are laid out in ascending VariableIndex order with
/// an end-first offset table. A memo/OLE column's value is written as an *inline* long-value
/// (12-byte descriptor + payload, §8) when it is small enough; anything larger is stored on LVAL
/// pages by <see cref="RowInserter"/> before the row reaches here, so only the descriptor is encoded.
/// A calculated column is the exception, because its value is derived here rather than supplied: an
/// oversized result is spilled through the <c>spillCalculated</c> callback the writer passes in.
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
/// instance "mode" means. Each takes either the row alone or a <see cref="Layout"/> the caller has already
/// parsed, so a caller wanting both off one row derives the trailer arithmetic once.</para>
/// </remarks>
public sealed class RowCodec(IReadOnlyList<ColumnDef> columns, JetFormatBase format,
    int? fixedDataLength = null, int? variableColumnCount = null,
    Func<ColumnDef, byte[], byte[]>? spillCalculated = null, int? columnIdHighWater = null,
    LongValueStore? longValues = null, bool[]? decode = null)
{
    private readonly IReadOnlyList<ColumnDef> _columns = columns;

    // The row's field sizes, the long-value descriptor sizes and the inline limit all come from here.
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

    // How many column ids the row's leading count and null bitmap span: the TDEF's 0x29 high-water when the
    // caller has it, which is what ACE writes even after the highest-id column has been dropped — else the
    // highest live id + 1, all a standalone encode can know.
    private readonly int? _columnIdHighWater = columnIdHighWater;

    // Fixed (non-boolean) columns occupy a contiguous region; its length is defined by the
    // table definition. Default to the tight max so a standalone encode round-trips; INSERT
    // passes the TDEF's actual fixed-row size so the on-disk layout matches Access.
    private readonly int _fixedDataLength = fixedDataLength ?? ComputeFixedDataLength(columns);

    public byte[] Encode(object?[] values) => Encode(values, null);

    /// <summary>Encodes a row, recomputing its calculated columns.</summary>
    /// <param name="values">The row's values, one per column in table order.</param>
    /// <param name="preservedCalculated">Envelopes to carry over verbatim, keyed by
    /// <see cref="ColumnDef.Index"/>. An UPDATE that touches nothing a calculated column reads must leave
    /// the cached value exactly as it was: ACE recomputes only when a referenced column is written, so
    /// recomputing unconditionally would write bytes ACE would not have (§3.4a). Null on INSERT, where every
    /// calculated column is computed fresh.</param>
    /// <param name="logicalValues">The row as its columns hold it, for the calculated columns to read, when
    /// <paramref name="values"/> already carries long values as their on-disk descriptors. A memo's text is
    /// what an expression reads, not the descriptor that points at it. Null when the two are the same.</param>
    public byte[] Encode(object?[] values, IReadOnlyDictionary<int, byte[]>? preservedCalculated,
        object?[]? logicalValues = null)
    {
        if (values.Length != _columns.Count)
            throw new ArgumentException($"Expected {_columns.Count} values, got {values.Length}.", nameof(values));

        // The leading count and the null-bitmap width span every column id the table has ever handed out — the
        // TDEF's 0x29 high-water — NOT the live column count, and not the highest live id either. The three
        // coincide while ids are contiguous (fresh table / ADD COLUMN) and diverge once one is dead: a burned
        // type-change id, or a DROP COLUMN gap. Measured vs ACE: after dropping the only other column of a
        // two-column table, ACE still writes count 2 (spec §5). AssembleRow derives the bitmap width from this.
        int maxColumnId = Math.Max(
            (_columnIdHighWater ?? 0) - 1,
            _columns.Count == 0 ? -1 : _columns.Max(c => c.ColumnId));

        // And the variable section is addressed the same way: by VariableIndex, NOT by position among the
        // live variable columns. DROP COLUMN leaves a hole in the index space — the TDEF's 0x2B count is a
        // high-water mark that never decrements, and a column added later takes the next index above it — so
        // packing the chunks densely puts every column after the hole one slot too low. The decoder reads
        // VarChunk(column.VariableIndex) and so does ACE, which is what makes it silent: the row is written
        // and read back happily by nothing at all.
        // The trailer survives the loss of the last variable column, too: ACE keeps writing it, with numVar at
        // the 0x2B high-water, once a table has ever had one (measured — a table whose only TEXT column was
        // dropped still gets `06 00 06 00 | 01 00`). Only a table that never had one has no trailer at all.
        var varCols = _columns.Where(c => !c.IsFixedLength).ToList();
        int numVar = Math.Max(
            _variableColumnCount ?? 0,
            varCols.Count == 0 ? 0 : varCols.Max(c => c.VariableIndex) + 1);

        // Encode each region's payload first so we can size the row exactly.
        var fixedRegion = new byte[_fixedDataLength];
        foreach (ColumnDef column in _columns)
        {
            if (column.Type == JetDataType.Boolean || !column.IsFixedLength) continue;
            object? v = values[column.Index];
            if (v is null) continue; // null fixed value: leave its slot zeroed, clear the bit below
            byte[] encoded = JetTypeCodec.Encode(column, v, _format);
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
                varChunks[column.VariableIndex] = EncodeCalculated(column, logicalValues ?? values, preservedCalculated);
                continue;
            }
            object? v = values[column.Index];
            varChunks[column.VariableIndex] = v is null ? [] : JetTypeCodec.Encode(column, v, _format);
        }

        return AssembleRow(_format, maxColumnId, fixedRegion, varChunks, _columns, values);
    }

    /// <summary>The slot for a calculated column: the envelope holding the result, wrapped in a long-value
    /// descriptor when the column owns a long-value map (which is how ACE stores a calculated Memo).</summary>
    private byte[] EncodeCalculated(ColumnDef column, object?[] values,
        IReadOnlyDictionary<int, byte[]>? preservedCalculated)
    {
        if (preservedCalculated is not null && preservedCalculated.TryGetValue(column.Index, out byte[]? kept))
            return kept;

        byte[] envelope = CalculatedValue.Encode(column, CalculatedValue.Evaluate(column, _columns, values), _format);
        if (!column.HasLongValueMap) return envelope;

        // A memo-backed result inlines while it fits and spills to an LVAL page once it does not — the same
        // 64-byte boundary a plain memo uses, and for the same reason: Access reads an inlined long value
        // back but refuses one that should have been on a page. What goes on the page is the WHOLE envelope,
        // header and padding included, because that is what the in-row slot would otherwise have held.
        if (envelope.Length <= _format.LongValueMaxInline)
            return JetTypeCodec.EncodeInlineLongValue(envelope, _format);

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
        if (column.Type is not (JetDataType.Text or JetDataType.Binary or JetDataType.BigBinary)) return;
        if (column.Length <= 0) return;

        // In the column's own units: TEXT declares characters, BINARY bytes. A TEXT value is counted by
        // decoding it, because WITH COMPRESSION stores a Latin-1 value one byte per character — counted in
        // bytes, a TEXT(5) would take 8 characters, where ACE refuses the sixth. Neither encoding spends
        // fewer than one byte per character, so a value within the declared count of BYTES is within the
        // declared count of characters and needs no decoding — which is every ordinary value.
        bool text = column.Type == JetDataType.Text;
        int declared = text ? column.Length / 2 : column.Length;
        if (encoded.Length <= declared) return;

        int actual = text ? JetTypeCodec.DecodeText(encoded).Length : encoded.Length;
        if (actual <= declared) return;
        throw new InvalidOperationException(
            $"The field '{column.Name}' is too small to accept the amount of data you attempted to add: "
            + $"{actual} {(text ? "characters" : "bytes")} into a column declared to hold {declared}.");
    }

    /// <summary>
    /// Smallest fixed region ACE writes in a row that has no variable trailer. It is a <b>floor, not an
    /// alignment</b>: measured against ACE, a region of 0 bytes (a table of only Booleans, which occupy none)
    /// is padded to 2 and 1 byte (a lone <c>BYTE</c> column) to 2, while 3 stays 3 — a three-<c>BYTE</c> table's
    /// row is 6 bytes, odd region and all. A row that carries a variable trailer is exempt: ACE leaves a
    /// <c>TEXT</c>-only table's fixed region at 0.
    /// </summary>
    /// <remarks>
    /// Matching it is not cosmetic. Without the pad an all-Boolean table of eight columns or fewer encodes to a
    /// 3-byte record, and <b>ACE misreads that record</b> — every Boolean in it comes back False, whichever
    /// engine created the table. Measured both ways round: ACE's own table filled by LibRed read False, and
    /// LibRed's table filled by ACE read True, which is what pins the fault to the record rather than the TDEF.
    /// The cliff is at 4 bytes — a 16-Boolean row (2-byte bitmap, so 4 bytes) reads back correctly — but ACE's
    /// own writer never emits a record under 5, so the short form is simply a shape its reader has never met.
    /// The TDEF's fixed-row length keeps the true, unpadded value: ACE stores 1 for a <c>BYTE</c> table while
    /// writing 5-byte rows into it, so this rounding happens at row-write time and nowhere else.
    /// </remarks>
    private const int MinFixedRegion = 2;

    /// <summary>Assembles the on-disk row bytes from a prepared fixed region and the ordered variable chunks:
    /// <c>[count][fixed][var data][var-offset table][numVar]</c> (the variable section is omitted entirely when
    /// there are none) then <c>[null bitmap]</c>. The count and bitmap width are <c>maxColumnId + 1</c>; a
    /// column's bit is set when present (Boolean = its truthy value), and a dead id's (a gap below the max, from a
    /// burned/dropped id) is taken from <paramref name="priorBitmap"/> — the row's bitmap before an ALTER COLUMN
    /// re-lay — or left clear for a new row — all verified vs ACE (§5). Shared by <c>Encode</c> and the ALTER
    /// COLUMN row re-lay so the two can never drift.</summary>
    /// <remarks>
    /// The declared-width check runs HERE rather than in <c>Encode</c>. It used to sit above this call,
    /// which meant the ALTER COLUMN re-lay — the other caller — never got it, and a narrowing retype could
    /// write rows Access refuses. A guard that both paths must pass through belongs on the shared path.
    /// </remarks>
    internal static byte[] AssembleRow(JetFormatBase format, int maxColumnId, ReadOnlySpan<byte> fixedRegion,
        IReadOnlyList<byte[]> varChunks, IReadOnlyList<ColumnDef> columns, object?[] values,
        ReadOnlySpan<byte> priorBitmap = default)
    {
        // A calculated column is exempt from the declared-width check: its length field is a constant ACE
        // writes (39 for a value type, 509 for any Text, whatever size was asked for), not a limit — a
        // Text(5) calculated column stores a far longer result and ACE reads it back in full (§3.4a).
        foreach (ColumnDef column in columns)
            if (!column.IsFixedLength && !column.IsCalculated
                && column.VariableIndex >= 0 && column.VariableIndex < varChunks.Count)
                EnsureFitsDeclaredLength(column, varChunks[column.VariableIndex]);

        int countSize = format.RowColumnCountSize;
        int offsetSize = format.RowVariableOffsetSize, numVarSize = format.RowVariableCountSize;
        int count = maxColumnId + 1;
        int nullBitmapSize = BitmapBits.ByteCount(count);
        int numVar = varChunks.Count;
        int varDataLength = 0;
        for (int j = 0; j < numVar; j++) varDataLength += varChunks[j].Length;
        int varSectionLen = numVar > 0 ? varDataLength + (numVar + 1) * offsetSize + numVarSize : 0;

        // ACE pads an all-fixed row's fixed region out to MinFixedRegion; a row with a variable trailer is
        // left alone. The pad is zero bytes between the fixed values and the null bitmap.
        int fixedLen = numVar > 0 ? fixedRegion.Length : Math.Max(fixedRegion.Length, MinFixedRegion);

        var row = new byte[countSize + fixedLen + varSectionLen + nullBitmapSize];
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(0, countSize), (ushort)count);
        fixedRegion.CopyTo(row.AsSpan(countSize));

        if (numVar > 0)
        {
            int varDataStart = countSize + fixedLen;
            int pos = varDataStart;
            for (int j = 0; j < numVar; j++) { varChunks[j].CopyTo(row.AsSpan(pos)); pos += varChunks[j].Length; }

            // End-first offset table: entry[numVar] = var-data start, entry[numVar-j-1] = end of var col j.
            int tableStart = pos;
            BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(tableStart + numVar * offsetSize, offsetSize), (ushort)varDataStart);
            int running = varDataStart;
            for (int j = 0; j < numVar; j++)
            {
                running += varChunks[j].Length;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    row.AsSpan(tableStart + (numVar - j - 1) * offsetSize, offsetSize), (ushort)running);
            }
            BinaryPrimitives.WriteUInt16LittleEndian(
                row.AsSpan(tableStart + (numVar + 1) * offsetSize, numVarSize), (ushort)numVar);
        }

        // The null bitmap ends the row whatever precedes it.
        Span<byte> nullBitmap = row.AsSpan(row.Length - nullBitmapSize);
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
            if (present) BitmapBits.Set(nullBitmap, column.ColumnId, true);
        }
        // A dead id's bit depends on which route wrote the row, and both are measured against ACE: the ALTER
        // COLUMN re-lay carries the old row's bit forward — set where the retyped column held a value, clear
        // where it was NULL — while a row INSERTED afterwards leaves it clear: the same statement pair gives
        // ACE 0x0F for a re-laid row with a value and 0x0D for the next insert.
        for (int id = 0; id <= maxColumnId && id < priorBitmap.Length * 8; id++)
            if (!liveIds.Contains(id) && BitmapBits.Get(priorBitmap, id))
                BitmapBits.Set(nullBitmap, id, true);
        return row;
    }

    /// <summary>Access truthiness for a Boolean (bit) value being stored: a bool is itself, any non-zero
    /// number is true, 0 / null is false. The index key uses the same rule, so a key always agrees with its row.</summary>
    internal static bool IsTruthy(object? value) => value switch
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

    private readonly bool[]? _decode = decode;

    private readonly LongValueStore? _longValues = longValues;

    // Decode runs once per row, so it walks an array rather than enumerating the interface (a boxed
    // enumerator per row), and answers Layout.HasVariableSection from the lowest variable column id
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
        bool hasVar = _lowestVariableId < Layout.ReadColumnCount(row, _format);
        Layout layout = Layout.Parse(row, _format, hasVar);

        foreach (ColumnDef column in _columnArray)
        {
            if (_decode is not null && !_decode[column.Index])
                continue;

            bool present = layout.IsPresent(column.ColumnId);

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
    /// <see cref="CalculatedSlots(IReadOnlyList{ColumnDef}, Layout, ReadOnlySpan{byte})"/> off one row, and
    /// <see cref="Layout"/> exists so that arithmetic is done once.</remarks>
    internal static Dictionary<int, byte[]> LongValueDescriptors(
        IReadOnlyList<ColumnDef> columns, Layout layout, ReadOnlySpan<byte> row)
    {
        var result = new Dictionary<int, byte[]>();

        // Keyed on owning a long-value map rather than on the declared type: a calculated Memo is declared
        // Text and still stores a descriptor, so a type test alone walks past its pages and orphans them.
        foreach (ColumnDef column in columns)
            if ((column.Type is JetDataType.Memo or JetDataType.Ole || column.HasLongValueMap)
                && layout.IsPresent(column.ColumnId))
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
    /// <see cref="LongValueDescriptors(IReadOnlyList{ColumnDef}, Layout, ReadOnlySpan{byte})"/> overload.</remarks>
    internal static Dictionary<int, byte[]> CalculatedSlots(
        IReadOnlyList<ColumnDef> columns, Layout layout, ReadOnlySpan<byte> row)
    {
        var result = new Dictionary<int, byte[]>();

        foreach (ColumnDef column in columns)
            if (column.IsCalculated && !column.IsFixedLength
                && layout.IsPresent(column.ColumnId)
                && column.VariableIndex >= 0 && column.VariableIndex < layout.NumVar)
                result[column.Index] = layout.VarChunk(column.VariableIndex).ToArray();

        return result;
    }

    private ReadOnlySpan<byte> FixedSlice(ReadOnlySpan<byte> row, Layout layout, ColumnDef column)
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
    private LongValueStore Reader(ColumnDef column) =>
        _longValues ?? throw new InvalidOperationException(
            $"Column '{column.Name}' stores its value on long-value pages, so decoding it needs a "
            + "LongValueReader; this RowDecoder was constructed without one. "
            + $"Use {nameof(LongValueDescriptors)} to read the stored descriptors instead.");

    internal static Layout ParseLayout(IReadOnlyList<ColumnDef> columns, JetFormatBase format, ReadOnlySpan<byte> row) =>
        Layout.Parse(row, format, Layout.HasVariableSection(row, columns, format));


    /// <summary>The widest a single non-Memo/OLE column may be declared: 255 Text characters, or 510 bytes
    /// of Binary.</summary>
    public const int MaxFieldBytes = 510;

    /// <summary>The widest a BigBinary column may be declared: <c>BIGBINARY(4001)</c> gives ACE's "Size of
    /// field is too long".</summary>
    public const int MaxBigBinaryBytes = 4000;

    /// <summary>Throws if a column is declared wider than ACE stores. Memo and OLE are exempt — their data
    /// lives on long-value pages and the in-row descriptor is a fixed size.</summary>
    public static void ValidateFieldWidth(string columnName, JetDataType type, int lengthBytes)
    {
        if (type is JetDataType.Memo or JetDataType.Ole) return;
        if (type == JetDataType.BigBinary)
        {
            if (lengthBytes > MaxBigBinaryBytes)
                throw new NotSupportedException(
                    $"Column '{columnName}' is declared {lengthBytes} bytes wide; a BigBinary column holds at most "
                    + $"{MaxBigBinaryBytes}.");
            return;
        }
        if (lengthBytes > MaxFieldBytes)
            throw new NotSupportedException(
                $"Column '{columnName}' is declared {lengthBytes} bytes wide; a column holds at most {MaxFieldBytes} "
                + $"bytes ({MaxFieldBytes / 2} Text characters). Use Memo or OLE for anything longer.");
    }

    /// <summary>The largest record the declaration can produce: the row header, the fixed region, the
    /// variable section's overhead and the null bitmap. Mirrors <see cref="RowCodec"/>'s layout
    /// except that a table with no variable columns still gets ACE's 4-byte allowance for the section.</summary>
    /// <param name="fixedBytes">Sum of the fixed columns' widths, excluding Boolean (which has no data).</param>
    /// <param name="variableColumns">Number of variable-length columns, Memo and OLE included.</param>
    /// <param name="columnCount">The column-id high-water plus one — what sizes the null bitmap.</param>
    /// <param name="format">The file's format, for the row's field sizes.</param>
    /// <remarks>The variable section is budgeted even with no variable columns — one offset entry and the count,
    /// which is ACE's allowance for that case.</remarks>
    public static int WidestRecord(int fixedBytes, int variableColumns, int columnCount, JetFormatBase format) =>
        format.RowColumnCountSize
        + fixedBytes
        + (variableColumns + 1) * format.RowVariableOffsetSize + format.RowVariableCountSize
        + BitmapBits.ByteCount(columnCount);

    /// <summary>Throws if the declaration's widest possible record is one ACE would refuse the file for.
    /// <paramref name="tableName"/> may be null where the caller does not know it (the table is still being
    /// built), in which case the message just says "the table".</summary>
    public static void ValidateRecordFits(
        string? tableName, int fixedBytes, int variableColumns, int columnCount, JetFormatBase format)
    {
        int widest = WidestRecord(fixedBytes, variableColumns, columnCount, format);
        if (widest > format.MaxRecordSize)
            throw new NotSupportedException(
                $"{(tableName is null ? "The table" : $"Table '{tableName}'")} declares {fixedBytes} bytes of "
                + $"fixed-length columns over {columnCount} column ids, so its widest record would be {widest} "
                + $"bytes; a record holds at most {format.MaxRecordSize}. Make the wide columns variable-length, "
                + "or move them to Memo/OLE, which live on their own pages.");
    }

    /// <summary>
    /// Parses the structural trailer of an inline row record once (spec §5), so the several call sites that
    /// need to locate a row's regions don't each re-derive the offset arithmetic. Layout:
    /// <code>
    /// [count] [fixed data] [var data] [varOffsetTable: numVar+1 entries] [numVar] [nullBitmap]
    /// </code>
    /// The field sizes are the format's (<see cref="JetFormatBase.RowColumnCountSize"/>,
    /// <see cref="JetFormatBase.RowVariableOffsetSize"/>, <see cref="JetFormatBase.RowVariableCountSize"/>). The leading
    /// count is <c>maxColumnId + 1</c> and sets the null-bitmap width, one bit per column id. A table with NO variable
    /// columns omits the whole variable section (offset table + numVar) — such a row can't self-describe that, so the
    /// caller passes <c>hasVar</c> from the schema.
    /// </summary>
    internal readonly ref struct Layout
    {
        private readonly ReadOnlySpan<byte> _row;
        private readonly int _offsetSize;

        /// <summary>The leading column count (= max column id + 1).</summary>
        public int ColumnCount { get; }
        /// <summary>Null-bitmap width in bytes, from the leading count.</summary>
        public int NullBitmapSize { get; }
        /// <summary>Number of variable columns stored (0 when the table has no variable section).</summary>
        public int NumVar { get; }
        /// <summary>Offset of the variable-offset table, or -1 when there is no variable section.</summary>
        public int VarTableStart { get; }
        /// <summary>Length of the fixed-data region (bytes between the leading count and the variable data).</summary>
        public int FixedRegionLength { get; }

        private Layout(ReadOnlySpan<byte> row, JetFormatBase format, bool hasVar)
        {
            int countSize = format.RowColumnCountSize;
            _row = row;
            _offsetSize = format.RowVariableOffsetSize;
            ColumnCount = ReadColumnCount(row, format);
            NullBitmapSize = BitmapBits.ByteCount(ColumnCount);
            int minimum = MinimumLength(format, ColumnCount, hasVar);
            if (row.Length < minimum)
                throw new InvalidDataException(
                    $"Row is {row.Length} bytes, shorter than the {minimum} its {NullBitmapSize}-byte null bitmap"
                    + (hasVar ? " and variable-column trailer need." : " needs."));

            if (!hasVar)
            {
                NumVar = 0;
                VarTableStart = -1;
                FixedRegionLength = row.Length - countSize - NullBitmapSize;
                return;
            }

            int numVarSize = format.RowVariableCountSize;
            int numVarPos = row.Length - NullBitmapSize - numVarSize;
            NumVar = BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(numVarPos, numVarSize));
            long tableStart = (long)numVarPos - ((long)NumVar + 1) * _offsetSize;
            if (tableStart < countSize || tableStart > numVarPos)
                throw new InvalidDataException(
                    $"Row declares {NumVar} variable slots, placing its offset table outside the row.");
            VarTableStart = (int)tableStart;

            int previous = VarOffset(0);
            if (previous < countSize || previous > VarTableStart)
                throw new InvalidDataException(
                    $"Row variable-data end {previous} is outside the data region ending at {VarTableStart}.");
            for (int entry = 1; entry <= NumVar; entry++)
            {
                int current = VarOffset(entry);
                if (current < countSize || current > previous)
                    throw new InvalidDataException(
                        $"Row variable offset {entry} ({current}) is outside or above its preceding boundary {previous}.");
                previous = current;
            }
            // The last offset-table entry is the variable-data start (= count field + fixed region).
            FixedRegionLength = previous - countSize;
        }

        /// <summary>Parses <paramref name="row"/>; <paramref name="hasVar"/> is whether the row carries a variable
        /// section (<see cref="HasVariableSection"/>).</summary>
        public static Layout Parse(ReadOnlySpan<byte> row, JetFormatBase format, bool hasVar) => new(row, format, hasVar);

        /// <summary>The fewest bytes a row of <paramref name="columnCount"/> column ids can take: the count, the null
        /// bitmap, and — when it has a variable section — the offset table's one mandatory entry (the variable-data
        /// start) and the variable-column count. Fixed data and variable data may both be empty.</summary>
        public static int MinimumLength(JetFormatBase format, int columnCount, bool hasVar) =>
            format.RowColumnCountSize + BitmapBits.ByteCount(columnCount)
            + (hasVar ? format.RowVariableOffsetSize + format.RowVariableCountSize : 0);

        /// <summary>The row's leading column count: the highest column id the table had handed out when the row was
        /// written, plus one.</summary>
        public static int ReadColumnCount(ReadOnlySpan<byte> row, JetFormatBase format)
        {
            int countSize = format.RowColumnCountSize;
            if (row.Length < countSize)
                throw new InvalidDataException($"Row is too short to contain its {countSize}-byte column count.");
            return BinaryPrimitives.ReadUInt16LittleEndian(row[..countSize]);
        }

        /// <summary>Whether <b>this row</b> carries a variable section — the argument every <see cref="Parse"/>
        /// caller needs, derived once here rather than at each call site.</summary>
        /// <remarks>
        /// It is not "does the schema have a variable column": a row written before the table's first variable
        /// ADD COLUMN has no trailer even though the current schema does, because ADD COLUMN is metadata-only.
        /// The row's own stored count is what dates it — a column id at or above the count did not exist when
        /// the row was written. Get this wrong and the parse reads the last bytes of FIXED data as numVar and an
        /// offset table, which the bounds checks above usually catch, but not always.
        /// </remarks>
        public static bool HasVariableSection(ReadOnlySpan<byte> row, IReadOnlyList<ColumnDef> columns, JetFormatBase format)
        {
            int storedCount = ReadColumnCount(row, format);
            foreach (ColumnDef column in columns)
                if (!column.IsFixedLength && column.ColumnId < storedCount) return true;
            return false;
        }

        /// <summary>The row's null bitmap: one bit per column id below <see cref="ColumnCount"/>, set when the column
        /// holds a value — or, for a Boolean, when it is true.</summary>
        public ReadOnlySpan<byte> NullBitmap => _row[^NullBitmapSize..];

        /// <summary>Whether column <paramref name="columnId"/>'s null-bitmap bit is set. A column id the row predates —
        /// at or above its stored count — has no bit, and reads as clear.</summary>
        public bool IsPresent(int columnId) =>
            columnId >= 0 && columnId < ColumnCount && BitmapBits.Get(NullBitmap, columnId);

        /// <summary>The raw bytes of variable column <paramref name="variableIndex"/> (end-first offset table).</summary>
        public ReadOnlySpan<byte> VarChunk(int variableIndex)
        {
            if (variableIndex < 0 || variableIndex >= NumVar)
                throw new InvalidDataException(
                    $"Row has {NumVar} variable slots but column metadata requests slot {variableIndex}.");
            int start = VarOffset(NumVar - variableIndex);
            int end = VarOffset(NumVar - variableIndex - 1);
            return _row[start..end];
        }

        private int VarOffset(int entry) =>
            BinaryPrimitives.ReadUInt16LittleEndian(_row.Slice(VarTableStart + entry * _offsetSize, _offsetSize));
    }

}