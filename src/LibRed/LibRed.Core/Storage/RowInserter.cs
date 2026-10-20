using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace LibRed.Storage;

/// <summary>
/// Inserts a row into a table: finds free space on an already-owned data page or allocates a new one,
/// stores any long values, maintains every index B-tree, and enforces unique keys. The page's slot
/// directory grows forward while row data is packed from the page end backward (see <see cref="DataPage"/>).
/// </summary>
internal sealed class RowInserter(PageChannel channel, TableDefinition table)
{
    private readonly PageChannel _channel = channel;
    private readonly UsageMap _usageMaps = new(channel);
    private readonly TableDefinition _table = table;

    private bool HasCalculatedColumns => _table.Columns.Any(c => c.IsCalculated);

    /// <summary>Encodes and writes <paramref name="values"/> (aligned to column Index) into the table.</summary>
    public void Insert(object?[] values) => Insert(values, updateIndexes: true);

    /// <summary>
    /// Inserts a row, optionally skipping index maintenance. Every caller now passes true — the catalog
    /// rows written during CREATE TABLE included, since Access resolves a table through the MSysObjects
    /// <c>ParentIdName</c> index rather than by scan. The heap-only path is retained for seeding a table
    /// whose indexes do not exist yet; it has no caller today.
    /// </summary>
    /// <param name="values">The row's values, aligned to column Index.</param>
    /// <param name="updateIndexes">Whether to write the row's index entries.</param>
    /// <param name="defaults">The table's all-defaults record — each column's evaluated default, null where it has
    /// none — that ACE writes a new row over; with <paramref name="order"/> it decides the record's slack (spec §5).
    /// Null for a row written with no defaults.</param>
    /// <param name="order">The columns the statement names, in the order it names them.</param>
    /// <param name="validate">The caller's checks of the row, run once its ids are drawn (see <see cref="Table.Insert"/>).</param>
    /// <param name="reference">The caller's check of the row's references (see <see cref="Table.Insert"/>).</param>
    public void Insert(object?[] values, bool updateIndexes, object?[]? defaults = null, IReadOnlyList<int>? order = null,
        Action<object?[]>? validate = null, Action<object?[]>? reference = null)
    {
        JetFormatBase format = _channel.Format;
        RejectExplicitCalculatedValues(values, null);
        WrapBytes(values);

        // Assign AutoNumber ids for any AutoNumber column the caller left unset (the usual case —
        // Jet SQL omits the AutoNumber column from the insert). An explicitly supplied value is kept
        // as-is (Jet, unlike SQL Server, permits explicit AutoNumber values); either way the row's
        // final id drives both the row encoding and the high-water update below. A row refused from here on
        // keeps its drawn ids used, which a rollback writes back (PageChannel.KeepThroughRollback).
        bool[]? generatedAutoNumbers = AssignAutoNumbers(format, values);
        validate?.Invoke(values);
        if (updateIndexes) EnforceNonNullKeys(values);   // after AssignAutoNumbers, so a counter key has its value

        // From here the row exists in two forms, and they are two ARRAYS. `values` stays as the caller gave
        // it — the logical row — and `storage` is the copy long values are materialised into, where a memo
        // becomes the 12-byte descriptor naming its pages. Everything that needs the value reads `values`
        // (index keys — a Memo is indexable, on the collation key of its first 255 characters — and
        // calculated expressions); only the encoder reads `storage`. See ToStorageValues.
        bool calculated = HasCalculatedColumns;
        object?[] storage = ToStorageValues(values);

        // Encode first, to know how much room the record needs.
        var encoder = new RowCodec(_table.Columns, format,
            _table.VariableColumnCount, SpillCalculated, _table.ColumnIdHighWater);
        byte[] record = encoder.Encode(storage, null, calculated ? values : null,
            slackOf: defaults is null || order is null ? null : InsertSlack(format, defaults, order));

        EnsureRecordFits(format, record);

        // Then find an owned page with room for the record plus its slot entry.
        (int pageNumber, byte[] page) = FindPageWithRoom(format, record.Length + format.DataRowDirectoryEntrySize);
        int rowCount = AppendRow(format, pageNumber, page, record, RowSlotFlags.None);
        _channel.WritePage(pageNumber, page);

        // ACE writes the row before it gives the indexes its keys, and refuses a missing parent or a duplicate key
        // only there — the refused row has taken its page by then (measured), which the statement's rollback frees.
        if (updateIndexes)
        {
            reference?.Invoke(values);
            EnforceUniqueIndexes(values);
        }

        // Asked before the row's own entries go in, so an index "already has" a key only through another row.
        HashSet<int> newKeys = updateIndexes ? IndexesGainingANewKey(values) : [];
        UpdateTdefCounters(format, values, generatedAutoNumbers, newKeys);
        if (updateIndexes)
            UpdateIndexes(values, new RowId(pageNumber, rowCount));
    }

    /// <summary>
    /// Rewrites an existing row in place at its slot (page + row index preserved, matching Access, so
    /// index rowid pointers stay valid). Any changed memo/OLE value is re-materialized onto LVAL pages;
    /// the page is repacked to absorb a size change (slot order = physical order, as Access keeps it).
    /// A row that no longer fits its page is <b>relocated</b> — the slot becomes a 4-byte forward pointer and
    /// the row moves to a page with room, as Access does — and one that outgrows its new page moves again.
    /// Index-key maintenance for a changed indexed column is the caller's responsibility. <c>buffer</c> is the one
    /// the statement builds its updated rows in (see RowCodec's <c>Encode</c>), or null for a row rewritten on its own.
    /// </summary>
    public void Update(RowId id, object?[] values, IReadOnlySet<int> changedColumns, byte[]? buffer = null)
    {
        // An update is several writes — the old long values are freed, the new ones materialised onto pages,
        // then the row itself — and the row is the last of them, so it is the last thing that can fail (a
        // record that has grown past 4060, a page that will not take it). Under the SQL engine every
        // statement already runs in one, but a caller using the Core API directly had no such cover: the
        // failure left the old memo's pages freed and the new value's written, with the row still naming the
        // old one. The order is deliberate and unchanged — ACE frees an update's pages at once, and the new
        // value may land on them — so what this adds is only the undo.
        bool ownTransaction = !_channel.InTransaction;
        if (ownTransaction) _channel.BeginTransaction();
        try
        {
            UpdateCore(id, values, changedColumns, buffer);
            if (ownTransaction) _channel.CommitTransaction(flush: false);
        }
        catch when (ownTransaction)
        {
            _channel.RollbackTransaction(failed: true);
            throw;
        }
    }

    private void UpdateCore(RowId id, object?[] values, IReadOnlySet<int> changedColumns, byte[]? buffer)
    {
        JetFormatBase format = _channel.Format;
        RejectExplicitCalculatedValues(values, changedColumns);
        WrapBytes(values);
        EnforceNonNullKeys(values, changedColumns);
        EnforceUniqueIndexes(values, id.Packed, changedColumns);

        // Two arrays, as on the insert path: `values` stays the logical row the caller gave, and `storage` is
        // where the long values become descriptors. The caller keeps the values it passed — an index entry
        // this update moves has to be keyed off them, and a descriptor is not a key.
        object?[] storage = (object?[])values.Clone();

        // Long-value (memo/OLE) columns: keep an unchanged column's on-disk descriptor verbatim (so it is not
        // needlessly re-materialised onto fresh LVAL pages), and free a changed column's old chained pages —
        // after the new values are written, as ACE does: the UPDATE that frees them never reuses them.
        byte[] oldRow = ReadRowBytes(id);
        // One layout for both: the trailer arithmetic is the same for either selection off this row.
        RowCodec.Layout oldLayout = RowCodec.ParseLayout(_table, format, oldRow);
        var oldDescriptors = RowCodec.LongValueDescriptors(_table.Columns, oldLayout, oldRow);
        var oldCalculated = RowCodec.CalculatedSlots(_table.Columns, oldLayout, oldRow);
        var replaced = new List<(ColumnDef Column, byte[] Descriptor)>();
        foreach (ColumnDef column in _table.Columns)
        {
            if (column.Type is not (JetDataType.Memo or JetDataType.Ole)) continue;
            if (!oldDescriptors.TryGetValue(column.Index, out byte[]? oldDescriptor)) continue; // old value was null
            if (changedColumns.Contains(column.Index)) replaced.Add((column, oldDescriptor));
            else storage[column.Index] = new LongValueStore.DescriptorValue(oldDescriptor);
        }

        // A calculated column is recomputed only when the UPDATE writes a column its expression READS —
        // measured against ACE, which leaves the cached value untouched otherwise and refreshes it even when
        // a referenced column is rewritten with the value it already had (§3.4a). Carrying the old envelope
        // over verbatim is therefore not an optimisation: recomputing every time would write bytes ACE never
        // would, and a cache neither engine re-derives on read is exactly where that stays invisible.
        Dictionary<int, byte[]>? preservedCalculated = null;
        foreach (ColumnDef column in _table.Columns)
        {
            if (!column.IsCalculated) continue;
            if (CalculatedValue.ReferencedIndexes(column, _table.Columns).Overlaps(changedColumns)) continue;
            if (!oldCalculated.TryGetValue(column.Index, out byte[]? kept)) continue;
            (preservedCalculated ??= [])[column.Index] = kept;
        }

        // A calculated column that IS being recomputed abandons whatever its old result occupied, so a
        // memo-backed one has to give its LVAL pages back — nothing else will, and the row's new descriptor
        // overwrites the only reference to them. A preserved column keeps its pages precisely because the
        // descriptor carried over verbatim still points at them.
        foreach (ColumnDef column in _table.Columns)
        {
            if (!column.IsCalculated || !column.HasLongValueMap) continue;
            if (preservedCalculated?.ContainsKey(column.Index) == true) continue;
            if (oldCalculated.TryGetValue(column.Index, out byte[]? stale)
                && stale.Length >= format.LongValueDescriptorSize)
                FreeLongValue(column, stale, releaseAtClose: false);
        }

        List<int> unwritten = MaterializeLongValues(storage, [.. replaced
            .Where(r => LongValueStore.Read(r.Descriptor, format).Storage == LongValueStore.StorageKind.Chained)
            .Select(r => r.Column.Index)]);
        // A chain goes back at once; a single-page value's page this empties is held, as a delete holds it (measured).
        foreach ((ColumnDef column, byte[] descriptor) in replaced)
            FreeLongValue(column, descriptor, releaseAtClose: true);
        foreach (int page in unwritten) _channel.Allocator.Free(page);

        PageBuffer srcPage = _channel.ReadPageShared(id.Page);
        // Written over the old row, as ACE writes it: its fixed bytes, its dropped columns' chunks and their bits
        // stay wherever the new values do not land.
        var encoder = new RowCodec(_table.Columns, format,
            _table.VariableColumnCount, SpillCalculated, _table.ColumnIdHighWater);
        byte[] record = encoder.Encode(storage, preservedCalculated, HasCalculatedColumns ? values : null, oldLayout,
            UpdateSlack(oldLayout, changedColumns), buffer);

        // Here as well as on the insert path, and before the in-place rewrite rather than beside the
        // page-search: a row that grows past the cap but still fits its current page is rewritten where it
        // lies and never reaches the search, so guarding only there let an UPDATE produce exactly the
        // unreadable row an INSERT was stopped from producing.
        EnsureRecordFits(format, record);

        if (DataPage.ReadSlot(srcPage.Span, format, id.Row).Flags.HasFlag(RowSlotFlags.Overflow))
        {
            // This slot is a forward record pointer to the real (relocated) row; rewrite it on its target page
            // (which keeps its hidden "deleted" flag).
            DataPage.TryReadRow(srcPage, format, id.Row,
                out DataPage.RowSlot sourceSlot, out ReadOnlySpan<byte> sourceBytes);
            DataPage.RelocatedRow target = DataPage.ResolveRelocation(
                _channel, _table.DefinitionPage, sourceSlot, sourceBytes);
            if (TryRewriteRowInPlace(target.Buffer.PageNumber, target.RowNumber, record)) return;

            // It has outgrown the page it was moved to as well, so it moves again. The old hidden row is
            // reclaimed first — through the same path a delete uses, and while the source slot still points at
            // it — and the pointer is then re-aimed, exactly as the first move wrote it. Nothing else changes:
            // the row keeps its id, so every index entry still names it.
            ReclaimRelocationTarget(format, target);
            Relocate(format, id, record);
            return;
        }

        // Normal row: rewrite in place if it still fits its page (row id preserved).
        if (TryRewriteRowInPlace(id.Page, id.Row, record)) return;

        // It no longer fits: relocate the row to another page as a hidden ("deleted") record, and turn this
        // slot into a 4-byte forward pointer (row id preserved, so index entries stay valid) — Access's own
        // overflow mechanism, verified against ACE.
        Relocate(format, id, record);
    }

