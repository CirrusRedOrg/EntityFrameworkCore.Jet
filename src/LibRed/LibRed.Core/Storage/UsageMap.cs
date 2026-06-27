using LibRed.Catalog;
using LibRed.IO;

namespace LibRed.Storage;

/// <summary>
/// Enumerates the data pages that belong to a table. Jet stores this as either an
/// inline bitmap (small tables) or a reference map pointing at dedicated bitmap
/// pages (large tables); both are surfaced here as a flat page sequence.
/// </summary>
public sealed class UsageMap(PageChannel channel, TableDef table)
{
    private readonly PageChannel _channel = channel;
    private readonly TableDef _table = table;

    /// <summary>Yields the page numbers of every data page owned by the table.</summary>
    public IEnumerable<int> DataPages()
    {
        // TODO: read the inline/reference usage map from the TDEF and yield set bits.
        _ = _channel;
        _ = _table;
        yield break;
    }
}
