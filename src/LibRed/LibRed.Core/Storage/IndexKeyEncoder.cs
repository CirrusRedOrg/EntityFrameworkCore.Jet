using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using LibRed.Catalog;
using LibRed.Formats;

namespace LibRed.Storage;

/// <summary>
/// Encodes index column values into Jet's order-preserving key bytes — the inverse of
/// <see cref="IndexKeyDecoder"/>. Lexicographic comparison of the produced bytes matches the
/// index's logical order, so a freshly encoded key can be slotted into a leaf by byte compare.
/// </summary>
/// <remarks>
/// Each non-boolean column is prefixed by a flag byte (0x7F start / 0x00 null ascending;
/// 0x80 / 0xFF descending). Fixed/numeric types use the reversible transform (sign-bit flip +
/// big-endian for integers; an IEEE transform for floating point); descending inverts the bytes.
/// GUID keys are encoded byte-faithfully (string-order halves split by 0x09, terminated by 0x08).
/// Text uses Jet's collation; general Binary keys use the same 0x09-chunked layout for any length.
/// </remarks>
public static class IndexKeyEncoder
{
    /// <summary>Access indexes only the first 255 characters of a Memo (Long Text) value (verified vs ACE).</summary>
    private const int MemoKeyMaxChars = 255;

    /// <summary>
    /// The longest index entry ACE stores verbatim. Measured: an entry of exactly 510 bytes comes back
    /// byte-for-byte, and one that would be 511 comes back as 510 — the weights cut short and the last two
    /// bytes replaced by a value that varies with the string (<c>…0E0602</c> for one 254-character value,
    /// <c>…0EDE2A</c> for the 255-character one). That is a truncated key plus a checksum, which is why two
    /// long values never collide, and it is why LibRed cannot simply cut its own key to match.
    /// <para>
    /// It caps the whole entry rather than each column: two 200-character text columns are about 404 bytes
    /// of key each and ACE stores their combined entry hashed at 510.
    /// </para>
    /// </summary>
    private const int MaxIndexKeyBytes = 510;

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