    /// <summary>The slack a new record keeps (spec §5): ACE writes the statement's values over the all-defaults
    /// record in the order the statement names them, and a value shorter than the one it replaces leaves the
    /// difference behind, which a longer one takes up before the record grows. A Memo or OLE default is no part of
    /// that record (verified). Read off the chunks the new record is encoded with.</summary>
    private Func<IReadOnlyList<byte[]>, int> InsertSlack(JetFormatBase format, object?[] defaults, IReadOnlyList<int> order)
    {
        // Each named variable column's slot, and the bytes its default takes there.
        var named = new List<(int Slot, int From)>();
        foreach (int index in order)
        {
            ColumnDef column = _table.Columns[index];
            if (column.IsFixedLength || column.IsCalculated) continue;
            int from = column.Type is JetDataType.Memo or JetDataType.Ole || defaults[index] is not { } value
                ? 0
                : Types.JetTypeCodec.Encode(column, value, format).Length;
            named.Add((column.VariableIndex, from));
        }
        return chunks =>
        {
            int slack = 0;
            foreach ((int slot, int from) in named)
            {
                int to = chunks[slot].Length;
                slack = to < from ? slack + from - to : Math.Max(0, slack - (to - from));
            }
            return slack;
        };
    }

    /// <summary>The slack an updated record keeps (spec §5): the old record's, less what the new values grow by —
    /// unless one of them shrinks, which compacts the record and leaves none (verified). Read off the chunks the new
    /// record is encoded with.</summary>
    private Func<IReadOnlyList<byte[]>, int> UpdateSlack(in RowCodec.Layout old, IReadOnlySet<int> changedColumns)
    {
        // Each changed variable column's slot, and the bytes it took in the old record.
        var changed = new List<(int Slot, int From)>();
        foreach (int index in changedColumns)
        {
            ColumnDef column = _table.Columns[index];
            if (column.IsFixedLength || column.IsCalculated) continue;
            changed.Add((column.VariableIndex,
                column.VariableIndex < old.NumVar ? old.VarChunk(column.VariableIndex).Length : 0));
        }
        int oldSlack = old.Slack;
        return chunks =>
        {
            int slack = oldSlack;
            foreach ((int slot, int from) in changed)
            {
                int to = chunks[slot].Length;
                if (to < from) return 0;
                slack = Math.Max(0, slack - (to - from));
            }
            return slack;
        };
    }

    /// <summary>Writes <paramref name="record"/> as a hidden row on a page with room and turns the slot at
    /// <paramref name="id"/> into the forward record pointer that names it. A pointer always fits the slot the
    /// row is vacating, so this cannot fail for want of space.</summary>
    private void Relocate(JetFormatBase format, RowId id, byte[] record)
    {
        (int targetPage, int targetRow) = WriteHiddenRow(format, record);
        var pointerBytes = new byte[PageBuffer.RecordPointerSize];
        PageBuffer.WriteRecordPointer(pointerBytes, 0, targetRow, targetPage);
        TryRewriteRowInPlace(id.Page, id.Row, pointerBytes, addFlags: RowSlotFlags.Overflow);
    }

    /// <summary>Rewrites the row at (page, slot) in place, repacking the page from the end in slot order so
    /// every row id is preserved and each slot keeps its flags (plus <paramref name="addFlags"/> on the
    /// target). Returns false without writing if the row no longer fits the page.</summary>
    private bool TryRewriteRowInPlace(int pageNumber, int slot, byte[] record, RowSlotFlags addFlags = RowSlotFlags.None)
    {
        JetFormatBase format = _channel.Format;
        // Pool the transient working page: read into a rented buffer, repack in place, write back, return it —
        // instead of allocating (and immediately discarding) two 4 KB arrays (ReadPage's + ToArray's) per rewrite.
        byte[] page = ArrayPool<byte>.Shared.Rent(format.PageSize);
        try
        {
            PageBuffer source = _channel.ReadPageShared(pageNumber);
            source.Span.CopyTo(page);
            int rowCount = DataPage.ReadRowCount(page, format);

            var rows = new ReadOnlyMemory<byte>[rowCount];
            var flags = new RowSlotFlags[rowCount];
            int directoryEnd = DataPage.DirectoryEnd(format, rowCount);
            int prevEnd = format.PageSize;
            for (int i = 0; i < rowCount; i++)
            {
                (int offset, flags[i]) = DataPage.ReadSlot(page, format, i);
                // Same invariant DataPage enforces on the read side — this repacker rewrites the page, so a
                // bad directory entry must be reported before the first CopyTo rather than mid-repack.
                DataPage.ValidateSlot(pageNumber, format.PageSize, i, offset, prevEnd, directoryEnd);
                rows[i] = source.Data.Slice(offset, prevEnd - offset); // preserve every row's bytes (deleted/overflow included)
                prevEnd = offset;
            }
            rows[slot] = record;
            flags[slot] |= addFlags;

            if (!DataPage.LayRows(page.AsSpan(0, format.PageSize), format, rows, flags)) return false;
            _channel.WritePage(pageNumber, page.AsSpan(0, format.PageSize));
            return true;
        }
        finally { ArrayPool<byte>.Shared.Return(page); }
    }

    /// <summary>Writes a relocated row's bytes onto a page with room, as a hidden slot (Access marks it
    /// "deleted" so scans skip it there — it's only reached via the forward pointer). Returns its location.</summary>
    private (int Page, int Row) WriteHiddenRow(JetFormatBase format, byte[] record)
    {
        (int pageNumber, byte[] page) = FindPageWithRoom(format, record.Length + format.DataRowDirectoryEntrySize);
        int rowCount = AppendRow(format, pageNumber, page, record, RowSlotFlags.Deleted); // hidden target
        _channel.WritePage(pageNumber, page);
        return (pageNumber, rowCount);
    }

    /// <summary>Appends <paramref name="record"/> to a page <see cref="FindPageWithRoom"/> chose, returning its row.
    /// That page declared the room, so a page whose rows leave none is corrupt.</summary>
    private static int AppendRow(JetFormatBase format, int pageNumber, byte[] page, byte[] record, RowSlotFlags flags) =>
        DataPage.TryAppendRow(page, format, record, flags, out int row)
            ? row
            : throw new InvalidDataException(
                $"Data page {pageNumber} declares room for a {record.Length}-byte row, but its rows leave none.");

