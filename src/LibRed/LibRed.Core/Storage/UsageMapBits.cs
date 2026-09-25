namespace LibRed.Storage;

/// <summary>
/// The walk from a usage-map bitmap to the page numbers it marks — bit <c>i</c> of byte <c>n</c> meaning page
/// <c>basePage + n×8 + i</c> (page-05 §9). Every map form reduces to this: an inline record's single bitmap,
/// each bitmap page of a reference record, and a long-value column's maps of either form.
/// </summary>
internal static class UsageMapBits
{
    /// <summary>
    /// Appends the pages <paramref name="bitmap"/> marks to <paramref name="pages"/>.
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
    public static void Append(
        List<int> pages, ReadOnlySpan<byte> bitmap, int basePage, int? rejectBeyond, string what)
    {
        // The bound is read once by the caller and passed in: this loop runs per set bit on every insert, and
        // PageChannel.PageCount used to be a file-length syscall, which made a per-bit test cost a
        // non-transactional insert ~1.9x. Nothing here writes, so the count cannot move under it.
        for (int i = 0; i < bitmap.Length; i++)
        {
            byte b = bitmap[i];
            if (b == 0) continue;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((b & (1 << bit)) == 0) continue;
                long page = (long)basePage + i * 8 + bit;
                if (rejectBeyond is { } pageCount && (page <= 1 || page >= pageCount))
                    throw new InvalidDataException(
                        $"{what} names page {page}, outside the file's 2..{pageCount - 1} range.");
                pages.Add((int)page);
            }
        }
    }
}
