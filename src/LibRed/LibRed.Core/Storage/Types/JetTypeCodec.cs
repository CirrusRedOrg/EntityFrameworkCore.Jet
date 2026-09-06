using System.Buffers.Binary;
using System.Text;
using LibRed.Catalog;
using LibRed.Formats;

namespace LibRed.Storage.Types;

/// <summary>
/// Decodes individual column values from their on-disk byte representation. Centralises
/// the per-type quirks: the 1899-12-30 OLE date epoch, Jet CURRENCY (scaled int64),
/// GUID byte order, and UTF-16LE text. Long values (memo/OLE) that live on LVAL pages
/// are not resolved here yet.
/// </summary>
public static class JetTypeCodec
{
    /// <summary>Decodes a single non-null column value from its raw bytes.</summary>
    public static object? Decode(ColumnDef column, ReadOnlySpan<byte> value)
    {
        int expectedLength = column.Type switch
        {
            JetDataType.Byte => 1,
            JetDataType.Int16 => 2,
            JetDataType.Int32 or JetDataType.Single => 4,
            JetDataType.Int64 or JetDataType.Double or JetDataType.DateTime or JetDataType.Currency => 8,
            JetDataType.Guid => 16,
            JetDataType.FixedPoint => 17,
            JetDataType.DateTimeExtended => ExtendedDateTimeLength,
            _ => -1,
        };
        if (expectedLength >= 0 && value.Length != expectedLength)
            throw new InvalidDataException(
                $"Column '{column.Name}' ({column.Type}) has {value.Length} bytes; expected {expectedLength}.");

        switch (column.Type)
        {
            case JetDataType.Boolean:
                return value.Length > 0 && value[0] != 0;
            case JetDataType.Byte:
                return value[0];
            case JetDataType.Int16:
                return BinaryPrimitives.ReadInt16LittleEndian(value);
            case JetDataType.Int32:
                return BinaryPrimitives.ReadInt32LittleEndian(value);
            case JetDataType.Int64: // ACE 16 BIGINT
                return BinaryPrimitives.ReadInt64LittleEndian(value);
            case JetDataType.Single:
                return BinaryPrimitives.ReadSingleLittleEndian(value);
            case JetDataType.Double:
                return BinaryPrimitives.ReadDoubleLittleEndian(value);
            case JetDataType.DateTime:
                return DateTime.FromOADate(BinaryPrimitives.ReadDoubleLittleEndian(value));
            case JetDataType.DateTimeExtended: // ACE 17 DATETIME2
                return DecodeExtendedDateTime(value);
            case JetDataType.Currency:
                return BinaryPrimitives.ReadInt64LittleEndian(value) / 10000m;
            case JetDataType.Guid:
                return new Guid(value[..16]);
            case JetDataType.Text:
                return DecodeText(value);
            case JetDataType.Binary:
                return value.ToArray();
            case JetDataType.FixedPoint:
                return DecodeNumeric(value, column.Scale);

            // Long values live on LVAL pages, so the inline bytes are only a descriptor. Resolving them needs
            // page access this codec deliberately does not have: RowDecoder holds the LongValueReader and
            // substitutes the real value, and hands the raw bytes here only when it has none.
            case JetDataType.Memo:
            case JetDataType.Ole:
            case JetDataType.Complex:
            default:
                return value.ToArray();
        }
    }

    /// <summary>
    /// Decodes an ACE 17 DATETIME2 value: a fixed 42-byte ASCII string
    /// "&lt;day&gt;:&lt;time&gt;:&lt;precision&gt;" where <c>day</c> is the .NET day number and
    /// <c>time</c> is the count of 100-ns ticks within the day. Both are zero-padded to 19
    /// digits so that byte order equals chronological order.
    /// </summary>
    private static DateTime DecodeExtendedDateTime(ReadOnlySpan<byte> value)
    {
        Span<char> chars = stackalloc char[value.Length];
        int n = Encoding.ASCII.GetChars(value, chars);
        ReadOnlySpan<char> s = chars[..n];

        int c1 = s.IndexOf(':');
        int c2 = s.Slice(c1 + 1).IndexOf(':') + c1 + 1;
        long day = long.Parse(s[..c1]);
        long time = long.Parse(s.Slice(c1 + 1, c2 - c1 - 1));

        return new DateTime(day * TimeSpan.TicksPerDay + time);
    }

