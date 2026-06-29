using System.Buffers.Binary;
using System.Globalization;
using LibRed.Catalog;

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
/// Text/Binary/GUID keys use Jet's lossy collation, which is not reproduced yet — those throw.
/// </remarks>
public static class IndexKeyEncoder
{
    private const byte AscStartFlag = 0x7F;
    private const byte AscNullFlag = 0x00;
    private const byte DescStartFlag = 0x80;
    private const byte DescNullFlag = 0xFF;

    public static byte[] Encode(IReadOnlyList<(ColumnDef Column, bool Ascending)> columns, object?[] values)
    {
        var buffer = new List<byte>();

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
                buffer.Add(ascending ? AscNullFlag : DescNullFlag);
                continue;
            }

            // Text uses Jet's collation: start flag then the collation key body (weights, inline
            // ignorable codes, terminator) from JetTextCollation.
            if (column.Type == JetDataType.Text)
            {
                if (!ascending)
                    throw new NotSupportedException("Descending text index keys are not supported yet.");
                buffer.Add(AscStartFlag);
                if (!JetTextCollation.TryEncode((string)value, buffer))
                    throw new NotSupportedException($"Text index key '{value}' contains a character whose collation weight is not implemented yet.");
                continue;
            }

            int size = FixedKeySize(column.Type);
            if (size <= 0)
                throw new NotSupportedException(
                    $"Index key encoding for {column.Type} (binary collation) is not supported yet.");

            buffer.Add(ascending ? AscStartFlag : DescStartFlag);
            byte[] raw = EncodeFixed(column.Type, value);
            if (!ascending)
                for (int j = 0; j < raw.Length; j++) raw[j] = (byte)~raw[j];
            buffer.AddRange(raw);
        }

        return [.. buffer];
    }

    private static int FixedKeySize(JetDataType type) => type switch
    {
        JetDataType.Byte => 1,
        JetDataType.Int16 => 2,
        JetDataType.Int32 => 4,
        JetDataType.Single => 4,
        JetDataType.Double or JetDataType.DateTime => 8,
        JetDataType.Currency => 8,
        _ => -1,
    };

    private static byte[] EncodeFixed(JetDataType type, object value)
    {
        var c = CultureInfo.InvariantCulture;
        switch (type)
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
            default:
                throw new NotSupportedException($"Index key type {type} is not encodable.");
        }
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