    private static byte[] Encode(
        IReadOnlyList<(ColumnDef Column, bool Ascending)> columns, object?[] values, bool enforceLengthLimit)
    {
        var buffer = new List<byte>();
        bool anyWordSortRecord = false;

        for (int i = 0; i < columns.Count; i++)
        {
            (ColumnDef column, bool ascending) = columns[i];
            object? value = values[column.Index];

            if (column.Type == JetDataType.Boolean)
            {
                // No flag byte: ascending true sorts before false (0x00 < 0xFF); descending mirrors.
                bool b = value is true;
                buffer.Add((byte)((b ^ !ascending) ? 0x00 : 0xFF));
                continue;
            }

            if (value is null)
            {
                buffer.Add(ascending ? IndexKeyFlags.AscNull : IndexKeyFlags.DescNull);
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

                var ascendingKey = new List<byte> { IndexKeyFlags.AscStart };
                bool encoded = column.Collation.Version == Collation.GeneralVersion
                    ? JetTextCollationV1.TryEncode(text, ascendingKey, out bool wordSort)
                    : JetTextCollation.TryEncode(
                        text, ascendingKey, JetLocaleTailoring.For(column.Collation), out wordSort);
                anyWordSortRecord |= wordSort;
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
                    buffer.Add(0x00);
                }
                continue;
            }

            // GUID key (verified against ACE): the start flag, then the 16 GUID bytes in canonical *string*
            // order (NOT the mixed-endian .ToByteArray layout), split into two 8-byte halves by a constant
            // 0x09 marker, and terminated by 0x08. Fixed 19-byte key; data bytes equal to 0x08/0x09 need no
            // escaping because every field is at a fixed offset. Descending inverts every byte EXCEPT the
            // 0x09 field marker (which stays constant so the structure is parseable, and doesn't affect
            // ordering since it's equal in every key) — verified against ACE.
            if (column.Type == JetDataType.Guid)
            {
                Guid guid = value switch
                {
                    Guid g => g,
                    byte[] b when b.Length == 16 => new Guid(b),
                    _ => throw new NotSupportedException($"Cannot encode GUID index key from {value.GetType().Name}."),
                };
                byte[] s = Convert.FromHexString(guid.ToString("N")); // 16 bytes, canonical string order

                if (ascending)
                {
                    buffer.Add(IndexKeyFlags.AscStart);           // 0x7F
                    buffer.AddRange(s.AsSpan(0, 8));
                    buffer.Add(0x09);
                    buffer.AddRange(s.AsSpan(8, 8));
                    buffer.Add(0x08);
                }
                else
                {
                    buffer.Add(IndexKeyFlags.DescStart);          // 0x80 = ~0x7F
                    for (int j = 0; j < 8; j++) buffer.Add((byte)~s[j]);
                    buffer.Add(0x09);                   // field marker kept as-is
                    for (int j = 8; j < 16; j++) buffer.Add((byte)~s[j]);
                    buffer.Add(unchecked((byte)~0x08)); // 0xF7
                }
                continue;
            }

            // Binary key (verified against ACE's EverythingIsBytes fixture): the start flag, then the raw
            // bytes in **8-byte chunks**. Each chunk is 8 bytes (real bytes left-aligned, zero-padded on the
            // right) followed by a control byte: 0x09 when another chunk follows (a full 8-byte chunk with
            // more to come), otherwise the real-byte count of this final chunk (1..8; 0x08 for a full final
            // chunk, 0x00 for empty data). This is the same chunking as the GUID key (a 16-byte value → two
            // chunks: 8, 0x09, 8, 0x08); the old fixed 4-byte MSysQueries.Order case is the single-chunk form
            // (7F <4B> 00 00 00 00 04). Descending inverts every byte EXCEPT the 0x09 continuation markers
            // (which stay constant so structure is parseable and they're equal across keys) — mirrors GUID.
            if (column.Type == JetDataType.Binary)
            {
                EncodeBinaryChunked(buffer, (byte[])value, ascending);
                continue;
            }

            int size = FixedKeySize(column.Type);
            if (size <= 0)
                throw new NotSupportedException(
                    $"Index key encoding for {column.Type} (binary collation) is not supported yet.");

            buffer.Add(ascending ? IndexKeyFlags.AscStart : IndexKeyFlags.DescStart);
            byte[] raw = EncodeFixed(column, value);
            if (!ascending)
                for (int j = 0; j < raw.Length; j++) raw[j] = (byte)~raw[j];
            buffer.AddRange(raw);
        }

        // Past 510 bytes ACE keeps the first 508 and replaces the rest with a checksum over what it dropped,
        // which is why two long values sharing a prefix still sort apart. The limit is on the WHOLE entry,
        // not per column: two 200-character text columns weigh about 404 bytes each, comfortably under the
        // cap individually, and ACE stores their combined entry truncated.
        if (!enforceLengthLimit || buffer.Count <= MaxIndexKeyBytes) return [.. buffer];

        // Except where the dropped bytes hold a word-sort record. That case cannot be verified even in
        // principle — the record sits in the part ACE discarded, so what it actually contained is
        // unobservable, and if ACE recomputes its position when truncating then the checksum's input is not
        // what is reconstructed here. Refuse rather than write a key that might disagree, because a wrong
        // index key is silent: ACE writes its own into the same index and a seek misses rows.
        if (anyWordSortRecord)
            throw new NotSupportedException(
                $"These values need a {buffer.Count}-byte index key across {columns.Count} column(s), past the " +
                $"{MaxIndexKeyBytes} ACE stores, and one of them contains an apostrophe or hyphen. ACE truncates " +
                $"and appends a checksum, and for a discarded word-sort record that checksum is not verifiable, " +
                $"so LibRed will not guess at it. Shorten the value or drop it from the index.");

