using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

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
/// maps are located only through their page-0 pointers, row included — page 1 rows 0 and 1 where ACE creates
/// them, but ACE follows the pointers wherever they lead (docs/format/page-05-usage-maps.md §9.1).
/// Freed pages take one of two routes, as ACE's do: <see cref="Free"/> makes a page reusable at once, and
/// <see cref="Release"/> holds it until <see cref="ReturnReleasedPages"/> runs when the handle closes.
/// </remarks>
internal sealed class PageAllocator
{
    private readonly PageChannel _channel;

    internal PageAllocator(PageChannel channel) => _channel = channel;

    /// <summary>
    /// Allocates a fresh page by growing the file by one page, returning its number. Jet also
    /// recycles freed pages via usage maps; appending at the end is always valid since the page
    /// count is simply the file length divided by the page size.
    /// </summary>
    internal int Append()
    {
        if (_channel.IsReadOnly)
            throw new InvalidOperationException("This channel was opened read-only.");

        int pageNumber = _channel.PageCount;
        _channel.WritePage(pageNumber, new byte[_channel.PageSize]);
        return pageNumber;
    }

    /// <summary>One of the two global map records, as read through its page-0 pointer.</summary>
    private sealed record MapRecord(string Name, int PageNumber, int Row, byte[] Page, DataPage.RowSlot Slot)
    {
        public ReadOnlySpan<byte> Record => Page.AsSpan(Slot.Offset, Slot.Length);
        public UsageMapType Type => UsageMap.RecordType(Record);
    }