    /// <summary>
    /// Deletes the row at <paramref name="id"/> and reclaims its space, then decrements the TDEF row count
    /// (0x10). The caller removes the row's index entries first. A relocated row's hidden target is reclaimed
    /// too, and its long values are freed as ACE frees a delete's (see <see cref="FreeLongValue"/>). A page
    /// whose last live row this was is released outright (<see cref="WriteReclaimedPage"/>).
    /// </summary>
    public void Delete(RowId id)
    {
        JetFormatBase format = _channel.Format;

        // Free the deleted row's long values — a chained value's pages at once, a packed value held until close,
        // as ACE frees them.
        byte[] rowBytes = ReadRowBytes(id);
        var oldDescriptors = RowCodec.LongValueDescriptors(_table, format, rowBytes);

        // Which real indexes this row was the LAST holder of a key for. Asked before the row goes, and with
        // the row itself excluded, so the answer is "does another row still carry this key" either way —
        // the caller may or may not have removed this row's entries yet.
        HashSet<int> lastKeyLost = IndexesLosingTheirLastKey(format, rowBytes, id);
        foreach (ColumnDef column in _table.Columns)
            if ((column.Type is JetDataType.Memo or JetDataType.Ole || column.HasLongValueMap)
                && oldDescriptors.TryGetValue(column.Index, out byte[]? d))
                FreeLongValue(column, d, releaseAtClose: true);

        byte[] page = ArrayPool<byte>.Shared.Rent(format.PageSize);
        try
        {
            _channel.ReadPage(id.Page, page);
            RowSlotFlags entry = DataPage.ReadSlot(page, format, id.Row).Flags;

            // A LIVE slot carrying the overflow flag is a 4-byte forward pointer to a relocated row rather
            // than the row itself, so the row it forwards to has to go first — otherwise deleting a relocated
            // row strands it on its own page for ever, which for a widened row is far more than the
            // pointer's own four bytes. ACE reclaims both (verified: the target's page comes back to a bare
            // 4080 free, and the pointer's four bytes return to this one).
            //
            // Deleted must be excluded, not just overflow: ReclaimRow's own tombstone is deleted+overflow
            // (0xC000) and zero-length, so testing the overflow bit alone treats a tombstone as a relocation
            // source and reads four bytes of the NEIGHBOURING row as a page/row pointer.
            if ((entry & (RowSlotFlags.Deleted | RowSlotFlags.Overflow)) == RowSlotFlags.Overflow)
            {
                DataPage.TryReadRow(new PageBuffer(page.AsMemory(0, format.PageSize), id.Page), format, id.Row,
                    out DataPage.RowSlot sourceSlot, out ReadOnlySpan<byte> sourceBytes);
                ReclaimRelocationTarget(format, DataPage.ResolveRelocation(
                    _channel, _table.DefinitionPage, sourceSlot, sourceBytes));
            }

            DataPage.ReclaimRow(page.AsSpan(0, format.PageSize), format, id.Row);
            // The page has room again, so its bit goes back into the table's free-pages map — the mirror of the
            // insert path clearing it when the page filled (AllocateDataPage). Without this the space a delete
            // frees in a full page is space no later insert ever looks at, and ACE sets the bit here too
            // (measured: deleting a row from the first of many pages, ACE writes the map holder and LibRed did not).
            // A page the delete empties gets it too, before its release takes it out again: a map that cannot take
            // the bit turns reference form on the way, as ACE's does (measured: deleting every seventh row of a
            // one-row-a-page table gives its free-pages map a bitmap for each range).
            UpdateUsageBit(format.TdefFreePagesOffset, id.Page, set: true);
            WriteReclaimedPage(format, page, id.Page);
        }
        finally { ArrayPool<byte>.Shared.Return(page); }

        byte[] tdef = ArrayPool<byte>.Shared.Rent(format.PageSize);
        try
        {
            _channel.ReadPage(_table.DefinitionPage, tdef);
            TableDefinition.WriteRowCount(tdef, format, TableDefinition.ReadRowCount(tdef, format) - 1);

            // ACE's index entry counts on delete, measured against a DAO delete of the same row
            // (ComplexDeleteByteParityProbeTest): the TOTAL count (+0) drops by one on every real index, and
            // the UNIQUE count (+4) only where the row was the last holder of its key. Deliberately NOT the
            // mirror of insert, which leaves the total alone and only raises the unique count — so the total
            // drifts downward across insert/delete cycles in ACE too. Faithful, not tidy.
            //
            // A total already reading 0 is the exception: ACE leaves that index's pair ENTIRELY alone, unique
            // included. Measured on the same probe — a flat table's index with total 0 and unique 3 came back
            // from a DAO delete still 0 and 3, while its siblings with a non-zero total each dropped by one.
            // So it is not a clamp (a clamp would still have taken unique to 2); a zero total marks a counter
            // ACE is not maintaining, and it stops touching the pair. Without this the totals go negative.
            //
            // While the total is still counting, though, the unique count is held to no more than it, as an UPDATE's
            // key move holds it (CountKeyMoved). Measured by whole-file diff (WholeFileParityProbeTest, DELETE FROM
            // Customer): an index reading total 6 and unique 7 over twelve rows of distinct keys ends 0 and 0 in ACE;
            // the sixth delete takes the total to 0 and the unique count with it, and the six after it touch nothing.
            // Its sibling at total 0 and unique 13 stayed 0 and 13.
            foreach (IndexDef index in _table.Indexes.GroupBy(i => i.RealIndexOrdinal).Select(g => g.First()))
            {
                (int total, int unique) = TableDefinition.ReadIndexCounts(tdef, format, index.RealIndexOrdinal);
                if (total == 0) continue;
                TableDefinition.WriteIndexCounts(tdef, format, index.RealIndexOrdinal, total - 1,
                    Math.Min(lastKeyLost.Contains(index.RealIndexOrdinal) ? unique - 1 : unique, total - 1));
            }

            WriteDefinitionCounters(tdef.AsSpan(0, format.PageSize));
        }
        finally { ArrayPool<byte>.Shared.Return(tdef); }
    }

    /// <summary>
    /// Empties the table the way ACE runs a <c>DELETE</c> with no <c>WHERE</c>: every page goes, and no row is
    /// touched. Each data page and each long-value page is taken out of its owned and free maps and released,
    /// its bytes left exactly as they were — rows, slots and type alike, so no <c>0x0109</c> stamp and no
    /// tombstones, and the table's first data page goes with the rest. Every index keeps only its root, which
    /// becomes an empty leaf over its old entry bytes; the other pages of the tree leave its map and are released
    /// untouched. The row count and every index's pair of counts read 0.
    /// </summary>
    /// <remarks>
    /// Measured by whole-file diff against ACE (<c>WholeFileParityProbeTest</c>), over a 112-page table with two
    /// two-level indexes, a table with chained and packed long values, a definition on continuation pages, a
    /// table already emptied row by row, and tables with no index, a composite key and a foreign key of their
    /// own. The release is held until this handle closes, the route ACE takes for a delete's pages. The engine
    /// decides when ACE takes this path; see <c>StatementExecutor.Delete</c>.
    /// </remarks>
    public void Truncate()
    {
        JetFormatBase format = _channel.Format;
        var maps = new UsageMap(_channel, _table);

        foreach (int page in maps.DataPages().ToList())
            ReleaseDataPage(format, page);

        TableDefinition definition = LongValueMaps;
        foreach ((int columnId, (int Row, int Page) owned) in definition.LongValueOwnedMaps)
        {
            if (owned.Page == 0) continue;
            definition.LongValueFreeMaps.TryGetValue(columnId, out (int Row, int Page) free);
            foreach (int page in maps.PagesInMap(owned.Row, owned.Page).ToList())
                ReleaseLongValuePage(page, owned, free, releaseAtClose: true);
        }

        IndexDef[] realIndexes = [.. _table.Indexes.GroupBy(i => i.RealIndexOrdinal).Select(g => g.First())];
        var tree = new IndexTree(_channel, _table);
        foreach (IndexDef index in realIndexes)
            tree.Empty(index);

        byte[] tdef = ArrayPool<byte>.Shared.Rent(format.PageSize);
        try
        {
            _channel.ReadPage(_table.DefinitionPage, tdef);
            TableDefinition.WriteRowCount(tdef, format, 0);
            foreach (IndexDef index in realIndexes)
                TableDefinition.WriteIndexCounts(tdef, format, index.RealIndexOrdinal, 0, 0);
            WriteDefinitionCounters(tdef.AsSpan(0, format.PageSize));
        }
        finally { ArrayPool<byte>.Shared.Return(tdef); }
    }

    /// <summary>Counts an UPDATE's key change in <paramref name="index"/>'s statistics as ACE does (verified): the
    /// total drops by one, as on a delete, and the unique count is then held to no more than the total. Whether the
    /// old key had another holder does not matter, and the new key advances nothing. A total already reading 0 is
    /// left alone, as a delete leaves it.</summary>
    public void CountKeyMoved(IndexDef index)
    {
        JetFormatBase format = _channel.Format;
        byte[] tdef = ArrayPool<byte>.Shared.Rent(format.PageSize);
        try
        {
            _channel.ReadPage(_table.DefinitionPage, tdef);
            (int total, int unique) = TableDefinition.ReadIndexCounts(tdef, format, index.RealIndexOrdinal);
            if (total == 0) return;
            TableDefinition.WriteIndexCounts(tdef, format, index.RealIndexOrdinal, total - 1, Math.Min(unique, total - 1));
            WriteDefinitionCounters(tdef.AsSpan(0, format.PageSize));
        }
        finally { ArrayPool<byte>.Shared.Return(tdef); }
    }

    /// <summary>
    /// The real-index ordinals for which the row at <paramref name="id"/> holds the only copy of its key, so
    /// removing the row also removes a distinct key. An index the row is excluded from (IgnoreNulls with a
    /// null key) never counted it, so it is never counted down either — the mirror of the insert side.
    /// </summary>
    private HashSet<int> IndexesLosingTheirLastKey(JetFormatBase format, byte[] rowBytes, RowId id)
    {
        var lost = new HashSet<int>();
        if (_table.Indexes.Count == 0) return lost;

        // With a long-value reader, because these values are handed to the index key encoder: a Memo column IS
        // indexable, and decoded without the reader it comes back as its 12-byte on-disk descriptor, which the
        // encoder's text path casts to string and dies on. A decode that feeds a key needs the value the
        // column holds, not the pointer to it.
        object?[] values = new RowCodec(_table.Columns, format, _table.VariableColumnCount,
            columnIdHighWater: _table.ColumnIdHighWater, longValues: new LongValueStore(_channel)).Decode(rowBytes);
        var writer = new IndexTree(_channel, _table);
        foreach (IndexDef index in _table.Indexes.GroupBy(i => i.RealIndexOrdinal).Select(g => g.First()))
        {
            if (index.RootPage <= 0) continue;
            if (index.IgnoreNulls && HasNullKey(index, values)) continue;
            if (!writer.KeyExists(index, values, id.Packed))
                lost.Add(index.RealIndexOrdinal);
        }
        return lost;
    }

