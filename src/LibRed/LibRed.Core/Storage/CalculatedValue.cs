using System.Buffers.Binary;
using LibRed.Catalog;

namespace LibRed.Storage;

/// <summary>
/// Unwraps the envelope ACE stores a calculated column's cached result in (spec: page-02b-columns §
/// "Calculated columns"). A calculated column is always variable-length, and its slot holds a fixed
/// header rather than a bare value:
/// <code>
/// [status:4] [reserved:12] [payloadLength:4] [payload:n] [padding:3]
/// </code>
/// <para><b>Status is not reserved.</b> Zero means the expression produced a value; anything else is the VBA
/// error number it raised, and there is no payload — which is a different thing from Null, even though both
/// have an empty payload. The conversions raise rather than propagating, so <c>CDbl(Null)</c> caches 94
/// ("invalid use of Null") where <c>[Qty]/3</c> over the same Null row caches a plain Null.</para>
/// The payload is the value in its ordinary on-disk encoding for the column's declared type, so it goes
/// straight to <see cref="Types.JetTypeCodec"/> — text keeps the usual compressed/raw UTF-16 choice.
/// When the column also owns a long-value map (a calculated Memo), the slot is a long-value descriptor
/// and the envelope is what that descriptor resolves to.
/// </summary>
internal static class CalculatedValue
{
    /// <summary>Header bytes before the length: a 4-byte status followed by 12 reserved bytes that are zero
    /// in every file measured.</summary>
    private const int HeaderSize = 16;

    /// <summary>The status field's size. Zero is the success code; anything else is a <b>VBA runtime error
    /// number</b>, and the expression produced no value at all.</summary>
    private const int StatusSize = 4;

    /// <summary>Little-endian payload length.</summary>
    private const int LengthSize = 4;

    /// <summary>Zero padding after the payload.</summary>
    private const int PaddingSize = 3;

    /// <summary>The smallest envelope: header, length and padding with an empty payload.</summary>
    public const int MinimumSize = HeaderSize + LengthSize + PaddingSize;

    /// <summary>Evaluates a calculated column's expression against one row and coerces the result to the
    /// type its payload is stored in.</summary>
    public static object? Evaluate(ColumnDef column, IReadOnlyList<ColumnDef> columns, object?[] values)
    {
        if (column.CalculatedExpression is not { Length: > 0 } text)
            throw new Calculated.CalculatedExpressionException(
                $"Calculated column '{column.Name}' has no stored expression, so its value cannot be recomputed. " +
                "The table's property blob is missing or damaged.");

        object? result = Calculated.CalculatedEvaluator.Evaluate(
            Calculated.CalculatedExpression.ParseCached(text),
            name => Lookup(column, columns, values, name));

        return Calculated.CalculatedEvaluator.Coerce(result, StoredType(column, 0));
    }

    /// <summary>The <see cref="ColumnDef.Index"/> of every column the expression reads. An UPDATE recomputes
    /// only when one of these is in its changed set — ACE leaves the cached value alone otherwise, and
    /// recomputing anyway would write bytes it never would (§3.4a).</summary>
    public static IReadOnlySet<int> ReferencedIndexes(ColumnDef column, IReadOnlyList<ColumnDef> columns)
    {
        var indexes = new HashSet<int>();
        if (column.CalculatedExpression is not { Length: > 0 } text) return indexes;

        foreach (string name in Calculated.CalculatedExpression.ReferencedColumns(
                     Calculated.CalculatedExpression.ParseCached(text)))
            foreach (ColumnDef candidate in columns)
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                    indexes.Add(candidate.Index);
        return indexes;
    }

    private static object? Lookup(ColumnDef column, IReadOnlyList<ColumnDef> columns, object?[] values, string name)
    {
        foreach (ColumnDef candidate in columns)
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                return candidate.Index < values.Length ? values[candidate.Index] : null;

        throw new Calculated.CalculatedExpressionException(
            $"Calculated column '{column.Name}' refers to '{name}', which this table does not have.");
    }

    /// <summary>Builds the envelope for <paramref name="value"/> — the inverse of
    /// <see cref="Decode"/>. A Null result is a zero-length payload, not an absent one: the column's
    /// null-bitmap bit stays set either way (§3.4a).</summary>
    public static byte[] Encode(ColumnDef column, object? value)
    {
        JetDataType type = StoredType(column, 0);
        byte[] payload = value is null ? [] : EncodePayload(column, type, value);

        var envelope = new byte[MinimumSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(HeaderSize, LengthSize), (uint)payload.Length);
        payload.CopyTo(envelope.AsSpan(HeaderSize + LengthSize));
        return envelope;
    }

