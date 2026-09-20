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

    // The chain holds the definition and then its 8-byte reserve, which spills onto a page of its own when it does
    // not fit (docs/format/page-02a-tdef.md §3.2). The free space of each page is ACE's, measured for the same
    // tables: 115 LONG columns make 4,086 bytes, each character on the first name adds two, 232 columns make 8,181.
    [Theory]
    [InlineData(115, 1, new[] { 0 })]
    [InlineData(115, 2, new[] { 0, 4086 })]        // 4,090 bytes: the continuation holds two reserve bytes
    [InlineData(115, 5, new[] { 0, 4080 })]        // 4,096 bytes: it holds the whole reserve and no definition
    [InlineData(115, 6, new[] { 0, 4078 })]
    [InlineData(232, 0, new[] { 0, 0, 4083 })]     // 8,181 bytes: a third page for five reserve bytes
    public void A_definition_near_a_page_boundary_spills_its_reserve_and_reads_back(int columns, int longerName, int[] free)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "spill-");
        try
        {
            string[] names = ["Id", "c001" + new string('x', longerName), .. Enumerable.Range(2, columns - 2).Select(i => $"c{i:D3}")];
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("L", names.Select(n => new ColumnSpec(n, JetDataType.Int32, 4, IsFixedLength: true)).ToList());
                db.OpenTable("L").Insert([7, .. Enumerable.Repeat<object?>(null, columns - 1)]);
            }

            using (var ch = PageChannel.Open(path, readOnly: true))
            {
                var chainFree = new List<int>();
                for (int page = new JetCatalog(ch).FindTable("L")!.DefinitionPage; page != 0; page = ch.ReadPage(page).ReadInt32(ch.Format.TdefNextPageOffset))
                    chainFree.Add(ch.ReadPage(page).ReadUInt16(ch.Format.TdefFreeSpaceOffset));
                Assert.Equal(free, chainFree);
            }

            using (var db = JetDatabase.Open(path))
            {
                Assert.Equal(names, db.Catalog.FindTable("L")!.Columns.Select(c => c.Name));
                Assert.Equal(7, db.OpenTable("L").Rows().Single()[0]);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
