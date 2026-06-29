using System.Buffers.Binary;
using System.Numerics;
using LibRed.IO;

namespace LibRed.Storage;

/// <summary>
/// Allocates database pages the way Access does: through the **global free-pages map** at page 1,
/// row 0 (an inline usage map where a set bit marks a free page). Allocation takes a free page,
/// clears its bit (so it is no longer free), and returns it — reusing freed pages rather than
/// always growing the file. The file is grown only when no free page is available.
/// </summary>
public sealed class PageAllocator(PageChannel channel)
{
    private const int GlobalMapPage = 1;
    private const byte InlineMapType = 0x00;
    private const int RowOffsetMask = 0x1FFF;

    private readonly PageChannel _channel = channel;

    public int Allocate()
    {
        var format = _channel.Format;
        byte[] page = _channel.ReadPage(GlobalMapPage).Span.ToArray();

        // The free map is row 0 — the highest-offset row, running to the page end.
        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2));
        if (rowCount < 1)
            return _channel.AllocatePage();

        int mapOffset = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset, 2)) & RowOffsetMask;
        if (page[mapOffset] != InlineMapType)
            return _channel.AllocatePage(); // reference-type global map (huge DBs) not handled yet

        int startPage = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(mapOffset + 1, 4));
        int bitmapStart = mapOffset + 5;
        for (int i = bitmapStart; i < format.PageSize; i++)
        {
            if (page[i] == 0) continue;
            int bit = BitOperations.TrailingZeroCount(page[i]);
            int allocated = startPage + (i - bitmapStart) * 8 + bit;
            page[i] &= (byte)~(1 << bit); // no longer free
            _channel.WritePage(GlobalMapPage, page);
            return allocated;
        }

        // No free page recorded; fall back to growing the file (the new page is used, not free).
        return _channel.AllocatePage();
    }
}
