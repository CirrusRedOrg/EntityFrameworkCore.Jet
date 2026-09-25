using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using System.Buffers.Binary;

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
    private const int ReferenceMapSlots = 17;
    private const int ReferenceMapRecordSize = 1 + ReferenceMapSlots * 4;

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

    /// <summary>The pages recorded by the usage map at an explicit <paramref name="mapPage"/>:<paramref
    /// name="mapRow"/> pointer, rather than one of the TDEF's two fixed-offset maps. A long-value column's
    /// owned and free maps are reached this way — their pointers sit in the TDEF keyed by column id, so the
    /// pages holding a table's Memo/OLE content are invisible to <see cref="DataPages"/>.</summary>
    public IEnumerable<int> PagesInMap(int mapRow, int mapPage) => ReadMapAt(mapRow, mapPage);

    /// <summary>The dedicated bitmap pages (type 0x05) a reference-form map record at the pointer names, each
    /// validated; none for an inline record.</summary>
    public IReadOnlyList<int> BitmapPagesOf(int mapRow, int mapPage)
    {
        if (mapPage <= 1 || mapPage >= _channel.PageCount)
            throw new InvalidDataException(
                $"Usage-map pointer names page {mapPage}, outside the file's 2..{_channel.PageCount - 1} range.");
        var holder = new DataPage();
        holder.Read(_channel.ReadPage(mapPage), _channel.Format);
        if (mapRow < 0 || mapRow >= holder.RowCount)
            throw new InvalidDataException($"Usage-map row {mapPage}:{mapRow} does not exist.");
        ReadOnlySpan<byte> map = holder.GetRow(mapRow);
        if (map.Length == 0 || map[0] != MapTypeReference) return [];

        ValidateReferenceRecord(map);
        var pages = new List<int>();
        for (int e = 0; e < ReferenceMapSlots; e++)
        {
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1 + e * 4, 4));
            if (bitmapPage == 0) continue;
            _ = ReadBitmapPage(bitmapPage);
            pages.Add(bitmapPage);
        }
        return pages;
    }

    /// <summary>The highest-numbered data page the table owns, or -1 when it owns none.</summary>
    public int MaxDataPage() => EdgeDataPage(fromEnd: true);

    /// <summary>The lowest-numbered data page the table owns, or -1 when it owns none.</summary>
    public int MinDataPage() => EdgeDataPage(fromEnd: false);

    /// <summary>The owned-pages map's last (<paramref name="fromEnd"/>) or first page.</summary>
    /// <remarks>
    /// Scans the bitmap from one end rather than enumerating <see cref="DataPages"/> and taking the extreme:
    /// callers ask this on every page allocation and on every delete that empties a page, and materializing
    /// every owned page each time would make a bulk load quadratic. Cost here is bounded by the bitmap size,
    /// not the table's page count. Both ends share this walk so only the scan direction differs.
    /// </remarks>
    private int EdgeDataPage(bool fromEnd)
    {
        byte[] record = ReadMapRecord(_channel.Format.TdefOwnedPagesOffset);

        if (record.Length == 0)
            throw new InvalidDataException("A usage-map record cannot be empty.");

        if (record[0] == MapTypeInline)
        {
            if (record.Length < 5)
                throw new InvalidDataException("An inline usage-map record must contain its 5-byte header.");
            int startPage = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(1, 4));
            int bit = EdgeSetBit(record.AsSpan(5), fromEnd);
            return bit < 0 ? -1 : startPage + bit;
        }

        if (record[0] != MapTypeReference)
            throw new NotSupportedException($"Unknown usage map type 0x{record[0]:X2}.");

        ValidateReferenceRecord(record);

        int pagesPerBitmap = (_channel.PageSize - BitmapPageHeaderSize) * 8;
        for (int k = 0; k < ReferenceMapSlots; k++)
        {
            int e = fromEnd ? ReferenceMapSlots - 1 - k : k;
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(1 + e * 4, 4));
            if (bitmapPage == 0) continue;

            int bit = EdgeSetBit(ReadBitmapPage(bitmapPage), fromEnd);
            if (bit >= 0) return e * pagesPerBitmap + bit;
        }

        return -1;
    }

    /// <summary>Index of the highest (<paramref name="fromEnd"/>) or lowest set bit in
    /// <paramref name="bitmap"/>, or -1 if it is all zeros.</summary>
    private static int EdgeSetBit(ReadOnlySpan<byte> bitmap, bool fromEnd)
    {
        for (int n = 0; n < bitmap.Length; n++)
        {
            int i = fromEnd ? bitmap.Length - 1 - n : n;
            if (bitmap[i] == 0) continue;
            for (int m = 0; m < 8; m++)
            {
                int bit = fromEnd ? 7 - m : m;
                if ((bitmap[i] & (1 << bit)) != 0) return i * 8 + bit;
            }
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
    private List<int> PagesAt(int pointerOffset)
    {
        PageBuffer tdef = _channel.ReadPage(_table.DefinitionPage);
        return ReadMapAt(tdef.ReadByte(pointerOffset), tdef.ReadInt24(pointerOffset + 1));
    }

    /// <summary>Reads the usage-map record at a (row, page) pointer. Shared by the TDEF's own two maps and by
    /// the per-column long-value maps, which differ only in where the pointer is stored.</summary>
    private List<int> ReadMapAt(int mapRow, int mapPage)
    {
        // Both halves of the pointer come out of the TDEF, so both are corruption when wrong. Unchecked, the
        // page number reached the channel as an out-of-range read and the row number reached GetRow as an
        // index; the long-value map's equivalent pointer is validated the same way in RowInserter.MapPages.
        if (mapPage <= 1 || mapPage >= _channel.PageCount)
            throw new InvalidDataException(
                $"Usage-map pointer names page {mapPage}, outside the file's 2..{_channel.PageCount - 1} range.");

        var holder = new DataPage();
        holder.Read(_channel.ReadPage(mapPage), _channel.Format);
        if (mapRow < 0 || mapRow >= holder.RowCount)
            throw new InvalidDataException($"Usage-map row {mapPage}:{mapRow} does not exist.");
        ReadOnlySpan<byte> map = holder.GetRow(mapRow);

        if (map.Length == 0)
            throw new InvalidDataException("A usage-map record cannot be empty.");

        return map[0] switch
        {
            MapTypeInline => ReadInlineMap(map),
            MapTypeReference => ReadReferenceMap(map),
            byte t => throw new NotSupportedException($"Unknown usage map type 0x{t:X2}."),
        };
    }

    private List<int> ReadInlineMap(ReadOnlySpan<byte> map)
    {
        if (map.Length < 5)
            throw new InvalidDataException("An inline usage-map record must contain its 5-byte header.");
        int startPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1, 4));
        var pages = new List<int>();
        UsageMapBits.Append(pages, map[5..], startPage, _channel.PageCount, "A usage map");
        return pages;
    }

    private List<int> ReadReferenceMap(ReadOnlySpan<byte> map)
    {
        ValidateReferenceRecord(map);
        int pagesPerBitmap = (_channel.PageSize - BitmapPageHeaderSize) * 8;
        var pages = new List<int>();

        // The record is a list of 4-byte pointers to bitmap pages; pointer k's bitmap
        // covers the page range starting at k * pagesPerBitmap. A zero pointer means the
        // range has no owned pages.
        for (int e = 0; e < ReferenceMapSlots; e++)
        {
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1 + e * 4, 4));
            if (bitmapPage == 0) continue;

            int rangeBase = e * pagesPerBitmap;
            ReadOnlySpan<byte> bitmap = ReadBitmapPage(bitmapPage);
            UsageMapBits.Append(pages, bitmap, rangeBase, _channel.PageCount, "A usage map");
        }

        return pages;
    }

    private static void ValidateReferenceRecord(ReadOnlySpan<byte> map)
    {
        if (map.Length != ReferenceMapRecordSize)
            throw new InvalidDataException(
                $"A reference usage-map record must be exactly {ReferenceMapRecordSize} bytes; got {map.Length}.");
    }

    private ReadOnlySpan<byte> ReadBitmapPage(int pageNumber)
    {
        if (pageNumber <= 0 || pageNumber >= _channel.PageCount)
            throw new InvalidDataException($"Usage-map bitmap page {pageNumber} is outside the database.");

        ReadOnlySpan<byte> page = _channel.ReadPage(pageNumber).Span;
        if (page[0] != (byte)PageType.PageUsageBitmap || page[1] != 0x01 || page[2] != 0 || page[3] != 0)
            throw new InvalidDataException($"Usage-map pointer {pageNumber} does not reference a valid bitmap page.");
        return page[BitmapPageHeaderSize..];
    }

}