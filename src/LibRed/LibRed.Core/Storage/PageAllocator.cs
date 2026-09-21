using LibRed.IO;
using LibRed.Pages;
using System.Buffers.Binary;
using System.Numerics;

namespace LibRed.Storage;

/// <summary>
/// Allocates database pages the way Access does: through the **global free-pages map**, the usage-map record
/// page 0 names at <c>0x18</c> (an inline or reference map where a set bit marks a free page). Allocation takes a
/// free page, clears its bit (so it is no longer free), and returns it — reusing freed pages rather than always
/// growing the file. The file is grown only when no free page is available.
/// </summary>
/// <remarks>
/// Pages set in the **global released-pages map** (named at <c>0x1C</c>) are never allocated, as ACE never
/// allocates them: they were released by a session that has not yet merged them back into the free map. Both
/// maps are located only through their page-0 pointers, row included — page 1 rows 0 and 1 in every file ACE
/// writes, but ACE follows the pointers wherever they lead (docs/format/page-05-usage-maps.md §9.1).
/// Freed pages take one of two routes, as ACE's do: <see cref="Free"/> makes a page reusable at once, and
/// <see cref="Release"/> holds it until <see cref="ReturnReleasedPages"/> runs when the handle closes.
/// </remarks>
public sealed class PageAllocator(PageChannel channel)
{
    private const byte InlineMapType = 0x00;
    private const byte ReferenceMapType = 0x01;
    /// <summary>Bytes preceding the bitmap on a dedicated usage-bitmap page (type 0x05).</summary>
    private const int BitmapPageHeaderSize = 4;

    /// <summary>A reference map is a fixed 69-byte record: the type byte + 17 bitmap-page pointers (17 being
    /// exactly enough to span Jet's 2 GB ceiling). See the usage-maps spec (§9).</summary>
    private const int ReferenceMapSlots = 17;

    private readonly PageChannel _channel = channel;

    /// <summary>One of the two global map records, as read through its page-0 pointer.</summary>
    private sealed record MapRecord(string Name, int PageNumber, int Row, byte[] Page, RowSlot Slot)
    {
        public ReadOnlySpan<byte> Record => Page.AsSpan(Slot.Offset, Slot.Length);
        public byte Type => Page[Slot.Offset];
    }

    public int Allocate()
    {
        (MapRecord free, MapRecord released) = ReadGlobalMaps();
        var releasedPages = new ReleasedPages(this, released);
        if (free.Type == ReferenceMapType)
            return AllocateFromReferenceMap(free, released, releasedPages);

        byte[] page = free.Page;
        int mapOffset = free.Slot.Offset;
        int startPage = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(mapOffset + 1, 4));
        int bitmapStart = mapOffset + 5;
        int bitmapEnd = mapOffset + free.Slot.Length;
        for (int i = bitmapStart; i < bitmapEnd; i++)
        {
            int bits = page[i];
            while (bits != 0)
            {
                int bit = BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                int allocated = startPage + (i - bitmapStart) * 8 + bit;
                if (releasedPages.Contains(allocated)) continue; // released, not yet reusable
                ValidateReusablePage(allocated, "inline free bit", free, released, AppendBoundary(releasedPages));
                EnsurePhysicalAllocation(allocated, releasedPages);
                page[i] &= (byte)~(1 << bit); // no longer free
                _channel.WritePage(free.PageNumber, page);
                return allocated;
            }
        }

