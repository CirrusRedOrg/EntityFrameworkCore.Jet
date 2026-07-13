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
    private const byte ReferenceMapType = 0x01;
    private const int RowOffsetMask = 0x1FFF;

    /// <summary>Bytes preceding the bitmap on a dedicated usage-bitmap page (type 0x05).</summary>
    private const int BitmapPageHeaderSize = 4;

    /// <summary>A reference map is a fixed 69-byte record: the type byte + 17 bitmap-page pointers (17 being
    /// exactly enough to span Jet's 2 GB ceiling). See the usage-maps spec (§9).</summary>
    private const int ReferenceMapSlots = 17;

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
        byte mapType = page[mapOffset];
        if (mapType == ReferenceMapType)
            return AllocateFromReferenceMap(page, mapOffset);
        if (mapType != InlineMapType)
            return _channel.AllocatePage(); // unknown map type

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

    /// <summary>Returns a page to the global free-pages map (sets its bit) so it can be reused — the inverse
    /// of <see cref="Allocate"/>. Used when dropping an index frees its B-tree pages, matching Access.</summary>
    public void Free(int page)
    {
        var format = _channel.Format;
        byte[] p = _channel.ReadPage(GlobalMapPage).Span.ToArray();

        int mapOffset = BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(format.DataRowDirectoryOffset, 2)) & RowOffsetMask;
        byte mapType = p[mapOffset];
        if (mapType == ReferenceMapType)
        {
            FreeInReferenceMap(p, mapOffset, page);
            return;
        }
        if (mapType != InlineMapType) return; // unknown map type

        int startPage = BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(mapOffset + 1, 4));
        int bit = page - startPage;
        int byteIndex = mapOffset + 5 + bit / 8;
        if (bit < 0 || byteIndex >= format.PageSize) return; // outside the inline window

        p[byteIndex] |= (byte)(1 << (bit % 8));
        _channel.WritePage(GlobalMapPage, p);
    }

    /// <summary>Allocates from a reference-type global free map (huge databases): the record is a list of
    /// pointers to dedicated bitmap pages (type 0x05), pointer <c>k</c> covering the page range starting at
    /// <c>k × (pageSize − 4) × 8</c>. A **set bit is a free page** (the global map's sense, opposite of a
    /// per-table owned map). Finds the first free page, clears its bit on the bitmap page, and returns it;
    /// grows the file when no bitmap records a free page.</summary>
    private int AllocateFromReferenceMap(byte[] mapPage, int mapOffset)
    {
        var format = _channel.Format;
        int pagesPerBitmap = (format.PageSize - BitmapPageHeaderSize) * 8;
        int slots = Math.Min(ReferenceMapSlots, (format.PageSize - (mapOffset + 1)) / 4);

        for (int slot = 0; slot < slots; slot++)
        {
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(mapPage.AsSpan(mapOffset + 1 + slot * 4, 4));
            if (bitmapPage == 0) continue; // no bitmap page allocated for this range

            byte[] bitmap = _channel.ReadPage(bitmapPage).Span.ToArray();
            for (int i = BitmapPageHeaderSize; i < format.PageSize; i++)
            {
                if (bitmap[i] == 0) continue;
                int bit = BitOperations.TrailingZeroCount(bitmap[i]);
                int allocated = slot * pagesPerBitmap + (i - BitmapPageHeaderSize) * 8 + bit;
                bitmap[i] &= (byte)~(1 << bit); // no longer free
                _channel.WritePage(bitmapPage, bitmap);
                return allocated;
            }
        }

        // No free page recorded in any bitmap page; grow the file (the new page is used, not free).
        return _channel.AllocatePage();
    }

    /// <summary>Returns a page to a reference-type global free map by setting its bit on the bitmap page for
    /// its range. If that range has no bitmap page (e.g. a page grown past the map's coverage), the page is
    /// left unrecorded — it simply won't be reused, matching the pre-existing inline-window behaviour.</summary>
    private void FreeInReferenceMap(byte[] mapPage, int mapOffset, int page)
    {
        var format = _channel.Format;
        int pagesPerBitmap = (format.PageSize - BitmapPageHeaderSize) * 8;
        int slot = page / pagesPerBitmap;
        if (slot < 0 || slot >= ReferenceMapSlots) return; // beyond the map's ~2 GB reach

        int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(mapPage.AsSpan(mapOffset + 1 + slot * 4, 4));
        if (bitmapPage == 0) return; // range has no bitmap page — nothing to record into

        int bitInRange = page - slot * pagesPerBitmap;
        byte[] bitmap = _channel.ReadPage(bitmapPage).Span.ToArray();
        bitmap[BitmapPageHeaderSize + bitInRange / 8] |= (byte)(1 << (bitInRange % 8));
        _channel.WritePage(bitmapPage, bitmap);
    }
}
