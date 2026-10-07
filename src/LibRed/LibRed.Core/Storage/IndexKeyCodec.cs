using EntityFrameworkCore.Jet.Data;
using LibRed.Catalog;
using LibRed.Storage.Types;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LibRed.Storage;

/// <summary>
/// Reads and writes Jet/ACE index keys, owning their layout, reversible value transforms,
/// collation framing, length limit and truncation checksum.
/// </summary>
/// <remarks>
/// Encoding reads values by <see cref="ColumnDef.Index"/>; decoding returns values in index-column order.
/// Text, Binary and DATETIME2 cannot be decoded reliably from a stored key. Decoding stops at the first
/// such column or incomplete value, leaving it and subsequent columns null. The existing text collations
/// supply weights; this codec owns the key structure around them.
/// </remarks>
public static class IndexKeyCodec
{
    // --- Limits ---

    /// <summary>
    /// The longest index entry ACE stores verbatim. Measured: an entry of exactly 510 bytes comes back
    /// byte-for-byte, and one that would be 511 comes back as 510 — the weights cut short and the last two
    /// bytes replaced by a value that varies with the string (<c>…0E0602</c> for one 254-character value,
    /// <c>…0EDE2A</c> for the 255-character one). That is a truncated key plus a checksum, which is why two
    /// long values never collide. It caps the whole entry rather than each column: two 200-character text
    /// columns are about 404 bytes of key each and ACE stores their combined entry truncated.
    /// </summary>
    internal const int MaxKeyBytes = 510;

    /// <summary>The checksum that ends a truncated key, big-endian.</summary>
    internal const int ChecksumSize = sizeof(ushort);

    /// <summary>The bytes of an over-long key ACE keeps ahead of its checksum.</summary>
    internal const int KeptKeyBytes = MaxKeyBytes - ChecksumSize;

    /// <summary>Access indexes only the first 255 characters of a Memo (Long Text) value (verified vs ACE).</summary>
    internal const int MemoKeyMaxChars = 255;

    // --- Column prefixes and fixed-width values ---

    /// <summary>The prefix of a present value in a column of the given direction.</summary>
    internal static byte Start(bool ascending) => (byte)(ascending ? IndexKeyPrefix.AscStart : IndexKeyPrefix.DescStart);

    /// <summary>The prefix of a null in a column of the given direction.</summary>
    internal static byte Null(bool ascending) => (byte)(ascending ? IndexKeyPrefix.AscNull : IndexKeyPrefix.DescNull);

    /// <summary>The byte after a Yes/No column's start prefix: <c>00</c> for true and <c>FF</c> for false, so true
    /// sorts first, both inverted in a descending column (verified against ACE: 7F 00 / 7F FF, 80 FF / 80 00).</summary>
    internal static byte Boolean(bool value, bool ascending)
    {
        byte b = value ? (byte)0x00 : (byte)0xFF;
        return ascending ? b : (byte)~b;
    }

    /// <summary>
    /// The key width of a fixed-width column type, or -1 where the key is not fixed-width — TEXT, Binary,
    /// GUID and DATETIME2 all encode to a variable, and for the first three lossy, form
    /// (page-03-04 §10.4). Here for the same reason as the prefixes above: the encoder and the decoder each had
    /// their own copy of this table and they drifted, the decoder never learning the widths the encoder
    /// writes for <see cref="JetDataType.Complex"/> and <see cref="JetDataType.FixedPoint"/>.
    /// </summary>
    internal static int FixedKeySize(JetDataType type) => type switch
    {
        JetDataType.Byte => 1,
        JetDataType.Int16 => 2,
        JetDataType.Int32 => 4,
        // A complex (multi-value / attachment) column's key is its Int32 complex id, encoded exactly as an
        // Int32 — verified against ACE over 43 entries across 9 such indexes in two files, covering
        // attachment, Text and Long element types, with no difference in any byte.
        JetDataType.Complex => 4,
        JetDataType.Single => 4,
        JetDataType.Double or JetDataType.DateTime => 8,
        // Int64/BIGINT keys like Currency — both are an int64, sign bit flipped, big-endian. Its VARIABLE
        // storage does not change that: this dispatch is on the type, not on where the row keeps it.
        JetDataType.Currency or JetDataType.Int64 => 8,
        JetDataType.FixedPoint => FixedPointMagnitudeOffset + FixedPointMagnitudeSize,
        _ => -1,
    };

