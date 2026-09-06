using System.Buffers.Binary;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Writes a usage map — the bitmap recording which pages belong to a table, an index, or a long-value
/// column. The counterpart to <see cref="UsageMap"/>, which reads them.
/// </summary>
/// <remarks>
/// A map is a single record on a usage-map page, addressed by a (row, page) pointer in the TDEF. It starts
/// as an <b>inline</b> map (type <c>0x00</c>: a start page plus a bitmap) and, once its record can no longer
/// grow within its page, is rewritten as a <b>reference</b> map (type <c>0x01</c>: pointers to dedicated
/// bitmap pages). See §9 of the format spec.
/// </remarks>
public sealed class UsageMapWriter(PageChannel channel)
{
    // Access grows an inline usage-map bitmap in 32-bit (4-byte) steps. Verified against owned-map record
    // lengths on a 255-column ACE table whose data pages start at 353: 8,000 rows → 1053, 12,000 → 1553,
    // 30,000 → 3801, i.e. 5 + roundUp(ceil((353 + rows) / 8), 4) exactly. (A 32-byte chunk would give 1056 /
    // 1568 / 3808.) Getting this right matters beyond tidiness: overshooting the record length spends the
    // usage-map page's remaining room and converts the map to reference type earlier than Access would.
    private const int UsageMapChunkBytes = 4;

    private const byte InlineMapType = 0x00;
    private const byte ReferenceMapType = 0x01;
    private const int InlineMapHeaderSize = 5;      // type byte + 4-byte start page
    private const int BitmapPageHeaderSize = 4;     // type + flags + 2 unused, then the bitmap

    // A reference map's record is a type byte followed by 17 four-byte bitmap-page pointers (69 bytes).
    // Seventeen is not arbitrary: each bitmap page covers (pageSize - 4) * 8 = 32,736 pages, so 17 slots
    // span ~2.28 GB — just past Jet's 2 GB file ceiling. Verified against an ACE-built 134 MB table.
    private const int ReferenceMapSlots = 17;
    private const int ReferenceMapRecordSize = 1 + ReferenceMapSlots * 4;

    // A movable inline window is exactly 512 pages (a 64-byte bitmap) aligned to a 512-page boundary.
    // Verified against ACE free-pages maps: the sole set bit at page 852/1227/1852/2852 sat in a 64-byte
    // record whose startPage was 512/1024/1536/2560 — i.e. floor(page / 512) * 512.
    private const int InlineWindowBitmapBytes = 64;
    private const int InlineWindowPages = InlineWindowBitmapBytes * 8;

    private readonly PageChannel _channel = channel;

    /// <summary>Sets or clears the bit for <paramref name="targetPage"/> in the usage map at record
    /// <paramref name="mapRow"/> on <paramref name="mapPage"/>.</summary>
    /// <remarks>
    /// Handles both map types. An inline map (0x00) is grown in place while its record still fits the page;
    /// once it cannot, the map is converted to a reference map (0x01) — exactly Access's own threshold.
    /// <para>
    /// <paramref name="movableWindow"/> marks a map whose set bits stay clustered near the append tail — a
    /// free-pages map. Rather than growing a bitmap from <c>startPage = 0</c> all the way out to the tail,
    /// such a map slides a fixed 512-page window, as Access does, so its record stays 69 bytes forever.
    /// An owned-pages map cannot do this: it must retain every page it has ever taken.
    /// </para>
    /// </remarks>
    public void SetBit(int mapRow, int mapPage, int targetPage, bool set, bool movableWindow = false)
    {
        JetFormatBase format = _channel.Format;
        byte[] page = _channel.ReadPage(mapPage).Span.ToArray();
        var holder = new DataPage();
        holder.Read(_channel.ReadPage(mapPage), format);
        int mapOffset = holder.Rows[mapRow].Offset;

        if (page[mapOffset] == ReferenceMapType)
        {
            SetReferenceBit(page, mapPage, mapOffset, targetPage, set);
            return;
        }

        int startPage = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(mapOffset + 1, 4));
        int bitmapBits = (holder.Rows[mapRow].Length - InlineMapHeaderSize) * 8;
        int bitIndex = targetPage - startPage;

        // The inline bitmap covers pages [startPage, startPage + bitmapBits). Clearing a bit outside that
        // window is a no-op — it is already 0.
        if (bitIndex < 0 || bitIndex >= bitmapBits)
        {
            if (!set) return;

            // A free-pages map slides its window onto the target instead of growing (and can therefore also
            // move *backwards*, which a grown map could never do).
            if (movableWindow && TryRepositionWindow(page, holder, format, mapPage, mapRow, targetPage))
                return;

            if (bitIndex < 0)
                throw new NotSupportedException(
                    $"Page {targetPage} is below the usage map's start page {startPage}; this map's window cannot move.");
        }

