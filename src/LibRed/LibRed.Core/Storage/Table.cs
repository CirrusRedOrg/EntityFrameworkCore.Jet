using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// An opened table: pairs a <see cref="TableDefinition"/> with the means to read its rows.
/// The primary entry point for scanning data out of the storage layer.
/// </summary>
public sealed class Table
{
    internal Table(PageChannel channel, TableDefinition definition, JetCatalog catalog)
    {
        Channel = channel;
        _catalog = catalog;
        Definition = definition;
        UsageMap = new UsageMap(channel, definition);
    }

    internal PageChannel Channel { get; }
    private readonly JetCatalog _catalog;
    public TableDefinition Definition { get; }
    internal UsageMap UsageMap { get; }

    // The buffer this table's updated rows are built in (Update), made on the first.
    private byte[]? _updateBuffer;

    public string Name => Definition.Name;

    /// <summary>Returns a forward-only cursor over all rows in the table.</summary>
    /// <param name="decode">Which columns to decode, by <see cref="ColumnDef.Index"/>, or null for all; a column
    /// left out reads as null. For a reader that never looks at it — see <see cref="RowCodec"/>.</param>
    public TableCursor Rows(bool[]? decode = null) => new(this, decode);

    /// <summary>A decode mask for <see cref="Rows"/> and the seeks that reads only <paramref name="columns"/>
    /// (by <see cref="ColumnDef.Index"/>): for a reader that looks at nothing else, such as a key comparison,
    /// which would otherwise decode every other column of every row it passes over for nothing.</summary>
    public bool[] DecodeOnly(IEnumerable<int> columns)
    {
        var mask = new bool[Definition.Columns.Count];
        foreach (int column in columns) mask[column] = true;
        return mask;
    }

    /// <summary>The rows whose <paramref name="keyColumns"/> satisfy <paramref name="match"/>, each read in full.
    /// The search decodes only the key; a match is read again whole, because a caller looking a row up by key
    /// is usually about to rewrite or delete it, which takes every value. Lazy, as <see cref="Rows"/> is.</summary>
    public IEnumerable<(RowId Id, object?[] Values)> RowsWhere(IEnumerable<int> keyColumns, Func<object?[], bool> match)
    {
        foreach ((RowId id, object?[] key) in Rows(DecodeOnly(keyColumns)).WithIds())
            if (match(key))
                yield return (id, GetRow(id)
                    ?? throw new InvalidDataException($"Row {id.Page}:{id.Row} of '{Name}' was read a moment ago and is gone."));
    }

    /// <summary>The rows whose <paramref name="columns"/> hold <paramref name="key"/> (position for position),
    /// each read in full — seeked through a usable index on exactly those columns when the table has one, and found
    /// by <see cref="RowsWhere"/>'s scan otherwise. <paramref name="match"/> decides every row either way:
    /// an index key is lossy (text folds case and trailing spaces), so the seek only narrows.</summary>
    /// <remarks>The key has to be in each column's own kind already, because an index answers only in its
    /// column's kind; a caller that cannot promise that uses <see cref="RowsWhere"/>. A null in the key scans,
    /// since how an index keys a null is not the question a key comparison asks.</remarks>
    public IEnumerable<(RowId Id, object?[] Values)> RowsWithKey(
        int[] columns, object?[] key, Func<object?[], bool> match)
    {
        IndexDef? index = key.Any(k => k is null) ? null : Definition.Indexes.FirstOrDefault(i =>
            i.RootPage > 0 && i.Columns.Count == columns.Length && i.Columns.All(c => columns.Contains(c.Column.Index)
                && (c.Column.Type is not (JetDataType.Text or JetDataType.Memo) || c.Column.Collation.IsIndexKeyEncodable)));
        if (index is null)
            return RowsWhere(columns, match);

        // A seek key is addressed by column ordinal, not by position in the index.
        var seekKey = new object?[Definition.Columns.Count];
        for (int i = 0; i < columns.Length; i++)
            seekKey[columns[i]] = key[i];
        return SeekRowsWithIds(index, seekKey).Where(r => match(r.Values));
    }

