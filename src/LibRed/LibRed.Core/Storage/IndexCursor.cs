using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Walks an index B-tree and yields the row pointers in index (key) order.
/// </summary>
/// <remarks>
/// Each index page has an entry-position bitmask at <see cref="EntryMaskOffset"/> whose set
/// bits give the end offsets of successive entries within the entry-data region that begins
/// at <see cref="EntryDataOffset"/>. A leaf entry ends with a 4-byte big-endian row pointer
/// (page in the high 24 bits, row in the low 8); a node entry instead ends with the 4-byte
/// child page number. Key bytes are not decoded here — only the trailing pointers are read —
/// so the order-preserving key encoding is not needed to enumerate rows in order.
/// </remarks>
public sealed class IndexCursor(PageChannel channel, int rootPage)
{
    private const int EntryMaskOffset = 0x1B;
    private const int EntryDataOffset = 0x1E0;

    /// <summary>On a node page, the rightmost child page — not referenced by any entry.</summary>
    private const int ChildTailOffset = 0x14;

    private readonly PageChannel _channel = channel;
    private readonly int _rootPage = rootPage;

    public IEnumerable<RowId> RowIds() => Walk(_rootPage);

    private IEnumerable<RowId> Walk(int pageNumber)
    {
        PageBuffer page = _channel.ReadPage(pageNumber);
        var type = (PageType)page.ReadByte(0);

        foreach ((int start, int end) in EntryRanges(page))
        {
            // The pointer is the last 4 bytes of the entry, stored big-endian.
            int pointer = ReadInt32BigEndian(page, EntryDataOffset + end - 4);

            if (type == PageType.LeafIndexPage)
            {
                yield return new RowId(pointer >> 8, pointer & 0xFF);
            }
            else if (type == PageType.IntermediateIndexPage)
            {
                foreach (RowId rowId in Walk(pointer))
                    yield return rowId;
            }
            else
            {
                throw new NotSupportedException($"Page {pageNumber} is not an index page (type 0x{(byte)type:X2}).");
            }

            _ = start;
        }

        // A node page has one more child than it has entries: the rightmost subtree,
        // pointed to from the header rather than by an entry.
        if (type == PageType.IntermediateIndexPage)
        {
            foreach (RowId rowId in Walk(page.ReadInt32(ChildTailOffset)))
                yield return rowId;
        }
    }

    /// <summary>Yields the (start, end) byte ranges of each entry, relative to the entry-data region.</summary>
    private static IEnumerable<(int Start, int End)> EntryRanges(PageBuffer page)
    {
        int start = 0;
        for (int i = EntryMaskOffset; i < EntryDataOffset; i++)
        {
            byte mask = page.ReadByte(i);
            if (mask == 0) continue;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;
                int end = (i - EntryMaskOffset) * 8 + bit;
                yield return (start, end);
                start = end;
            }
        }
    }

    private static int ReadInt32BigEndian(PageBuffer page, int offset) =>
        (page.ReadByte(offset) << 24) | (page.ReadByte(offset + 1) << 16) |
        (page.ReadByte(offset + 2) << 8) | page.ReadByte(offset + 3);
}