    public int Allocate()
    {
        (MapRecord free, MapRecord released) = ReadGlobalMaps();
        var releasedPages = new ReleasedPages(this, released);
        if (free.Type == UsageMapType.Reference)
            return AllocateFromReferenceMap(free, released, releasedPages);

        JetFormatBase format = _channel.Format;
        int startPage = UsageMap.StartPage(free.Record, format);
        Span<byte> bitmap = UsageMap.InlineBits(free.Page.AsSpan(free.Slot.Offset, free.Slot.Length), format);
        for (int bit = BitmapBits.NextSetBit(bitmap, 0); bit >= 0; bit = BitmapBits.NextSetBit(bitmap, bit + 1))
        {
            int allocated = startPage + bit;
            if (releasedPages.Contains(allocated)) continue; // released, not yet reusable
            ValidateReusablePage(allocated, "inline free bit", free, released, AppendBoundary(releasedPages));
            EnsurePhysicalAllocation(allocated, releasedPages);
            BitmapBits.Set(bitmap, bit, false); // no longer free
            _channel.WritePage(free.PageNumber, free.Page);
            return allocated;
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

        if (free.Type == UsageMapType.Reference)
        {
            FreeInReferenceMap(free, released, page);
            return;
        }

        JetFormatBase format = _channel.Format;
        int startPage = UsageMap.StartPage(free.Record, format);
        Span<byte> bitmap = UsageMap.InlineBits(free.Page.AsSpan(free.Slot.Offset, free.Slot.Length), format);
        int bit = page - startPage;
        // A page the map has no bit for cannot be recorded as free, and dropping it here is how a page is lost
        // for good — nothing else remembers it. It does not arise in a well-formed file: allocation extends the
        // map to the file's frontier, and ACE's own map covers its whole file (measured: a 1,761-page file
        // carries a 229-byte record from page 0, and records pages freed above the original 512-page window).
        // So this is a malformed or foreign map, and it says so rather than quietly leaking the page.
        if (bit < 0 || bit >= bitmap.Length * 8)
            throw new InvalidDataException(
                $"Cannot record page {page} as free: the global free-pages map covers pages {startPage} through "
                + $"{startPage + bitmap.Length * 8 - 1}, so the page has no bit in it.");

        BitmapBits.Set(bitmap, bit, true);
        _channel.WritePage(free.PageNumber, free.Page);
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
        // Every bit kept, none bounded by the file: a released page past its end was never materialized, and is
        // skipped below.
        pages.UnionWith(UsageMap.PagesInRecord(_channel, released.Record, rejectBeyond: null, "The global released-pages map"));
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

    /// <summary>
    /// Sizes the released-pages map for <paramref name="pages"/> the way ACE's close does, before they are merged.
    /// An inline record that already covers them is left alone. Otherwise it is lengthened, keeping its start
    /// page, to the shortest that covers the highest page — the header, then the bitmap in whole growth steps —
    /// as long as its holder keeps <see cref="JetFormatBase.UsageMapHolderReserve"/> bytes free. When that is too
    /// long, the window moves instead: the start becomes the lowest page released rounded down to a byte, and the
    /// record is sized from there. When even that is too long, the record is grown at its old start to cover the
    /// highest released page it can reach, the released pages it covers are marked in it, and it is converted to
    /// reference form: a bitmap page is allocated for each range holding a released page, in range order, and a
    /// reference record takes its place, the longer record's bytes staying on the page below it. A map already in
    /// reference form gains a bitmap page for each range holding a released page that it has none for.
    /// </summary>
    private void SizeReleasedMap(SortedSet<int> pages)
    {
        JetFormatBase format = _channel.Format;
        (_, MapRecord released) = ReadGlobalMaps();
        if (released.Type == UsageMapType.Reference)
        {
            byte[] existing = released.Record.ToArray();
            if (!AddReleasedBitmapPages(existing, pages)) return;
            (_, released) = ReadGlobalMaps();
            LayMapRecord(released, existing);
            return;
        }

        int headerSize = format.UsageMapInlineHeaderSize;
        int start = UsageMap.StartPage(released.Record, format);
        int lowest = pages.Min, highest = pages.Max;
        int Covering(int from) => headerSize + UsageMap.InlineBitmapBytes(format, highest - from + 1);
        if (lowest >= start && highest < start + (released.Slot.Length - headerSize) * 8) return;

        var holder = new DataPage();
        holder.Read(new PageBuffer(released.Page, released.PageNumber), format);
        int others = 0;
        for (int row = 0; row < holder.RowCount; row++)
            if (row != released.Row) others += holder.Rows[row].Length;
        int room = format.PageSize - DataPage.DirectoryEnd(format, holder.RowCount) - others - format.UsageMapHolderReserve;
        int growth = format.UsageMapInlineGrowthSize;
        int longest = Math.Max(released.Slot.Length, headerSize + (room - headerSize) / growth * growth);

        if (lowest >= start && Covering(start) <= longest)
        {
            LayMapRecord(released, UsageMap.NewInlineRecord(format, start, Covering(start) - headerSize));
            return;
        }

        int moved = lowest / 8 * 8;
        if (Covering(moved) <= longest)
        {
            LayMapRecord(released,
                UsageMap.NewInlineRecord(format, moved, Math.Max(Covering(moved), released.Slot.Length) - headerSize));
            return;
        }

        // Grown only as far as the highest released page it can still cover — the whole of its longest length only
        // when released pages reach that far.
        int reach = start + (longest - headerSize) * 8 - 1;
        SortedSet<int> reachable = pages.GetViewBetween(Math.Min(start, reach), reach);
        int covered = reachable.Count == 0 ? released.Slot.Length
            : Math.Max(released.Slot.Length, headerSize + UsageMap.InlineBitmapBytes(format, reachable.Max - start + 1));
        byte[] record = UsageMap.NewInlineRecord(format, start, covered - headerSize);
        foreach (int page in pages.GetViewBetween(start, start + (covered - headerSize) * 8 - 1))
            BitmapBits.Set(UsageMap.InlineBits(record, format), page - start, true);
        LayMapRecord(released, record);

        byte[] reference = UsageMap.NewReferenceRecord(format);
        AddReleasedBitmapPages(reference, pages);
        (_, released) = ReadGlobalMaps();
        LayMapRecord(released, reference);
    }

    /// <summary>Allocates an empty bitmap page, in range order, for every range of <paramref name="pages"/> that
    /// the reference record has no bitmap page for, and writes its pointer into the record. Returns whether any
    /// was added.</summary>
    private bool AddReleasedBitmapPages(byte[] reference, SortedSet<int> pages)
    {
        JetFormatBase format = _channel.Format;
        int pagesPerBitmap = format.UsageMapPagesPerBitmapPage;
        bool added = false;
        foreach (int slot in pages.Select(p => p / pagesPerBitmap).Distinct())
        {
            if (slot >= format.UsageMapReferenceSlots)
                throw new InvalidDataException($"Released page {pages.Max} lies past the global map's bitmap slots.");
            if (UsageMap.ReferencePointer(reference, slot, format) != 0) continue;
            int bitmapPage = Allocate();
            _channel.WritePage(bitmapPage, UsageMap.NewBitmapPage(format));
            UsageMap.WriteReferencePointer(reference, slot, format, bitmapPage);
            added = true;
        }
        return added;
    }

    /// <summary>Replaces a global map record, repacking its holder's records from the page end.</summary>
    private void LayMapRecord(MapRecord map, byte[] record)
    {
        JetFormatBase format = _channel.Format;
        byte[] page = _channel.ReadPage(map.PageNumber).Span.ToArray();
        var holder = new DataPage();
        holder.Read(new PageBuffer(page, map.PageNumber), format);
        byte[] repacked = UsageMap.ReplaceMapRecord(page, holder, format, map.Row, record, out _)
            ?? throw new InvalidDataException($"Global {map.Name} map cannot fit its holder page.");
        _channel.WritePage(map.PageNumber, repacked);
    }

    /// <summary>Clears every bit of the global released-pages map: in place for an inline record, and on each
    /// bitmap page, header kept, for a reference record.</summary>
    private void ClearReleasedMap(MapRecord released)
    {
        if (released.Type == UsageMapType.Reference)
        {
            new UsageMap(_channel).ClearBitmapPages(released.Record);
            return;
        }

        UsageMap.InlineBits(released.Page.AsSpan(released.Slot.Offset, released.Slot.Length), _channel.Format).Clear();
        _channel.WritePage(released.PageNumber, released.Page);
    }

    /// <summary>
    /// Checks that page 0's two global map pointers name distinct, well-formed usage-map records inside the
    /// file. A pointer past the end of the file makes ACE mark the database corrupt, and one naming anything
    /// other than a usage map fails at the first allocation, so a writable open refuses both up front.
    /// </summary>
    public void ValidateGlobalMaps() => ReadGlobalMaps();

    /// <summary>Allocates from a reference-type global free map (huge databases): the record is a list of
    /// pointers to dedicated bitmap pages (type 0x0105), pointer <c>k</c> covering the page range starting at
    /// <c>k × UsageMapPagesPerBitmapPage</c>. A **set bit is a free page** (the global map's sense, opposite of a
    /// per-table owned map). Finds the first free page, clears its bit on the bitmap page, and returns it;
    /// grows the file when no bitmap records a free page.</summary>
    private int AllocateFromReferenceMap(MapRecord free, MapRecord released, ReleasedPages releasedPages)
    {
        JetFormatBase format = _channel.Format;
        ReadOnlySpan<byte> map = free.Record;
        int pagesPerBitmap = format.UsageMapPagesPerBitmapPage;
        HashSet<int> bitmapPages = ReferenceBitmapPages(free, released);

        for (int slot = 0; slot < format.UsageMapReferenceSlots; slot++)
        {
            int bitmapPage = UsageMap.ReferencePointer(map, slot, format);
            if (bitmapPage == 0) continue; // no bitmap page allocated for this range

            byte[] page = ValidateBitmapPage(bitmapPage, free, released);
            Span<byte> bitmap = UsageMap.BitmapPageBits(page, format);
            for (int bit = BitmapBits.NextSetBit(bitmap, 0); bit >= 0; bit = BitmapBits.NextSetBit(bitmap, bit + 1))
            {
                int allocated = slot * pagesPerBitmap + bit;
                if (releasedPages.Contains(allocated)) continue; // released, not yet reusable
                ValidateReusablePage(allocated, $"reference-map slot {slot} free bit", free, released,
                    AppendBoundary(releasedPages));
                if (bitmapPages.Contains(allocated))
                    throw new InvalidDataException($"Global free map marks bitmap page {allocated} itself as free.");
                EnsurePhysicalAllocation(allocated, releasedPages);
                BitmapBits.Set(bitmap, bit, false); // no longer free
                _channel.WritePage(bitmapPage, page);
                return allocated;
            }
        }

        return GrowAndAllocate();
    }

    /// <summary>Extends allocation metadata before the physical file: inline growth in
    /// <see cref="JetFormatBase.UsageMapInlineGrowthSize"/> steps, <see cref="JetFormatBase.UsageMapHolderReserve"/>
    /// spare bytes left on the holder page before promoting to reference form, and a reference bitmap allocated
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
        JetFormatBase format = _channel.Format;
        (MapRecord free, MapRecord released) = ReadGlobalMaps();
        var releasedPages = new ReleasedPages(this, released);
        byte[] record = free.Record.ToArray();
        int frontier = _channel.PageCount;
        if (free.Type == UsageMapType.Inline)
        {
            int headerSize = format.UsageMapInlineHeaderSize;
            int start = UsageMap.StartPage(record, format);
            if (start != 0)
                throw new NotSupportedException("Cannot grow a global inline map with a nonzero start page.");
            int bitmapBytes = UsageMap.InlineBitmapBytes(format, frontier + 1);
            if (bitmapBytes <= record.Length - headerSize)
                return AppendUnreleasedPage(releasedPages); // already represented as used

            var grown = new byte[headerSize + bitmapBytes];
            // Preserve existing free bits; newly covered physical pages are already used. Only future
            // pages start free. The requested frontier is cleared by the ordinary allocation path.
            record.CopyTo(grown, 0);
            for (int bit = frontier; bit < bitmapBytes * 8; bit++)
                BitmapBits.Set(UsageMap.InlineBits(grown, format), bit, true);
            var holder = new DataPage();
            holder.Read(_channel.ReadPage(free.PageNumber), format);
            byte[]? rewritten = UsageMap.ReplaceMapRecord(free.Page, holder, format, free.Row, grown, out _);
            if (rewritten is not null &&
                DataPage.ReadFreeSpace(rewritten, format) >= format.UsageMapHolderReserve)
            {
                _channel.WritePage(free.PageNumber, rewritten);
                return Allocate();
            }

            // Inline exhausted: every existing page is used (Allocate already searched all free bits).
            // Reserve the bitmap pages first so their own bits are clear in the finished map.
            record = UsageMap.NewReferenceRecord(format);
            int span = format.UsageMapPagesPerBitmapPage;
            for (int range = 0; range <= _channel.PageCount / span; range++)
            {
                if (range >= format.UsageMapReferenceSlots)
                    throw new NotSupportedException("Global allocation map has no remaining bitmap slots.");
                UsageMap.WriteReferencePointer(record, range, format, AppendUnreleasedPage(releasedPages));
            }
            for (int range = 0; range < format.UsageMapReferenceSlots; range++)
            {
                int bitmap = UsageMap.ReferencePointer(record, range, format);
                if (bitmap != 0) WriteNewGlobalBitmap(bitmap, range);
            }
            LayMapRecord(free, record);
            return Allocate();
        }

        int rangeIndex = frontier / format.UsageMapPagesPerBitmapPage;
        if (rangeIndex >= format.UsageMapReferenceSlots)
            throw new NotSupportedException("Global allocation map has no remaining bitmap slots.");
        if (UsageMap.ReferencePointer(record, rangeIndex, format) != 0)
            return AppendUnreleasedPage(releasedPages); // represented range, bit already clear

        int newBitmap = AppendUnreleasedPage(releasedPages);
        UsageMap.WriteReferencePointer(record, rangeIndex, format, newBitmap);
        WriteNewGlobalBitmap(newBitmap, rangeIndex);
        LayMapRecord(free, record);
        return Allocate();
    }

    /// <summary>Appends the next page past the end of the file that is not released. Released pages at the
    /// frontier are materialized — the file stays contiguous — but not handed out.</summary>
    private int AppendUnreleasedPage(ReleasedPages releasedPages)
    {
        while (releasedPages.Contains(_channel.PageCount))
            Append();
        return Append();
    }

    private void WriteNewGlobalBitmap(int number, int range)
    {
        JetFormatBase format = _channel.Format;
        byte[] bitmap = UsageMap.NewBitmapPage(format);
        int span = format.UsageMapPagesPerBitmapPage;
        int firstFree = Math.Clamp(_channel.PageCount - range * span, 0, span);
        for (int bit = firstFree; bit < span; bit++)
            BitmapBits.Set(UsageMap.BitmapPageBits(bitmap, format), bit, true);
        _channel.WritePage(number, bitmap);
    }

    /// <summary>Returns a page to a reference-type global free map by setting its bit on the bitmap page for
    /// its range. If that range has no bitmap page (e.g. a page grown past the map's coverage), the page is
    /// left unrecorded — it simply won't be reused, matching the pre-existing inline-window behaviour.</summary>
    private void FreeInReferenceMap(MapRecord free, MapRecord released, int page)
    {
        JetFormatBase format = _channel.Format;
        ReadOnlySpan<byte> map = free.Record;
        int pagesPerBitmap = format.UsageMapPagesPerBitmapPage;
        int slot = page / pagesPerBitmap;
        if (slot < 0 || slot >= format.UsageMapReferenceSlots) return; // beyond the map's ~2 GB reach

        int bitmapPage = UsageMap.ReferencePointer(map, slot, format);
        if (bitmapPage == 0) return; // range has no bitmap page — nothing to record into

        int bitInRange = page - slot * pagesPerBitmap;
        byte[] bitmap = ValidateBitmapPage(bitmapPage, free, released);
        if (page == bitmapPage)
            throw new InvalidDataException($"Usage-map bitmap page {page} cannot be marked globally free.");
        BitmapBits.Set(UsageMap.BitmapPageBits(bitmap, format), bitInRange, true);
        _channel.WritePage(bitmapPage, bitmap);
    }

    /// <summary>Reads both global map records through page 0's pointers, and refuses pointers that are equal,
    /// name a page outside the file, or name anything other than a usage-map record.</summary>
    private (MapRecord Free, MapRecord Released) ReadGlobalMaps()
    {
        ReadOnlySpan<byte> page0 = _channel.ReadPage(0).Span;
        JetFormatBase format = _channel.Format;
        (int Row, int Page) freePointer =
            DatabaseDefinitionPage.ReadMapPointer(page0, format.FreePagesMapPointerOffset, format);
        (int Row, int Page) releasedPointer =
            DatabaseDefinitionPage.ReadMapPointer(page0, format.ReleasedPagesMapPointerOffset, format);
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
        // The global maps' holder belongs to no table either, but its owner field reads 1 rather than 0 (observed),
        // so it takes only the common checks. The page is copied: Allocate and Free write the record in place.
        (PageBuffer page, _, DataPage.RowSlot slot) = UsageMap.ReadRecord(
            _channel, pointer.Row, pointer.Page, $"Page 0's global {name} map pointer");
        return new MapRecord(name, pointer.Page, pointer.Row, page.Span.ToArray(), slot);
    }

    /// <summary>The bitmap pages the two reference-form maps own, each validated; a page may belong to only
    /// one slot of one map.</summary>
    private HashSet<int> ReferenceBitmapPages(MapRecord free, MapRecord released)
    {
        JetFormatBase format = _channel.Format;
        var pages = new HashSet<int>();
        foreach (MapRecord map in new[] { free, released })
        {
            if (map.Type != UsageMapType.Reference) continue;
            ReadOnlySpan<byte> record = map.Record;
            for (int slot = 0; slot < format.UsageMapReferenceSlots; slot++)
            {
                int bitmapPage = UsageMap.ReferencePointer(record, slot, format);
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
        return UsageMap.ReadBitmapPage(_channel, pageNumber).Span.ToArray();
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
            Append();
        }
        if (page < _channel.PageCount) return;
        int allocated = Append();
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
            JetFormatBase format = _owner._channel.Format;
            ReadOnlySpan<byte> record = _map.Record;
            if (_map.Type == UsageMapType.Inline)
            {
                ReadOnlySpan<byte> bits = record[format.UsageMapInlineHeaderSize..];
                long bit = (long)page - UsageMap.StartPage(record, format);
                return bit >= 0 && bit < bits.Length * 8L && BitmapBits.Get(bits, (int)bit);
            }

            int pagesPerBitmap = format.UsageMapPagesPerBitmapPage;
            int slot = page / pagesPerBitmap;
            if (slot >= format.UsageMapReferenceSlots) return false;
            if (!_bitmaps.TryGetValue(slot, out byte[]? bitmap))
            {
                int bitmapPage = UsageMap.ReferencePointer(record, slot, format);
                bitmap = bitmapPage == 0 ? null : UsageMap.ReadBitmapPage(_owner._channel, bitmapPage).Span.ToArray();
                _bitmaps[slot] = bitmap;
            }
            return bitmap is not null
                && BitmapBits.Get(UsageMap.BitmapPageBits(bitmap, format), page - slot * pagesPerBitmap);
        }
    }
}