    /// <summary>A row decoder over this table's columns — reuse one across a seek/scan rather than allocating
    /// per row (each carries a shared <see cref="LongValueStore"/>).</summary>
    private RowCodec NewDecoder(bool[]? decode = null) =>
        new(Definition.Columns, Channel.Format, longValues: new LongValueStore(Channel), decode: decode);

    /// <summary>The decoder seeks and unmasked row lookups read with: the last one made, while it was made for the same column
    /// mask (the same array — a caller works one out and passes it to every seek), else a new one.</summary>
    /// <remarks>An index-nested-loop join seeks once per outer row, and building a decoder each time — its column
    /// array copied, a long-value reader made — was a sixth of what such a join allocated. A decoder holds nothing
    /// that changes as it decodes, so one serves every seek, interleaved or not. The mask and its decoder are held
    /// as one object, so no reader can pair one with the other's partner.</remarks>
    private RowCodec SeekDecoder(bool[]? decode)
    {
        if (_seekDecoder is { } held && ReferenceEquals(held.Mask, decode)) return held.Decoder;
        RowCodec decoder = NewDecoder(decode);
        _seekDecoder = new MaskedDecoder(decode, decoder);
        return decoder;
    }

    private sealed record MaskedDecoder(bool[]? Mask, RowCodec Decoder);

    private MaskedDecoder? _seekDecoder;

    /// <summary>The index reader every seek goes through, made once: seeking changes nothing about it.</summary>
    private IndexTree IndexReader => _indexReader ??= new IndexTree(Channel, Definition);

    private IndexTree? _indexReader;

    /// <summary>Decodes the row at <paramref name="id"/> (following an overflow forward-pointer to a
    /// relocated row), or <see langword="null"/> if the slot is empty/deleted. Used by an index seek, which
    /// yields row ids. <paramref name="decode"/> is as for <see cref="Rows"/>.</summary>
    public object?[]? GetRow(RowId id, bool[]? decode = null) =>
        GetRow(id, decode is null ? SeekDecoder(null) : NewDecoder(decode));

    private object?[]? GetRow(RowId id, RowCodec decoder)
    {
        if (id.Page <= 0 || id.Page >= Channel.PageCount)
            throw new InvalidDataException(
                $"Row pointer {id.Page}:{id.Row} is outside the file's 1..{Channel.PageCount - 1} page range.");

        // Read just the one wanted slot straight from the page directory (O(1)), over the shared cache buffer
        // without copying the 4 KB page out — the bytes are consumed immediately by Decode. Both were the
        // seek's per-row hot cost.
        PageBuffer page = Channel.ReadPageShared(id.Page);
        if (!Pages.DataPage.TryReadRow(page, Channel.Format, id.Row, out DataPage.RowSlot slot, out ReadOnlySpan<byte> bytes))
            return null;

        uint owner = DataPage.ReadOwner(page.Span, Channel.Format);
        if (owner != (uint)Definition.DefinitionPage)
            throw new InvalidDataException($"Row page {id.Page} belongs to TDEF {owner}, not TDEF {Definition.DefinitionPage}.");

        if (slot.IsDeleted) return null;
        if (slot.HasOverflow)
        {
            DataPage.RelocatedRow target = DataPage.ResolveRelocation(
                Channel, Definition.DefinitionPage, slot, bytes);
            return decoder.Decode(target.Bytes);
        }
        return decoder.Decode(bytes);
    }

    /// <summary>Yields the rows whose <paramref name="index"/> key equals <paramref name="values"/> — an index
    /// seek (equality) instead of a full scan. May over-return (lossy text/binary keys); the caller re-checks
    /// the predicate. <paramref name="decode"/> is as for <see cref="Rows"/>.</summary>
    public IEnumerable<object?[]> SeekRows(IndexDef index, object?[] values, bool[]? decode = null)
    {
        RowCodec decoder = SeekDecoder(decode);
        foreach (RowId id in IndexReader.Seek(index, values))
            if (GetRow(id, decoder) is { } row)
                yield return row;
    }

    /// <summary>Like <see cref="SeekRows"/> but yields each matching row together with its <see cref="RowId"/> —
    /// for an UPDATE/DELETE join that must know which physical row to rewrite/remove, not just its values.
    /// <paramref name="decode"/> is as for <see cref="Rows"/>: a row that is then written has to be read again
    /// in full.</summary>
    public IEnumerable<(RowId Id, object?[] Values)> SeekRowsWithIds(IndexDef index, object?[] values, bool[]? decode = null)
    {
        RowCodec decoder = SeekDecoder(decode);
        foreach (RowId id in IndexReader.Seek(index, values))
            if (GetRow(id, decoder) is { } row)
                yield return (id, row);
    }

