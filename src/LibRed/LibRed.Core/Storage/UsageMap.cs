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
/// as owned. A reference map (type 0x01, for very large tables) instead stores a list of
/// pointers to dedicated bitmap pages (type 0x05); pointer k's bitmap covers the page
/// range starting at k * (pageSize - 4) * 8.
/// </remarks>
public sealed class UsageMap(PageChannel channel, TableDef table)
{
    private const byte MapTypeInline = 0x00;
    private const byte MapTypeReference = 0x01;

    /// <summary>Bytes preceding the bitmap on a dedicated usage-bitmap page (type 0x05).</summary>
    private const int BitmapPageHeaderSize = 4;

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
            MapTypeReference => ReadReferenceMap(map),
            byte t => throw new NotSupportedException($"Unknown usage map type 0x{t:X2}."),
        };
    }

    private static List<int> ReadInlineMap(ReadOnlySpan<byte> map)
    {
        int startPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1, 4));
        var pages = new List<int>();
        AppendSetBits(pages, map[5..], startPage);
        return pages;
    }

    private List<int> ReadReferenceMap(ReadOnlySpan<byte> map)
    {
        int pagesPerBitmap = (_channel.PageSize - BitmapPageHeaderSize) * 8;
        var pages = new List<int>();

        // The record is a list of 4-byte pointers to bitmap pages; pointer k's bitmap
        // covers the page range starting at k * pagesPerBitmap. A zero pointer means the
        // range has no owned pages.
        int entryCount = (map.Length - 1) / 4;
        for (int e = 0; e < entryCount; e++)
        {
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1 + e * 4, 4));
            if (bitmapPage == 0) continue;

            int rangeBase = e * pagesPerBitmap;
            ReadOnlySpan<byte> bitmap = _channel.ReadPage(bitmapPage).Span[BitmapPageHeaderSize..];
            AppendSetBits(pages, bitmap, rangeBase);
        }

        return pages;
    }

    private static void AppendSetBits(List<int> pages, ReadOnlySpan<byte> bitmap, int basePage)
    {
        for (int i = 0; i < bitmap.Length; i++)
        {
            byte b = bitmap[i];
            if (b == 0) continue;
            for (int bit = 0; bit < 8; bit++)
                if ((b & (1 << bit)) != 0)
                    pages.Add(basePage + i * 8 + bit);
        }
    }
}