    /// <summary>The sign byte of a non-negative FixedPoint (Decimal/Numeric) key. A negative key is the bitwise
    /// complement of the whole positive form, so its sign byte reads <c>00</c>.</summary>
    internal const byte FixedPointNonNegative = 0xFF;

    /// <summary>A FixedPoint key's magnitude — |value| × 10^scale as one big-endian 128-bit integer — after the sign
    /// byte.</summary>
    internal const int FixedPointMagnitudeOffset = 1;
    internal const int FixedPointMagnitudeSize = 16;

    // --- Chunked values (Binary, GUID, DATETIME2) ---

    /// <summary>The data bytes in one chunk of a chunked key, the last zero-padded to the full width.</summary>
    internal const int ChunkSize = 8;

    /// <summary>The control byte after a chunk that more follow. Constant in a descending column too, where the
    /// final chunk's control byte — its real-byte count — is inverted.</summary>
    internal const byte ChunkContinues = 0x09;

    // --- Text ---

    /// <summary>Appended after a descending text key's inverted bytes (verified against ACE).</summary>
    internal const byte DescendingTextEnd = 0x00;

    // The bytes inside a text key's body, shared by every collation (General v0, General v1 and the locale
    // tailorings over them). [MS-UCODEREF] frames the key as primaries SEP diacritics SEP case SEP extra SEP
    // specials TERM; Access leaves the case section empty.

    /// <summary>Separates the sections of a text key — primaries, diacritics, case, extra.</summary>
    internal const byte SectionSeparator = 0x01;

    /// <summary>Ends a text key's body.</summary>
    internal const byte EndKey = 0x00;

    /// <summary>The top byte of an inline (positional) record's 16-bit position field.</summary>
    internal const byte InlineStart = 0x80;

    /// <summary>The byte inside the extra section a kana fills: between its small-form codes and its mark codes,
    /// inside its closing run, and after that run when inline records follow. What it denotes is not
    /// established; where it sits is measured.</summary>
    internal const byte KanaRunSeparator = 0xFF;

    /// <summary>The secondary weight of a character with no accent.</summary>
    internal const byte DefaultSecondary = 0x02;

    /// <summary>The primary ACE gives a mark with nothing to act on — a shadda or an iteration mark with no weight
    /// before it — and the unweighted characters of both tables.</summary>
    internal static ReadOnlySpan<byte> UnweightedPrimary => [0xFF, 0xFF];

    /// <summary>What a Han character's four-byte primary starts with under General (v1), before its own alphabetic
    /// and diacritic weights (verified against ACE: U+4E00 is <c>7F FD FF 3C 6A 01 00</c>).</summary>
    internal static ReadOnlySpan<byte> HanPrimaryMarker => [0xFD, 0xFF];

    /// <summary>
    /// [MS-UCODEREF] <c>PUNCTUATION</c>, the script member of a word-sort ignorable: it carries no primary weight but
    /// is recorded positionally so <c>co-op</c> stays beside <c>coop</c>. The apostrophe and hyphen live here (their
    /// <c>0x80</c>/<c>0x82</c> inline codes are simply their Alphabetic Weights), which is why exactly those two are
    /// special — it is the platform's rule, not an Access one. Both versions write it in the inline record.
    /// </summary>
    internal const byte WordSortScriptMember = 6;
    /// <summary>
    /// The per-column prefix byte of an order-preserving index key. Each non-boolean column is prefixed by a
    /// start byte (present value) or null byte, with distinct values for ascending vs descending columns so that
    /// a lexicographic byte compare matches the index's logical order.
    /// </summary>
    private enum IndexKeyPrefix : byte
    {
        /// <summary>Ascending column, null value.</summary>
        AscNull = 0x00,

        /// <summary>Ascending column, present value.</summary>
        AscStart = 0x7F,

        /// <summary>Descending column, present value.</summary>
        DescStart = 0x80,