    /// <summary>Yields the rows whose <paramref name="index"/> key lies in [<paramref name="low"/>,
    /// <paramref name="high"/>] (either bound null = open) — an index range scan. May over-return at the
    /// boundaries; the caller re-checks the predicate. <paramref name="decode"/> is as for <see cref="Rows"/>.</summary>
    public IEnumerable<object?[]> SeekRangeRows(IndexDef index, object?[]? low, object?[]? high, bool[]? decode = null)
    {
        RowCodec decoder = SeekDecoder(decode);
        foreach (RowId id in IndexReader.SeekRange(index, low, high))
            if (GetRow(id, decoder) is { } row)
                yield return row;
    }

    /// <summary>Inserts a row (values aligned to column <see cref="ColumnDef.Index"/>) into the table.</summary>
    /// <param name="values">The row's values.</param>
    /// <param name="defaults">The table's all-defaults record, which a statement's row is written over, and
    /// <paramref name="order"/>, the columns the statement names in its own order — together what decides the
    /// record's slack (spec §5). Both null for a row written with no defaults.</param>
    /// <param name="order">See <paramref name="defaults"/>.</param>
    /// <param name="validate">The caller's own checks of the row, run once its ids are drawn and before anything is
    /// written — so a row they refuse still uses up its AutoNumber, as ACE's does (measured).</param>
    /// <param name="reference">The caller's check of the row's references to other tables' rows, run once the row is
    /// written and before its keys go into the indexes, where ACE refuses a missing parent (measured: the refused row
    /// has taken its page).</param>
    public void Insert(object?[] values, object?[]? defaults = null, IReadOnlyList<int>? order = null,
        Action<object?[]>? validate = null, Action<object?[]>? reference = null) =>
        Write(() => new RowInserter(Channel, Definition).Insert(values, updateIndexes: true, defaults, order, validate, reference));

    /// <summary>Every column but the calculated ones — what rewriting or deleting a row reads of it. A calculated
    /// result is never a key and never the caller's to change, and the rewrite carries it over or recomputes it
    /// from the stored bytes; reading it would only refuse a row whose result cannot be read, which ACE still
    /// updates and deletes.</summary>
    // Private and invariant for this table definition, so writes can share its decoder too. A caller's
    // mutable mask passed to GetRow still creates a fresh decoder on every call.
    private bool[] StoredColumns => field ??= DecodeOnly(Definition.Columns.Where(c => !c.IsCalculated).Select(c => c.Index));

    /// <summary>Rewrites the row at <paramref name="id"/> in place with new values (row id preserved).
    /// <paramref name="changedColumns"/> are the columns that actually changed — an unchanged memo/OLE column
    /// keeps its stored descriptor (no re-materialise), a changed one has its old LVAL pages reclaimed. Every row
    /// this table object updates is built in one buffer, as ACE builds a statement's (RowCodec's <c>Encode</c>); a
    /// statement opens its own.</summary>
    public void Update(RowId id, object?[] values, IReadOnlySet<int> changedColumns)
    {
        Write(() =>
        {
            bool[] stored = StoredColumns;
            object?[] original = GetRow(id, SeekDecoder(stored)) ?? throw new InvalidOperationException($"Row '{id}' does not exist.");
            var changed = new HashSet<int>(changedColumns);
            for (int i = 0; i < values.Length; i++)
                if (i < stored.Length && !stored[i]) continue;   // a calculated result: never read
                else if (original[i] is byte[] oldBytes && values[i] is byte[] newBytes
                    ? !oldBytes.AsSpan().SequenceEqual(newBytes) : !Equals(original[i], values[i]))
                    changed.Add(i);

            new RowInserter(Channel, Definition).Update(id, values, changed,
                _updateBuffer ??= new byte[Channel.Format.PageSize]);
            foreach (IndexDef index in Definition.RealIndexes
                .Where(i => i.Columns.Any(c => changed.Contains(c.Column.Index))))
                MoveIndexEntry(index, original, values, id);
        });
    }

