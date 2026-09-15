using LibRed;
using LibRed.Catalog;
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
            DatabaseCreator.CreateEmpty(path);
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
            Assert.Equal((byte)LibRed.Pages.PageType.ReleasedTableDefinition, releasedTdef[0]);

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

    /// <summary>How many pages the global free-pages map (page 1, row 0) currently calls free.</summary>
    private static int FreePages(JetDatabase db)
    {
        var channel = db.OpenTable("MSysObjects").Channel;
        int free = 0;
        for (int page = 2; page < channel.PageCount; page++)
            if (IsFree(channel, page)) free++;
        return free;
    }

    private static bool IsFree(LibRed.IO.PageChannel channel, int page)
    {
        var buffer = new byte[channel.PageSize];
        channel.ReadPage(1, buffer);
        var holder = new LibRed.Pages.DataPage();
        holder.Read(channel.ReadPage(1), channel.Format);
        if (holder.RowCount < 1) return false;
        var slot = holder.Rows[0];
        if (buffer[slot.Offset] != 0x00) return false;      // inline form only; a tiny file never grows past it
        int start = BitConverter.ToInt32(buffer, slot.Offset + 1);
        int bit = page - start;
        int index = slot.Offset + 5 + bit / 8;
        if (bit < 0 || index >= slot.Offset + slot.Length) return false;
        return (buffer[index] & (1 << (bit % 8))) != 0;
    }
}
