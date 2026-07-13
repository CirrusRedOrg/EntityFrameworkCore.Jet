using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;

namespace LibRed.Storage;

/// <summary>
/// Decodes the order-preserving key bytes of an index entry back into column values.
/// </summary>
/// <remarks>
/// Each non-boolean column is prefixed by a flag byte (0x7F start / 0x00 null for ascending;
/// 0x80 / 0xFF for descending). Fixed/numeric types use a reversible transform (sign-bit flip
/// + big-endian for integers; an IEEE transform for floating point). TEXT/Binary/GUID keys use
/// Jet's collation encoding, which is lossy and not reversible — decoding stops at the first
/// such column (its value and any following columns are returned as null).
/// </remarks>
public static class IndexKeyDecoder
{
    private const byte AscBooleanTrue = 0x00; // ascending: true sorts before false

    public static object?[] Decode(IReadOnlyList<(ColumnDef Column, bool Ascending)> columns, ReadOnlySpan<byte> key)
    {
        var values = new object?[columns.Count];
        int pos = 0;

        for (int i = 0; i < columns.Count; i++)
        {
            (ColumnDef column, bool ascending) = columns[i];
            if (pos >= key.Length) break;

            // Booleans carry no flag byte — the value IS the byte.
            if (column.Type == JetDataType.Boolean)
            {
                byte b = key[pos++];
                values[i] = ascending ? b == AscBooleanTrue : b != AscBooleanTrue;
                continue;
            }

            byte flag = key[pos++];
            if (flag == (ascending ? IndexKeyFlags.AscNull : IndexKeyFlags.DescNull))
            {
                values[i] = null;
                continue;
            }
            // Otherwise flag is the start flag (0x7F / 0x80).

            // GUID key: 8 bytes, a 0x09 marker, 8 bytes, a terminator — the 16 bytes are the GUID's
            // canonical string order (see IndexKeyEncoder). Descending inverts every data byte (the 0x09
            // marker stays constant), so undo that here.
            if (column.Type == JetDataType.Guid)
            {
                if (pos + 18 > key.Length) break;
                var s = new byte[16];
                for (int j = 0; j < 8; j++) s[j] = ascending ? key[pos + j] : (byte)~key[pos + j];
                for (int j = 0; j < 8; j++) s[8 + j] = ascending ? key[pos + 9 + j] : (byte)~key[pos + 9 + j];
                pos += 18;
                values[i] = new Guid(Convert.ToHexString(s));
                continue;
            }

            int size = FixedKeySize(column.Type);
            if (size <= 0 || pos + size > key.Length)
                break; // text/binary/unsupported (lossy) — cannot reliably continue

            Span<byte> raw = key.Slice(pos, size).ToArray();
            pos += size;
            values[i] = DecodeFixed(column.Type, raw, ascending);
        }

        return values;
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

    private static object DecodeFixed(JetDataType type, Span<byte> raw, bool ascending)
    {
        switch (type)
        {
            case JetDataType.Byte:
                if (!ascending) raw[0] = (byte)~raw[0];
                return raw[0];

            case JetDataType.Int16:
                return (short)DecodeInteger(raw, ascending);
            case JetDataType.Int32:
                return (int)DecodeInteger(raw, ascending);
            case JetDataType.Currency:
                return DecodeInteger(raw, ascending) / 10000m;

            case JetDataType.Single:
                return BitConverter.Int32BitsToSingle((int)DecodeFloatBits(raw, ascending));
            case JetDataType.Double:
                return BitConverter.Int64BitsToDouble(DecodeFloatBits(raw, ascending));
            case JetDataType.DateTime:
                return DateTime.FromOADate(BitConverter.Int64BitsToDouble(DecodeFloatBits(raw, ascending)));

            default:
                throw new NotSupportedException($"Index key type {type} is not decodable.");
        }
    }

    /// <summary>Reverses the integer key transform (descending = bytes inverted; sign bit flipped; big-endian).</summary>
    private static long DecodeInteger(Span<byte> raw, bool ascending)
    {
        if (!ascending)
            for (int i = 0; i < raw.Length; i++) raw[i] = (byte)~raw[i];
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
            else for (int i = 0; i < raw.Length; i++) raw[i] = (byte)~raw[i]; // was negative: undo full invert
        }
        else
        {
            if ((raw[0] & 0x80) == 0)                                  // was positive
            {
                for (int i = 0; i < raw.Length; i++) raw[i] = (byte)~raw[i];
                raw[0] ^= 0x80;
            }
            // was negative: stored as-is
        }

        long bits = 0;
        foreach (byte b in raw) bits = (bits << 8) | b;
        return bits;
    }
}