    /// <summary>Rewrites the row treating every column as changed (materialises all long values).</summary>
    public void Update(RowId id, object?[] values) =>
        Update(id, values, new HashSet<int>(System.Linq.Enumerable.Range(0, values.Length)));

    /// <summary>Moves a row's entry in one index when its key changes (remove old key, add new; row id
    /// unchanged), and counts the move in the index's statistics as ACE does. Used by UPDATE of an indexed
    /// column.</summary>
    internal void MoveIndexEntry(IndexDef index, object?[] oldValues, object?[] newValues, RowId id)
    {
        new IndexTree(Channel, Definition).MoveEntry(index, oldValues, newValues, id);
        // An IGNORE NULL index the row was absent from never counted it, so has nothing to count out.
        if (!(index.IgnoreNulls && IndexTree.HasNullKey(index, oldValues)))
            new RowInserter(Channel, Definition).CountKeyMoved(index);
    }

    /// <summary>Whether <paramref name="values"/>' key already exists in <paramref name="index"/> for a row
    /// other than <paramref name="excludeRow"/> — used to enforce a UNIQUE/PRIMARY index on UPDATE.</summary>
    public bool HasDuplicateKey(IndexDef index, object?[] values, RowId excludeRow) =>
        new IndexTree(Channel, Definition).KeyExists(index, values, excludeRow.Packed);

    /// <summary>Soft-deletes the row at <paramref name="id"/> (row bytes kept, slot flagged; TDEF row count
    /// decremented), removing every index entry before reclaiming the row.</summary>
    public void Delete(RowId id)
    {
        Write(() =>
        {
            object?[] values = GetRow(id, StoredColumns) ?? throw new InvalidOperationException($"Row '{id}' does not exist.");
            foreach (ComplexColumn complex in _catalog.LoadComplexColumns(Definition.DefinitionPage))
            {
                if (values[complex.OwnerTable.RequireColumn(complex.ColumnName).Index] is not { } raw) continue;
                int recordId = Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
                var flat = new Table(Channel, complex.FlatTable, _catalog);
                int linkColumn = complex.OwnerLink.Index;
                foreach ((RowId flatId, _) in flat.RowsWithKey([linkColumn], [recordId], row =>
                    row[linkColumn] is { } link
                    && Convert.ToInt32(link, System.Globalization.CultureInfo.InvariantCulture) == recordId).ToList())
                    flat.Delete(flatId);
            }
            foreach (IndexDef index in Definition.RealIndexes)
                RemoveIndexEntry(index, values, id);
            new RowInserter(Channel, Definition).Delete(id);
        });
    }

    /// <summary>Deletes every row at once, as ACE runs a <c>DELETE</c> with no <c>WHERE</c> — see
    /// <see cref="RowInserter.Truncate"/>. Rows are not visited, so nothing a row reaches is followed — a complex
    /// column's flat rows included, which ACE leaves too: the caller keeps to tables that no relationship names as
    /// its parent and that hold no CHECK constraint.</summary>
    public void Truncate() => Write(() => new RowInserter(Channel, Definition).Truncate());

    /// <inheritdoc cref="RowInserter.CountKeysBeforeRefusal"/>
    public void CountKeysBeforeRefusal(IndexDef refusing, object?[] values) =>
        new RowInserter(Channel, Definition).CountKeysBeforeRefusal(refusing, values);

    private void Write(Action action)
    {
        // A read-only database refuses a row written through this API as ACE refuses a DAO recordset's (verified).
        // SQL refuses earlier, with its own messages, before it reaches here.
        Channel.RefuseIfReadOnly(ReadOnlyWrite.DatabaseOrObject);
        bool ownTransaction = !Channel.InTransaction;
        if (ownTransaction) Channel.BeginTransaction();
        try
        {
            action();
            if (ownTransaction) Channel.CommitTransaction(flush: false);
        }
        catch
        {
            if (ownTransaction) Channel.RollbackTransaction(failed: true);
            throw;
        }
    }

    /// <summary>Removes a deleted row's entry from one index.</summary>
    internal void RemoveIndexEntry(IndexDef index, object?[] values, RowId id) =>
        new IndexTree(Channel, Definition).DeleteEntry(index, values, id);
}