        // An unrepresented page is not safely recorded as used. Grow the global map before appending.
        return GrowAndAllocate();
    }

    /// <summary>Returns a page to the global free-pages map now (sets its bit), so it can be reused — the
    /// inverse of <see cref="Allocate"/>. ACE frees this way only the pages of a long value an UPDATE replaces;
    /// every other freed page is held until the session closes, through <see cref="Release"/>.</summary>
    public void Free(int page)
    {
        (MapRecord free, MapRecord released) = ReadGlobalMaps();
        ValidateReusablePage(page, "page being freed", free, released, _channel.PageCount - 1);

        if (free.Type == ReferenceMapType)
        {
            FreeInReferenceMap(free, released, page);
            return;
        }

        byte[] p = free.Page;
        int mapOffset = free.Slot.Offset;
        int startPage = BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(mapOffset + 1, 4));
        int bit = page - startPage;
        int byteIndex = mapOffset + 5 + bit / 8;
        if (bit < 0 || byteIndex >= mapOffset + free.Slot.Length) return; // outside the inline window

        p[byteIndex] |= (byte)(1 << (bit % 8));
        _channel.WritePage(free.PageNumber, p);
    }

    /// <summary>
    /// Frees a page the way ACE frees the pages of a deleted row's long values, a dropped index and a dropped
    /// table: it is not reusable while this handle stays open, and goes back to the global free map only when
    /// <see cref="ReturnReleasedPages"/> runs at close. Inside a transaction it is released only if the
    /// transaction commits.
    /// </summary>
    public void Release(int page)
    {
        (MapRecord free, MapRecord released) = ReadGlobalMaps();
        ValidateReusablePage(page, "page being released", free, released, _channel.PageCount - 1);
        _channel.ReleaseAtClose(page);
    }

    /// <summary>
    /// What ACE does at close: every page this handle released, and every page already set in the global
    /// released-pages map, goes back to the global free-pages map, and the released map is cleared. First an
    /// inline released map is sized to cover the highest page released (<see cref="SizeReleasedMap"/>), which
    /// can convert it to reference form. Writes nothing when nothing was released and this handle changed
    /// nothing.
    /// </summary>
    public void ReturnReleasedPages()
    {
        SortedSet<int> pages = [.. _channel.PagesReleasedAtClose];
        if (pages.Count == 0 && !_channel.HasPublishedWrites) return;
        (_, MapRecord released) = ReadGlobalMaps();
        pages.UnionWith(ReleasedMapPages(released));
        if (pages.Count == 0) return;

        bool ownTransaction = !_channel.InTransaction;
        if (ownTransaction) _channel.BeginTransaction();
        try
        {
            // Sized before the merge: a conversion allocates its bitmap pages from the free map as it stands,
            // without the pages being released.
            SizeReleasedMap(pages);

            // A released page past the end of the file was never materialized; the free map already records
            // every page past the end as free.
            foreach (int page in pages)
                if (page < _channel.PageCount) Free(page);

            (_, released) = ReadGlobalMaps();
            ClearReleasedMap(released);
            _channel.ClearPagesReleasedAtClose();
            if (ownTransaction) _channel.CommitTransaction(flush: false);
        }
        catch
        {
            if (ownTransaction) _channel.RollbackTransaction();
            throw;
        }
    }

    /// <summary>The pages set in the global released-pages map, inline or reference form.</summary>
    private List<int> ReleasedMapPages(MapRecord released)
    {
        ReadOnlySpan<byte> record = released.Record;
        var pages = new List<int>();
        if (released.Type == InlineMapType)
        {
            int start = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(1, 4));
            for (int i = 5; i < record.Length; i++)
                for (int bits = record[i]; bits != 0; bits &= bits - 1)
                    pages.Add(start + (i - 5) * 8 + BitOperations.TrailingZeroCount(bits));
            return pages;
        }

        int pagesPerBitmap = (_channel.PageSize - BitmapPageHeaderSize) * 8;
        for (int slot = 0; slot < ReferenceMapSlots; slot++)
        {
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(1 + slot * 4, 4));
            if (bitmapPage == 0) continue;
            ReadOnlySpan<byte> bitmap = _channel.ReadPage(bitmapPage).Span;
            for (int i = BitmapPageHeaderSize; i < bitmap.Length; i++)
                for (int bits = bitmap[i]; bits != 0; bits &= bits - 1)
                    pages.Add(slot * pagesPerBitmap + (i - BitmapPageHeaderSize) * 8 + BitOperations.TrailingZeroCount(bits));
        }
        return pages;
    }

    /// <summary>
    /// Sizes the released-pages map for <paramref name="pages"/> the way ACE's close does, before they are merged.
    /// An inline record that already covers them is left alone. Otherwise it is lengthened, keeping its start
    /// page, to the shortest that covers the highest page — the 5-byte header, then the bitmap in whole 4-byte
    /// words — as long as its holder keeps 4 bytes free. When that is too long, the window moves instead: the
    /// start becomes the lowest page released rounded down to a byte, and the record is sized from there. When
    /// even that is too long, the record is grown at its old start to cover the highest released page it can
    /// reach, the released pages it covers are marked in it, and it is converted to reference form: a bitmap page is allocated for each range
    /// holding a released page, in range order, and a 69-byte reference record takes its place, the longer
    /// record's bytes staying on the page below it. A map already in reference form gains a bitmap page for each
    /// range holding a released page that it has none for.
    /// </summary>
    private void SizeReleasedMap(SortedSet<int> pages)
    {
        (_, MapRecord released) = ReadGlobalMaps();
        if (released.Type == ReferenceMapType)
        {
            byte[] existing = released.Record.ToArray();
            if (!AddReleasedBitmapPages(existing, pages)) return;
            (_, released) = ReadGlobalMaps();
            LayMapRecord(released, existing);
            return;
        }

        int start = BinaryPrimitives.ReadInt32LittleEndian(released.Record.Slice(1, 4));
        int lowest = pages.Min, highest = pages.Max;
        int Covering(int from) => 5 + ((highest - from) / 8 + 1 + 3) / 4 * 4;
        if (lowest >= start && highest < start + (released.Slot.Length - 5) * 8) return;

        var holder = new DataPage();
        holder.Read(new PageBuffer(released.Page, released.PageNumber), _channel.Format);
        int others = 0;
        for (int row = 0; row < holder.RowCount; row++)
            if (row != released.Row) others += holder.Rows[row].Length;
        int room = _channel.PageSize - (_channel.Format.DataRowDirectoryOffset + holder.RowCount * 2) - others - 4;
        int longest = Math.Max(released.Slot.Length, 5 + (room - 5) / 4 * 4);

        if (lowest >= start && Covering(start) <= longest)
        {
            var grown = new byte[Covering(start)];
            released.Record[..5].CopyTo(grown);
            LayMapRecord(released, grown);
            return;
        }

        int moved = lowest / 8 * 8;
        if (Covering(moved) <= longest)
        {
            var window = new byte[Math.Max(Covering(moved), released.Slot.Length)];
            BinaryPrimitives.WriteInt32LittleEndian(window.AsSpan(1, 4), moved);
            LayMapRecord(released, window);
            return;
        }

        // Grown only as far as the highest released page it can still cover — the whole of its longest length only
        // when released pages reach that far.
        int reach = start + (longest - 5) * 8 - 1;
        SortedSet<int> reachable = pages.GetViewBetween(Math.Min(start, reach), reach);
        int covered = reachable.Count == 0 ? released.Slot.Length
            : Math.Max(released.Slot.Length, 5 + ((reachable.Max - start) / 8 + 1 + 3) / 4 * 4);
        var record = new byte[covered];
        released.Record[..5].CopyTo(record);
        foreach (int page in pages.GetViewBetween(start, start + (record.Length - 5) * 8 - 1))
            record[5 + (page - start) / 8] |= (byte)(1 << ((page - start) % 8));
        LayMapRecord(released, record);

        var reference = new byte[1 + ReferenceMapSlots * 4];
        reference[0] = ReferenceMapType;
        AddReleasedBitmapPages(reference, pages);
        (_, released) = ReadGlobalMaps();
        LayMapRecord(released, reference);
    }

    /// <summary>Allocates an empty bitmap page, in range order, for every range of <paramref name="pages"/> that
    /// the reference record has no bitmap page for, and writes its pointer into the record. Returns whether any
    /// was added.</summary>
    private bool AddReleasedBitmapPages(byte[] reference, SortedSet<int> pages)
    {
        int pagesPerBitmap = (_channel.PageSize - BitmapPageHeaderSize) * 8;
        bool added = false;
        foreach (int slot in pages.Select(p => p / pagesPerBitmap).Distinct())
        {
            if (slot >= ReferenceMapSlots)
                throw new InvalidDataException($"Released page {pages.Max} lies past the global map's bitmap slots.");
            if (BinaryPrimitives.ReadInt32LittleEndian(reference.AsSpan(1 + slot * 4)) != 0) continue;
            int bitmapPage = Allocate();
            var bitmap = new byte[_channel.PageSize];
            bitmap[0] = (byte)PageType.PageUsageBitmap;
            bitmap[1] = 1;
            _channel.WritePage(bitmapPage, bitmap);
            BinaryPrimitives.WriteInt32LittleEndian(reference.AsSpan(1 + slot * 4), bitmapPage);
            added = true;
        }
        return added;
    }

    /// <summary>Replaces a global map record, repacking its holder's records from the page end, and lays the result
    /// over the page as it stands: bytes a moved record vacates are not cleared, as ACE leaves them.</summary>
    private void LayMapRecord(MapRecord map, byte[] record)
    {
        byte[] page = _channel.ReadPage(map.PageNumber).Span.ToArray();
        var holder = new DataPage();
        holder.Read(new PageBuffer(page, map.PageNumber), _channel.Format);
        byte[] repacked = UsageMapWriter.ReplaceMapRecord(page, holder, _channel.Format, map.Row, record, out _)
            ?? throw new InvalidDataException($"Global {map.Name} map cannot fit its holder page.");

        var laid = new DataPage();
        laid.Read(new PageBuffer(repacked, map.PageNumber), _channel.Format);
        repacked.AsSpan(0, _channel.Format.DataRowDirectoryOffset + laid.RowCount * 2).CopyTo(page);
        foreach (RowSlot slot in laid.Rows)
            repacked.AsSpan(slot.Offset, slot.Length).CopyTo(page.AsSpan(slot.Offset));
        _channel.WritePage(map.PageNumber, page);
    }

    /// <summary>Clears every bit of the global released-pages map: in place for an inline record, and on each
    /// bitmap page, header kept, for a reference record.</summary>
    private void ClearReleasedMap(MapRecord released)
    {
        if (released.Type == ReferenceMapType)
        {
            ReadOnlySpan<byte> map = released.Record;
            for (int slot = 0; slot < ReferenceMapSlots; slot++)
            {
                int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1 + slot * 4, 4));
                if (bitmapPage == 0) continue;
                byte[] bitmap = _channel.ReadPage(bitmapPage).Span.ToArray();
                bitmap.AsSpan(BitmapPageHeaderSize).Clear();
                _channel.WritePage(bitmapPage, bitmap);
            }
            return;
        }

        released.Page.AsSpan(released.Slot.Offset + 5, released.Slot.Length - 5).Clear();
        _channel.WritePage(released.PageNumber, released.Page);
    }

    /// <summary>
    /// Checks that page 0's two global map pointers name distinct, well-formed usage-map records inside the
    /// file. A pointer past the end of the file makes ACE mark the database corrupt, and one naming anything
    /// other than a usage map fails at the first allocation, so a writable open refuses both up front.
    /// </summary>
    public void ValidateGlobalMaps() => ReadGlobalMaps();

    /// <summary>Allocates from a reference-type global free map (huge databases): the record is a list of
    /// pointers to dedicated bitmap pages (type 0x05), pointer <c>k</c> covering the page range starting at
    /// <c>k × (pageSize − 4) × 8</c>. A **set bit is a free page** (the global map's sense, opposite of a
    /// per-table owned map). Finds the first free page, clears its bit on the bitmap page, and returns it;
    /// grows the file when no bitmap records a free page.</summary>
    private int AllocateFromReferenceMap(MapRecord free, MapRecord released, ReleasedPages releasedPages)
    {
        var format = _channel.Format;
        ReadOnlySpan<byte> map = free.Record;
        int pagesPerBitmap = (format.PageSize - BitmapPageHeaderSize) * 8;
        HashSet<int> bitmapPages = ReferenceBitmapPages(free, released);

        for (int slot = 0; slot < ReferenceMapSlots; slot++)
        {
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1 + slot * 4, 4));
            if (bitmapPage == 0) continue; // no bitmap page allocated for this range

            byte[] bitmap = ValidateBitmapPage(bitmapPage, free, released);
            for (int i = BitmapPageHeaderSize; i < format.PageSize; i++)
            {
                int bits = bitmap[i];
                while (bits != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    int allocated = slot * pagesPerBitmap + (i - BitmapPageHeaderSize) * 8 + bit;
                    if (releasedPages.Contains(allocated)) continue; // released, not yet reusable
                    ValidateReusablePage(allocated, $"reference-map slot {slot} free bit", free, released,
                        AppendBoundary(releasedPages));
                    if (bitmapPages.Contains(allocated))
                        throw new InvalidDataException($"Global free map marks bitmap page {allocated} itself as free.");
                    EnsurePhysicalAllocation(allocated, releasedPages);
                    bitmap[i] &= (byte)~(1 << bit); // no longer free
                    _channel.WritePage(bitmapPage, bitmap);
                    return allocated;
                }
            }
        }

        return GrowAndAllocate();
    }

    /// <summary>Extends allocation metadata before the physical file: four-byte inline growth, four spare
    /// bytes left on the holder page before promoting to reference form, and a reference bitmap allocated
    /// before the first data page in its range. All three measured against ACE and asserted by
    /// <c>GlobalMapGrowthTests</c>.</summary>
    private int GrowAndAllocate()
    {
        bool ownTransaction = !_channel.InTransaction;
        if (ownTransaction) _channel.BeginTransaction();
        try
        {
            int result = GrowAndAllocateCore();
            if (ownTransaction) _channel.CommitTransaction(flush: false);
            return result;
        }
        catch
        {
            if (ownTransaction) _channel.RollbackTransaction();
            throw;
        }
    }

    private int GrowAndAllocateCore()
    {
        (MapRecord free, MapRecord released) = ReadGlobalMaps();
        var releasedPages = new ReleasedPages(this, released);
        byte[] record = free.Record.ToArray();
        int frontier = _channel.PageCount;
        if (record[0] == InlineMapType)
        {
            int start = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(1));
            if (start != 0)
                throw new NotSupportedException("Cannot grow a global inline map with a nonzero start page.");
            int bitmapBytes = ((frontier / 8 + 1 + 3) / 4) * 4;
            if (bitmapBytes <= record.Length - 5)
                return AppendUnreleasedPage(releasedPages); // already represented as used

            var grown = new byte[5 + bitmapBytes];
            // Preserve existing free bits; newly covered physical pages are already used. Only future
            // pages start free. The requested frontier is cleared by the ordinary allocation path.
            record.CopyTo(grown, 0);
            for (int bit = frontier; bit < bitmapBytes * 8; bit++)
                grown[5 + bit / 8] |= (byte)(1 << (bit % 8));
            var holder = new DataPage();
            holder.Read(_channel.ReadPage(free.PageNumber), _channel.Format);
            byte[]? rewritten = UsageMapWriter.ReplaceMapRecord(free.Page, holder, _channel.Format, free.Row, grown, out _);
            if (rewritten is not null &&
                BinaryPrimitives.ReadUInt16LittleEndian(rewritten.AsSpan(_channel.Format.DataFreeSpaceOffset)) >= 4)
            {
                _channel.WritePage(free.PageNumber, rewritten);
                return Allocate();
            }

            // Inline exhausted: every existing page is used (Allocate already searched all free bits).
            // Reserve the bitmap pages first so their own bits are clear in the finished map.
            record = new byte[1 + ReferenceMapSlots * 4];
            record[0] = ReferenceMapType;
            int span = (_channel.PageSize - BitmapPageHeaderSize) * 8;
            for (int range = 0; range <= _channel.PageCount / span; range++)
            {
                if (range >= ReferenceMapSlots)
                    throw new NotSupportedException("Global allocation map has no remaining bitmap slots.");
                int bitmap = AppendUnreleasedPage(releasedPages);
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(1 + range * 4), bitmap);
            }
            for (int range = 0; range < ReferenceMapSlots; range++)
            {
                int bitmap = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(1 + range * 4));
                if (bitmap != 0) WriteNewGlobalBitmap(bitmap, range);
            }
            WriteGlobalRecord(free, record);
            return Allocate();
        }

        int pagesPerBitmap = (_channel.PageSize - BitmapPageHeaderSize) * 8;
        int rangeIndex = frontier / pagesPerBitmap;
        if (rangeIndex >= ReferenceMapSlots)
            throw new NotSupportedException("Global allocation map has no remaining bitmap slots.");
        if (BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(1 + rangeIndex * 4)) != 0)
            return AppendUnreleasedPage(releasedPages); // represented range, bit already clear

        int newBitmap = AppendUnreleasedPage(releasedPages);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(1 + rangeIndex * 4), newBitmap);
        WriteNewGlobalBitmap(newBitmap, rangeIndex);
        WriteGlobalRecord(free, record);
        return Allocate();
    }

    /// <summary>Appends the next page past the end of the file that is not released. Released pages at the
    /// frontier are materialized — the file stays contiguous — but not handed out.</summary>
    private int AppendUnreleasedPage(ReleasedPages releasedPages)
    {
        while (releasedPages.Contains(_channel.PageCount))
            _channel.AllocatePage();
        return _channel.AllocatePage();
    }

    private void WriteNewGlobalBitmap(int number, int range)
    {
        var bitmap = new byte[_channel.PageSize];
        bitmap[0] = (byte)PageType.PageUsageBitmap;
        bitmap[1] = 1;
        int span = (_channel.PageSize - BitmapPageHeaderSize) * 8;
        int firstFree = Math.Clamp(_channel.PageCount - range * span, 0, span);
        for (int bit = firstFree; bit < span; bit++)
            bitmap[BitmapPageHeaderSize + bit / 8] |= (byte)(1 << (bit % 8));
        _channel.WritePage(number, bitmap);
    }

    private void WriteGlobalRecord(MapRecord free, byte[] record)
    {
        byte[] page = _channel.ReadPage(free.PageNumber).Span.ToArray();
        var holder = new DataPage();
        holder.Read(_channel.ReadPage(free.PageNumber), _channel.Format);
        byte[] rewritten = UsageMapWriter.ReplaceMapRecord(page, holder, _channel.Format, free.Row, record, out _)
            ?? throw new InvalidDataException("Global allocation map cannot fit its holder page.");
        _channel.WritePage(free.PageNumber, rewritten);
    }

    /// <summary>Returns a page to a reference-type global free map by setting its bit on the bitmap page for
    /// its range. If that range has no bitmap page (e.g. a page grown past the map's coverage), the page is
    /// left unrecorded — it simply won't be reused, matching the pre-existing inline-window behaviour.</summary>
    private void FreeInReferenceMap(MapRecord free, MapRecord released, int page)
    {
        var format = _channel.Format;
        ReadOnlySpan<byte> map = free.Record;
        int pagesPerBitmap = (format.PageSize - BitmapPageHeaderSize) * 8;
        int slot = page / pagesPerBitmap;
        if (slot < 0 || slot >= ReferenceMapSlots) return; // beyond the map's ~2 GB reach

        int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1 + slot * 4, 4));
        if (bitmapPage == 0) return; // range has no bitmap page — nothing to record into

        int bitInRange = page - slot * pagesPerBitmap;
        byte[] bitmap = ValidateBitmapPage(bitmapPage, free, released);
        if (page == bitmapPage)
            throw new InvalidDataException($"Usage-map bitmap page {page} cannot be marked globally free.");
        bitmap[BitmapPageHeaderSize + bitInRange / 8] |= (byte)(1 << (bitInRange % 8));
        _channel.WritePage(bitmapPage, bitmap);
    }

    /// <summary>Reads both global map records through page 0's pointers, and refuses pointers that are equal,
    /// name a page outside the file, or name anything other than a usage-map record.</summary>
    private (MapRecord Free, MapRecord Released) ReadGlobalMaps()
    {
        ReadOnlySpan<byte> page0 = _channel.ReadPage(0).Span;
        (int Row, int Page) freePointer =
            DatabaseDefinitionPage.ReadMapPointer(page0, Formats.JetFormatBase.FreePagesMapPointerOffset);
        (int Row, int Page) releasedPointer =
            DatabaseDefinitionPage.ReadMapPointer(page0, Formats.JetFormatBase.ReleasedPagesMapPointerOffset);
        if (freePointer == releasedPointer)
            throw new InvalidDataException(
                $"Page 0 names the same record (page {freePointer.Page}, row {freePointer.Row}) for the global " +
                "free-pages and released-pages maps; they must be distinct.");

        MapRecord free = ReadMapRecord("free-pages", freePointer);
        MapRecord released = ReadMapRecord("released-pages", releasedPointer);
        ReferenceBitmapPages(free, released);
        return (free, released);
    }

    private MapRecord ReadMapRecord(string name, (int Row, int Page) pointer)
    {
        if (pointer.Page <= 0 || pointer.Page >= _channel.PageCount)
            throw new InvalidDataException(
                $"Page 0's global {name} map pointer names page {pointer.Page}, outside the file's pages 1..{_channel.PageCount - 1}.");
        PageBuffer buffer = _channel.ReadPage(pointer.Page);
        if (buffer.Span[0] != (byte)PageType.DataPage)
            throw new InvalidDataException(
                $"Page 0's global {name} map pointer names page {pointer.Page}, which is not a data page.");
        var data = new DataPage();
        data.Read(buffer, _channel.Format);
        if (pointer.Row >= data.RowCount)
            throw new InvalidDataException(
                $"Page 0's global {name} map pointer names row {pointer.Row} of page {pointer.Page}, which has {data.RowCount} rows.");
        RowSlot slot = data.Rows[pointer.Row];
        if (slot.IsDeleted || slot.HasOverflow || slot.Length == 0)
            throw new InvalidDataException(
                $"Global {name} map (page {pointer.Page}, row {pointer.Row}) is deleted, overflowed, or empty.");

        var record = new MapRecord(name, pointer.Page, pointer.Row, buffer.Span.ToArray(), slot);
        if (record.Type == InlineMapType)
        {
            if (slot.Length < 5)
                throw new InvalidDataException($"Global inline {name} map is shorter than its 5-byte header.");
        }
        else if (record.Type == ReferenceMapType)
        {
            if (slot.Length != 1 + ReferenceMapSlots * 4)
                throw new InvalidDataException(
                    $"Global reference {name} map must be exactly {1 + ReferenceMapSlots * 4} bytes; got {slot.Length}.");
        }
        else
        {
            throw new InvalidDataException($"Global {name} map has unknown type 0x{record.Type:X2}.");
        }
        return record;
    }

    /// <summary>The bitmap pages the two reference-form maps own, each validated; a page may belong to only
    /// one slot of one map.</summary>
    private HashSet<int> ReferenceBitmapPages(MapRecord free, MapRecord released)
    {
        var pages = new HashSet<int>();
        foreach (MapRecord map in new[] { free, released })
        {
            if (map.Type != ReferenceMapType) continue;
            ReadOnlySpan<byte> record = map.Record;
            for (int slot = 0; slot < ReferenceMapSlots; slot++)
            {
                int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(1 + slot * 4, 4));
                if (bitmapPage == 0) continue;
                ValidateBitmapPage(bitmapPage, free, released);
                if (!pages.Add(bitmapPage))
                    throw new InvalidDataException($"Global reference maps repeat bitmap page {bitmapPage}.");
            }
        }
        return pages;
    }

    private byte[] ValidateBitmapPage(int pageNumber, MapRecord free, MapRecord released)
    {
        ValidateReusablePage(pageNumber, "usage-map bitmap pointer", free, released, _channel.PageCount - 1);
        byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
        if (page[0] != (byte)PageType.PageUsageBitmap || page[1] != 0x01 || page[2] != 0 || page[3] != 0)
            throw new InvalidDataException($"Global map pointer {pageNumber} does not target a valid bitmap page.");
        return page;
    }

    /// <summary>Page 0 and the pages holding the two global maps are never allocatable; nor is a page past
    /// <paramref name="maximum"/>.</summary>
    private static void ValidateReusablePage(int page, string source, MapRecord free, MapRecord released, int maximum)
    {
        if (page <= 0 || page == free.PageNumber || page == released.PageNumber || page > maximum)
            throw new InvalidDataException(
                $"Global {source} names page {page}, which is page 0, a global-map holder page " +
                $"({free.PageNumber}/{released.PageNumber}), or past the contiguous range ending at {maximum}.");
    }

    /// <summary>The highest page an allocation may name: the next page past the end of the file, pushed
    /// further by any released pages sitting at the frontier (they are materialized, not handed out).</summary>
    private int AppendBoundary(ReleasedPages releasedPages)
    {
        int boundary = _channel.PageCount;
        while (releasedPages.Contains(boundary)) boundary++;
        return boundary;
    }

    private void EnsurePhysicalAllocation(int page, ReleasedPages releasedPages)
    {
        while (_channel.PageCount < page)
        {
            if (!releasedPages.Contains(_channel.PageCount))
                throw new InvalidDataException(
                    $"Global free map selected page {page} past a gap at page {_channel.PageCount} that is neither free nor released.");
            _channel.AllocatePage();
        }
        if (page < _channel.PageCount) return;
        int allocated = _channel.AllocatePage();
        if (allocated != page)
            throw new InvalidDataException(
                $"Global free map selected append page {page}, but contiguous allocation produced page {allocated}.");
    }

    /// <summary>Membership in the global released-pages map, inline or reference form.</summary>
    private sealed class ReleasedPages
    {
        private readonly PageAllocator _owner;
        private readonly MapRecord _map;
        private readonly Dictionary<int, byte[]?> _bitmaps = [];

        public ReleasedPages(PageAllocator owner, MapRecord map)
        {
            _owner = owner;
            _map = map;
        }

        public bool Contains(int page)
        {
            if (page < 0) return false;
            ReadOnlySpan<byte> record = _map.Record;
            if (_map.Type == InlineMapType)
            {
                int start = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(1, 4));
                long bit = (long)page - start;
                if (bit < 0 || bit / 8 >= record.Length - 5) return false;
                return (record[5 + (int)(bit / 8)] & (1 << (int)(bit % 8))) != 0;
            }

            int pagesPerBitmap = (_owner._channel.PageSize - BitmapPageHeaderSize) * 8;
            int slot = page / pagesPerBitmap;
            if (slot >= ReferenceMapSlots) return false;
            if (!_bitmaps.TryGetValue(slot, out byte[]? bitmap))
            {
                int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(1 + slot * 4, 4));
                bitmap = bitmapPage == 0 ? null : _owner._channel.ReadPage(bitmapPage).Span.ToArray();
                _bitmaps[slot] = bitmap;
            }
            if (bitmap is null) return false;
            int inRange = page - slot * pagesPerBitmap;
            return (bitmap[BitmapPageHeaderSize + inRange / 8] & (1 << (inRange % 8))) != 0;
        }
    }
}