        byte[] truncated = new byte[MaxIndexKeyBytes];
        buffer.CopyTo(0, truncated, 0, JetIndexKeyChecksum.KeptBytes);
        ushort checksum = JetIndexKeyChecksum.Compute(CollectionsMarshal.AsSpan(buffer)[JetIndexKeyChecksum.KeptBytes..]);
        truncated[JetIndexKeyChecksum.KeptBytes] = (byte)(checksum >> 8);
        truncated[JetIndexKeyChecksum.KeptBytes + 1] = (byte)checksum;
        return truncated;
    }

    /// <summary>
    /// Appends Jet's order-preserving binary index key: start flag, then 8-byte chunks each followed by
    /// a control byte (0x09 = "full chunk, more follow"; 1..8 = real-byte count of the final chunk).
    /// Descending inverts every byte except the 0x09 continuation markers (verified against ACE).
    /// </summary>
    private static void EncodeBinaryChunked(List<byte> buffer, byte[] data, bool ascending)
    {
        buffer.Add(ascending ? IndexKeyFlags.AscStart : IndexKeyFlags.DescStart);
        // ACE represents an empty Binary value by the start flag alone.
        if (data.Length == 0)
            return;

        int offset = 0;
        do
        {
            int n = Math.Min(8, data.Length - offset);
            for (int j = 0; j < 8; j++)
            {
                byte b = j < n ? data[offset + j] : (byte)0;
                buffer.Add(ascending ? b : (byte)~b);
            }
            offset += n;

            if (offset < data.Length)
                buffer.Add(0x09);                                  // continuation marker (constant either way)
            else
                buffer.Add(ascending ? (byte)n : (byte)~n);        // terminator = final-chunk length
        }
        while (offset < data.Length);
    }

    private static int FixedKeySize(JetDataType type) => type switch
    {
        JetDataType.Byte => 1,
        JetDataType.Int16 => 2,
        JetDataType.Int32 => 4,
        JetDataType.Single => 4,
        JetDataType.Double or JetDataType.DateTime => 8,
        JetDataType.Currency => 8,
        JetDataType.FixedPoint => 17, // sign byte + 16-byte big-endian magnitude
        _ => -1,
    };

    private static byte[] EncodeFixed(ColumnDef column, object value)
    {
        var c = CultureInfo.InvariantCulture;
        switch (column.Type)
        {
            case JetDataType.Byte:
                return [Convert.ToByte(value, c)];
            case JetDataType.Int16:
                return EncodeInteger(Convert.ToInt16(value, c), 2);
            case JetDataType.Int32:
                return EncodeInteger(Convert.ToInt32(value, c), 4);
            case JetDataType.Currency:
                return EncodeInteger((long)decimal.Round(Convert.ToDecimal(value, c) * 10000m), 8);
            case JetDataType.Single:
                return EncodeFloatBits(BitConverter.SingleToInt32Bits(Convert.ToSingle(value, c)), 4);
            case JetDataType.Double:
                return EncodeFloatBits(BitConverter.DoubleToInt64Bits(Convert.ToDouble(value, c)), 8);
            case JetDataType.DateTime:
                return EncodeFloatBits(BitConverter.DoubleToInt64Bits(Convert.ToDateTime(value, c).ToOADate()), 8);
            case JetDataType.FixedPoint:
                return EncodeFixedPoint(Convert.ToDecimal(value, c), column.Scale);
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
    /// makes a larger magnitude sort earlier among negatives. Zero encodes as positive.
    /// Verified byte-for-byte against ACE (see <c>DecimalKeyEncodingTests</c>).
    /// </summary>
    private static byte[] EncodeFixedPoint(decimal value, byte scale)
    {
        decimal factor = 1m;
        for (int i = 0; i < scale; i++) factor *= 10m;
        decimal magnitude = decimal.Truncate(decimal.Round(Math.Abs(value) * factor, 0));
        int[] bits = decimal.GetBits(magnitude); // [lo, mid, hi, flags]; magnitude has scale 0

        var key = new byte[17];
        key[0] = 0xFF;                                                          // non-negative marker
        // 16-byte big-endian magnitude: the top 32-bit word is always 0 for a System.Decimal, then hi/mid/lo.
        BinaryPrimitives.WriteUInt32BigEndian(key.AsSpan(1, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(key.AsSpan(5, 4), (uint)bits[2]);
        BinaryPrimitives.WriteUInt32BigEndian(key.AsSpan(9, 4), (uint)bits[1]);
        BinaryPrimitives.WriteUInt32BigEndian(key.AsSpan(13, 4), (uint)bits[0]);

        if (value < 0)
            for (int i = 0; i < key.Length; i++) key[i] = (byte)~key[i];
        return key;
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
            for (int i = 0; i < size; i++) raw[i] = (byte)~raw[i];

        return raw;
    }
}
