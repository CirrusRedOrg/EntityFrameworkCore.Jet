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

    /// <summary>Encodes a CLR value back to its on-disk representation.</summary>
    public static byte[] Encode(ColumnDef column, object? value)
    {
        // TODO: inverse of Decode.
        _ = (column, value);
        return [];
    }
}
