using System.Buffers.Binary;
using System.Text;
using LibRed.Catalog;

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
            case JetDataType.DateTimeExtended: // ACE 16 DATETIME2
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

            // Long values stored on LVAL pages — needs the long-value reader. TODO.
            case JetDataType.Memo:
            case JetDataType.Ole:
            case JetDataType.Complex:
            default:
                return value.ToArray();
        }
    }

    /// <summary>
    /// Decodes an ACE 16 DATETIME2 value: a fixed 42-byte ASCII string
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
    /// Decodes a Jet text value, honoring compressed Unicode. A value beginning with the
    /// 0xFF 0xFE marker stores ASCII-range characters as one byte each; otherwise it is
    /// UTF-16LE.
    /// </summary>
    /// <remarks>
    /// TODO: the full compressed format can toggle between 1-byte and 2-byte runs mid-string
    /// (via embedded markers) for mixed scripts; this handles the common all-compressed case.
    /// </remarks>
    public static string DecodeText(ReadOnlySpan<byte> value)
    {
        if (value.Length >= 2 && value[0] == 0xFF && value[1] == 0xFE)
            return Encoding.Latin1.GetString(value[2..]);

        return Encoding.Unicode.GetString(value);
    }

    /// <summary>
    /// Encodes a non-null CLR value to its on-disk bytes — the inverse of <see cref="Decode"/>.
    /// Boolean is not handled here (its value lives in the null bitmap). Text is written as
    /// uncompressed UTF-16LE. Memo/OLE values are written as an <b>inline</b> long value
    /// (see <see cref="EncodeInlineLongValue"/>); chained LVAL pages for larger values are not yet written.
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
            case JetDataType.Currency:
                return Bytes(8, b => BinaryPrimitives.WriteInt64LittleEndian(b, (long)decimal.Round(Convert.ToDecimal(value, c) * 10000m)));
            case JetDataType.Guid:
                return ((Guid)value).ToByteArray();
            case JetDataType.Text:
                return EncodeText(column, AsText(value, c));
            case JetDataType.Binary:
                return EncodeBinary(column, (byte[])value);
            case JetDataType.FixedPoint:
                return EncodeNumeric(Convert.ToDecimal(value, c), column.Scale);

            // Long values (memo/OLE): store the payload inline after the 12-byte descriptor (memo
            // text as UTF-16LE, OLE as raw bytes). LongValueReader reads this back via the inline
            // flag. Chained LVAL pages for values too large to inline are not written yet.
            case JetDataType.Memo:
                return EncodeInlineLongValue(Encoding.Unicode.GetBytes(AsText(value, c)));
            case JetDataType.Ole:
                return EncodeInlineLongValue((byte[])value);

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

    /// <summary>Encodes text. A fixed-length (CHAR/NCHAR) column is **space-padded** (or truncated) to its byte
    /// length — matching ACE, which stores and returns fixed text space-padded to the full width; a variable
    /// column (TEXT/VARCHAR) is its exact UTF-16 bytes.</summary>
    private static byte[] EncodeText(ColumnDef column, string value)
    {
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
    /// (24-bit length, the <c>0x80</c> inline flag, then 8 unused bytes) followed by the payload.
    /// This is the exact shape <see cref="LibRed.Storage.LongValueReader"/> reads back for an inline
    /// value.
    /// </summary>
    private static byte[] EncodeInlineLongValue(ReadOnlySpan<byte> payload)
    {
        var result = new byte[12 + payload.Length];
        result[0] = (byte)payload.Length;
        result[1] = (byte)(payload.Length >> 8);
        result[2] = (byte)(payload.Length >> 16);
        result[3] = 0x80; // inline
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