    /// <summary>Two payloads the ordinary codec would get wrong.
    /// <para><b>Boolean</b>: a real Boolean column carries its value in the null bitmap and never reaches the
    /// codec, so the codec has no Boolean case. A calculated one stores a single byte inside the envelope —
    /// <c>FF</c> or <c>00</c>.</para>
    /// <para><b>Text</b>: ACE compresses a calculated <b>Text</b> column's payload even though the column's
    /// extended flags carry <c>0xC0</c> (calculated) and not <c>0x01</c> (compressed-Unicode capable), so the
    /// capable flag must not gate it — the same exemption inline long values already get. The
    /// under-three-character rule still applies and is ACE's: <c>"hello-x"</c> compresses, <c>"-x"</c> stays
    /// UTF-16.</para>
    /// <para>A calculated <b>Memo</b> — the one carrying a long-value map — is <b>never</b> compressed,
    /// whatever its length, and <see cref="StoredType"/> folding Memo onto Text is what hid that. Measured:
    /// ACE stores <c>"hello-memo"</c> as 20 bytes of UTF-16 in a Memo column and <c>"hello-x"</c> as 9
    /// compressed bytes in a Text one. It decides more than the bytes, because compressing shrinks the
    /// envelope back under the 64-byte inline limit and so decides whether the value reaches a page at all.
    /// </para>
    /// </summary>
    private static byte[] EncodePayload(ColumnDef column, JetDataType type, object value)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        switch (type)
        {
            case JetDataType.Boolean:
                return [Convert.ToBoolean(value, culture) ? (byte)0xFF : (byte)0x00];
            case JetDataType.Text:
            {
                string text = Convert.ToString(value, culture) ?? "";
                byte[] utf16 = System.Text.Encoding.Unicode.GetBytes(text);
                if (column.HasLongValueMap) return utf16;        // a calculated Memo: never compressed
                return Types.JetTypeCodec.TryCompressText(column, text, requireCapableFlag: false) ?? utf16;
            }
            default:
                return Types.JetTypeCodec.Encode(column, type, value);
        }
    }

    /// <summary>Decodes the cached result inside <paramref name="envelope"/>, or <c>null</c> when the
    /// expression evaluated to Null (ACE stores that as a zero-length payload, not a cleared null bit).
    /// Throws when the cached state is an <b>error</b> rather than a value.</summary>
    public static object? Decode(ColumnDef column, ReadOnlySpan<byte> envelope)
    {
        // A failed expression is cached as its VBA error number with no payload, so it is NOT a Null even
        // though it has a Null's empty payload -- and reading it as one is a silent wrong answer, on a value
        // neither engine re-derives. ACE will not hand back such a row at all.
        if (Status(envelope) is not 0 and var error)
            throw new Calculated.CalculatedExpressionException(
                $"Calculated column '{column.Name}' holds a cached error, not a value: "
                + $"{VbaErrorText(error)}. Its expression is '{column.CalculatedExpression ?? "(unknown)"}'.");

        ReadOnlySpan<byte> payload = Payload(envelope, column);
        JetDataType type = StoredType(column, payload.Length);

        // A zero-length payload is a Null result -- EXCEPT for text, where it is the empty string. The two
        // are indistinguishable on disk (an empty string also encodes to nothing), and ACE resolves the
        // ambiguity towards "": `Left([A],2)` over a Null [A] stores an empty payload and ACE reads back
        // "", while the same empty payload under a DateTime result type reads back Null.
        if (payload.Length == 0)
            return type == JetDataType.Text ? "" : null;

        return Types.JetTypeCodec.Decode(column, type, payload);
    }

    /// <summary>The envelope's leading status: 0 when the expression produced a value, otherwise the VBA
    /// runtime error number it raised. A short envelope reads as 0 — a truncated slot is the width check's
    /// business, not this one's.</summary>
    private static uint Status(ReadOnlySpan<byte> envelope) =>
        envelope.Length >= StatusSize ? BinaryPrimitives.ReadUInt32LittleEndian(envelope[..StatusSize]) : 0;

    /// <summary>Names the VBA errors a calculated expression can actually raise, so the message says what
    /// went wrong rather than quoting a number. Measured: <c>CDbl(Null)</c> caches 94 and <c>CDbl('hello')</c>
    /// caches 13 — the conversions raise rather than propagating, which is why they can fail at all.</summary>
    private static string VbaErrorText(uint error) => error switch
    {
        5 => "VBA error 5, invalid procedure call or argument",
        6 => "VBA error 6, overflow",
        11 => "VBA error 11, division by zero",
        13 => "VBA error 13, type mismatch",
        94 => "VBA error 94, invalid use of Null",
        _ => $"VBA error {error}",
    };

    /// <summary>
    /// The type the payload is actually encoded in.
    /// <para><see cref="ColumnDef.CalculatedResultType"/> — the <c>ResultType</c> property of the table's
    /// <c>LvProp</c> blob — is the authority, and is used whenever it is present. The descriptor's
    /// <see cref="ColumnDef.Type"/> is the promoted storage type of the *expression*, which need not agree:
    /// a <c>CDbl</c> expression on a column declared LONG has descriptor Double, <c>ResultType</c> Int32 and
    /// a four-byte payload.</para>
    /// <para>The width rule below is only the fallback for a file whose blob is missing or damaged. It
    /// reverses ACE's promotion (Boolean→Int16, Byte/Int16→Int32, Single→Double), which is correct while the
    /// declared and expression types agree — every column DAO authors — and wrong when they do not.</para>
    /// </summary>
    private static JetDataType StoredType(ColumnDef column, int payloadLength)
    {
        JetDataType type = column.CalculatedResultType ?? column.Type switch
        {
            JetDataType.Int16 when payloadLength == 1 => JetDataType.Boolean,
            JetDataType.Int32 when payloadLength == 1 => JetDataType.Byte,
            JetDataType.Int32 when payloadLength == 2 => JetDataType.Int16,
            JetDataType.Double when payloadLength == 4 => JetDataType.Single,
            _ => column.Type,
        };

        // A Memo result is plain text INSIDE the envelope — the long-value indirection wraps the whole
        // envelope and the caller has already resolved it, so the codec's Memo branch (which hands back a
        // raw descriptor) must not run here.
        return type == JetDataType.Memo ? JetDataType.Text : type;
    }

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