        /// <summary>Descending column, null value.</summary>
        DescNull = 0xFF,
    }

    public static byte[] Encode(IReadOnlyList<(ColumnDef Column, bool Ascending)> columns, object?[] values) =>
        Encode(columns, values, enforceLengthLimit: true);

    /// <summary>
    /// The key LibRed would build if ACE had no length limit — the input the truncation works ON.
    /// </summary>
    /// <remarks>
    /// Only the research that is trying to identify ACE's two-byte checksum wants this: recovering the
    /// function means pairing what ACE stored against the full key it was derived from, and the ordinary
    /// entry point refuses exactly those values. Not a way around the limit — a key this returns is longer
    /// than ACE would store and must never be written to a file.
    /// </remarks>
    internal static byte[] EncodeWithoutLengthLimit(
        IReadOnlyList<(ColumnDef Column, bool Ascending)> columns, object?[] values) =>
        Encode(columns, values, enforceLengthLimit: false);

    // The lists a key is built in, this thread's, cleared rather than allocated — as the collation encoders keep
    // theirs: an index-nested-loop join encodes a key for every outer row, and two growing lists per key were most
    // of what the seek allocated. Safe because nothing here re-enters Encode.
    [ThreadStatic] private static List<byte>? t_buffer;
    [ThreadStatic] private static List<byte>? t_textKey;

    private static byte[] Encode(
        IReadOnlyList<(ColumnDef Column, bool Ascending)> columns, object?[] values, bool enforceLengthLimit)
    {
        List<byte> buffer = t_buffer ??= [];
        buffer.Clear();

        for (int i = 0; i < columns.Count; i++)
        {
            (ColumnDef column, bool ascending) = columns[i];
            object? value = values[column.Index];

            if (column.Type == JetDataType.Boolean)
            {
                // The start flag, then the value's byte. The value is read with the row's truthiness — -1 and 7
                // are true — or the key disagrees with the row it indexes. A Yes/No column cannot be null, and a
                // null is keyed as the false the row stores for it.
                buffer.Add(Start(ascending));
                buffer.Add(Boolean(RowCodec.IsTruthy(value), ascending));
                continue;
            }

            if (value is null)
            {
                buffer.Add(Null(ascending));
                continue;
            }

            // Text uses Jet's collation: start flag then the collation key body (weights, inline
            // ignorable codes, terminator). Descending inverts every byte of that ascending key
            // and appends a 0x00 (verified against ACE).
            //
            // A Memo (Long Text) column IS indexable in Access, and its key is the *same* collation key
            // over only the value's first 255 characters — verified vs ACE: a 256- or 300-character memo
            // produces byte-for-byte the key of its 255-character prefix.
            if (column.Type is JetDataType.Text or JetDataType.Memo)
            {
                // Weights are implemented for the two General orders plus the locale tailorings in
                // JetLocaleTailoring. Refuse anything else up front rather than emit wrong bytes with the
                // English table — a wrong key does not fail, it silently disagrees with ACE's. The collation
                // is read per-column from the descriptor (0x0B–0x0E).
                if (!column.Collation.IsIndexKeyEncodable)
                    throw new NotSupportedException(
                        $"Index key encoding for column '{column.Name}' uses collation {column.Collation.Order} " +
                        $"version {column.Collation.Version}" +
                        (column.Collation.SortId == 0 ? "" : $" sort id {column.Collation.SortId}") +
                        ", which is not implemented yet.");

                string text = (string)value;
                if (column.Type == JetDataType.Memo && text.Length > MemoKeyMaxChars)
                    text = text[..MemoKeyMaxChars];

                List<byte> ascendingKey = t_textKey ??= [];
                ascendingKey.Clear();
                ascendingKey.Add(Start(ascending: true));
                LocaleTailoring? tailoring = JetLocaleTailoring.For(column.Collation);
                bool encoded = column.Collation.Version == Collation.GeneralVersion
                    ? JetTextCollationV1.TryEncode(text, ascendingKey, tailoring)
                    : JetTextCollation.TryEncode(text, ascendingKey, tailoring);
                if (!encoded)
                    throw new NotSupportedException(
                        $"Text index key '{text}' contains a character with no weight in the {column.Collation.Order} " +
                        $"v{column.Collation.Version} collation table.");

                if (ascending)
                {
                    buffer.AddRange(ascendingKey);
                }
                else
                {
                    foreach (byte b in ascendingKey) buffer.Add((byte)~b);
                    buffer.Add(DescendingTextEnd);
                }
                continue;
            }

            // GUID key (verified against ACE): the 16 GUID bytes in canonical *string* order (NOT the
            // mixed-endian .ToByteArray layout), chunked exactly as a 16-byte Binary value is — two full chunks,
            // the first followed by the continuation marker and the second by its count, 8. Descending inverts
            // every byte except the continuation marker — verified against ACE.
            if (column.Type == JetDataType.Guid)
            {
                Guid guid = value switch
                {
                    Guid g => g,
                    byte[] b when b.Length == 16 => new Guid(b),
                    string text when JetTypeCodec.TryParseGuid(text, out Guid parsed) => parsed,
                    _ => throw new NotSupportedException($"Cannot encode GUID index key from {value.GetType().Name}."),
                };
                EncodeBinaryChunked(buffer, Convert.FromHexString(guid.ToString("N")), ascending);
                continue;
            }

            // Binary key (verified against ACE's EverythingIsBytes fixture): see EncodeBinaryChunked. The old fixed
            // 4-byte MSysQueries.Order case is the single-chunk form (7F <4B> 00 00 00 00 04).
            if (column.Type == JetDataType.Binary)
            {
                EncodeBinaryChunked(buffer, (byte[])value, ascending);
                continue;
            }

            // DATETIME2 keys the whole 42-byte stored value through that same chunking, rather than folding
            // it to a number the way DateTime folds to its OA double — verified against ACE, which stores
            // 7F <8B> 09 … <final> <count> over exactly the bytes on the page. It works because the encoding
            // is already order-preserving: both numeric fields are zero-padded to 19 digits, so byte order is
            // chronological order. Note the value's 42nd byte is a NUL (see JetTypeCodec) and lands in the key.
            if (column.Type == JetDataType.DateTimeExtended)
            {
                EncodeBinaryChunked(
                    buffer,
                    JetTypeCodec.EncodeExtendedDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture)),
                    ascending);
                continue;
            }

