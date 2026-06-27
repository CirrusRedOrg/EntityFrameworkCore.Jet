using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Enumerates the data pages that belong to a table by reading its owned-pages usage map.
/// </summary>
/// <remarks>
/// The TDEF holds a pointer (row + page) to the usage-map record. An inline map
/// (type 0x00) stores a start page and a bitmap where bit i marks page (startPage + i)
/// as owned. A reference map (type 0x01, only for very large tables) instead points at
/// dedicated bitmap pages — not yet parsed, so we fall back to a full owner-scan there.
/// </remarks>
public sealed class UsageMap(PageChannel channel, TableDef table)
{
    private const byte MapTypeInline = 0x00;
    private const byte MapTypeReference = 0x01;

    private readonly PageChannel _channel = channel;
    private readonly TableDef _table = table;

    /// <summary>Yields the page numbers of every data page owned by the table, in ascending order.</summary>
    public IEnumerable<int> DataPages()
    {
        JetFormatBase format = _channel.Format;

        PageBuffer tdef = _channel.ReadPage(_table.DefinitionPage);
        int mapRow = tdef.ReadByte(format.TdefOwnedPagesOffset);
        int mapPage = tdef.ReadInt24(format.TdefOwnedPagesOffset + 1);

        var holder = new DataPage();
        holder.Read(_channel.ReadPage(mapPage), format);
        ReadOnlySpan<byte> map = holder.GetRow(mapRow);

        return map[0] switch
        {
            MapTypeInline => ReadInlineMap(map),
            // TODO: parse reference maps (dedicated bitmap pages). Fall back meanwhile.
            MapTypeReference => OwnerScan(),
            byte t => throw new NotSupportedException($"Unknown usage map type 0x{t:X2}."),
        };
    }

    private static List<int> ReadInlineMap(ReadOnlySpan<byte> map)
    {
        int startPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1, 4));
        var pages = new List<int>();

        for (int i = 5; i < map.Length; i++)
        {
            byte b = map[i];
            if (b == 0) continue;
            for (int bit = 0; bit < 8; bit++)
                if ((b & (1 << bit)) != 0)
                    pages.Add(startPage + (i - 5) * 8 + bit);
        }

        return pages;
    }

    /// <summary>Fallback: scan every page and match the owning-table pointer.</summary>
    private IEnumerable<int> OwnerScan()
    {
        for (int p = 0; p < _channel.PageCount; p++)
        {
            PageBuffer buffer = _channel.ReadPage(p);
            if (buffer.ReadByte(0) != (byte)PageType.DataPage) continue;

            var page = new DataPage();
            page.Read(buffer, _channel.Format);
            if (page.OwningTablePage == _table.DefinitionPage) yield return p;
        }
    }
}
