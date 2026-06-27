using LibRed.Catalog;
using LibRed.IO;

namespace LibRed.Storage;

/// <summary>
/// Decodes a single raw row record into CLR values. Handles the Jet row layout:
/// fixed-length columns first, then the variable-length column offset table and the
/// trailing null bitmask.
/// </summary>
public sealed class RowDecoder(TableDef table)
{
    private readonly TableDef _table = table;

    /// <summary>Decodes the row at <paramref name="row"/> into one boxed value per column.</summary>
    public object?[] Decode(ReadOnlySpan<byte> row)
    {
        var values = new object?[_table.Columns.Count];
        // TODO: read fixed-length values, the var-length offset jump table and null mask;
        // dispatch each column through JetTypeCodec.
        return values;
    }
}