            int size = FixedKeySize(column.Type);
            if (size <= 0)
                throw new NotSupportedException(
                    $"Index key encoding for {column.Type} (binary collation) is not supported yet.");

            buffer.Add(Start(ascending));
            byte[] raw = EncodeFixed(column, value, size);
            if (!ascending)
                Complement(raw);
            buffer.AddRange(raw);
        }

        // Past MaxKeyBytes ACE keeps the leading KeptKeyBytes and replaces the rest with a checksum over what it
        // dropped, which is why two long values sharing a prefix still sort apart. The limit is on the WHOLE
        // entry, not per column (see MaxKeyBytes).
        if (!enforceLengthLimit || buffer.Count <= MaxKeyBytes) return [.. buffer];

        // A discarded word-sort record used to be refused here, on the reasoning that the record sits in the
        // part ACE dropped and so what it held is unobservable — and that if ACE recomputed its position when
        // truncating, LibRed's reconstruction would be feeding the checksum the wrong bytes. Both halves are
        // now measured and neither holds: ACE does NOT recompute the position (the record's position byte
        // tracks where the mark actually sat), and the checksum over LibRed's reconstructed key reproduces
        // ACE's exactly — 16 of 16 over two mark characters at eight positions each. The record was never
        // unobservable; it just could not be checked until the checksum's own arithmetic was pinned down.
        // See docs/design/index-key-checksum.md.
        byte[] truncated = new byte[MaxKeyBytes];
        buffer.CopyTo(0, truncated, 0, KeptKeyBytes);
        BinaryPrimitives.WriteUInt16BigEndian(truncated.AsSpan(KeptKeyBytes),
            ComputeChecksum(CollectionsMarshal.AsSpan(buffer)[KeptKeyBytes..]));
        return truncated;
    }

    /// <summary>
    /// Appends Jet's order-preserving chunked key: the start flag, then the data in
    /// <see cref="ChunkSize"/>-byte chunks — real bytes left-aligned, the last zero-padded — each
    /// followed by a control byte: <see cref="ChunkContinues"/> when another chunk follows, otherwise
    /// the real-byte count of this final chunk (<c>1..8</c>). Descending inverts every byte except the continuation
    /// markers, which stay constant so the structure stays parseable. Binary, GUID and DATETIME2 keys all take this
    /// form (verified against ACE). An empty value is the start flag alone.
    /// </summary>
    private static void EncodeBinaryChunked(List<byte> buffer, byte[] data, bool ascending)
    {
        buffer.Add(Start(ascending));
        if (data.Length == 0)
            return;

        int offset = 0;
        do
        {
            int n = Math.Min(ChunkSize, data.Length - offset);
            for (int j = 0; j < ChunkSize; j++)
            {
                byte b = j < n ? data[offset + j] : (byte)0;
                buffer.Add(ascending ? b : (byte)~b);
            }
            offset += n;

            if (offset < data.Length)
                buffer.Add(ChunkContinues);
            else
                buffer.Add(ascending ? (byte)n : (byte)~n);
        }
        while (offset < data.Length);
    }

    /// <summary>A fixed-width column's value as its <paramref name="size"/>-byte ascending key
    /// (<see cref="FixedKeySize"/>).</summary>
    private static byte[] EncodeFixed(ColumnDef column, object value, int size)
    {
        var c = CultureInfo.InvariantCulture;
        switch (column.Type)
        {
            case JetDataType.Byte:
                return [Convert.ToByte(value, c)];
            case JetDataType.Int16:
                return EncodeInteger(Convert.ToInt16(value, c), size);
            case JetDataType.Int32:
            case JetDataType.Complex: // the complex id, keyed as the Int32 it is
                return EncodeInteger(Convert.ToInt32(value, c), size);
            case JetDataType.Currency:
                return EncodeInteger(JetTypeCodec.CurrencyToScaled(value, c), size);
            case JetDataType.Int64: // BIGINT — verified against ACE across 0, ±1, ±42 and both extremes
                return EncodeInteger(Convert.ToInt64(value, c), size);
            case JetDataType.Single:
                return EncodeFloatBits(BitConverter.SingleToInt32Bits(Convert.ToSingle(value, c)), size);
            case JetDataType.Double:
                return EncodeFloatBits(BitConverter.DoubleToInt64Bits(Convert.ToDouble(value, c)), size);
            case JetDataType.DateTime:
                return EncodeFloatBits(BitConverter.DoubleToInt64Bits(JetTypeCodec.ToOaDate(column, value, c)), size);
            case JetDataType.FixedPoint:
                return EncodeFixedPoint(JetDecimalConverter.ToDecimal(value, c), column.Scale, size);
            default:
                throw new NotSupportedException($"Index key type {column.Type} is not encodable.");
        }
    }

    /// <summary>
    /// Encodes a FixedPoint (Numeric/Decimal) index key — a sign byte followed by the value's 16-byte
    /// big-endian **unscaled magnitude** (|value| × 10^scale, the same integer the row codec stores).
    /// A non-negative value uses sign <c>0xFF</c>; a negative value is the **bitwise complement of the
    /// whole 17-byte positive form** (sign becomes <c>0x00</c>, magnitude is one's-complemented), so byte
    /// order equals numeric order: negatives (sign 0x00) precede non-negatives (0xFF), and complementing
    /// makes a larger magnitude sort earlier among negatives. A negative zero — what a value too small for
    /// the scale truncates to — keeps its sign, as ACE keys it (<c>7F 00 FF…FF</c>), so the sign is taken with
    /// <see cref="decimal.IsNegative"/>: <c>&lt; 0</c> is false for <c>-0.0000m</c>.
    /// Verified byte-for-byte against ACE (see <c>DecimalKeyEncodingTests</c>).
    /// </summary>
    private static byte[] EncodeFixedPoint(decimal value, byte scale, int size)
    {
        decimal factor = 1m;
        for (int i = 0; i < scale; i++) factor *= 10m;
        // Truncated toward zero, matching ACE and — necessarily — JetTypeCodec.EncodeNumeric: quantise a key
        // differently from its row and the value is indexed under a number the row does not contain.
        decimal magnitude = decimal.Truncate(Math.Abs(value) * factor);
        int[] bits = decimal.GetBits(magnitude); // [lo, mid, hi, flags]; magnitude has scale 0

        var key = new byte[size];
        key[0] = FixedPointNonNegative;
        // The 96-bit magnitude as the 128-bit field holds it, so its top 32 bits are always 0.
        var unscaled = ((UInt128)(uint)bits[2] << 64) | ((UInt128)(uint)bits[1] << 32) | (uint)bits[0];
        BinaryPrimitives.WriteUInt128BigEndian(
            key.AsSpan(FixedPointMagnitudeOffset, FixedPointMagnitudeSize), unscaled);

        if (decimal.IsNegative(value))
            Complement(key);
        return key;
    }

    /// <summary>Complements a key payload for descending order or a negative magnitude.</summary>
    private static void Complement(Span<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)~bytes[i];
    }

    /// <summary>Big-endian with the sign bit flipped, so signed values sort lexicographically.</summary>
    private static byte[] EncodeInteger(long value, int size)
    {
        var raw = new byte[size];
        for (int i = size - 1; i >= 0; i--)
        {
            raw[i] = (byte)(value & 0xFF);
            value >>= 8;
        }
        raw[0] ^= 0x80;
        return raw;
    }

    /// <summary>
    /// IEEE bits big-endian with the order-preserving transform: positive numbers flip the high
    /// bit, negative numbers invert every byte (so negatives sort below positives, descending).
    /// </summary>
    private static byte[] EncodeFloatBits(long bits, int size)
    {
        var raw = new byte[size];
        for (int i = size - 1; i >= 0; i--)
        {
            raw[i] = (byte)(bits & 0xFF);
            bits >>= 8;
        }

        if (raw[0] < 0x80) // sign bit clear → non-negative value
            raw[0] ^= 0x80;
        else
            Complement(raw);

        return raw;
    }
    public static object?[] Decode(IReadOnlyList<(ColumnDef Column, bool Ascending)> columns, ReadOnlySpan<byte> key)
    {
        var values = new object?[columns.Count];
        int pos = 0;

        for (int i = 0; i < columns.Count; i++)
        {
            (ColumnDef column, bool ascending) = columns[i];
            if (pos >= key.Length) break;

            byte flag = key[pos++];
            if (flag == Null(ascending))
            {
                values[i] = null;
                continue;
            }
            // Otherwise flag is the start flag.

            if (column.Type == JetDataType.Boolean)
            {
                if (pos >= key.Length) break;
                values[i] = key[pos++] == Boolean(true, ascending);
                continue;
            }

            // GUID key: the 16 bytes of the GUID's canonical string order, chunked (see IndexKeyCodec).
            if (column.Type == JetDataType.Guid)
            {
                if (ReadChunked(key, ref pos, ascending) is not { Length: 16 } guid) break;
                values[i] = new Guid(Convert.ToHexString(guid));
                continue;
            }

            int size = FixedKeySize(column.Type);
            if (size <= 0 || pos + size > key.Length)
                break; // text/binary/unsupported (lossy) — cannot reliably continue

            Span<byte> raw = key.Slice(pos, size).ToArray();
            pos += size;
            values[i] = DecodeFixed(column, raw, ascending);
        }

        return values;
    }

    /// <summary>Reads a chunked value — the inverse of <see cref="EncodeBinaryChunked"/> — from just after
    /// its start flag, advancing <paramref name="pos"/> past it. Null when the key ends inside it or a final chunk
    /// claims more than a chunk holds.</summary>
    private static byte[]? ReadChunked(ReadOnlySpan<byte> key, ref int pos, bool ascending)
    {
        var data = new List<byte>();
        while (true)
        {
            if (pos + ChunkSize + 1 > key.Length) return null;
            ReadOnlySpan<byte> chunk = key.Slice(pos, ChunkSize);
            byte control = key[pos + ChunkSize];
            pos += ChunkSize + 1;

            bool more = control == ChunkContinues;
            int real = more ? ChunkSize : ascending ? control : (byte)~control;
            if (real > ChunkSize) return null;
            foreach (byte b in chunk[..real]) data.Add(ascending ? b : (byte)~b);
            if (!more) return [.. data];
        }
    }

    private static object DecodeFixed(ColumnDef column, Span<byte> raw, bool ascending)
    {
        switch (column.Type)
        {
            case JetDataType.Byte:
                if (!ascending) raw[0] = (byte)~raw[0];
                return raw[0];

            case JetDataType.Int16:
                return (short)DecodeInteger(raw, ascending);
            case JetDataType.Int32:
            case JetDataType.Complex: // the complex id, keyed as the Int32 it is
                return (int)DecodeInteger(raw, ascending);
            case JetDataType.Currency:
                return JetTypeCodec.CurrencyFromScaled(DecodeInteger(raw, ascending));
            case JetDataType.Int64:
                return DecodeInteger(raw, ascending);

            case JetDataType.Single:
                return BitConverter.Int32BitsToSingle((int)DecodeFloatBits(raw, ascending));
            case JetDataType.Double:
                return BitConverter.Int64BitsToDouble(DecodeFloatBits(raw, ascending));
            case JetDataType.DateTime:
                double serial = BitConverter.Int64BitsToDouble(DecodeFloatBits(raw, ascending));
                return JetTypeCodec.TryFromOaDate(serial, out DateTime date)
                    ? date
                    : throw new InvalidDataException($"An index key on '{column.Name}' holds {serial}, which is not a date.");

            case JetDataType.FixedPoint:
                return DecodeFixedPoint(raw, ascending, column.Scale);

            default:
                throw new NotSupportedException($"Index key type {column.Type} is not decodable.");
        }
    }

    /// <summary>Reverses <see cref="EncodeFixedPoint"/>: a negative key is the complement of the positive
    /// form, whose sign byte is <see cref="FixedPointNonNegative"/> and whose 16-byte big-endian
    /// magnitude is the value times 10^scale. A negative zero keeps its sign, as the key does.</summary>
    private static decimal DecodeFixedPoint(Span<byte> raw, bool ascending, byte scale)
    {
        if (!ascending)
            Complement(raw);
        bool negative = raw[0] != FixedPointNonNegative;
        if (negative)
            Complement(raw);

        UInt128 unscaled = BinaryPrimitives.ReadUInt128BigEndian(
            raw.Slice(FixedPointMagnitudeOffset, FixedPointMagnitudeSize));
        if (unscaled >> 96 != 0)
            throw new OverflowException("A Decimal index key's magnitude exceeds the 96 bits System.Decimal holds.");
        return new decimal((int)(uint)unscaled, (int)(uint)(unscaled >> 32), (int)(uint)(unscaled >> 64), negative, scale);
    }

    /// <summary>Reverses the integer key transform (descending = bytes inverted; sign bit flipped; big-endian).</summary>
    private static long DecodeInteger(Span<byte> raw, bool ascending)
    {
        if (!ascending)
            Complement(raw);
        raw[0] ^= 0x80;

        long value = 0;
        bool negative = (raw[0] & 0x80) != 0;
        if (negative) value = -1; // sign-extend
        foreach (byte b in raw) value = (value << 8) | b;
        return value;
    }

    /// <summary>Reverses the floating-point key transform, returning the raw IEEE bits big-endian.</summary>
    private static long DecodeFloatBits(Span<byte> raw, bool ascending)
    {
        if (ascending)
        {
            if ((raw[0] & 0x80) != 0) raw[0] ^= 0x80;                 // was positive: undo first-bit flip
            else Complement(raw); // was negative: undo full invert
        }
        else
        {
            if ((raw[0] & 0x80) == 0)                                  // was positive
            {
                Complement(raw);
                raw[0] ^= 0x80;
            }
            // was negative: stored as-is
        }

        long bits = 0;
        foreach (byte b in raw) bits = (bits << 8) | b;
        return bits;
    }
    /// <summary>
    /// The step's action on each bit. The upper eight are a plain right shift by eight, which makes the
    /// operator the familiar <c>(x >> 8) ^ T(x &amp; 0xFF)</c> of a table-driven CRC; the lower eight are the
    /// table itself, measured from ACE.
    /// </summary>
    private static ReadOnlySpan<ushort> ChecksumStepBits =>
    [
        0x0580, 0x0F80, 0x1B80, 0x3380, 0x6380, 0xC380, 0x8381, 0x0383,
        0x0001, 0x0002, 0x0004, 0x0008, 0x0010, 0x0020, 0x0040, 0x0080,
    ];

    private static readonly ushort[] ChecksumTable = BuildChecksumTable();

    private static ushort[] BuildChecksumTable()
    {
        var table = new ushort[256];
        for (int value = 0; value < 256; value++)
        {
            ushort result = 0;
            for (int bit = 0; bit < 8; bit++) if ((value & (1 << bit)) != 0) result ^= ChecksumStepBits[bit];
            table[value] = result;
        }
        return table;
    }

    /// <summary>
    /// The checksum over the bytes ACE dropped — everything from <see cref="KeptKeyBytes"/> on.
    /// </summary>
    /// <remarks>
    /// A key of at most <see cref="MaxKeyBytes"/> bytes is stored as built. Past that ACE keeps
    /// the first <see cref="KeptKeyBytes"/> and replaces the rest with this value, computed over
    /// the bytes it dropped — which is why two long values that share a
    /// 508-byte prefix still sort apart instead of colliding.
    /// <para>
    /// Recovered by measurement, not documentation. Three tails differing in one byte showed the function is
    /// affine over GF(2) (<c>L(0xA3) ^ L(0x13) = L(0xB0)</c> exactly), and it proved shift-invariant across 173
    /// observations, so a byte at distance d from the end contributes <c>S^(d-1)</c> of itself. Sweeping all
    /// 65,536 polynomials in the usual framings found nothing, because the usual framing is wrong: the standard
    /// reflected update is <c>crc = (crc >> 8) ^ T[(crc ^ b) &amp; 0xFF]</c>, passing the byte THROUGH the table,
    /// while ACE computes <c>crc = (crc >> 8) ^ T[crc &amp; 0xFF] ^ b</c> and injects it raw. The step operator
    /// was then solved directly by Gaussian elimination over the measured contributions, and predicts all 657 of
    /// them. There is no initial value and no final XOR.
    /// </para>
    /// <para>
    /// <b>It holds where the dropped bytes contain a word-sort record too</b>, which was long assumed
    /// uncheckable: the record sits in the part ACE discarded, so what it held looked unobservable, and ACE might
    /// have recomputed its position when truncating. Neither is so — the position byte tracks where the mark
    /// actually sat, and the checksum over the reconstructed key matches ACE's for both mark characters at
    /// positions spread through the value. See <see cref="IndexKeyCodec"/>.
    /// </para>
    /// <para>
    /// The last byte does not go through a full step. Every byte before it is folded in the usual way, and
    /// then the final one is XORed into the <b>high</b> half — it never gets its own shift or table lookup.
    /// <para>This was first written as "the terminator is excluded", which is the same thing whenever that
    /// byte is <c>0x00</c>: XOR-ing zero changes nothing. A key ending in text always ends in its <c>0x00</c>
    /// terminator, and every measurement behind the original rule used one, so the two readings could not be
    /// told apart. They diverge the moment the key's last column is numeric — <c>(TEXT, TEXT, LONG)</c> put
    /// a data byte there and the keys parted company from ACE's, silently. Re-measured over LONG, CURRENCY
    /// and DOUBLE tails across 24 keys: this form matches ACE on every one, and still matches on the all-text
    /// keys the old form was derived from. See <c>docs/design/index-key-checksum.md</c>.</para>
    /// </para>
    /// </remarks>
    private static ushort ComputeChecksum(ReadOnlySpan<byte> discarded)
    {
        ushort crc = 0;
        foreach (byte b in discarded[..^1]) crc = (ushort)((crc >> 8) ^ ChecksumTable[crc & 0xFF] ^ b);
        return (ushort)(crc ^ (discarded[^1] << 8));
    }
}