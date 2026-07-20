using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>An index entry: the decoded key values (in index column order) and the row they point at.</summary>
public readonly record struct IndexEntry(object?[] Key, RowId Row);

/// <summary>
/// Walks an index B-tree and yields the row pointers in index (key) order.
/// </summary>
/// <remarks>
/// Each index page has an entry-position bitmask at <see cref="IndexPageReader.EntryMaskOffset"/> whose set
/// bits give the end offsets of successive entries within the entry-data region that begins
/// at <see cref="IndexPageReader.EntryDataOffset"/>. A leaf entry ends with a 4-byte big-endian row pointer
/// (page in the high 24 bits, row in the low 8); a node entry instead ends with the 4-byte
/// child page number. Key bytes are not decoded here — only the trailing pointers are read —
/// so the order-preserving key encoding is not needed to enumerate rows in order.
/// </remarks>
public sealed class IndexCursor(PageChannel channel, int rootPage)
{
    private readonly PageChannel _channel = channel;
    private readonly int _rootPage = rootPage;

    public IEnumerable<RowId> RowIds() => WalkRaw().Select(e => e.Row);

    /// <summary>
    /// Yields each entry with its decoded key (per <paramref name="columns"/>) in index order.
    /// Key columns that use Jet's lossy text/binary collation decode as null.
    /// </summary>
    public IEnumerable<IndexEntry> Entries(IReadOnlyList<(ColumnDef Column, bool Ascending)> columns) =>
        WalkRaw().Select(e => new IndexEntry(IndexKeyDecoder.Decode(columns, e.Key), e.Row));

    /// <summary>
    /// Yields each entry's full (decompressed) key bytes and row pointer, without decoding — used
    /// to verify the order-preserving key encoding byte-for-byte against what Access stored.
    /// </summary>
    public IEnumerable<(byte[] Key, RowId Row)> RawEntries() => WalkRaw();

    private IEnumerable<(byte[] Key, RowId Row)> WalkRaw()
    {
        var pending = new Stack<int>();
        var visited = new HashSet<int>();
        int? owner = null;
        pending.Push(_rootPage);
        while (pending.Count > 0)
        {
            int pageNumber = pending.Pop();
            if (!visited.Add(pageNumber))
                throw new InvalidDataException($"Index traversal contains a repeated/cyclic page {pageNumber}.");
            CheckedIndexPage page = IndexPageReader.Read(_channel, pageNumber, owner);
            owner ??= page.Owner;

            if (page.Type == PageType.LeafIndexPage)
            {
                byte[] prefix = [];
                bool first = true;
                foreach ((int start, int end) in page.EntryRanges)
                {
                    int pointer = IndexPageReader.ReadInt32BigEndian(
                        page.Buffer, IndexPageReader.EntryDataOffset + end - 4);
                    ReadOnlySpan<byte> storedKey = page.Buffer.Slice(
                        IndexPageReader.EntryDataOffset + start, end - start - 4);
                    byte[] key = first ? storedKey.ToArray() : Concat(prefix, storedKey);
                    if (first) { prefix = key[..page.CompressedByteCount]; first = false; }
                    yield return (key, new RowId(pointer >> 8, pointer & 0xFF));
                }
                continue;
            }
            pending.Push(page.Tail);
            for (int i = page.EntryRanges.Count - 1; i >= 0; i--)
                pending.Push(IndexPageReader.ReadInt32BigEndian(
                    page.Buffer, IndexPageReader.EntryDataOffset + page.EntryRanges[i].End - 4));
        }
    }

    private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result);
        b.CopyTo(result.AsSpan(a.Length));
        return result;
    }
}