    /// <summary>Reclaims the row a relocation pointer forwards to, on whatever page it lives. The caller resolves
    /// <paramref name="target"/> through <see cref="DataPage"/>, whose checks matter more here than on a
    /// read, because this WRITES: an unvalidated pointer runs ReclaimRow over a live row of an unrelated page,
    /// sliding its neighbours and stamping a tombstone.
    /// <para>A failed check is <b>corruption, and says so</b>. It used to return quietly so that a delete
    /// still removed the row the caller asked about — but the caller only reaches here because the slot is a
    /// live relocation pointer, so a pointer that does not resolve means the file already disagrees with
    /// itself, and carrying on strands the moved row silently. That is also how a bug in this engine's own
    /// relocation writer would look: nothing raised, a page quietly leaked.</para></summary>
    private void ReclaimRelocationTarget(JetFormatBase format, DataPage.RelocatedRow target)
    {
        byte[] page = ArrayPool<byte>.Shared.Rent(format.PageSize);
        try
        {
            target.Buffer.Span.CopyTo(page);
            DataPage.ReclaimRow(page.AsSpan(0, format.PageSize), format, target.RowNumber);
            // Written back, never released, even when that was the page's last row — ACE keeps a page emptied
            // this way as an ordinary data page (measured: deleting a relocated row leaves the page it had
            // moved to at type 0x01 with a bare 4080 free and its one slot tombstoned, still in the table's
            // maps). The release belongs to deleting a LIVE row (see WriteReclaimedPage); the row reclaimed
            // here was already hidden, so emptying its page is a space operation rather than a deletion.
            _channel.WritePage(target.Buffer.PageNumber, page.AsSpan(0, format.PageSize));
        }
        finally { ArrayPool<byte>.Shared.Return(page); }
    }

    /// <summary>
    /// Writes back the page a DELETE has just reclaimed a row from, and gives the page away when that row was
    /// the last live one on it. Only the delete path: a page emptied by
    /// reclaiming a hidden relocation target is kept, as ACE keeps it (see <see cref="ReclaimRelocationTarget"/>).
    /// </summary>
    /// <remarks>
    /// <para>A page emptied by DELETE is stamped <see cref="PageType.ReleasedDataPage"/> (<c>0x0109</c>), taken
    /// out of the table's owned and free maps, and released — held until this handle closes, the route ACE
    /// takes for a delete's pages. Nothing else about the page changes: the row count stands and every slot
    /// stays the 0-length deleted+overflow tombstone <see cref="ReclaimRow"/> left, which is already what ACE
    /// writes, so the stamp is the only differing byte.</para>
    /// <para>Measured against ACE over a 1,200-row table with 699 rows deleted from the low end: 18 data pages
    /// emptied, and on each one ACE changed exactly the type's low byte, cleared the page from both of the table's
    /// maps, and set its bit in the global map. This is the same event as an emptied packed long-value page
    /// (<see cref="ReleasePackedValue"/>) and carries the same stamp — <c>0x0109</c> is not confined to
    /// long-value pages, and these carry the table's own owner at <c>0x04</c> rather than an <c>LVAL</c>
    /// signature.</para>
    /// <para>"Emptied" is every slot deleted <b>and</b> zero-length, not merely every slot deleted: a hidden
    /// relocation target is a live row carrying the deleted flag, so a page holding one is not empty.</para>
    /// <para>A page no lower page of the free-pages map stands before is the exception and is kept — see
    /// <see cref="IsLowestFreePage"/>.</para>
    /// </remarks>
    private void WriteReclaimedPage(JetFormatBase format, byte[] page, int pageNumber)
    {
        bool release = IsEmptied(format, page) && !IsLowestFreePage(pageNumber);
        if (release) DataPage.MarkReleased(page);
        _channel.WritePage(pageNumber, page.AsSpan(0, format.PageSize));
        if (release) ReleaseDataPage(format, pageNumber);
    }

    /// <summary>Takes a data page out of the table's owned and free maps and releases it — held until this handle
    /// closes, the route ACE takes for a delete's pages.</summary>
    private void ReleaseDataPage(JetFormatBase format, int pageNumber)
    {
        UpdateUsageBit(format.TdefOwnedPagesOffset, pageNumber, set: false);
        UpdateUsageBit(format.TdefFreePagesOffset, pageNumber, set: false);
        _channel.Allocator.Release(pageNumber);
    }

    /// <summary>
    /// Whether <paramref name="pageNumber"/>, just emptied and given its free-pages bit, is the lowest page that map
    /// marks — the page a row-by-row delete keeps, however empty it gets (a <c>DELETE</c> with no <c>WHERE</c> takes
    /// it with the rest: <see cref="Truncate"/>). Measured: deleting every row of a 35-page table releases 34 of them
    /// and leaves the lowest at <c>0x01</c>, still in both of the table's maps; over one-row pages with none marked
    /// free, deleting Ids 10–19 keeps the first page it empties, a later DELETE of 20–24 keeps none, and one of 0–4
    /// keeps its first again, below it — and rows added since that fill the kept page take it out of the map, so the
    /// next DELETE keeps its first emptied page once more.
    /// </summary>
    private bool IsLowestFreePage(int pageNumber) =>
        new UsageMap(_channel, _table).MinFreeDataPage() == pageNumber;

    /// <summary>True when no live row is left on the page: every slot a zero-length deleted tombstone. Slot
    /// offsets are non-increasing, so a slot is zero-length exactly when it repeats the previous one's offset
    /// (the page size standing in for the first).</summary>
    private static bool IsEmptied(JetFormatBase format, byte[] page)
    {
        int rowCount = DataPage.ReadRowCount(page, format);
        int previous = format.PageSize;
        for (int i = 0; i < rowCount; i++)
        {
            (int offset, RowSlotFlags flags) = DataPage.ReadSlot(page, format, i);
            if (!flags.HasFlag(RowSlotFlags.Deleted) || offset != previous) return false;
            previous = offset;
        }
        return true;
    }

    /// <summary>The full inline bytes of the row at <paramref name="id"/> (following an overflow pointer).</summary>
    public byte[] ReadRow(RowId id) => ReadRowBytes(id);

    /// <summary>Rewrites the row at <paramref name="id"/> with the exact <paramref name="record"/> bytes given,
    /// handling growth the same way <see cref="Update"/> does — repack in place if it fits, else relocate as a
    /// hidden row with a forward pointer. The row id is preserved (so index entries stay valid).</summary>
    public void RewriteRowRaw(RowId id, byte[] record)
    {
        JetFormatBase format = _channel.Format;

        // The third entry point that writes a row, and the one most likely to cross the cap: its caller is
        // the in-place ALTER re-lay, which by design makes rows LONGER — the retyped column is appended and
        // its old slot left as dead space. The record arrives pre-built (BuildRelaidRecord → AssembleRow),
        // so it never passes through RowCodec.Encode or either guarded entry point.
        EnsureRecordFits(format, record);

        byte[] srcPage = _channel.ReadPageShared(id.Page).Span.ToArray();
        if (DataPage.ReadSlot(srcPage, format, id.Row).Flags.HasFlag(RowSlotFlags.Overflow))
        {
            DataPage.TryReadRow(new PageBuffer(srcPage, id.Page), format, id.Row,
                out DataPage.RowSlot sourceSlot, out ReadOnlySpan<byte> sourceBytes);
            DataPage.RelocatedRow target = DataPage.ResolveRelocation(
                _channel, _table.DefinitionPage, sourceSlot, sourceBytes);
            if (TryRewriteRowInPlace(target.Buffer.PageNumber, target.RowNumber, record)) return;

            // Outgrown its new page as well, so it moves again — the old hidden row reclaimed while the
            // pointer still names it, then the pointer re-aimed. See Update, which does the same.
            ReclaimRelocationTarget(format, target);
            Relocate(format, id, record);
            return;
        }
        if (TryRewriteRowInPlace(id.Page, id.Row, record)) return;
        Relocate(format, id, record);
    }

    /// <summary>The full inline bytes of the row at <paramref name="id"/>, following an overflow-forward
    /// pointer to the row's real location if the slot has been relocated.</summary>
    private byte[] ReadRowBytes(RowId id)
    {
        JetFormatBase format = _channel.Format;
        PageBuffer page = _channel.ReadPageShared(id.Page);
        if (!DataPage.TryReadRow(page, format, id.Row, out DataPage.RowSlot slot, out ReadOnlySpan<byte> bytes))
            throw new InvalidDataException($"Row {id.Page}:{id.Row} does not exist.");
        if (!slot.HasOverflow) return bytes.ToArray();

        return DataPage.ResolveRelocation(_channel, _table.DefinitionPage, slot, bytes).Bytes.ToArray();
    }

    /// <summary>
    /// Reclaims the LVAL pages of a replaced/deleted long value. A <b>chained</b> value owns dedicated pages
    /// (one chunk per page): each is freed to the global map and cleared from the column's owned/free maps.
    /// Inline (0x80) values have no pages; a single-page (0x40) value shares its page, which goes back only with
    /// the last value on it (<see cref="ReleasePackedValue"/>).
    /// </summary>
    /// <remarks>
    /// Measured against ACE on one connection: a chained value's pages are set in the global free map at once,
    /// whether an UPDATE replaced the value or a DELETE removed its row. A packed value's page an UPDATE or a DELETE
    /// empties is held until close (<paramref name="releaseAtClose"/>, see <see cref="PageAllocator.Release"/>).
    /// </remarks>
    private void FreeLongValue(ColumnDef column, byte[] descriptor, bool releaseAtClose)
    {
        // The descriptor comes off the row's variable chunk, so its width is whatever the offset table said, and
        // it is read — and checked — exactly as LongValueStore reads it: a short chunk would otherwise index past
        // the end and escape Delete/Update as IndexOutOfRangeException.
        (_, LongValueStore.StorageKind storage, int valueRow, int valuePage, _) = LongValueStore.Read(descriptor, _channel.Format);
        // An inline value keeps its payload in the row, so there is nothing to give back.
        if (storage == LongValueStore.StorageKind.Inline) return;

        TableDefinition definition = LongValueMaps;
        definition.LongValueOwnedMaps.TryGetValue(column.ColumnId, out (int Row, int Page) owned);
        definition.LongValueFreeMaps.TryGetValue(column.ColumnId, out (int Row, int Page) free);

        // A single-page value shares its page with other values, so the page goes back only once the last of
        // them is gone — until then just its own row is retired.
        if (storage == LongValueStore.StorageKind.SinglePage)
        {
            ReleasePackedValue(column, valuePage, valueRow, owned, free, releaseAtClose);
            return;
        }
        var reader = new LongValueStore(_channel);
        _ = reader.ResolveWithPages(descriptor, out IReadOnlyList<int> pages);
        var maps = new UsageMap(_channel, _table);
        HashSet<int> ownedPages = maps.PagesInMap(owned.Row, owned.Page).ToHashSet();
        if (free.Page != 0) _ = maps.PagesInMap(free.Row, free.Page); // validate both mutation targets before the first free
        foreach (int page in pages)
            if (!ownedPages.Contains(page))
                throw new InvalidDataException(
                    $"Long-value page {page} is not present in column '{column.Name}'s owned-pages map.");

        // Validation above completes before the first free-map or page mutation, so a malformed chain cannot
        // partially free. The writes themselves are atomic under the caller's transaction (the engine opens
        // one per statement); a direct Core caller without one gets no more than any other multi-page write.
        foreach (int page in pages)
            ReleaseLongValuePage(page, owned, free, releaseAtClose: false);
    }

