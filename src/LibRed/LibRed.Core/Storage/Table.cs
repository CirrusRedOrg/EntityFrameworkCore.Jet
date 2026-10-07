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
    /// each read in full — seeked through an index on exactly those columns when the table has one, and found
    /// by <see cref="RowsWhere"/>'s scan when it has none. <paramref name="match"/> decides every row either way:
    /// an index key is lossy (text folds case and trailing spaces), so the seek only narrows.</summary>
    /// <remarks>The key has to be in each column's own kind already, because an index answers only in its
    /// column's kind; a caller that cannot promise that uses <see cref="RowsWhere"/>. A null in the key scans,
    /// since how an index keys a null is not the question a key comparison asks.</remarks>
    public IEnumerable<(RowId Id, object?[] Values)> RowsWithKey(
        int[] columns, object?[] key, Func<object?[], bool> match)
    {
        IndexDef? index = key.Any(k => k is null) ? null : Definition.Indexes.FirstOrDefault(i =>
            i.RootPage > 0 && i.Columns.Count == columns.Length && i.Columns.All(c => columns.Contains(c.Column.Index)));
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

    /// <summary>The decoder a seek reads its rows with: the last one made, while it was made for the same column
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
    /// yields row ids.</summary>
    public object?[]? GetRow(RowId id) => GetRow(id, NewDecoder());

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
    public void Insert(object?[] values) => new RowInserter(Channel, Definition).Insert(values);

    /// <summary>Rewrites the row at <paramref name="id"/> in place with new values (row id preserved).
    /// <paramref name="changedColumns"/> are the columns that actually changed — an unchanged memo/OLE column
    /// keeps its stored descriptor (no re-materialise), a changed one has its old LVAL pages reclaimed.</summary>
    public void Update(RowId id, object?[] values, IReadOnlySet<int> changedColumns)
    {
        Write(() =>
        {
            object?[] original = GetRow(id) ?? throw new InvalidOperationException($"Row '{id}' does not exist.");
            var changed = new HashSet<int>(changedColumns);
            for (int i = 0; i < values.Length; i++)
                if (original[i] is byte[] oldBytes && values[i] is byte[] newBytes
                    ? !oldBytes.AsSpan().SequenceEqual(newBytes) : !Equals(original[i], values[i]))
                    changed.Add(i);

            new RowInserter(Channel, Definition).Update(id, values, changed);
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
            object?[] values = GetRow(id) ?? throw new InvalidOperationException($"Row '{id}' does not exist.");
            foreach (ComplexColumn complex in _catalog.ComplexColumns)
            {
                if (complex.OwnerTable.DefinitionPage != Definition.DefinitionPage) continue;
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

    private void Write(Action action)
    {
        bool ownTransaction = !Channel.InTransaction;
        if (ownTransaction) Channel.BeginTransaction();
        try
        {
            action();
            if (ownTransaction) Channel.CommitTransaction(flush: false);
        }
        catch
        {
            if (ownTransaction) Channel.RollbackTransaction();
            throw;
        }
    }

    /// <summary>Removes a deleted row's entry from one index.</summary>
    internal void RemoveIndexEntry(IndexDef index, object?[] values, RowId id) =>
        new IndexTree(Channel, Definition).DeleteEntry(index, values, id);
}