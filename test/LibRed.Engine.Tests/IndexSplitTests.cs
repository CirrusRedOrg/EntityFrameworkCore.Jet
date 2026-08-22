using LibRed;
using LibRed.Engine;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Inserting more keys than a single index leaf can hold forces B-tree <b>leaf/node splitting</b>: the
/// leaf splits, a separator is promoted, and — the first time — a new root node grows (the PK root page
/// changes from a leaf 0x04 to a node 0x03). These verify, through LibRed's own reader and query engine,
/// that every key survives, stays in sorted order, and is still findable after the tree grows several
/// levels. (The byte-faithful ACE cross-check — Access opens the split file and indexed seek/range
/// return the right rows — lives in LibRed.Core.Tests' IndexSplitAccessTests.)
/// </summary>
public class IndexSplitTests
{
    private const int N = 1500; // well past one leaf of 4-byte-int keys, so the root grows a level

    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "split-");
        return path;
    }

    [Fact]
    public void Leaf_splitting_keeps_every_key_in_order_and_findable()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                new QueryEngine(db).ExecuteNonQuery("CREATE TABLE `Big` (`Id` INTEGER PRIMARY KEY, `T` VARCHAR(20))");
                var t = db.OpenTable("Big");
                for (int i = 1; i <= N; i++) t.Insert([i, $"r{i}"]);
            }

            using (var ch = PageChannel.Open(path, readOnly: true))
            {
                var pk = new LibRed.Catalog.JetCatalog(ch).FindTable("Big")!.Indexes.Single(x => x.IsPrimaryKey);

                // The root grew into a node — the tree is genuinely multi-level, not a single fat leaf.
                Assert.Equal(PageType.IntermediateIndexPage, (PageType)ch.ReadPage(pk.RootPage).ReadByte(0));

                var cursor = new IndexCursor(ch, pk.RootPage);
                Assert.Equal(N, cursor.RowIds().Count());

                // A full ordered walk must yield exactly 1..N with no gaps, dupes, or misorderings.
                int expected = 1;
                foreach (var entry in cursor.Entries(pk.Columns))
                    Assert.Equal(expected++, Convert.ToInt32(entry.Key[0]));
                Assert.Equal(N + 1, expected);
            }

            using (var db = JetDatabase.Open(path))
            {
                var rs = new QueryEngine(db).ExecuteQuery("SELECT `T` FROM `Big` WHERE `Id` = 1234");
                Assert.Equal("r1234", rs.Rows.Single()[0]);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
