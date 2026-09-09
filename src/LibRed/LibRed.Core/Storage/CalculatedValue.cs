using System.Buffers.Binary;
using LibRed.Catalog;

namespace LibRed.Storage;

/// <summary>
/// Unwraps the envelope ACE stores a calculated column's cached result in (spec: page-02b-columns §
/// "Calculated columns"). A calculated column is always variable-length, and its slot holds a fixed
/// header rather than a bare value:
/// <code>
/// [reserved:16] [payloadLength:4] [payload:n] [padding:3]
/// </code>
/// The payload is the value in its ordinary on-disk encoding for the column's declared type, so it goes
/// straight to <see cref="Types.JetTypeCodec"/> — text keeps the usual compressed/raw UTF-16 choice.
/// When the column also owns a long-value map (a calculated Memo), the slot is a long-value descriptor
/// and the envelope is what that descriptor resolves to.
/// </summary>
internal static class CalculatedValue
{
    /// <summary>Reserved bytes before the length; zero in every file measured.</summary>
    private const int HeaderSize = 16;

    /// <summary>Little-endian payload length.</summary>
    private const int LengthSize = 4;

    /// <summary>Zero padding after the payload.</summary>
    private const int PaddingSize = 3;

    /// <summary>The smallest envelope: header, length and padding with an empty payload.</summary>
    public const int MinimumSize = HeaderSize + LengthSize + PaddingSize;

    /// <summary>Decodes the cached result inside <paramref name="envelope"/>, or <c>null</c> when the
    /// expression evaluated to Null (ACE stores that as a zero-length payload, not a cleared null bit).</summary>
    public static object? Decode(ColumnDef column, ReadOnlySpan<byte> envelope)
    {
        ReadOnlySpan<byte> payload = Payload(envelope, column);
        return payload.Length == 0 ? null : Types.JetTypeCodec.Decode(column, StoredType(column, payload.Length), payload);
    }

    /// <summary>
    /// The type the payload is actually encoded in. ACE widens a calculated column's descriptor type to the
    /// next storage type in its family and keeps the payload at the result's natural width, so the declared
    /// type alone reads the wrong number of bytes. Measured against ACE-authored columns of every type DAO
    /// will create; the (declared, width) pair is unambiguous, including Boolean and Byte, which promote to
    /// different declared types and so stay distinguishable at one byte.
    /// </summary>
    private static JetDataType StoredType(ColumnDef column, int payloadLength) => column.Type switch
    {
        JetDataType.Int16 when payloadLength == 1 => JetDataType.Boolean,
        JetDataType.Int32 when payloadLength == 1 => JetDataType.Byte,
        JetDataType.Int32 when payloadLength == 2 => JetDataType.Int16,
        JetDataType.Double when payloadLength == 4 => JetDataType.Single,
        _ => column.Type,
    };

    /// <summary>The stored value inside <paramref name="envelope"/>, ready for the ordinary type codec.</summary>
    public static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> envelope, ColumnDef column)
    {
        if (envelope.Length < MinimumSize)
            throw new InvalidDataException(
                $"Calculated column '{column.Name}' has a {envelope.Length}-byte stored value; " +
                $"its envelope needs at least {MinimumSize}.");

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(envelope.Slice(HeaderSize, LengthSize));
        if (length > envelope.Length - MinimumSize)
            throw new InvalidDataException(
                $"Calculated column '{column.Name}' declares a {length}-byte value inside a " +
                $"{envelope.Length}-byte envelope.");

        return envelope.Slice(HeaderSize + LengthSize, (int)length);
    }
}
