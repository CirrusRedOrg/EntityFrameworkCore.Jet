using System.Buffers.Binary;
using System.Numerics;
using LibRed.IO;
using LibRed.Pages;

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
    /// <summary>Bytes preceding the bitmap on a dedicated usage-bitmap page (type 0x05).</summary>
    private const int BitmapPageHeaderSize = 4;

    /// <summary>A reference map is a fixed 69-byte record: the type byte + 17 bitmap-page pointers (17 being
    /// exactly enough to span Jet's 2 GB ceiling). See the usage-maps spec (§9).</summary>
    private const int ReferenceMapSlots = 17;

    private readonly PageChannel _channel = channel;

    public int Allocate()
    {
        (byte[] page, RowSlot slot) = ReadGlobalMap();
        int mapOffset = slot.Offset;
        byte mapType = page[mapOffset];
        if (mapType == ReferenceMapType)
            return AllocateFromReferenceMap(page.AsSpan(mapOffset, slot.Length));
        if (mapType != InlineMapType)
            throw new InvalidDataException($"Global free-pages map has unknown type 0x{mapType:X2}.");
        if (slot.Length < 5)
            throw new InvalidDataException("Global inline free-pages map is shorter than its 5-byte header.");

        int startPage = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(mapOffset + 1, 4));
        int bitmapStart = mapOffset + 5;
        int bitmapEnd = mapOffset + slot.Length;
        for (int i = bitmapStart; i < bitmapEnd; i++)
        {
            if (page[i] == 0) continue;
            int bit = BitOperations.TrailingZeroCount(page[i]);
            int allocated = startPage + (i - bitmapStart) * 8 + bit;
            ValidateReusablePage(allocated, "inline free bit", allowAppendBoundary: true);
            EnsurePhysicalAllocation(allocated);
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
        ValidateReusablePage(page, "page being freed", allowAppendBoundary: false);
        (byte[] p, RowSlot slot) = ReadGlobalMap();

        int mapOffset = slot.Offset;
        byte mapType = p[mapOffset];
        if (mapType == ReferenceMapType)
        {
            FreeInReferenceMap(p.AsSpan(mapOffset, slot.Length), page);
            return;
        }
        if (mapType != InlineMapType)
            throw new InvalidDataException($"Global free-pages map has unknown type 0x{mapType:X2}.");
        if (slot.Length < 5)
            throw new InvalidDataException("Global inline free-pages map is shorter than its 5-byte header.");

        int startPage = BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(mapOffset + 1, 4));
        int bit = page - startPage;
        int byteIndex = mapOffset + 5 + bit / 8;
        if (bit < 0 || byteIndex >= mapOffset + slot.Length) return; // outside the inline window

        p[byteIndex] |= (byte)(1 << (bit % 8));
        _channel.WritePage(GlobalMapPage, p);
    }

    /// <summary>Allocates from a reference-type global free map (huge databases): the record is a list of
    /// pointers to dedicated bitmap pages (type 0x05), pointer <c>k</c> covering the page range starting at
    /// <c>k × (pageSize − 4) × 8</c>. A **set bit is a free page** (the global map's sense, opposite of a
    /// per-table owned map). Finds the first free page, clears its bit on the bitmap page, and returns it;
    /// grows the file when no bitmap records a free page.</summary>
    private int AllocateFromReferenceMap(ReadOnlySpan<byte> map)
    {
        var format = _channel.Format;
        ValidateReferenceMap(map);
        int pagesPerBitmap = (format.PageSize - BitmapPageHeaderSize) * 8;
        var bitmapPages = new HashSet<int>();
        for (int slot = 0; slot < ReferenceMapSlots; slot++)
        {
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1 + slot * 4, 4));
            if (bitmapPage == 0) continue;
            ValidateBitmapPage(bitmapPage);
            if (!bitmapPages.Add(bitmapPage))
                throw new InvalidDataException($"Global reference free map repeats bitmap page {bitmapPage}.");
        }

        for (int slot = 0; slot < ReferenceMapSlots; slot++)
        {
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1 + slot * 4, 4));
            if (bitmapPage == 0) continue; // no bitmap page allocated for this range

            byte[] bitmap = ValidateBitmapPage(bitmapPage);
            for (int i = BitmapPageHeaderSize; i < format.PageSize; i++)
            {
                if (bitmap[i] == 0) continue;
                int bit = BitOperations.TrailingZeroCount(bitmap[i]);
                int allocated = slot * pagesPerBitmap + (i - BitmapPageHeaderSize) * 8 + bit;
                ValidateReusablePage(allocated, $"reference-map slot {slot} free bit", allowAppendBoundary: true);
                if (bitmapPages.Contains(allocated))
                    throw new InvalidDataException($"Global free map marks bitmap page {allocated} itself as free.");
                EnsurePhysicalAllocation(allocated);
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
    private void FreeInReferenceMap(ReadOnlySpan<byte> map, int page)
    {
        var format = _channel.Format;
        ValidateReferenceMap(map);
        int pagesPerBitmap = (format.PageSize - BitmapPageHeaderSize) * 8;
        int slot = page / pagesPerBitmap;
        if (slot < 0 || slot >= ReferenceMapSlots) return; // beyond the map's ~2 GB reach

        int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1 + slot * 4, 4));
        if (bitmapPage == 0) return; // range has no bitmap page — nothing to record into

        int bitInRange = page - slot * pagesPerBitmap;
        byte[] bitmap = ValidateBitmapPage(bitmapPage);
        if (page == bitmapPage)
            throw new InvalidDataException($"Usage-map bitmap page {page} cannot be marked globally free.");
        bitmap[BitmapPageHeaderSize + bitInRange / 8] |= (byte)(1 << (bitInRange % 8));
        _channel.WritePage(bitmapPage, bitmap);
    }

    private (byte[] Page, RowSlot Slot) ReadGlobalMap()
    {
        if (_channel.PageCount <= GlobalMapPage)
            throw new InvalidDataException("Database has no global free-pages map at page 1.");
        PageBuffer buffer = _channel.ReadPage(GlobalMapPage);
        var data = new DataPage();
        data.Read(buffer, _channel.Format);
        if (data.RowCount < 1)
            throw new InvalidDataException("Global free-pages map page has no row 0.");
        RowSlot slot = data.Rows[0];
        if (slot.IsDeleted || slot.HasOverflow || slot.Length == 0)
            throw new InvalidDataException("Global free-pages map row 0 is deleted, overflowed, or empty.");
        return (buffer.Span.ToArray(), slot);
    }

    private static void ValidateReferenceMap(ReadOnlySpan<byte> map)
    {
        if (map.Length != 1 + ReferenceMapSlots * 4)
            throw new InvalidDataException(
                $"Global reference free map must be exactly {1 + ReferenceMapSlots * 4} bytes; got {map.Length}.");
    }

    private byte[] ValidateBitmapPage(int pageNumber)
    {
        ValidateReusablePage(pageNumber, "usage-map bitmap pointer", allowAppendBoundary: false);
        byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
        if (page[0] != (byte)PageType.PageUsageBitmap || page[1] != 0x01 || page[2] != 0 || page[3] != 0)
            throw new InvalidDataException($"Global free-map pointer {pageNumber} does not target a valid bitmap page.");
        return page;
    }

    private void ValidateReusablePage(int page, string source, bool allowAppendBoundary)
    {
        int maximum = allowAppendBoundary ? _channel.PageCount : _channel.PageCount - 1;
        if (page <= GlobalMapPage || page > maximum)
            throw new InvalidDataException(
                $"Global {source} names page {page}, outside the reusable contiguous range 2..{maximum}.");
    }

    private void EnsurePhysicalAllocation(int page)
    {
        if (page < _channel.PageCount) return;
        int allocated = _channel.AllocatePage();
        if (allocated != page)
            throw new InvalidDataException(
                $"Global free map selected append page {page}, but contiguous allocation produced page {allocated}.");
    }
}
