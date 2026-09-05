using LibRed;
using LibRed.Catalog;
using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

public class MultiPageDefinitionTests
{
    // Adding enough indexes overflows a single TDEF page, so the definition spills onto a continuation
    // page. The definition must still read back correctly (the reader stitches continuation pages).
    [Fact]
    public void Index_that_overflows_the_tdef_page_spills_to_a_continuation_and_round_trips()
    {
        const int n = 30;
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "cont-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Wide",
                    Enumerable.Range(0, n)
                        .Select(i => new ColumnSpec($"C{i:D2}", JetDataType.Int32, 4, IsFixedLength: true))
                        .ToList());
                for (int i = 0; i < n; i++)
                    db.CreateIndex("Wide", $"IX{i:D2}", [($"C{i:D2}", false)]);
            }

            using (var ch = PageChannel.Open(path, readOnly: true))
            {
                var def = new JetCatalog(ch).FindTable("Wide")!;
                var buf = ch.ReadPage(def.DefinitionPage);
                Assert.True(buf.ReadInt32(0x08) > ch.Format.PageSize);              // definition exceeds one page
                Assert.NotEqual(0, buf.ReadInt32(ch.Format.TdefNextPageOffset));    // a continuation page exists
            }

            using (var db = JetDatabase.Open(path))
            {
                var t = db.Catalog.FindTable("Wide")!;
                Assert.Equal(n, t.Indexes.Count);
                for (int i = 0; i < n; i++)
                    Assert.Contains(t.Indexes, ix => ix.Name == $"IX{i:D2}"
                        && ix.Columns.Select(c => c.Column.Name).SequenceEqual([$"C{i:D2}"]));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
