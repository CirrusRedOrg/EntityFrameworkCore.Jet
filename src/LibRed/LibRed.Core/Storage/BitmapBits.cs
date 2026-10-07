using System.Numerics;

namespace LibRed.Storage;

/// <summary>
/// The bits of the format's bitmaps, which all number their bits the same way — bit <c>i</c> of byte <c>n</c> is
/// bit <c>n×8 + i</c>, least significant first: a usage map's (bit <c>k</c> marking page <c>basePage + k</c>,
/// page-05 §9), a row's null bitmap (bit <c>k</c> for column id <c>k</c>, page-01 §5) and an index page's
/// entry-position mask (bit <c>k</c> ending an entry at offset <c>k</c>, page-03-04 §10.2). Every read and write of
/// a bit in any of them comes through here; the caller passes the bitmap without whatever header carried it.
/// </summary>
internal static class BitmapBits
{
    /// <summary>The bytes a bitmap of <paramref name="bits"/> bits takes.</summary>
    public static int ByteCount(int bits) => (bits + 7) >> 3;

    /// <summary>Whether <paramref name="bit"/> is set.</summary>
    public static bool Get(ReadOnlySpan<byte> bitmap, int bit) => (bitmap[bit >> 3] & (1 << (bit & 7))) != 0;

    /// <summary>Sets or clears <paramref name="bit"/>.</summary>
    public static void Set(Span<byte> bitmap, int bit, bool value)
    {
        byte mask = (byte)(1 << (bit & 7));
        if (value) bitmap[bit >> 3] |= mask;
        else bitmap[bit >> 3] &= (byte)~mask;
    }

    /// <summary>The lowest set bit at or above <paramref name="from"/>, or -1 when there is none.</summary>
    public static int NextSetBit(ReadOnlySpan<byte> bitmap, int from)
    {
        for (int i = from >> 3; i < bitmap.Length; i++)
        {
            int bits = i == from >> 3 ? bitmap[i] & (0xFF << (from & 7)) : bitmap[i];
            if (bits != 0) return i * 8 + BitOperations.TrailingZeroCount(bits);
        }
        return -1;
    }

    /// <summary>The highest set bit, or -1 when there is none.</summary>
    public static int LastSetBit(ReadOnlySpan<byte> bitmap)
    {
        for (int i = bitmap.Length - 1; i >= 0; i--)
            if (bitmap[i] != 0) return i * 8 + 31 - BitOperations.LeadingZeroCount((uint)bitmap[i]);
        return -1;
    }

    /// <summary>
    /// Appends the pages a usage-map <paramref name="bitmap"/> marks to <paramref name="pages"/>.
    /// </summary>
    /// <param name="pages">Collects the page numbers, in ascending order.</param>
    /// <param name="bitmap">The map's bits, without whatever header carried them.</param>
    /// <param name="basePage">The page bit 0 stands for.</param>
    /// <param name="rejectBeyond">
    /// The file's page count, to reject a bit naming a page outside it — or null to keep every bit.
    /// <para>Which of those is right is a property of the caller, not an oversight. A map <b>read</b> feeds
    /// its numbers straight into page reads, so one outside the file is corruption and says so. A map being
    /// <b>rewritten</b> must keep bits it cannot currently represent: a free map's window slides with the
    /// append tail and can sit above a page it still has to record, so the record widens rather than dropping
    /// them (page-05 §9). Rejecting there is not theoretical — it failed an ordinary DROP TABLE on
    /// <c>complex1.accdb</c>, whose <c>MSysObjects</c> free map starts at page 2288 while its catalog rows
    /// live at page 17.</para>
    /// </param>
    /// <param name="what">Names the map in the rejection message.</param>
    public static void AppendPages(
        List<int> pages, ReadOnlySpan<byte> bitmap, int basePage, int? rejectBeyond, string what)
    {
        // The bound is read once by the caller and passed in: this loop runs per set bit on every insert, and
        // PageChannel.PageCount used to be a file-length syscall, which made a per-bit test cost a
        // non-transactional insert ~1.9x. Nothing here writes, so the count cannot move under it.
        for (int bit = NextSetBit(bitmap, 0); bit >= 0; bit = NextSetBit(bitmap, bit + 1))
        {
            long page = (long)basePage + bit;
            if (rejectBeyond is { } pageCount && (page <= 1 || page >= pageCount))
                throw new InvalidDataException(
                    $"{what} names page {page}, outside the file's 2..{pageCount - 1} range.");
            pages.Add((int)page);
        }
    }
}