    /// <summary>Gives a long-value page back — held until close or free at once — and clears it from the
    /// column's owned map and, where the column has one, its free map.</summary>
    private void ReleaseLongValuePage(int page, (int Row, int Page) owned, (int Row, int Page) free, bool releaseAtClose)
    {
        if (releaseAtClose) _channel.Allocator.Release(page);
        else _channel.Allocator.Free(page);
        _usageMaps.SetBit(owned.Row, owned.Page, page, set: false);
        if (free.Page != 0) _usageMaps.SetBit(free.Row, free.Page, page, set: false);
    }

    /// <summary>
    /// Retires one value from a shared (single-page form) long-value page: its row becomes a <b>0-length
    /// deleted+overflow tombstone</b> and the page is re-laid, the surviving records packing from the page end
    /// in slot order so the freed space is reclaimed. When nothing live is left the page is given back — its
    /// type set to <see cref="PageType.ReleasedDataPage"/>, its bit cleared from the column's owned
    /// and free maps, and the page returned to the global allocator — unless the column has no free-pages map,
    /// where the released page stays owned and allocated.
    /// </summary>
    /// <remarks>
    /// Measured against ACE, deleting 4 of 12 rows whose 400-character memos shared one page, then all 12:
    /// <code>
    /// start   0x01 n=5 free=72   [3296,2496,1696,896,96]
    /// 4 gone  0x01 n=5 free=3272 [4096DO,4096DO,4096DO,4096DO,3296]   the survivor slid to the top
    /// all     0x09 n=5 free=4072 [4096DO x5]
    /// </code>
    /// This is one of the two routes to page type <c>0x09</c>; the other is an ordinary data page emptied by
    /// DELETE (see <see cref="Delete"/>). Chained values take neither: they own their pages outright and are
    /// freed below, leaving them at <c>0x01</c>, which is why no experiment that used a memo large enough to
    /// chain ever produced one.
    /// </remarks>
    private void ReleasePackedValue(ColumnDef column, int pageNumber, int row,
        (int Row, int Page) owned, (int Row, int Page) free, bool releaseAtClose)
    {
        JetFormatBase format = _channel.Format;
        if (pageNumber <= 0 || pageNumber >= _channel.PageCount)
            throw new InvalidDataException($"Long-value page pointer {pageNumber} is outside the file.");

        byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
        var holder = new DataPage();
        holder.Read(new PageBuffer(page, pageNumber), format);
        if (!holder.IsLongValuePage)
            throw new InvalidDataException($"Long-value pointer {pageNumber}:{row} targets a non-LVAL data page.");
        if (row < 0 || row >= holder.RowCount)
            throw new InvalidDataException(
                $"Long-value row pointer {pageNumber}:{row} is outside the page's 0..{holder.RowCount - 1} range.");
        if (holder.Rows[row].IsDeleted) return;   // already retired; freeing twice must not double-count

        // Every dead row — this one, and any already retired — becomes a 0-length deleted + overflow tombstone, as
        // ACE writes it.
        var records = new ReadOnlyMemory<byte>[holder.RowCount];
        var flags = new RowSlotFlags[holder.RowCount];
        for (int i = 0; i < holder.RowCount; i++)
        {
            bool dead = i == row || holder.Rows[i].IsDeleted;
            records[i] = dead ? default : page.AsSpan(holder.Rows[i].Offset, holder.Rows[i].Length).ToArray();
            flags[i] = dead ? RowSlotFlags.Deleted | RowSlotFlags.Overflow : DataPage.Flags(holder.Rows[i]);
        }
        DataPage.LayRows(page, format, records, flags); // shrinking, so it always fits

        bool emptied = records.All(r => r.Length == 0);
        if (emptied) DataPage.MarkReleased(page);
        _channel.WritePage(pageNumber, page);

        // A page that survives has room again, so it goes back into the column's free-pages map — the same
        // map TryAppend consults when looking for somewhere to pack the next small value. A column with no
        // free-pages map packs nothing, so there it simply stays owned.
        if (!emptied)
        {
            if (free.Page != 0) _usageMaps.SetBit(free.Row, free.Page, pageNumber, set: true);
            return;
        }

        // Without a free-pages map even an emptied page is kept: ACE marks it released but leaves it in the owned
        // map and allocated in the global one, and the column's next value takes a fresh page all the same
        // (measured). A chained value's pages are freed as usual — ACE does that for such a column too.
        if (free.Page == 0) return;
        ReleaseLongValuePage(pageNumber, owned, free, releaseAtClose);
    }

    /// <summary>Rejects the insert if a primary-key or WITH DISALLOW NULL index would get a Null in any of its
    /// columns — ACE refuses it on every write, not only when the index is built (verified).</summary>
    private void EnforceNonNullKeys(object?[] values, IReadOnlySet<int>? changedColumns = null)
    {
        foreach (IndexDef index in _table.Indexes.Where(i => (i.IsPrimaryKey || i.Required) && i.RootPage > 0))
        {
            if (changedColumns is not null && !index.Columns.Any(c => changedColumns.Contains(c.Column.Index))) continue;
            if (index.Columns.FirstOrDefault(c => values[c.Column.Index] is null or DBNull).Column is { } nullColumn)
                throw new InvalidOperationException(
                    $"Index or primary key cannot contain a Null value: column '{nullColumn.Name}' of "
                    + $"'{_table.Name}' is Null, and index '{index.Name}' does not allow it.");
        }
    }

    /// <summary>Rejects the insert if a UNIQUE or PRIMARY index would gain a duplicate key. A row with a
    /// null in any of a unique index's columns is skipped — Jet treats nulls as distinct, so a unique index
    /// allows multiple nulls (verified vs ACE). An insert's runs once its row is written, before its index entries,
    /// as ACE's does; the statement's transaction undoes the row.</summary>
    private void EnforceUniqueIndexes(object?[] values, int? excludePointer = null, IReadOnlySet<int>? changedColumns = null)
    {
        IndexTree? writer = null;
        foreach (IndexDef index in _table.RealIndexes.Where(i => i.IsUnique))
        {
            if (changedColumns is not null && !index.Columns.Any(c => changedColumns.Contains(c.Column.Index))) continue;
            if (HasNullKey(index, values)) continue; // nulls are distinct — multiple allowed
            writer ??= new IndexTree(_channel, _table);
            if (!writer.KeyExists(index, values, excludePointer)) continue;
            if (excludePointer is null) CountKeysBeforeRefusal(index, values);
            throw new ConstraintViolationException(
                $"Cannot {(excludePointer is null ? "insert into" : "update")} '{_table.Name}': a row with the same {(index.IsPrimaryKey ? "primary key" : "unique key")} " +
                $"already exists (index '{index.Name}').",
                index.Name,
                index.IsPrimaryKey);
        }
    }

    /// <summary>Adds the new row to every index B-tree (deduped by root page, since relationship
    /// indexes share a real index's data) so indexed lookups — and Access — find it.</summary>
    private void UpdateIndexes(object?[] values, RowId rowId)
    {
        var writer = new IndexTree(_channel, _table);
        foreach (IndexDef index in _table.RealIndexes)
        {
            // WITH IGNORE NULL: a row with a null in any indexed column is not added to this index.
            if (index.IgnoreNulls && HasNullKey(index, values)) continue;
            writer.AddEntry(index, values, rowId);
        }
    }

    /// <summary>
    /// The root pages of the indexes the row brings a key they do not hold yet — the ones whose unique-entry count
    /// advances (verified vs ACE: a key equal to one already in the index, collation included, adds nothing; a key
    /// whose last row was deleted counts again; a Null key counts like any other, except in an IGNORE NULL index,
    /// which does not hold it). A unique index is no exception: it may hold a key with a Null in it more than
    /// once, and a repeat adds nothing (measured, whole-file diff — WholeFileParityProbeTest: two more rows with a
    /// Null in a unique index's column left its count at 1). A unique key without a Null cannot be there already,
    /// so only one with a Null is looked up — unless the row is <paramref name="refusedAt"/> an index: then only the
    /// indexes ACE gives the key before refusing it count, the real indexes taken in order up to that one, or to an
    /// earlier one that refuses it first — a unique index holding the key, or a primary or required one meeting a
    /// Null (measured: a missing parent counts the primary key ahead of the relationship's index; a duplicate key
    /// counts nothing ahead of the primary key it fails at).
    /// </summary>
    private HashSet<int> IndexesGainingANewKey(object?[] values, IndexDef? refusedAt = null)
    {
        var gaining = new HashSet<int>();
        IndexTree? writer = null;
        foreach (IndexDef index in refusedAt is null ? _table.RealIndexes : _table.RealIndexes.OrderBy(i => i.RealIndexOrdinal))
        {
            bool nullKey = HasNullKey(index, values);
            if (refusedAt is not null
                && (index.RealIndexOrdinal >= refusedAt.RealIndexOrdinal || nullKey && (index.IsPrimaryKey || index.Required)))
                break;
            if (index.IgnoreNulls && nullKey) continue;
            if (index.IsUnique && !nullKey && refusedAt is null)
            {
                gaining.Add(index.RootPage);
                continue;
            }
            writer ??= new IndexTree(_channel, _table);
            if (!writer.KeyExists(index, values)) gaining.Add(index.RootPage);
            else if (index.IsUnique && !nullKey) break;
        }
        return gaining;
    }

