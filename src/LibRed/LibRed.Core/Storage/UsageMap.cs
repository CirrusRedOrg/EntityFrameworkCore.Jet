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
    public IEnumerable<int> DataPages() => PagesAt(_channel.Format.TdefOwnedPagesOffset);

    /// <summary>Yields the table's data pages that still have room for a row, in ascending order — the
    /// free-pages map. A strict subset of <see cref="DataPages"/>, and normally just the page currently
    /// being appended to, so it is the map to consult when looking for somewhere to put a new row.</summary>
    public IEnumerable<int> FreeDataPages() => PagesAt(_channel.Format.TdefFreePagesOffset);

    /// <summary>The highest-numbered data page the table owns, or -1 when it owns none.</summary>
    /// <remarks>
    /// Scans the bitmap backwards rather than enumerating <see cref="DataPages"/> and taking the maximum:
    /// callers ask this on every page allocation, and materializing every owned page each time would make a
    /// bulk load quadratic. Cost here is bounded by the bitmap size, not the table's page count.
    /// </remarks>
    public int MaxDataPage()
    {
        byte[] record = ReadMapRecord(_channel.Format.TdefOwnedPagesOffset);

        if (record[0] == MapTypeInline)
        {
            int startPage = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(1, 4));
            int bit = HighestSetBit(record.AsSpan(5));
            return bit < 0 ? -1 : startPage + bit;
        }

        int pagesPerBitmap = (_channel.PageSize - BitmapPageHeaderSize) * 8;
        for (int e = (record.Length - 1) / 4 - 1; e >= 0; e--)
        {
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(1 + e * 4, 4));
            if (bitmapPage == 0) continue;

            int bit = HighestSetBit(_channel.ReadPage(bitmapPage).Span[BitmapPageHeaderSize..]);
            if (bit >= 0) return e * pagesPerBitmap + bit;
        }

        return -1;
    }

    /// <summary>Index of the highest set bit in <paramref name="bitmap"/>, or -1 if it is all zeros.</summary>
    private static int HighestSetBit(ReadOnlySpan<byte> bitmap)
    {
        for (int i = bitmap.Length - 1; i >= 0; i--)
        {
            if (bitmap[i] == 0) continue;
            for (int bit = 7; bit >= 0; bit--)
                if ((bitmap[i] & (1 << bit)) != 0)
                    return i * 8 + bit;
        }
        return -1;
    }

    /// <summary>The raw usage-map record whose (row, page) pointer sits at <paramref name="pointerOffset"/>
    /// in the TDEF.</summary>
    private byte[] ReadMapRecord(int pointerOffset)
    {
        JetFormatBase format = _channel.Format;
        PageBuffer tdef = _channel.ReadPage(_table.DefinitionPage);

        var holder = new DataPage();
        holder.Read(_channel.ReadPage(tdef.ReadInt24(pointerOffset + 1)), format);
        return holder.GetRow(tdef.ReadByte(pointerOffset)).ToArray();
    }

    /// <summary>Reads the usage map whose (row, page) pointer sits at <paramref name="pointerOffset"/> in
    /// the TDEF. Both maps share the same pointer shape and record format.</summary>
    private IEnumerable<int> PagesAt(int pointerOffset)
    {
        JetFormatBase format = _channel.Format;

        PageBuffer tdef = _channel.ReadPage(_table.DefinitionPage);
        int mapRow = tdef.ReadByte(pointerOffset);
        int mapPage = tdef.ReadInt24(pointerOffset + 1);

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
