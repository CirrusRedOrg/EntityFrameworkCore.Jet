using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using System.Numerics;

namespace LibRed.Storage;

/// <summary>
/// Allocates database pages the way Access does: through the **global free-pages map**, the usage-map record
/// page 0 names at <c>0x18</c> (an inline or reference map where a set bit marks a free page). Allocation takes a
/// free page, clears its bit (so it is no longer free), and returns it — reusing freed pages rather than always
/// growing the file. Shared reservations and owner-private groups select eligible pages; the file grows
/// when that selection cannot find a reusable page or group.
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
    private int _reservationStart;
    private uint _reservationBits;
    private int _bitmapSlot;
    private Dictionary<(int Row, int Page), int> _owners = [];
    private Dictionary<(int Row, int Page), (int Start, byte Bits)> _groups = [];
    private Dictionary<(int Row, int Page), int> _otherGroups = [];
    private Dictionary<(int Row, int Page), (int Page, int Held)> _dataGroups = [];
    private List<int> _heldUntilCommit = [];

    /// <summary>Session allocation state captured with a transaction or savepoint's page images.</summary>
    internal sealed record AllocationState(int Start, uint Bits, int BitmapSlot, Dictionary<(int Row, int Page), int> Owners,
        Dictionary<(int Row, int Page), (int Start, byte Bits)> Groups, Dictionary<(int Row, int Page), int> OtherGroups,
        Dictionary<(int Row, int Page), (int Page, int Held)> DataGroups, List<int> HeldUntilCommit);

    internal AllocationState State =>
        new(_reservationStart, _reservationBits, _bitmapSlot, new(_owners), new(_groups), new(_otherGroups), new(_dataGroups),
            [.. _heldUntilCommit]);

    /// <summary>
    /// Rolls the session's allocation back to <paramref name="state"/>, as ACE rolls back a transaction: all of it
    /// but the shared reservation, which a rollback does not give back. The pages the undone writes took from the
    /// reservation go to the global free map rather than back into it, and what it still holds stays held, its bits
    /// clear (measured). Called once the undone writes are discarded, so the global map is again the one
    /// <paramref name="state"/> was taken with.
    /// </summary>
    internal void RollBackTo(AllocationState state)
    {
        int start = _reservationStart;
        uint bits = _reservationBits, before = state.Start == start ? state.Bits : 0;
        (MapRecord free, MapRecord released) = ReadGlobalMaps();
        // The reservation stays, unless it lies in pages the restored map has no bit for: then it came with map growth
        // the rollback undid, and goes with it.
        if (Enumerable.Range(0, 32).Any(bit => (bits & 1u << bit) != 0 && FreeBit(start + bit, free) is null))
            (start, bits, before) = (state.Start, state.Bits, state.Bits);
        _reservationStart = start;
        _reservationBits = bits;
        _bitmapSlot = state.BitmapSlot;
        _owners = new(state.Owners);
        _groups = new(state.Groups);
        _otherGroups = new(state.OtherGroups);
        _dataGroups = new(state.DataGroups);
        _heldUntilCommit = [.. state.HeldUntilCommit];

        // What the undone writes took from the reservation they started with goes to the free map; what the
        // reservation holds that the restored map shows free is reserved again.
        uint taken = state.Start == start ? state.Bits & ~bits : state.Bits;
        for (int bit = 0; bit < 32; bit++)
            if ((taken & 1u << bit) != 0) Free(state.Start + bit);
        (free, released) = ReadGlobalMaps();
        for (int bit = 0; bit < 32; bit++)
        {
            if ((bits & ~before & 1u << bit) == 0) continue;
            // Unless another handle has committed the page meanwhile: the reservation lets it go.
            if (FreeBit(start + bit, free) == true) WriteFreeBit(free, released, start + bit, false);
            else _reservationBits &= ~(1u << bit);
        }
    }

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

    // rangeStart: for a usage map's new bitmap page, the first page of the range it covers. With the shared reservation
    // empty, such a page takes the lowest page not in use from there on — free, or held by an owner's group; a free
    // one opens the reservation round it as any other does (measured).
    public int Allocate((int Row, int Page)? owner = null, int previousPage = -1, (int Row, int Page)? data = null,
        bool leaf = false, bool materialize = true, int? rangeStart = null)
    {
        (MapRecord free, MapRecord released) = ReadGlobalMaps();
        var releasedPages = new ReleasedPages(this, released);
        (int Page, int Held) dataGroup = data is { } dataOwner
            ? (_owners.TryGetValue(dataOwner, out int dataPage) ? dataPage & ~7 : -1,
                _groups.TryGetValue(dataOwner, out var dataHeld) ? dataHeld.Start : -1)
            : (-1, -1);
        bool dataMoved = false, dataTookGroup = false;
        if (owner is { } indexOwner && data is not null)
        {
            if (_dataGroups.TryGetValue(indexOwner, out var seen))
                (dataMoved, dataTookGroup) = (seen.Page != dataGroup.Page, seen.Held != dataGroup.Held);
            _dataGroups[indexOwner] = dataGroup;
        }

        // An index works two groups, as ACE does (page-05-usage-maps.md). When the table's data has moved to another
        // group since the index's previous page, a leaf takes the lowest page of both not in use; otherwise it takes
        // the other group's lowest free page, or a fresh group's, and a node page takes the current group's, then the
        // other's. The shared reservation comes first, except that when the data took a new group of its own, a leaf
        // counts reserved pages too and leaves for the other group ahead of it. The group left returns its held pages,
        // to the shared reservation where it covers them, and becomes the other one.
        if (data is not null && owner is { } index && _groups.TryGetValue(index, out var current)
            && (leaf && dataTookGroup || _reservationBits == 0))
        {
            int other = _otherGroups.GetValueOrDefault(index, -1);
            int lowestOther = -1;
            for (int page = other; other >= 0 && page < other + 8 && lowestOther < 0; page++)
                if (IsFree(page, free, releasedPages) || leaf && dataTookGroup && Reserved(page)) lowestOther = page;
            int lowestCurrent = LowestHeld(current);
            bool keep = leaf
                ? dataMoved && lowestCurrent >= 0 && (lowestOther < 0 || lowestCurrent < lowestOther)
                : lowestCurrent >= 0 || lowestOther < 0;
            if (!keep && (lowestOther >= 0 || _reservationBits == 0))
            {
                ReturnGroup(current, toReservation: true);
                _groups.Remove(index);
                _otherGroups[index] = current.Start;
                (free, released) = ReadGlobalMaps();
                releasedPages = new ReleasedPages(this, released);
                if (Reserved(lowestOther))
                {
                    _groups[index] = (lowestOther & ~7, 0);
                    return TakeReserved(lowestOther, free, released, releasedPages, owner);
                }
                if (lowestOther >= 0) return TakePage(lowestOther, free, released, releasedPages, owner, reserve: false);
                _owners[index] = -1; // a fresh group: nothing to continue in, here or after growing the map
            }
        }

        if (_reservationBits != 0)
            return TakeReserved(_reservationStart + BitOperations.TrailingZeroCount(_reservationBits), free, released,
                releasedPages, owner, materialize);
        // For a bitmap page, a page held by an owner's group counts as not in use: the lowest of those and the free
        // pages is taken (measured).
        (int Row, int Page)? holder = null;
        int below = int.MaxValue;
        if (rangeStart is { } from)
            foreach (((int Row, int Page) key, (int Start, byte Bits) held) in _groups)
                if (LowestHeld(held, from) is var page and >= 0 && page < below) (holder, below) = (key, page);

        bool groups = false;
        if (owner is { } pointer)
        {
            if (_groups.TryGetValue(pointer, out var group) && group.Bits != 0) return TakeHeld(pointer, LowestHeld(group), owner);
            var maps = new UsageMap(_channel);
            ReadOnlySpan<byte> record = maps.ReadRecordAt(pointer.Row, pointer.Page);
            if (UsageMap.RecordType(record) == UsageMapType.Reference) groups = true;
            else
            {
                int count = 0;
                foreach (byte bits in record[_channel.Format.UsageMapInlineHeaderSize..])
                    count += BitOperations.PopCount((uint)bits);
                groups = count >= 8;
            }
            if (_owners.TryGetValue(pointer, out int last)) previousPage = last;
            else if (previousPage < 0) previousPage = maps.PagesInMap(pointer.Row, pointer.Page).LastOrDefault(-1);
            // The owner continues in the group of its last page — unless the global map is in reference form and that
            // group lies before the selected bitmap, which no search goes back to (measured: an index whose last page
            // sat in range 0 took its next statement's pages past range 1's start, its old group's pages still free).
            if (groups && previousPage >= 0 && (free.Type == UsageMapType.Inline
                || previousPage / _channel.Format.UsageMapPagesPerBitmapPage >= _bitmapSlot))
            {
                int first = previousPage & ~7;
                for (int page = first; page < first + 8; page++)
                    if (IsFree(page, free, releasedPages))
                        return TakePage(page, free, released, releasedPages, owner, reserve: false);
            }
        }
        if (free.Type == UsageMapType.Reference)
            return AllocateFromReferenceMap(free, released, releasedPages, owner, groups, materialize, rangeStart, holder, below);

        JetFormatBase format = _channel.Format;
        int startPage = UsageMap.StartPage(free.Record, format);
        Span<byte> bitmap = UsageMap.InlineBits(free.Page.AsSpan(free.Slot.Offset, free.Slot.Length), format);
        for (int bit = BitmapBits.NextSetBit(bitmap, Math.Max(0, (rangeStart ?? 0) - startPage)); bit >= 0;
            bit = BitmapBits.NextSetBit(bitmap, bit + 1))
        {
            int allocated = startPage + bit;
            if (allocated > below) break;
            if (releasedPages.Contains(allocated)) continue; // released, not yet reusable
            if (groups && (allocated % 8 != 0 || bit % 8 != 0 || bitmap[bit / 8] != byte.MaxValue
                || Enumerable.Range(allocated, 8).Any(releasedPages.Contains))) continue;
            return TakePage(allocated, free, released, releasedPages, owner, reserve: !groups, materialize);
        }
        if (holder is { } heldBy) return TakeHeld(heldBy, below, owner);

        if (groups)
        {
            int boundary = (_channel.PageCount + 7) & ~7;
            while (_channel.PageCount < boundary) Append();
        }
        // An unrepresented page is not safely recorded as used. Grow the global map before appending.
        return GrowAndAllocate(owner, previousPage);
    }

    /// <summary>The lowest page <paramref name="group"/> holds from <paramref name="from"/> on, or -1.</summary>
    private static int LowestHeld((int Start, byte Bits) group, int from = 0)
    {
        int skip = Math.Clamp(from - group.Start, 0, 8);
        uint bits = (uint)group.Bits >> skip << skip;
        return bits == 0 ? -1 : group.Start + BitOperations.TrailingZeroCount(bits);
    }

    /// <summary>Takes <paramref name="page"/>, which the group of <paramref name="holder"/> holds, for
    /// <paramref name="owner"/> — or, with none, for a usage map's bitmap page.</summary>
    private int TakeHeld((int Row, int Page) holder, int page, (int Row, int Page)? owner)
    {
        (int start, byte bits) = _groups[holder];
        // A bitmap page — no owner — gives the holder's group back and takes the page as any free page is taken, the
        // free pages round it becoming the reservation the next rows take (measured: an index's held pages 33165-33167,
        // the bitmap on 33165, then the rows on 33166, 33167 and the free pages after the next group's first page).
        if (owner is null)
        {
            ReturnGroup((start, bits), toReservation: false);
            _groups.Remove(holder);
            (MapRecord free, MapRecord released) = ReadGlobalMaps();
            return TakePage(page, free, released, new ReleasedPages(this, released), owner: null, reserve: true);
        }
        EnsurePhysicalAllocation(page);
        _groups[holder] = (start, (byte)(bits & ~(1 << (page - start))));
        _owners[owner.Value] = page;
        return page;
    }

    private bool IsFree(int page, MapRecord free, ReleasedPages releasedPages) =>
        !releasedPages.Contains(page) && FreeBit(page, free) == true;

    /// <summary><paramref name="page"/>'s bit in the global free-pages map, in either form: null where the map has
    /// no bit for it.</summary>
    private bool? FreeBit(int page, MapRecord free)
    {
        JetFormatBase format = _channel.Format;
        if (free.Type == UsageMapType.Inline)
        {
            int bit = page - UsageMap.StartPage(free.Record, format);
            ReadOnlySpan<byte> bits = free.Record[format.UsageMapInlineHeaderSize..];
            return bit >= 0 && bit < bits.Length * 8 ? BitmapBits.Get(bits, bit) : null;
        }
        int slot = page / format.UsageMapPagesPerBitmapPage;
        if (slot >= format.UsageMapReferenceSlots) return null;
        int bitmap = UsageMap.ReferencePointer(free.Record, slot, format);
        return bitmap != 0
            ? BitmapBits.Get(UsageMap.BitmapPageBits(UsageMap.ReadBitmapPage(_channel, bitmap).Span, format),
                page % format.UsageMapPagesPerBitmapPage)
            : null;
    }

    /// <summary>Takes one page or the free portion of its four-group reservation, using either global map form.</summary>
    private int TakePage(int page, MapRecord free, MapRecord released, ReleasedPages releasedPages,
        (int Row, int Page)? owner, bool reserve, bool materialize = true)
    {
        JetFormatBase format = _channel.Format;
        int start;
        byte[] bytes;
        Span<byte> bits;
        int holder;
        if (free.Type == UsageMapType.Inline)
        {
            start = UsageMap.StartPage(free.Record, format);
            bytes = free.Page;
            holder = free.PageNumber;
            bits = UsageMap.InlineBits(bytes.AsSpan(free.Slot.Offset, free.Slot.Length), format);
        }
        else
        {
            _bitmapSlot = page / format.UsageMapPagesPerBitmapPage;
            start = _bitmapSlot * format.UsageMapPagesPerBitmapPage;
            holder = UsageMap.ReferencePointer(free.Record, _bitmapSlot, format);
            bytes = ValidateBitmapPage(holder, free, released);
            bits = UsageMap.BitmapPageBits(bytes, format);
        }
        int first = page & ~7;
        int end = Math.Min(first + (reserve ? 32 : 8), start + bits.Length * 8);
        uint reservation = 0;
        HashSet<int> bitmapPages = ReferenceBitmapPages(free, released);
        for (int candidate = first; candidate < end; candidate++)
        {
            int bit = candidate - start;
            if (bit < 0 || !BitmapBits.Get(bits, bit) || releasedPages.Contains(candidate)) continue;
            ValidateReusablePage(candidate, "free bit", free, released, Math.Max(candidate, _channel.PageCount));
            if (bitmapPages.Contains(candidate))
                throw new InvalidDataException($"Global free map marks bitmap page {candidate} itself as free.");
            reservation |= 1u << (candidate - first);
        }
        // Validate/materialize before changing any free bits.
        if (materialize) EnsurePhysicalAllocation(page);
        for (int candidate = first; candidate < end; candidate++)
            if ((reservation & (1u << (candidate - first))) != 0)
                BitmapBits.Set(bits, candidate - start, false);
        _channel.WritePage(holder, bytes);
        if (reserve)
        {
            _reservationStart = first;
            _reservationBits = reservation & ~(1u << (page - first));
        }
        if (owner is { } pointer) _owners[pointer] = page;
        if (!reserve && owner is { } groupOwner)
            _groups[groupOwner] = (first, (byte)(reservation & ~(1u << (page - first))));
        return page;
    }

    /// <summary>Returns unused owner-private group pages, and the pages held until commit, at statement commit;
    /// shared reservations survive.</summary>
    internal void ReturnGroups()
    {
        foreach (var group in _groups.Values) ReturnGroup(group, toReservation: false);
        _groups.Clear();
        _otherGroups.Clear();
        _dataGroups.Clear();
        foreach (int page in _heldUntilCommit) Free(page);
        _heldUntilCommit.Clear();
    }

    /// <summary>Returns an owner group's held pages to the free map — or, with <paramref name="toReservation"/>, to
    /// the shared reservation where its window covers them while it lasts.</summary>
    private void ReturnGroup((int Start, byte Bits) group, bool toReservation)
    {
        for (int bit = 0; bit < 8; bit++)
        {
            if ((group.Bits & (1 << bit)) == 0) continue;
            int page = group.Start + bit;
            if (toReservation && _reservationBits != 0 && page >= _reservationStart && page < _reservationStart + 32)
                _reservationBits |= 1u << (page - _reservationStart);
            else Free(page);
        }
    }

    /// <summary>Whether <paramref name="page"/> is in the shared reservation.</summary>
    private bool Reserved(int page) => page >= _reservationStart && page < _reservationStart + 32
        && (_reservationBits & (1u << (page - _reservationStart))) != 0;

    /// <summary>Takes <paramref name="page"/> out of the shared reservation.</summary>
    private int TakeReserved(int page, MapRecord free, MapRecord released, ReleasedPages releasedPages,
        (int Row, int Page)? owner, bool materialize = true)
    {
        ValidateReusablePage(page, "session reservation", free, released, Math.Max(page, _channel.PageCount));
        if (releasedPages.Contains(page))
            throw new InvalidDataException($"Reserved page {page} is also released.");
        if (materialize) EnsurePhysicalAllocation(page);
        _reservationBits &= ~(1u << (page - _reservationStart));
        if (owner is { } reservedOwner) _owners[reservedOwner] = page;
        return page;
    }

    /// <summary>Returns a page to the global free-pages map now (sets its bit), so it can be reused — the
    /// inverse of <see cref="Allocate"/>. ACE frees this way only a chained long value's pages, when an UPDATE
    /// replaces the value or a DELETE removes its row, and the page such an UPDATE takes unwritten; every other
    /// freed page is held, through <see cref="Release"/>.</summary>
    public void Free(int page)
    {
        (MapRecord free, MapRecord released) = ReadGlobalMaps();
        bool reserved = Reserved(page);
        reserved |= _groups.Values.Any(g => page >= g.Start && page < g.Start + 8
            && (g.Bits & (1 << (page - g.Start))) != 0);
        // A page past the end of the file is one taken unwritten (Allocate without materialize); its free bit is the growth
        // region's, as a reserved page's is.
        ValidateReusablePage(page, "page being freed", free, released,
            reserved || page >= _channel.PageCount ? Math.Max(page, _channel.PageCount - 1) : _channel.PageCount - 1);
        WriteFreeBit(free, released, page, true);
    }

    /// <summary>Sets <paramref name="page"/>'s bit in the global free-pages map, in either form, to
    /// <paramref name="isFree"/>.</summary>
    private void WriteFreeBit(MapRecord free, MapRecord released, int page, bool isFree)
    {
        if (free.Type == UsageMapType.Reference)
        {
            WriteReferenceFreeBit(free, released, page, isFree);
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
                $"Cannot record page {page}: the global free-pages map covers pages {startPage} through "
                + $"{startPage + bitmap.Length * 8 - 1}, so the page has no bit in it.");

        BitmapBits.Set(bitmap, bit, isFree);
        _channel.WritePage(free.PageNumber, free.Page);
    }

    /// <summary>
    /// Frees a page the way ACE frees a dropped table, a dropped index, a deleted row's packed long value and
    /// the pages a delete empties: it is not reusable while this handle stays open, and goes back to the global
    /// free map only when <see cref="ReturnReleasedPages"/> runs at close. Inside a transaction it is released
    /// only if the transaction commits. With <paramref name="untilCommit"/> it is held only until the statement's
    /// transaction commits, as ACE holds a rewritten definition's old continuation pages — or, outside one, until
    /// close.
    /// </summary>
    public void Release(int page, bool untilCommit = false)
    {
        (MapRecord free, MapRecord released) = ReadGlobalMaps();
        ValidateReusablePage(page, "page being released", free, released, _channel.PageCount - 1);
        if (untilCommit) _heldUntilCommit.Add(page);
        else _channel.ReleaseAtClose(page);
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
        if (pages.Count == 0 && !_channel.HasPublishedWrites && _reservationBits == 0 && _groups.Count == 0) return;
        (_, MapRecord released) = ReadGlobalMaps();
        // Every bit kept, none bounded by the file: a released page past its end was never materialized, and is
        // skipped below.
        pages.UnionWith(UsageMap.PagesInRecord(_channel, released.Record, rejectBeyond: null, "The global released-pages map"));
        if (pages.Count == 0 && _reservationBits == 0 && _groups.Count == 0) return;

        bool ownTransaction = !_channel.InTransaction;
        if (ownTransaction) _channel.BeginTransaction();
        try
        {
            // Sized before the merge: a conversion allocates its bitmap pages from the free map as it stands,
            // without the pages being released.
            if (pages.Count != 0) SizeReleasedMap(pages);
            ReturnGroups();

            // A released page past the end of the file was never materialized; the free map already records
            // every page past the end as free.
            foreach (int page in pages)
                if (page < _channel.PageCount) Free(page);
            for (int bit = 0; bit < 32; bit++)
                if ((_reservationBits & (1u << bit)) != 0) Free(_reservationStart + bit);
            _reservationBits = 0;
            _owners.Clear();
            _bitmapSlot = 0;

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
    private int AllocateFromReferenceMap(MapRecord free, MapRecord released, ReleasedPages releasedPages,
        (int Row, int Page)? owner, bool groups, bool materialize, int? rangeStart, (int Row, int Page)? holder, int below)
    {
        JetFormatBase format = _channel.Format;
        ReadOnlySpan<byte> map = free.Record;
        int pagesPerBitmap = format.UsageMapPagesPerBitmapPage;
        HashSet<int> bitmapPages = ReferenceBitmapPages(free, released);

        // A bitmap page's search, like a new reservation's, does not wrap back before the selected bitmap (measured).
        for (int slot = rangeStart is { } from ? Math.Max(from / pagesPerBitmap, _bitmapSlot) : groups ? 0 : _bitmapSlot;
            slot < format.UsageMapReferenceSlots; slot++)
        {
            int bitmapPage = UsageMap.ReferencePointer(map, slot, format);
            if (bitmapPage == 0) continue; // no bitmap page allocated for this range

            byte[] page = ValidateBitmapPage(bitmapPage, free, released);
            Span<byte> bitmap = UsageMap.BitmapPageBits(page, format);
            for (int bit = BitmapBits.NextSetBit(bitmap, Math.Max(0, (rangeStart ?? 0) - slot * pagesPerBitmap)); bit >= 0;
                bit = BitmapBits.NextSetBit(bitmap, bit + 1))
            {
                int allocated = slot * pagesPerBitmap + bit;
                if (allocated > below) return TakeHeld(holder!.Value, below, owner);
                if (releasedPages.Contains(allocated)) continue; // released, not yet reusable
                if (groups && (bit % 8 != 0 || bitmap[bit / 8] != byte.MaxValue
                    || Enumerable.Range(allocated, 8).Any(releasedPages.Contains))) continue;
                if (bitmapPages.Contains(allocated))
                    throw new InvalidDataException($"Global free map marks bitmap page {allocated} itself as free.");
                return TakePage(allocated, free, released, releasedPages, owner, reserve: !groups, materialize);
            }
        }

        return holder is { } heldBy ? TakeHeld(heldBy, below, owner) : GrowAndAllocate(owner);
    }

    /// <summary>Extends allocation metadata before the physical file: inline growth in
    /// <see cref="JetFormatBase.UsageMapInlineGrowthSize"/> steps, <see cref="JetFormatBase.UsageMapHolderReserve"/>
    /// spare bytes left on the holder page before promoting to reference form, and a reference bitmap allocated
    /// before the first data page in its range. All three measured against ACE and asserted by
    /// <c>GlobalMapGrowthTests</c>.</summary>
    private int GrowAndAllocate((int Row, int Page)? owner = null, int previousPage = -1)
    {
        bool ownTransaction = !_channel.InTransaction;
        if (ownTransaction) _channel.BeginTransaction();
        try
        {
            int result = GrowAndAllocateCore(owner, previousPage);
            if (ownTransaction) _channel.CommitTransaction(flush: false);
            return result;
        }
        catch
        {
            if (ownTransaction) _channel.RollbackTransaction();
            throw;
        }
    }

    private int GrowAndAllocateCore((int Row, int Page)? owner, int previousPage)
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
            // Grown past every page the map already covers, never handing out a covered one: Allocate found no
            // eligible free page, and a covered page past the end of the file with its bit clear is not unused —
            // a reservation or an owner group took it before it was written — so appending it allocated it twice.
            int covered = (record.Length - headerSize) * 8;
            int bitmapBytes = UsageMap.InlineBitmapBytes(format, Math.Max(frontier, covered) + 1);

            var grown = new byte[headerSize + bitmapBytes];
            // Preserve existing free bits; newly covered physical pages are already used. Only future
            // pages start free. The requested frontier is cleared by the ordinary allocation path.
            record.CopyTo(grown, 0);
            for (int bit = Math.Max(frontier, covered); bit < bitmapBytes * 8; bit++)
                BitmapBits.Set(UsageMap.InlineBits(grown, format), bit, true);
            var holder = new DataPage();
            holder.Read(_channel.ReadPage(free.PageNumber), format);
            byte[]? rewritten = UsageMap.ReplaceMapRecord(free.Page, holder, format, free.Row, grown, out _);
            if (rewritten is not null &&
                DataPage.ReadFreeSpace(rewritten, format) >= format.UsageMapHolderReserve)
            {
                _channel.WritePage(free.PageNumber, rewritten);
                return Allocate(owner, previousPage);
            }

            // Inline exhausted: promote to reference form. Reserve the bitmap pages first so their own bits are
            // clear in the finished map, placing them past every page the inline map covered — a covered page past
            // the end of the file is free or held, never unused — and carry its bits into the new bitmaps.
            byte[] inline = record;
            if (covered > _channel.PageCount) EnsurePhysicalAllocation(covered - 1);
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
                if (bitmap != 0) WriteNewGlobalBitmap(bitmap, range, UsageMap.InlineBits(inline, format));
            }
            LayMapRecord(free, record);
            return Allocate(owner, previousPage);
        }

        // The first range with no bitmap, never handing out a page of a represented one: its pages past the end of
        // the file are free or held, not unused, as in the inline form. The new bitmap is the first page of its
        // range, as ACE places it, the gap before it written out.
        int rangeIndex = frontier / format.UsageMapPagesPerBitmapPage;
        while (rangeIndex < format.UsageMapReferenceSlots && UsageMap.ReferencePointer(record, rangeIndex, format) != 0)
            rangeIndex++;
        if (rangeIndex >= format.UsageMapReferenceSlots)
            throw new NotSupportedException("Global allocation map has no remaining bitmap slots.");
        int rangeStart = rangeIndex * format.UsageMapPagesPerBitmapPage;
        if (rangeStart > _channel.PageCount) EnsurePhysicalAllocation(rangeStart - 1);

        int newBitmap = AppendUnreleasedPage(releasedPages);
        UsageMap.WriteReferencePointer(record, rangeIndex, format, newBitmap);
        WriteNewGlobalBitmap(newBitmap, rangeIndex);
        LayMapRecord(free, record);
        return Allocate(owner, previousPage);
    }

    /// <summary>Appends the next page past the end of the file that is not released. Released pages at the
    /// frontier are materialized — the file stays contiguous — but not handed out.</summary>
    private int AppendUnreleasedPage(ReleasedPages releasedPages)
    {
        while (releasedPages.Contains(_channel.PageCount))
            Append();
        return Append();
    }

    /// <summary>Writes a new global bitmap page for <paramref name="range"/>: the pages below the end of the file
    /// used, the rest free — except those an inline map being promoted covered (<paramref name="inline"/>), which
    /// keep its bits, so a free page stays free and a held one past the end of the file stays held.</summary>
    private void WriteNewGlobalBitmap(int number, int range, ReadOnlySpan<byte> inline = default)
    {
        JetFormatBase format = _channel.Format;
        byte[] bitmap = UsageMap.NewBitmapPage(format);
        Span<byte> bits = UsageMap.BitmapPageBits(bitmap, format);
        int span = format.UsageMapPagesPerBitmapPage;
        int firstFree = Math.Clamp(_channel.PageCount - range * span, 0, span);
        for (int bit = firstFree; bit < span; bit++)
            BitmapBits.Set(bits, bit, true);
        for (int bit = 0; bit < span && range * span + bit < inline.Length * 8; bit++)
            BitmapBits.Set(bits, bit, BitmapBits.Get(inline, range * span + bit));
        _channel.WritePage(number, bitmap);
    }

    /// <summary>Sets a page's bit in a reference-type global free map, on the bitmap page for its range, to
    /// <paramref name="isFree"/>. If that range has no bitmap page (e.g. a page grown past the map's coverage), the
    /// page is left unrecorded — it simply won't be reused, matching the pre-existing inline-window behaviour.</summary>
    private void WriteReferenceFreeBit(MapRecord free, MapRecord released, int page, bool isFree)
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
        BitmapBits.Set(UsageMap.BitmapPageBits(bitmap, format), bitInRange, isFree);
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

    /// <summary>Grows the file to <paramref name="page"/>, materializing every page before it. A page past the end
    /// of the file is free (the growth region), released, or reserved — by this session or, with its free bit
    /// clear, by another handle's shared reservation or owner group, which is taken from the map before it is
    /// written (page-05 §9). So a gap is never a malformed map, and is written out as every other is. That a second
    /// handle meets another's reservation this way is reasoned from the single-session measurements, not measured
    /// against two ACE handles.</summary>
    private void EnsurePhysicalAllocation(int page)
    {
        while (_channel.PageCount < page) Append();
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