    private static bool HasNullKey(IndexDef index, object?[] values) =>
        index.Columns.Any(c => values[c.Column.Index] is null or DBNull);

    /// <summary>Finds a data page with room for a row of the needed size, growing the table if none has.</summary>
    /// <remarks>
    /// Consults the <i>free</i>-pages map, not the owned-pages map. The two agree only while a table is
    /// small: once a page fills, Access clears its free bit (see <see cref="AllocateDataPage"/>), so the free
    /// map is normally just the page currently being appended to. Scanning the owned map instead would read
    /// every already-full page from disk on every insert — O(pages) per row, i.e. quadratic over a bulk load.
    /// </remarks>
    /// <summary>Rejects a record ACE would refuse to store, before any page is touched. Without this
    /// <see cref="FindPageWithRoom"/> finds nothing with room, allocates a fresh page that cannot hold it
    /// either, and the offset arithmetic then throws an ArgumentOutOfRangeException that says nothing about
    /// the cause — and for the 4061..4080 band it would not even throw, writing a row ACE cannot read back.
    /// See <see cref="JetFormatBase.MaxRecordSize"/>.</summary>
    private static void EnsureRecordFits(JetFormatBase format, byte[] record)
    {
        if (record.Length > format.MaxRecordSize)
            throw new InvalidOperationException(
                $"Record is too large: {record.Length} bytes, and a record holds at most {format.MaxRecordSize} "
                + "bytes excluding long values. Move the large columns to Memo/OLE, which live on their own pages.");
    }

    private (int PageNumber, byte[] Page) FindPageWithRoom(JetFormatBase format, int needed)
    {
        foreach (int pageNumber in new UsageMap(_channel, _table).FreeDataPages().ToList())
        {
            byte[] page = _channel.ReadPageShared(pageNumber).Span.ToArray();
            int freeSpace = DataPage.ReadFreeSpace(page, format);
            // Space is not the only limit: an index addresses a row by a one-byte slot number, so a page that
            // already holds MaxRowsPerPage rows has no addressable slot left however much room it has. Narrow rows
            // hit this long before they fill the page.
            int rowCount = DataPage.ReadRowCount(page, format);
            if (freeSpace >= needed && rowCount < format.MaxRowsPerPage)
                return (pageNumber, page);
            // Access clears a page from the free-pages map when an insert finds it too full and moves on, so only
            // the page being appended to stays marked free (page-05).
            UpdateUsageBit(format.TdefFreePagesOffset, pageNumber, set: false);
        }

        return AllocateDataPage(format, needed);
    }

    /// <summary>
    /// Grows the table by one data page — like Access does for the first insert into a fresh
    /// (data-page-less) table: takes a page from the global free-pages map, initialises it as an
    /// empty data page owned by this table, and records it in the table's owned-pages usage map, and in its
    /// free-pages map when the <paramref name="needed"/> bytes it is taken for leave room for as many again.
    /// </summary>
    private (int PageNumber, byte[] Page) AllocateDataPage(JetFormatBase format, int needed)
    {
        // Only reached when no page in the free map had room, and FindPageWithRoom has cleared each of those, so
        // the new page is the only one that can be marked free. (Verified against an ACE sequential fill: only the
        // last of six equally-full pages stays in the free map.)
        var owned = _channel.ReadPageShared(_table.DefinitionPage).ReadRecordPointer(format.TdefOwnedPagesOffset);
        int pageNumber = _channel.Allocator.Allocate(owned);

        byte[] page = DataPage.NewPage(format, (uint)_table.DefinitionPage);
        _channel.WritePage(pageNumber, page);

        UpdateUsageBit(format.TdefOwnedPagesOffset, pageNumber, set: true);
        // ACE marks the new tail free only if a second record as large would still fit once this one is on it
        // (verified: a first row of 2019 bytes, 2061 left, puts the page in the map; one of 2059, 2021 left, does not).
        if (DataPage.ReadFreeSpace(page, format) - needed >= needed)
            UpdateUsageBit(format.TdefFreePagesOffset, pageNumber, set: true);

        return (pageNumber, page);
    }

    /// <summary>Sets or clears the bit for <paramref name="targetPage"/> in the inline usage map
    /// referenced by the TDEF pointer at <paramref name="tdefPointerOffset"/> (row byte + 3-byte page).</summary>
    private void UpdateUsageBit(int tdefPointerOffset, int targetPage, bool set)
    {
        (int row, int page) = _channel.ReadPageShared(_table.DefinitionPage).ReadRecordPointer(tdefPointerOffset);
        _usageMaps.SetBit(row, page, targetPage, set);
    }


    /// <summary>
    /// Access stores a memo/OLE value <b>inline</b> only up to 64 bytes (Jackcess
    /// <c>MAX_INLINE_LONG_VALUE_SIZE</c>, the same for Jet3/Jet4); a larger value goes on its own LVAL
    /// page. Inlining a long value works for LibRed's own reader but Access rejects it (e.g. it opens the
    /// database yet fails to run a view whose subquery Expression is inlined). So for each memo/OLE column
    /// whose value exceeds the inline limit, write it to an LVAL page and substitute the 12-byte reference
    /// descriptor (short values, and pre-built descriptors from other callers, are left as-is to inline).
    /// </summary>
    /// <summary>
    /// The row as it is <b>stored</b>: a copy of the logical values in which every memo/OLE value too large to
    /// inline has become the descriptor naming its pages.
    /// </summary>
    /// <remarks>
    /// The copy is the point. A row has two representations — the values a caller holds, and the descriptors
    /// the record carries — and they are indistinguishable at the type level, since both live in an
    /// <c>object?[]</c>. Materialising in place made the caller's array silently change meaning halfway
    /// through a write, and everything that then read it got the wrong one: a delete keyed an index off a
    /// <c>byte[]</c> descriptor, an update moved an index entry using a descriptor as a key, a caller was left
    /// holding pages that had been freed. Two arrays makes the distinction one the code can state.
    /// </remarks>
    private object?[] ToStorageValues(object?[] values)
    {
        object?[] storage = (object?[])values.Clone();
        MaterializeLongValues(storage);
        return storage;
    }

    /// <inheritdoc cref="ToStorageValues"/>
    /// <param name="values">The row, its long values replaced by their descriptors as they are written.</param>
    /// <param name="replacingChains">For an UPDATE, the columns whose old value is chained. Before anything is
    /// written ACE takes a long-value page for each whose new value does not chain — null, inline or single-page —
    /// and writes nothing to it (measured); those pages are returned for the caller to free with the old chains.</param>
    private List<int> MaterializeLongValues(object?[] values, IReadOnlyCollection<int>? replacingChains = null)
    {
        var unwritten = new List<int>();
        int maxSinglePage = _channel.Format.LongValueMaxSinglePage;
        foreach (int column in replacingChains ?? [])
            if (!Chains(values[column])) unwritten.Add(_channel.Allocator.Allocate(materialize: false));

        // ACE writes a row's chained values first and its single-page values after, each in column order
        // (measured), so a single-page value in an earlier column takes the page after the chains, not before.
        LongValueStore? writer = null;
        foreach (bool chained in new[] { true, false })
            foreach (ColumnDef column in _table.Columns)
                if (column.Type is JetDataType.Memo or JetDataType.Ole && Chains(values[column.Index]) == chained)
                    values[column.Index] = MaterializeLongValue(column, values[column.Index], ref writer);
        return unwritten;

        // The storage form follows the uncompressed length, as MaterializeLongValue decides it.
        bool Chains(object? value) => value switch
        {
            string s => Encoding.Unicode.GetByteCount(s) > maxSinglePage,
            byte[] b => b.Length > maxSinglePage,
            _ => false,
        };
    }

    /// <summary>The storage form of one Memo/OLE value: a value over 64 bytes is written to an LVAL page and
    /// becomes its <see cref="LongValueStore.DescriptorValue"/>; anything else — a short value, null, a descriptor already
    /// built — comes back as it was, for the row codec to inline. For a caller re-laying a single column.</summary>
    internal object? MaterializeLongValue(ColumnDef column, object? value)
    {
        LongValueStore? writer = null;
        return MaterializeLongValue(column, value, ref writer);
    }

    private object? MaterializeLongValue(ColumnDef column, object? value, ref LongValueStore? writer)
    {
        byte[]? payload = value switch
        {
            string s => Encoding.Unicode.GetBytes(s), // memo: UTF-16LE
            byte[] b => b,                             // OLE: raw bytes
            _ => null,                                 // null, or an already-built LongValueStore.DescriptorValue
        };
        JetFormatBase format = _channel.Format;
        if (payload is null || payload.Length <= format.LongValueMaxInline) return value;

        // Compress before storing, but AFTER the inline test above and using the uncompressed length for
        // the single-page test below: ACE decides the storage form on the uncompressed size and applies
        // compression to whatever form results, never to a chained value (LongTextStorageAccessTests).
        // ACE places a compressed value by its uncompressed bytes and leaves them behind (LongValueStore.TryAppend).
        byte[]? uncompressed = null;
        if (payload.Length <= format.LongValueMaxSinglePage
            && value is string text
            && Types.JetTypeCodec.TryCompressText(column, text) is { } compressed)
            (uncompressed, payload) = (payload, compressed);

        writer ??= new LongValueStore(_channel);
        TableDefinition definition = LongValueMaps;
        definition.LongValueOwnedMaps.TryGetValue(column.ColumnId, out (int Row, int Page) owned);
        definition.LongValueFreeMaps.TryGetValue(column.ColumnId, out (int Row, int Page) free);
        return new LongValueStore.DescriptorValue(StoreLongValue(writer, payload, owned, free, uncompressed));
    }

    // A page is dropped from the free-pages map once it cannot hold a 256-byte value and its row-directory entry
    // (verified vs ACE, memo and OLE alike: a page left with 257 bytes free leaves the map, one with 258 stays).
    // Not the smallest long value a page could still take — anything over the inline limit is one — but ACE's
    // own cut-off, below which it stops offering the page.
    private const int MinLvalValue = 256;

