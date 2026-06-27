using LibRed.Catalog;

namespace LibRed.Storage.Types;

/// <summary>
/// Encodes and decodes individual column values to/from their on-disk byte
/// representation. Centralises the per-type quirks: Jet CURRENCY (scaled int64),
/// the 1899-12-30 OLE date epoch, fixed-point NUMERIC, GUID byte order, and the
/// code-page/Unicode text handling.
/// </summary>
public static class JetTypeCodec
{
    /// <summary>Decodes a single fixed-or-variable value for <paramref name="column"/>.</summary>
    public static object? Decode(ColumnDef column, ReadOnlySpan<byte> value)
    {
        // TODO: switch on column.Type and decode accordingly.
        return column.Type switch
        {
            _ => null,
        };
    }

    /// <summary>Encodes a CLR value back to its on-disk representation.</summary>
    public static byte[] Encode(ColumnDef column, object? value)
    {
        // TODO: inverse of Decode.
        _ = (column, value);
        return [];
    }
}