        // When Access needs to mark a page beyond the window it grows the bitmap record in place (still
        // type 0x00, same startPage), extending it in 4-byte steps.
        if (bitIndex >= bitmapBits)
        {
            int neededBitmapBytes = RoundUpTo(bitIndex / 8 + 1, UsageMapChunkBytes);
            byte[] grownRecord = new byte[InlineMapHeaderSize + neededBitmapBytes]; // extra bitmap bytes stay zero
            page.AsSpan(mapOffset, holder.Rows[mapRow].Length).CopyTo(grownRecord);

            byte[]? grown = ReplaceMapRecord(page, holder, format, mapRow, grownRecord, out mapOffset);
            if (grown is null)
            {
                // The record can no longer grow within its page: switch to a reference map and retry there.
                ConvertInlineToReference(mapPage, mapRow);
                page = _channel.ReadPage(mapPage).Span.ToArray();
                var converted = new DataPage();
                converted.Read(_channel.ReadPage(mapPage), format);
                SetReferenceBit(page, mapPage, converted.Rows[mapRow].Offset, targetPage, set);
                return;
            }

            page = grown;
        }

        int byteIndex = mapOffset + InlineMapHeaderSize + bitIndex / 8;
        byte mask = (byte)(1 << (bitIndex % 8));
        if (set) page[byteIndex] |= mask;
        else page[byteIndex] &= (byte)~mask;
        _channel.WritePage(mapPage, page);
    }

    /// <summary>
    /// Slides an inline map's window onto <paramref name="targetPage"/>: a 64-byte bitmap starting at
    /// <c>floor(targetPage / 512) * 512</c>, carrying over every bit already set and adding the target's.
    /// Returns <see langword="false"/> — leaving the map untouched — when some page already marked would
    /// fall outside the new window, since moving would silently forget it; the caller then grows instead.
    /// </summary>
    private bool TryRepositionWindow(byte[] page, DataPage holder, JetFormatBase format, int mapPage, int mapRow, int targetPage)
    {
        RowSlot slot = holder.Rows[mapRow];
        int startPage = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(slot.Offset + 1, 4));
        ReadOnlySpan<byte> bitmap = page.AsSpan(slot.Offset + InlineMapHeaderSize, slot.Length - InlineMapHeaderSize);

        int newStart = targetPage / InlineWindowPages * InlineWindowPages;
        int newEnd = newStart + InlineWindowPages;

        var marked = new List<int>();
        for (int i = 0; i < bitmap.Length; i++)
        {
            if (bitmap[i] == 0) continue;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((bitmap[i] & (1 << bit)) == 0) continue;
                int markedPage = startPage + i * 8 + bit;
                if (markedPage < newStart || markedPage >= newEnd) return false;
                marked.Add(markedPage);
            }
        }

        var record = new byte[InlineMapHeaderSize + InlineWindowBitmapBytes];
        record[0] = InlineMapType;
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(1, 4), newStart);
        marked.Add(targetPage);
        foreach (int markedPage in marked)
        {
            int bit = markedPage - newStart;
            record[InlineMapHeaderSize + bit / 8] |= (byte)(1 << (bit % 8));
        }

        byte[]? rewritten = ReplaceMapRecord(page, holder, format, mapRow, record, out _);
        if (rewritten is null) return false; // shrinking or same size, so effectively unreachable

        _channel.WritePage(mapPage, rewritten);
        return true;
    }

    /// <summary>Number of pages one dedicated bitmap page (type 0x05) covers.</summary>
    private int PagesPerBitmapPage => (_channel.Format.PageSize - BitmapPageHeaderSize) * 8;

    /// <summary>Sets or clears <paramref name="targetPage"/>'s bit in a reference map: pointer slot
    /// <c>targetPage / PagesPerBitmapPage</c> names the bitmap page holding it. A slot's bitmap page is
    /// allocated lazily, only when a bit in its range is first set.</summary>
    private void SetReferenceBit(byte[] page, int mapPage, int mapOffset, int targetPage, bool set)
    {
        int slot = targetPage / PagesPerBitmapPage;
        if (slot >= ReferenceMapSlots)
            throw new NotSupportedException(
                $"Page {targetPage} lies past the {ReferenceMapSlots} bitmap slots a usage map can address (Jet's 2 GB file limit).");

        int pointerOffset = mapOffset + 1 + slot * 4;
        int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(pointerOffset, 4));
        if (bitmapPage == 0)
        {
            if (!set) return; // the bit is already clear — no need to materialize the bitmap page
            bitmapPage = AllocateBitmapPage();
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(pointerOffset, 4), bitmapPage);
            _channel.WritePage(mapPage, page);
        }

        SetBitmapPageBit(bitmapPage, targetPage % PagesPerBitmapPage, set);
    }

    private void SetBitmapPageBit(int bitmapPage, int bit, bool set)
    {
        byte[] bitmap = _channel.ReadPage(bitmapPage).Span.ToArray();
        int byteIndex = BitmapPageHeaderSize + bit / 8;
        byte mask = (byte)(1 << (bit % 8));
        if (set) bitmap[byteIndex] |= mask;
        else bitmap[byteIndex] &= (byte)~mask;
        _channel.WritePage(bitmapPage, bitmap);
    }

    /// <summary>Allocates and initialises an empty dedicated usage-bitmap page (type 0x05).</summary>
    private int AllocateBitmapPage()
    {
        int pageNumber = new PageAllocator(_channel).Allocate();
        var bitmap = new byte[_channel.Format.PageSize];
        bitmap[0] = (byte)PageType.PageUsageBitmap;
        bitmap[1] = 0x01; // page flags (observed constant, as on data pages)
        _channel.WritePage(pageNumber, bitmap);
        return pageNumber;
    }

    /// <summary>
    /// Rewrites an inline (0x00) usage map as a reference (0x01) map: every page the inline bitmap marked is
    /// re-marked in a dedicated bitmap page, and the record shrinks to the fixed 69-byte pointer table. Bits
    /// are grouped by slot so each bitmap page is written once rather than once per page.
    /// </summary>
    private void ConvertInlineToReference(int mapPage, int mapRow)
    {
        JetFormatBase format = _channel.Format;
        byte[] page = _channel.ReadPage(mapPage).Span.ToArray();
        var holder = new DataPage();
        holder.Read(_channel.ReadPage(mapPage), format);
        RowSlot slot = holder.Rows[mapRow];

        int startPage = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(slot.Offset + 1, 4));
        ReadOnlySpan<byte> bitmap = page.AsSpan(slot.Offset + InlineMapHeaderSize, slot.Length - InlineMapHeaderSize);

        var marked = new List<int>();
        for (int i = 0; i < bitmap.Length; i++)
            for (int bit = 0; bit < 8; bit++)
                if ((bitmap[i] & (1 << bit)) != 0)
                    marked.Add(startPage + i * 8 + bit);

        var record = new byte[ReferenceMapRecordSize];
        record[0] = ReferenceMapType;

        foreach (IGrouping<int, int> group in marked.GroupBy(p => p / PagesPerBitmapPage))
        {
            if (group.Key >= ReferenceMapSlots)
                throw new NotSupportedException(
                    $"Page {group.First()} lies past the {ReferenceMapSlots} bitmap slots a usage map can address (Jet's 2 GB file limit).");

            int bitmapPage = AllocateBitmapPage();
            byte[] bits = _channel.ReadPage(bitmapPage).Span.ToArray();
            foreach (int ownedPage in group)
            {
                int bit = ownedPage % PagesPerBitmapPage;
                bits[BitmapPageHeaderSize + bit / 8] |= (byte)(1 << (bit % 8));
            }
            _channel.WritePage(bitmapPage, bits);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(1 + group.Key * 4, 4), bitmapPage);
        }

        // Re-read: allocating bitmap pages above may have grown the file, though not this page.
        byte[] fresh = _channel.ReadPage(mapPage).Span.ToArray();
        var current = new DataPage();
        current.Read(_channel.ReadPage(mapPage), format);
        byte[] rewritten = ReplaceMapRecord(fresh, current, format, mapRow, record, out _)
            ?? throw new InvalidOperationException("The reference-map record does not fit its usage-map page.");
        _channel.WritePage(mapPage, rewritten);
    }

    private static int RoundUpTo(int value, int unit) => (value + unit - 1) / unit * unit;

    /// <summary>Replaces the usage-map record at <paramref name="mapRow"/> with <paramref name="newRecord"/>,
    /// repacking every record on the page from the end backward — the way Access enlarges a table's owned/free
    /// bitmap once it spans past the current window. Records keep their directory order (row 0 nearest the
    /// page end). Returns the rewritten page and the record's new offset, or <see langword="null"/> if the
    /// record no longer fits the page.</summary>
    internal static byte[]? ReplaceMapRecord(byte[] page, DataPage holder, JetFormatBase format, int mapRow, byte[] newRecord, out int newOffset)
    {
        int rowCount = holder.Rows.Count;
        var records = new byte[rowCount][];
        for (int i = 0; i < rowCount; i++)
            records[i] = page.AsSpan(holder.Rows[i].Offset, holder.Rows[i].Length).ToArray();
        records[mapRow] = newRecord;

        // Check the fit *before* laying anything out: the records are packed from the page end backward, so
        // an oversized record would otherwise run past offset 0 mid-copy rather than reporting "doesn't fit".
        newOffset = -1;
        int directoryEnd = format.DataRowDirectoryOffset + rowCount * 2;
        if (format.PageSize - records.Sum(r => r.Length) < directoryEnd) return null;

        var result = new byte[format.PageSize];
        Array.Copy(page, result, format.DataRowDirectoryOffset); // preserve type/flags/owner/header
        int offset = format.PageSize;
        for (int i = 0; i < rowCount; i++)
        {
            offset -= records[i].Length;
            Array.Copy(records[i], 0, result, offset, records[i].Length);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(format.DataRowDirectoryOffset + i * 2, 2), (ushort)offset);
            if (i == mapRow) newOffset = offset;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(format.DataRowCountOffset, 2), (ushort)rowCount);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(format.DataFreeSpaceOffset, 2), (ushort)(offset - directoryEnd));
        return result;
    }
}