    /// <summary>Rejects a caller-supplied value for a calculated column, as ACE does — it refuses both an
    /// INSERT naming one and an UPDATE setting one, with <i>"Cannot update 'x'; field not updateable."</i>
    /// Refusing beats ignoring: a caller who passes a value expects it stored, and quietly computing
    /// something else instead is the harder bug to find.
    /// <para>On INSERT the test is a value in the slot, because nothing else would put one there. On UPDATE
    /// it has to be <paramref name="changedColumns"/> — callers hand back the whole row they just read, so
    /// the slot legitimately still holds the cached value and its presence means nothing.</para></summary>
    private void RejectExplicitCalculatedValues(object?[] values, IReadOnlySet<int>? changedColumns)
    {
        foreach (ColumnDef column in _table.Columns)
        {
            // A write-once column takes its value on INSERT and refuses it on UPDATE, as ACE does (verified).
            if (column.IsWriteOnce && changedColumns?.Contains(column.Index) == true)
                throw new InvalidOperationException(
                    $"Cannot update '{column.Name}'; field not updateable. It is a write-once column, set only when "
                    + "its row is inserted.");
            if (!column.IsCalculated) continue;
            bool written = changedColumns is null
                ? values[column.Index] is not null
                : changedColumns.Contains(column.Index);
            if (written)
                throw new InvalidOperationException(
                    $"Cannot update '{column.Name}'; field not updateable. It is a calculated column, so its "
                    + "value comes from its expression and cannot be supplied.");
        }
    }

    /// <summary>A Byte written to a row takes the whole number's low byte, rounded half to even first, rather than
    /// refusing one past its range (verified vs ACE, for numbers and text alike: 256 stores 0, -1 255, 300 44 and
    /// 255.5 0). Done to the row's values before anything is encoded, so the row and every index key carry the
    /// same byte — and only on a write: a seek for 300 finds nothing, not the 44 a write of 300 stores.</summary>
    private void WrapBytes(object?[] values)
    {
        foreach (ColumnDef column in _table.Columns)
            if (column.Type == JetDataType.Byte && values[column.Index] is { } value and not (byte or bool))
                values[column.Index] = unchecked((byte)Convert.ToInt64(
                    Types.JetTypeCodec.NumberFromText(JetDataType.Byte, value), CultureInfo.InvariantCulture));
    }

    /// <summary>Writes an oversized calculated result to an LVAL page and returns its in-row descriptor.
    /// Handed to <see cref="RowCodec"/>, which derives the value and so is the only place that knows how
    /// big it turned out; the pages and their usage maps stay this class's business.</summary>
    private byte[] SpillCalculated(ColumnDef column, byte[] envelope)
        => StorePackedLongValue(column.ColumnId, envelope).Bytes;

    /// <summary>Stores <paramref name="payload"/> for long-value column <paramref name="columnId"/> and returns
    /// the in-row descriptor: inline up to 64 bytes, as any long value is, else on an LVAL page — packing onto a
    /// free page as usual. The MSysObjects <c>LvProp</c> property blob takes the same rule (verified: ACE inlines
    /// a 63-byte blob and puts a 65-byte one on a page). Call before <see cref="Insert(object?[], bool)"/> and put
    /// the descriptor in the row's slot for the column.</summary>
    public LongValueStore.DescriptorValue StorePackedLongValue(int columnId, byte[] payload)
    {
        JetFormatBase format = _channel.Format;
        if (payload.Length <= format.LongValueMaxInline)
            return new LongValueStore.DescriptorValue(Types.JetTypeCodec.EncodeInlineLongValue(payload, format));

        TableDefinition definition = ReadDefinition();
        definition.LongValueOwnedMaps.TryGetValue(columnId, out (int Row, int Page) owned);
        definition.LongValueFreeMaps.TryGetValue(columnId, out (int Row, int Page) free);
        return new LongValueStore.DescriptorValue(StoreLongValue(new LongValueStore(_channel), payload, owned, free));
    }

    /// <summary>
    /// Writes one long value to LVAL page(s) and returns its in-row descriptor, maintaining the column's
    /// §3.3.2 usage maps. A value up to one page is <b>packed</b> onto an existing free page (a page in the
    /// free-pages map with room), the way Access shares a page across many small values; only when none has
    /// room is a fresh page allocated (owned + free). A value larger than one page is chained across
    /// dedicated pages.
    /// <para>A column with an owned-pages map and <b>no free-pages map</b> (a free pointer to page 0) never shares
    /// a page: each value takes a fresh one, recorded in the owned map alone. Access writes <c>MSysNameMap.NameMap</c>
    /// and <c>MSysAccessXML.LValue</c> that way, and packs nothing into them — measured across every such file,
    /// one LVAL page per stored value.</para>
    /// </summary>
    private byte[] StoreLongValue(LongValueStore writer, byte[] payload, (int Row, int Page) owned, (int Row, int Page) free,
        byte[]? uncompressed = null)
    {
        if (owned.Page == 0)
            throw new InvalidDataException("Long-value column has no owned-pages usage-map pointer.");
        var maps = new UsageMap(_channel, _table);
        _ = maps.PagesInMap(owned.Row, owned.Page); // validate both map targets before allocating or writing LVAL pages
        IReadOnlyList<int> freePages = free.Page == 0 ? [] : [.. maps.PagesInMap(free.Row, free.Page)];

        JetFormatBase format = _channel.Format;
        if (payload.Length > format.LongValueMaxSinglePage)
        {
            (byte[] Descriptor, IReadOnlyList<int> OwnedPages, int FreePage) chained = writer.Write(payload);
            foreach (int page in chained.OwnedPages) _usageMaps.SetBit(owned.Row, owned.Page, page, set: true);
            if (chained.FreePage != 0 && free.Page != 0)
                _usageMaps.SetBit(free.Row, free.Page, chained.FreePage, set: true);
            return chained.Descriptor;
        }

        // Pack onto the first free page that has room for the value plus its directory entry.
        if (free.Page != 0)
            foreach (int page in freePages)
                if (writer.TryAppend(page, payload, uncompressed) is (int row, int remaining))
                {
                    if (remaining < MinLvalValue + format.DataRowDirectoryEntrySize)
                        _usageMaps.SetBit(free.Row, free.Page, page, set: false); // now full
                    return LongValueStore.Descriptor(format, payload.Length, LongValueStore.StorageKind.SinglePage, page, row);
                }

        // No free page had room: a fresh page (owned, and free — it still has spare room).
        int newPage = writer.WriteNewPage(payload, uncompressed);
        _usageMaps.SetBit(owned.Row, owned.Page, newPage, set: true);
        if (free.Page != 0)
            _usageMaps.SetBit(free.Row, free.Page, newPage, set: true);
        return LongValueStore.Descriptor(format, payload.Length, LongValueStore.StorageKind.SinglePage, newPage);
    }

    /// <summary>
    /// The table's definition, read once and kept — for the <b>long-value map pointers</b> only, which are what
    /// every caller of this wants and which no row write can move.
    /// </summary>
    /// <remarks>
    /// Freeing or storing a long value writes the map holder page and the global map;
    /// <see cref="UsageMap"/> never writes the definition page, and an inline-to-reference conversion
    /// rewrites the record inside the same map row. So the §3.3.2 pointers are fixed for the life of this
    /// inserter, where the row count and AutoNumber high-water on the same page are not — read those from the
    /// page, never from here.
    /// </remarks>
    private TableDefinition LongValueMaps => _longValueMaps ??= ReadDefinition();

    private TableDefinition? _longValueMaps;

    private TableDefinition ReadDefinition()
    {
        var definition = new TableDefinition();
        definition.Read(_channel, _table.DefinitionPage);
        return definition;
    }

    /// <summary>
    /// Fills each AutoNumber column the caller left null with the next id — the TDEF high-water value
    /// (`0x14`) plus the increment — matching how Jet assigns AutoNumbers. A value the caller supplied
    /// explicitly is left untouched (Jet allows it, and <see cref="UpdateTdefCounters"/> then bumps
    /// the high-water to it). Access permits only one AutoNumber column per table, but any number are
    /// handled here for safety. Returns a per-value flag array marking the ids this call generated
    /// (null when it generated none), which <see cref="UpdateTdefCounters"/> uses to tell a generated
    /// id from a caller-supplied one.
    /// </summary>
    private bool[]? AssignAutoNumbers(JetFormatBase format, object?[] values)
    {
        bool needed = false;
        foreach (ColumnDef column in _table.Columns)
            if (column.IsAutoNumber && values[column.Index] is null or DBNull) { needed = true; break; }
        if (!needed) return null;

        ReadOnlySpan<byte> tdef = _channel.ReadPageShared(_table.DefinitionPage).Span;
        int highWater = TableDefinition.ReadLastAutoNumber(tdef, format);

        // A complex (multi-value / attachment) column is an AutoNumber too, but it draws on its own counter
        // at 0x1C and there is ONE id per row shared by every complex column of the table — the counter is a
        // single TDEF word, not one per column (verified: complex1.accdb's Table1 has four attachment columns
        // and each record carries the same id in all four). So it is assigned once here, outside the 0x14
        // loop below, which must skip these columns or they would consume the table's ordinary counter.
        AssignComplexId(format, values, tdef);

        bool[] generated = new bool[values.Length];
        foreach (ColumnDef column in _table.Columns)
            if (column.IsAutoNumber && column.Type != JetDataType.Complex && values[column.Index] is null or DBNull)
            {
                if (column.IsRandomAutoNumber)
                    // "Random" AutoNumber (DefaultValue = GenUniqueID()): a random Int32, independent of the
                    // sequential counter. Access relies on the PK's uniqueness to reject the rare collision.
                    values[column.Index] = RandomAutoNumber();
                else
                {
                    // Next id = last-assigned + increment. On a fresh table the last value (0x14) is
                    // Seed-Increment, so the first assigned id is the Seed. The addition is deliberately
                    // unchecked: past int.MaxValue the counter wraps to int.MinValue and keeps going, which
                    // is exactly what ACE does (no overflow error exists — see AceAutoNumberOverflowProbeTest).
                    values[column.Index] = highWater = unchecked(highWater + column.Increment);
                    generated[column.Index] = true;
                    _channel.KeepThroughRollback(_table.DefinitionPage, format.TdefLastAutoNumberOffset, highWater);
                }
            }
        return generated;
    }

