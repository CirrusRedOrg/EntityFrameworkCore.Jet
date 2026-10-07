using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Dropping a table must return its long-value pages, not just its data pages. A Memo/OLE column owns its
/// LVAL pages through a per-column usage map whose pointer lives in the TDEF keyed by column id — those pages
/// are absent from the table's own data-page map, so a drop that frees only the data pages strands the entire
/// content of the table. Measured against ACE before the fix: dropping a 60-row memo table returned 123 pages
/// through ACE and 2 through LibRed, and a subsequent refill grew the file by 119 pages instead of reusing it.
/// </summary>
public class DropTableReclamationTests
{
    [Fact]
    public void Dropping_a_memo_table_frees_its_long_value_pages()
    {
        string path = TemporaryDatabase.CreatePath("libred_drop_lval_");
        try
        {
            JetDatabase.Create(path);
            string big = new('x', 3000);   // far past the inline threshold, so each value owns LVAL pages

            int filled, freeBefore, tdefPage;
            byte[] releasedTdef;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Victim", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, true),
                    new ColumnSpec("M", JetDataType.Memo, 0, false)]);
                db.Catalog.Invalidate();

                Table victim = db.OpenTable("Victim");
                for (int i = 0; i < 60; i++) victim.Insert([i, big]);

                filled = victim.Channel.PageCount;
                tdefPage = db.Catalog.FindTable("Victim")!.DefinitionPage;
                freeBefore = FreePages(db);
                db.DropTable("Victim");

                // Held until this handle closes, as ACE holds a dropped table's pages.
                Assert.Equal(freeBefore, FreePages(db));

                releasedTdef = new byte[victim.Channel.PageSize];
                victim.Channel.ReadPage(tdefPage, releasedTdef);
            }

            int freedPages;
            using (var db = JetDatabase.Open(path))
                freedPages = FreePages(db) - freeBefore;

            // Access marks the released definition page and leaves the rest of it alone — measured, exactly
            // one byte of the 4,096 changes across an ACE drop. The pages it frees alongside keep their types.
            Assert.Equal(LibRed.Pages.PageType.ReleasedTableDefinition, LibRed.Pages.PageHeader.ReadType(releasedTdef));

            // 60 values of 3,000 characters is ~360 KB of UTF-16, well over a hundred 4 KB pages. Before the
            // fix this was 2 — the single data page plus the TDEF.
            Assert.True(freedPages > 100,
                $"dropping the table returned only {freedPages} pages to the global free map");

            // The functional consequence: refilling reuses the space instead of extending the file.
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Keeper", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, true),
                    new ColumnSpec("M", JetDataType.Memo, 0, false)]);
                db.Catalog.Invalidate();

                Table keeper = db.OpenTable("Keeper");
                for (int i = 0; i < 60; i++) keeper.Insert([i, big]);

                Assert.True(keeper.Channel.PageCount <= filled + 8,
                    $"refilling grew the file from {filled} to {keeper.Channel.PageCount} pages, so the "
                    + "dropped table's pages were not reused");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>How many of the file's pages the global free-pages map — wherever page 0's <c>0x18</c> puts it —
    /// currently calls free.</summary>
    private static int FreePages(JetDatabase db)
    {
        var channel = db.OpenTable("MSysObjects").Channel;
        (_, _, byte[] holder, LibRed.Pages.DataPage.RowSlot slot) =
            TestDatabases.GlobalMap(channel, channel.Format.FreePagesMapPointerOffset);
        byte[] record = holder[slot.Offset..(slot.Offset + slot.Length)];
        // Inline form only: a file this small never grows the free map past it.
        Assert.Equal(UsageMapType.Inline, UsageMap.RecordType(record));
        int start = UsageMap.StartPage(record, channel.Format);
        Span<byte> bits = UsageMap.InlineBits(record, channel.Format);

        int free = 0;
        for (int page = 0; page < channel.PageCount; page++)
        {
            int bit = page - start;
            if (bit >= 0 && bit / 8 < bits.Length && BitmapBits.Get(bits, bit)) free++;
        }
        return free;
    }
}