    /// <summary>
    /// Encodes an ACE 17 DATETIME2 value — the inverse of <see cref="DecodeExtendedDateTime"/>. The 42 bytes are
    /// ASCII <c>"&lt;day&gt;:&lt;time&gt;:&lt;precision&gt;"</c>: the .NET day number and the 100-ns ticks within
    /// that day, each zero-padded to 19 digits so byte order equals chronological order, then the fractional
    /// precision — 41 characters, NUL-padded to the field's 42 (19 + 1 + 19 + 1 + 1 = 41).
    /// </summary>
    /// <remarks>
    /// <para>The padding byte is <c>0x00</c>, not a space: verified by reading the row bytes ACE itself wrote
    /// (<c>… 3A 37 00</c>). It matters beyond byte-faithfulness — the whole 42 bytes go into the index key
    /// verbatim (see <c>IndexKeyEncoder</c>), so a space there would put every key we wrote out of step with
    /// ACE's and make its seeks miss our rows.</para>
    /// <para>The precision is always 7. ACE's DDL accepts no other form: <c>DATETIME2(7)</c> and every other
    /// parenthesised spelling is a syntax error, so a Date/Time Extended column can only be declared bare, and
    /// the value ACE itself writes for one carries <c>7</c> (verified against Microsoft.ACE.OLEDB.16.0). A
    /// column's <see cref="ColumnDef.Precision"/> is not consulted: those descriptor bytes carry
    /// precision/scale for FixedPoint columns, not this.</para>
    /// </remarks>
    internal static byte[] EncodeExtendedDateTime(DateTime value)
    {
        long day = value.Ticks / TimeSpan.TicksPerDay;
        long time = value.Ticks % TimeSpan.TicksPerDay;

        string text = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{day:D19}:{time:D19}:7");

        byte[] bytes = new byte[ExtendedDateTimeLength];
        Encoding.ASCII.GetBytes(text, bytes);   // the 42nd byte stays 0x00
        return bytes;
    }

    /// <summary>The fixed on-disk width of a DATETIME2 (Date/Time Extended) value.</summary>
    internal const int ExtendedDateTimeLength = 42;

