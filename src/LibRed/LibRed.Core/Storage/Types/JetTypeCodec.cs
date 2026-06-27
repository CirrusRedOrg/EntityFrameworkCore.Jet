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
            case JetDataType.Single:
                return BinaryPrimitives.ReadSingleLittleEndian(value);
            case JetDataType.Double:
                return BinaryPrimitives.ReadDoubleLittleEndian(value);
            case JetDataType.DateTime:
                return DateTime.FromOADate(BinaryPrimitives.ReadDoubleLittleEndian(value));
            case JetDataType.Currency:
                return BinaryPrimitives.ReadInt64LittleEndian(value) / 10000m;
            case JetDataType.Guid:
                return new Guid(value[..16]);
            case JetDataType.Text:
                return DecodeText(value);
            case JetDataType.Binary:
                return value.ToArray();

            // Long values stored on LVAL pages — needs the long-value reader. TODO.
            case JetDataType.Memo:
            case JetDataType.Ole:
            case JetDataType.Complex:
            case JetDataType.FixedPoint:
            default:
                return value.ToArray();
        }
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