    /// <summary>
    /// Gives the row its complex id — one value from the table's <c>0x1C</c> high-water, written into every
    /// complex column the caller left unset. The id identifies the <b>record</b>, so all of the table's
    /// complex columns share it; each column's values are then the flat-table rows carrying it.
    /// </summary>
    /// <remarks>An id is allocated whether or not any value follows — that is what Access does, and it is why
    /// a non-null complex id is no evidence that the record holds anything. A caller-supplied id is left
    /// alone, as for any AutoNumber.</remarks>
    private void AssignComplexId(JetFormatBase format, object?[] values, ReadOnlySpan<byte> tdef)
    {
        var complex = _table.Columns
            .Where(c => c.Type == JetDataType.Complex && values[c.Index] is null or DBNull)
            .ToList();
        if (complex.Count == 0) return;

        int next = unchecked(TableDefinition.ReadComplexAutoNumber(tdef, format) + 1);
        foreach (ColumnDef column in complex) values[column.Index] = next;
        _channel.KeepThroughRollback(_table.DefinitionPage, format.TdefComplexAutoNumberOffset, next);
    }

    /// <summary>A random non-zero signed Int32 for a "Random" AutoNumber, mirroring Access's <c>GenUniqueID()</c>.</summary>
    private static int RandomAutoNumber()
    {
        int value;
        do { value = Random.Shared.Next(int.MinValue, int.MaxValue); } while (value == 0);
        return value;
    }

    /// <summary>
    /// Updates the TDEF counters Access maintains on insert, in one read-modify-write of the TDEF
    /// page:
    /// <list type="bullet">
    /// <item>Row count (`0x10`) — incremented.</item>
    /// <item>AutoNumber high-water (`0x14`) — set to the id just written whenever that id advances the
    /// counter: always for an id this insert generated (including the wrap past int.MaxValue), and for a
    /// caller-supplied id only when it moves further in the increment's direction. (Access reads `0x14`
    /// to pick the *next* id = this + increment; leaving it stale makes Access reissue an existing id and
    /// reject the insert as a duplicate primary key.)</item>
    /// <item>Per-index **unique-entry count** (`0x3F + ordinal×12`, `+4`) — incremented by one for
    /// each index in <paramref name="newKeys"/> (the row brings a key it does not hold yet). Access advances it on insert only,
    /// once per real index, and never decrements it. The sibling **total-entry count**
    /// (`+0`) is deliberately left untouched **on insert**: Access does not raise it there — it is written
    /// when the index is built (to its entries, in <c>SchemaEditor</c>'s back-fill) or the file compacted
    /// (verified: an index created on an empty table reads total `0` through any number of inserts).
    /// <b>Delete is not the mirror of this</b> — ACE drops the total by one on every real index when a row
    /// goes, so the figure drifts downward across insert/delete cycles in ACE as much as here. Measured, and
    /// matched, in <see cref="Delete"/>.</item>
    /// </list>
    /// </summary>
    private void UpdateTdefCounters(JetFormatBase format, object?[] values, bool[]? generatedAutoNumbers, HashSet<int> newKeys)
    {
        byte[] tdef = _channel.ReadPageShared(_table.DefinitionPage).Span.ToArray();

        TableDefinition.WriteRowCount(tdef, format, TableDefinition.ReadRowCount(tdef, format) + 1);

        // The record's complex id rides its own counter at 0x1C, and every complex column of the table holds
        // the one id, so the high-water moves once. Deleting a row never rolls it back (verified vs ACE —
        // ComplexDeleteCascadeProbeTest), which is the same high-water rule 0x14 follows.
        if (_table.Columns.FirstOrDefault(c => c.Type == JetDataType.Complex) is { } complexColumn
            && values[complexColumn.Index] is { } complexValue)
        {
            int assignedId = Convert.ToInt32(complexValue, CultureInfo.InvariantCulture);
            if (assignedId > TableDefinition.ReadComplexAutoNumber(tdef, format))
                TableDefinition.WriteComplexAutoNumber(tdef, format, assignedId);
        }

        foreach (ColumnDef column in _table.Columns)
        {
            if (!column.IsAutoNumber || values[column.Index] is not { } value) continue;
            // Complex columns are AutoNumbers on their own 0x1C counter, handled above; they must not touch
            // the table's ordinary one or a row would burn an id from both.
            if (column.Type == JetDataType.Complex) continue;
            // A "Random" AutoNumber leaves the high-water at its default (Access ignores 0x14 for it — verified:
            // a UI-authored Random AutoNumber reads last-value 0). Advancing it would be meaningless (random ids
            // don't form a monotone sequence) and diverge from Access's on-disk state.
            if (column.IsRandomAutoNumber) continue;
            int assigned = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            int highWater = TableDefinition.ReadLastAutoNumber(tdef, format);
            // An id this insert *generated* always becomes the new high-water: it came from 0x14 + increment,
            // so it is by construction the next value in the sequence. That includes the wrap past
            // int.MaxValue (or int.MinValue for a descending counter), where the new id compares as going
            // backwards yet is the correct continuation — ACE wraps and carries on, and the monotone rule
            // below would instead pin 0x14 forever and reissue the same wrapped id on every later insert
            // (a wedged table). Verified both sides in AceAutoNumberOverflowProbeTest.
            //
            // For a value the *caller* supplied explicitly, advance 0x14 only when it moves further in the
            // counter's direction — for a positive increment that's the max seen, for a negative (descending)
            // counter the min. Using max unconditionally would let a descending counter reissue the previous
            // id (duplicate key). NB deliberate divergence from Access: ACE seeds 0x14 from the *last
            // inserted* value regardless of direction (KB 884185), so an explicit lower INSERT drops the
            // counter and the next auto id collides with an existing row ("duplicate values in the
            // index/primary key"). LibRed's monotone rule is immune — verified both sides in
            // AutoNumberSeed[Immunity]Tests.
            bool advances = generatedAutoNumbers?[column.Index] == true
                || (column.Increment >= 0 ? assigned > highWater : assigned < highWater);
            int newHighWater = advances ? assigned : highWater;
            if (advances)
                TableDefinition.WriteLastAutoNumber(tdef, format, assigned);
            // Keep the cached catalog seed in sync with the on-disk 0x14. The insert path itself reads the
            // high-water from disk (AssignAutoNumbers), so this isn't needed for assigning ids — but anything that
            // reconstructs the counter from the cached ColumnDef.Seed would, left stale, reset it to its
            // create-time value after the high rows were deleted (seen once: next id dropped to 1 instead of
            // continuing past 6). Seed = next id.
            column.Seed = unchecked(newHighWater + column.Increment);
        }

        WriteNewKeyCounts(tdef, newKeys);
    }

    /// <summary>Counts a new key on each real index in <paramref name="newKeys"/> and writes the definition page.</summary>
    private void WriteNewKeyCounts(byte[] tdef, HashSet<int> newKeys)
    {
        JetFormatBase format = _channel.Format;
        // One count per real index, so a relationship's logical index sharing a real one does not advance it twice.
        Span<int> seen = stackalloc int[_table.Indexes.Count];
        int count = 0;
        foreach (IndexDef index in _table.Indexes)
        {
            if (seen[..count].Contains(index.RealIndexOrdinal)) continue;
            seen[count++] = index.RealIndexOrdinal;
            if (!newKeys.Contains(index.RootPage)) continue; // excluded from the index, or its key already there
            (int total, int unique) = TableDefinition.ReadIndexCounts(tdef, format, index.RealIndexOrdinal);
            TableDefinition.WriteIndexCounts(tdef, format, index.RealIndexOrdinal, total, unique + 1);
        }

        WriteDefinitionCounters(tdef);
    }

    /// <summary>Writes the table's definition page once a row has changed its counters, which a rollback then keeps
    /// as ACE keeps them (measured): a statement that fails leaves every table's row and index counts as the rows it
    /// accepted took them (<see cref="PageChannel.AcceptCounters"/>), with a refused row's counting up to the index
    /// that refused it; a ROLLBACK takes an ordinary table's back with the rows, and MSysRelationships', but not
    /// MSysObjects' or MSysACEs' — a rolled-back CREATE TABLE's catalog rows go, their counting stays.</summary>
    private void WriteDefinitionCounters(ReadOnlySpan<byte> tdef)
    {
        _channel.WritePage(_table.DefinitionPage, tdef);
        JetFormatBase format = _channel.Format;
        bool failedOnly = !(_table.IsSystem && (_table.Name.Equals("MSysObjects", StringComparison.OrdinalIgnoreCase)
            || _table.Name.Equals("MSysACEs", StringComparison.OrdinalIgnoreCase)));
        _channel.KeepThroughRollback(_table.DefinitionPage, format.TdefRowCountOffset, TableDefinition.ReadRowCount(tdef, format), failedOnly);
        Span<int> seen = stackalloc int[_table.Indexes.Count];
        int count = 0;
        foreach (IndexDef index in _table.Indexes)
        {
            int ordinal = index.RealIndexOrdinal;
            if (seen[..count].Contains(ordinal)) continue;
            seen[count++] = ordinal;
            int entry = TableDefinition.RealIndexEntry(format, ordinal);
            (int total, int unique) = TableDefinition.ReadIndexCounts(tdef, format, ordinal);
            _channel.KeepThroughRollback(_table.DefinitionPage, entry + format.RealIndexRowCountOffset, total, failedOnly);
            _channel.KeepThroughRollback(_table.DefinitionPage, entry + format.RealIndexUniqueCountOffset, unique, failedOnly);
        }
    }

    /// <summary>
    /// Counts what ACE counts of a row it refuses at <paramref name="refusing"/> — the new keys it gave the indexes
    /// ahead of that one (<see cref="IndexesGainingANewKey"/>) — and keeps the count, as a failed statement keeps
    /// its counters.
    /// </summary>
    public void CountKeysBeforeRefusal(IndexDef refusing, object?[] values)
    {
        HashSet<int> gaining = IndexesGainingANewKey(values, refusing);
        if (gaining.Count == 0) return;
        WriteNewKeyCounts(_channel.ReadPageShared(_table.DefinitionPage).Span.ToArray(), gaining);
        _channel.AcceptCounters();
    }
}