    /// <summary>
    /// Decodes a Jet Decimal/Numeric value (17 bytes): a sign byte (0x80 = negative) followed
    /// by a 128-bit magnitude stored as four 32-bit little-endian words in big-endian word
    /// order (the low word last). The value is the magnitude divided by 10^scale.
    /// </summary>
    private static decimal DecodeNumeric(ReadOnlySpan<byte> value, byte scale)
    {
        bool negative = (value[0] & 0x80) != 0;
        uint lo = BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(13, 4));
        uint mid = BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(9, 4));
        uint hi = BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(5, 4));
        uint top = BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(1, 4));

        if (top != 0)
            throw new OverflowException("Numeric value exceeds the range of System.Decimal.");

        return new decimal((int)lo, (int)mid, (int)hi, negative, scale);
    }

    /// <summary>
    /// Decodes a Jet text value, honoring compressed Unicode. A value beginning with the <c>FF FE</c> marker
    /// is compressed: it starts in 1-byte-per-character mode, and every <c>0x00</c> byte at a character
    /// boundary toggles between that and 2-byte UTF-16LE. Without the marker the value is plain UTF-16LE.
    /// </summary>
    /// <remarks>
    /// The toggling (mixed) form is not hypothetical and ACE writes it readily: <c>café中</c> is stored as
    /// <c>FF FE 63 61 66 E9 00 2D 4E</c> — "café" compressed, a switch, then 中. Decoding such a value as a
    /// single Latin1 run, as this used to, silently returns <c>café\0-N</c>: wrong data from a file Access
    /// wrote, with no error. Access's UI defaults Unicode Compression to Yes, so mixed-script text in a real
    /// database reaches this path routinely.
    /// <para>
    /// A character whose UTF-16LE <b>low</b> byte is <c>0x00</c> — U+4E00 is <c>00 4E</c> — would be
    /// indistinguishable from a switch. ACE resolves that by not using the mixed form at all when one is
    /// present: <c>aaaaa中</c> is mixed, <c>aaaaa一</c> is plain UTF-16. It also uses the form only when it
    /// strictly saves space, so <c>abc中</c> (8 bytes either way) stays UTF-16. Both measured in
    /// <c>MixedCompressionAccessTests</c>, and together they are what make toggling on <c>0x00</c> safe.
    /// </para>
    /// </remarks>
    public static string DecodeText(ReadOnlySpan<byte> value)
    {
        if (value.Length < 2 || value[0] != 0xFF || value[1] != 0xFE)
            return Encoding.Unicode.GetString(value);

        var text = new StringBuilder(value.Length - 2);
        bool oneByte = true;
        for (int i = 2; i < value.Length;)
        {
            if (value[i] == 0x00) { oneByte = !oneByte; i++; continue; }
            if (oneByte) { text.Append((char)value[i]); i++; }
            else
            {
                if (i + 1 >= value.Length) break;   // a truncated trailing pair: take what is whole
                text.Append((char)(value[i] | (value[i + 1] << 8)));
                i += 2;
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// Encodes a non-null CLR value to its on-disk bytes — the inverse of <see cref="Decode"/>.
    /// Boolean is not handled here (its value lives in the null bitmap). Text is written as UTF-16LE, or
    /// compressed (§7) when <see cref="TryCompressText"/> says ACE would. A memo/OLE value small enough to
    /// inline is written as an <b>inline</b> long value (see <see cref="EncodeInlineLongValue"/>); a larger
    /// one reaches here as the pre-built descriptor of a value
    /// <see cref="LibRed.Storage.RowInserter"/> has already put on LVAL pages.
    /// </summary>
    public static byte[] Encode(ColumnDef column, object value)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;

        // A long value already written to an LVAL page arrives as its pre-built 12-byte reference
        // descriptor, which is written verbatim (memo/OLE columns only).
        if (value is LibRed.Storage.LongValueDescriptor descriptor)
            return descriptor.Bytes;

        // Jet represents a boolean as -1 (true) / 0 (false). EF maps a CLR bool onto a numeric
        // (smallint) column, so a bool value here targets a numeric column — normalise it to the Jet
        // form so what we write matches Access (which also stores -1). (A real Boolean/YESNO column is
        // carried in the null bitmap and never reaches this method.)
        if (value is bool boolean)
            value = (short)(boolean ? -1 : 0);

        switch (column.Type)
        {
            case JetDataType.Byte:
                return [Convert.ToByte(value, c)];
            case JetDataType.Int16:
                return Bytes(2, b => BinaryPrimitives.WriteInt16LittleEndian(b, Convert.ToInt16(value, c)));
            case JetDataType.Int32:
                return Bytes(4, b => BinaryPrimitives.WriteInt32LittleEndian(b, Convert.ToInt32(value, c)));
            case JetDataType.Int64:
                return Bytes(8, b => BinaryPrimitives.WriteInt64LittleEndian(b, Convert.ToInt64(value, c)));
            case JetDataType.Single:
                return Bytes(4, b => BinaryPrimitives.WriteSingleLittleEndian(b, Convert.ToSingle(value, c)));
            case JetDataType.Double:
                return Bytes(8, b => BinaryPrimitives.WriteDoubleLittleEndian(b, Convert.ToDouble(value, c)));
            case JetDataType.DateTime:
                return Bytes(8, b => BinaryPrimitives.WriteDoubleLittleEndian(b, ToOaDate(value, c)));
            case JetDataType.DateTimeExtended: // ACE 17 DATETIME2
                return EncodeExtendedDateTime(Convert.ToDateTime(value, c));
            case JetDataType.Currency:
                return Bytes(8, b => BinaryPrimitives.WriteInt64LittleEndian(b, (long)decimal.Round(Convert.ToDecimal(value, c) * 10000m)));
            case JetDataType.Guid:
                return ((Guid)value).ToByteArray();
            case JetDataType.Text:
                return EncodeText(column, AsText(value, c));
            case JetDataType.Binary:
                return EncodeBinary(column, AsBinary(value));
            case JetDataType.FixedPoint:
                return EncodeNumeric(Convert.ToDecimal(value, c), column.Scale);

            // Long values (memo/OLE): store the payload inline after the 12-byte descriptor (memo
            // text as UTF-16LE, OLE as raw bytes). LongValueReader reads this back via the inline
            // flag. Chained LVAL pages for values too large to inline are not written yet.
            case JetDataType.Memo:
            {
                // An inline memo compresses whether or not the column was declared WITH COMPRESSION — the
                // capable flag gates single-page values, not inline ones (see TryCompressText). Only values
                // the caller has already decided to inline reach here, so no storage-form test is needed.
                string memo = AsText(value, c);
                return EncodeInlineLongValue(
                    TryCompressText(column, memo, requireCapableFlag: false) ?? Encoding.Unicode.GetBytes(memo));
            }
            case JetDataType.Ole:
                return EncodeInlineLongValue(AsBinary(value));

            default:
                throw new NotSupportedException($"Encoding {column.Type} is not supported yet.");
        }
    }

    /// <summary>The string form of a value being written to a Text/Memo column. A CLR string is used as-is;
    /// anything else (a DateTime, number, GUID …) is coerced to its invariant string form — Jet/ACE likewise
    /// stores a non-text value inserted into a text column as text (e.g. EF writes a DateTimeOffset lock
    /// timestamp into a TEXT column). Prevents an InvalidCastException on the hard <c>(string)</c> cast.</summary>
    private static string AsText(object value, IFormatProvider culture) =>
        value as string ?? Convert.ToString(value, culture) ?? "";

    /// <summary>
    /// The compressed form of <paramref name="value"/> — or null when ACE would not compress it. Characters
    /// that fit one byte are stored as one, wider ones as UTF-16LE, and a <c>0x00</c> switches between the
    /// two runs (see <see cref="EncodeCompressed"/>), so a mixed-script value is not forfeited the way an
    /// all-or-nothing writer forfeits it. Values under three characters stay UTF-16 whatever the arithmetic.
    /// <para>
    /// <paramref name="requireCapableFlag"/> is what the column's <c>WITH COMPRESSION</c> declaration gates,
    /// and it does not gate everything. Measured against ACE: an <b>inline</b> long value is compressed
    /// whether the flag is set or not, while a value on a single LVAL page is compressed <b>only</b> when it
    /// is set (a 40-character ASCII memo stores 80 bytes on a plain column and 42 on a compressed one), and
    /// a chained value never is. Ordinary Text columns do honour the flag. The storage form itself is chosen
    /// on the UNCOMPRESSED length either way — 33 ASCII characters are 66 bytes and go to a page, even
    /// though they would compress to 35 and fit inline. <c>MemoCompressionAccessTests</c>.
    /// </para>
    /// </summary>
    internal static byte[]? TryCompressText(ColumnDef column, string value, bool requireCapableFlag = true)
    {
        if (requireCapableFlag && !column.SupportsCompressedUnicode) return null;
        if (value.Length < MinCompressedCharacters) return null;

        // A 0x00 byte inside a run cannot be told from the mode switch, so ACE declines the form entirely
        // when one would appear — a NUL character, or a 2-byte character whose LOW byte is zero (U+4E00 is
        // 00 4E). One such character forfeits compression for the whole value.
        foreach (char c in value)
            if (c == '\0' || (c > 0xFF && (c & 0xFF) == 0)) return null;

        byte[] encoded = EncodeCompressed(value);
        int utf16 = value.Length * 2;

        // The two paths break an exact tie differently, measured both ways: a long value takes the form only
        // when it is strictly smaller, an ordinary Text column takes it when it is no larger. "ab中cd" is 10
        // bytes either way and comes back UTF-16 from a Memo, mixed from a Text column.
        bool longValue = column.Type is JetDataType.Memo or JetDataType.Ole;
        return (longValue ? encoded.Length >= utf16 : encoded.Length > utf16) ? null : encoded;
    }

    /// <summary>ACE leaves 1- and 2-character values as UTF-16 whatever the byte arithmetic says.</summary>
    private const int MinCompressedCharacters = 3;

    /// <summary>The compressed encoding: the <c>FF FE</c> marker, then runs of 1-byte (Latin1) and 2-byte
    /// (UTF-16LE) characters with a <c>0x00</c> switch at each change of run. An all-Latin1 value needs no
    /// switches and degenerates to the familiar marker-plus-bytes form; a value starting with a wide
    /// character opens with a switch, as ACE's does.</summary>
    private static byte[] EncodeCompressed(string value)
    {
        var bytes = new List<byte>(value.Length + 4) { 0xFF, 0xFE };
        bool oneByte = true;
        foreach (char c in value)
        {
            bool fits = c <= 0xFF;
            if (fits != oneByte)
            {
                bytes.Add(0x00);
                oneByte = fits;
            }
            bytes.Add((byte)c);
            if (!fits) bytes.Add((byte)(c >> 8));
        }
        return [.. bytes];
    }

    /// <summary>Encodes text. A fixed-length (CHAR/NCHAR) column is **space-padded** (or truncated) to its byte
    /// length — matching ACE, which stores and returns fixed text space-padded to the full width; a variable
    /// column (TEXT/VARCHAR) is its exact UTF-16 bytes, or the compressed form when the column allows it and
    /// ACE would use it (see <see cref="TryCompressText"/>).</summary>
    private static byte[] EncodeText(ColumnDef column, string value)
    {
        if (!column.IsFixedLength && TryCompressText(column, value) is { } compressed) return compressed;

        byte[] text = Encoding.Unicode.GetBytes(value);
        if (!column.IsFixedLength) return text;
        var padded = new byte[column.Length];
        for (int i = 0; i < column.Length; i += 2) padded[i] = 0x20; // UTF-16LE space (0x20 0x00)
        Array.Copy(text, padded, Math.Min(text.Length, column.Length));
        return padded;
    }

    /// <summary>Encodes binary. A fixed-length (BINARY) column is **zero-padded** (or truncated) to its byte
    /// length; a variable column (VARBINARY) is its exact bytes.</summary>
    private static byte[] EncodeBinary(ColumnDef column, byte[] value)
    {
        if (!column.IsFixedLength || value.Length == column.Length) return value;
        var padded = new byte[column.Length];
        Array.Copy(value, padded, Math.Min(value.Length, column.Length));
        return padded;
    }

    private static byte[] Bytes(int length, Action<Span<byte>> write)
    {
        var b = new byte[length];
        write(b);
        return b;
    }

    /// <summary>
    /// The bytes to store for a binary (BINARY/VARBINARY) or OLE column.
    /// </summary>
    /// <remarks>
    /// A <b>string</b> written to a binary column stores its UTF-16LE bytes, exactly as text does — verified
    /// against ACE for VARBINARY and LONGBINARY alike: <c>'A'</c> stores <c>4100</c>, <c>'AB'</c> stores
    /// <c>41004200</c>, <c>'é'</c> stores <c>E900</c>. The characters are NOT parsed as hex: <c>'41'</c>
    /// stores <c>34003100</c> — the digits '4' and '1' — not the byte <c>0x41</c>.
    ///
    /// The empty string therefore stores zero bytes, and that is the only way to write an empty binary as a
    /// literal: Access has no digitless <c>0x</c> (it rejects it), which is why
    /// <c>JetByteArrayTypeMapping</c> emits <c>''</c> for an empty array.
    /// </remarks>
    private static byte[] AsBinary(object value) => value switch
    {
        byte[] bytes => bytes,
        string text => Encoding.Unicode.GetBytes(text),
        _ => (byte[])value,
    };

    /// <summary>The OLE-automation epoch (1899-12-30), which is also Jet's zero date and the base for
    /// storing a <see cref="TimeSpan"/> / <see cref="TimeOnly"/> as a date offset.</summary>
    private static readonly DateTime OleEpoch = new(1899, 12, 30);

    /// <summary>
    /// Converts a date/time-ish CLR value to the OLE-automation double stored in a Jet DateTime
    /// column. Jet has no dedicated TimeSpan/DateOnly/TimeOnly type, so — like EFCore.Jet — a
    /// <see cref="TimeSpan"/> and <see cref="TimeOnly"/> are stored as an offset from the epoch, and a
    /// <see cref="DateOnly"/> as that date at midnight.
    /// </summary>
    private static double ToOaDate(object value, IFormatProvider c) => value switch
    {
        DateTime dt => dt.ToOADate(),
        TimeSpan ts => (OleEpoch + ts).ToOADate(),
        DateOnly d => d.ToDateTime(TimeOnly.MinValue).ToOADate(),
        TimeOnly t => (OleEpoch + t.ToTimeSpan()).ToOADate(),
        _ => Convert.ToDateTime(value, c).ToOADate(),
    };

    /// <summary>
    /// Builds an <b>inline</b> long-value (memo/OLE) in-row value: a 12-byte descriptor
    /// (length combined with the <c>0x80</c> inline flag, then 8 unused bytes) followed by the payload.
    /// This is the exact shape <see cref="LibRed.Storage.LongValueReader"/> reads back for an inline
    /// value.
    /// </summary>
    private static byte[] EncodeInlineLongValue(ReadOnlySpan<byte> payload)
    {
        LongValueFormat.ValidateLength(payload.Length);
        var result = new byte[12 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)payload.Length | 0x80000000u);
        payload.CopyTo(result.AsSpan(12));
        return result;
    }

    /// <summary>Inverse of <see cref="DecodeNumeric"/>: 17 bytes, sign + 128-bit magnitude (top word 0).</summary>
    private static byte[] EncodeNumeric(decimal value, byte scale)
    {
        decimal factor = 1m;
        for (int i = 0; i < scale; i++) factor *= 10m;
        decimal magnitude = decimal.Truncate(decimal.Round(Math.Abs(value) * factor, 0));

        int[] bits = decimal.GetBits(magnitude); // [lo, mid, hi, flags]; magnitude has scale 0
        var result = new byte[17];
        result[0] = (byte)(value < 0 ? 0x80 : 0x00);
        // bytes[1..5) top word = 0; hi at 5, mid at 9, lo at 13 (see DecodeNumeric).
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(5, 4), (uint)bits[2]);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(9, 4), (uint)bits[1]);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(13, 4), (uint)bits[0]);
        return result;
    }
}
