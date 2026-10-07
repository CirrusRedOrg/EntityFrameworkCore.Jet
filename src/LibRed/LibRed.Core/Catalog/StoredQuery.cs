using LibRed.Formats;
using System.Buffers.Binary;

namespace LibRed.Catalog;

/// <summary>A stored query read back from the catalog. A view (<paramref name="IsAction"/> false) carries its SELECT in
/// <paramref name="Sql"/>, or null when it does not reconstruct. An action query — a CREATE PROCEDURE body that is not a
/// SELECT — carries its reconstructed, executable statement there when LibRed supports the kind; otherwise it is null
/// and <paramref name="UnsupportedReason"/> explains why executing it throws.</summary>
public sealed record StoredQuery(string? Sql, string? UnsupportedReason, bool IsAction = true)
{
    /// <summary>The parameters the query declares, in declaration order; empty for one that declares none.</summary>
    public IReadOnlyList<StoredQueryParameter> Parameters { get; init; } = [];

    /// <summary><see cref="QueryAttribute.Type"/> Flag value for a SELECT query.</summary>
    internal const short QueryTypeSelect = 1;

    /// <summary><see cref="QueryAttribute.Column"/> Flag bit marking an appended literal value.</summary>
    internal const short AppendValueFlag = unchecked((short)0x8000);

    /// <summary>The <c>Order</c> value of a row: a 4-byte big-endian counter, kept per attribute.</summary>
    internal static byte[] PackOrder(int order)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, order);
        return bytes;
    }

    /// <summary>A row's <c>Order</c> counter back out of its value — the inverse of <see cref="PackOrder"/>; 0 when
    /// the row has none.</summary>
    internal static int UnpackOrder(object? order) =>
        order is byte[] b && b.Length >= sizeof(int) ? BinaryPrimitives.ReadInt32BigEndian(b) : 0;

    /// <summary>
    /// The <c>LvExtra</c> value for a declared parameter's facets, or null for a type that carries none.
    /// Measured against ACE: a <c>Text(50)</c> parameter stores the length, 50; a <c>Decimal(18,4)</c> packs
    /// both into one value, <c>(scale &lt;&lt; 16) | precision</c> = 262162; and a sized <c>Binary(10)</c>
    /// stores nothing at all, as every type without a declared size does. Access renders the query's
    /// PARAMETERS clause from this, so a parameter with no value here reads back as <c>Text(255)</c>.
    /// </summary>
    internal static int? PackParameterFacets(JetDataType type, int? size, int? scale) => type switch
    {
        JetDataType.Text => size,
        JetDataType.FixedPoint when size is { } precision => (scale ?? 0) << 16 | precision & 0xFFFF,
        _ => null,
    };

    /// <summary>A declared parameter's facets back out of its <c>LvExtra</c> — the inverse of
    /// <see cref="PackParameterFacets"/>. A text parameter reports its length as the size; a decimal reports
    /// the precision and scale packed into the one value.</summary>
    internal static (int? Size, int? Precision, int? Scale) UnpackParameterFacets(JetDataType type, int? lvExtra) =>
        lvExtra is not { } packed ? (null, null, null)
        : type == JetDataType.FixedPoint ? (null, packed & 0xFFFF, packed >> 16)
        : type == JetDataType.Text ? (packed, null, null)
        : (null, null, null);

}