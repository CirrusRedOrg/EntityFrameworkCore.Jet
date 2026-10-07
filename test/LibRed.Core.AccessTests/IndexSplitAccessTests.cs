using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Byte-faithful check that a LibRed index which has undergone B-tree leaf/node splitting is readable
/// by the real Access engine: after inserting enough rows to force splits (root grows from a leaf to a
/// node), Access opens the file and resolves indexed point seeks, an indexed range, a full table scan,
/// and a leaf-chain <c>COUNT(*)</c>/<c>SUM</c> — all reaching every row.
/// </summary>
[Collection(AceCollection.Name)]
public class IndexSplitAccessTests(ITestOutputHelper output)
{
    private const int N = 1200; // well past one leaf, so the PK B-tree splits and the root grows a level

    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_reads_a_split_index_by_seek_and_range()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-splitaccess-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Big",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("T", JetDataType.Text, 20, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                var t = db.OpenTable("Big");
                for (int i = 1; i <= N; i++) t.Insert([i, $"r{i}"]);
            }

            using var conn = OpenOleDb(path);

            // Indexed point seeks at spread-out keys exercise different leaves/subtrees of the split B-tree.
            foreach (int id in new[] { 1, 400, 512, 1000, N })
            {
                using var seek = conn.CreateCommand();
                seek.CommandText = $"SELECT T FROM Big WHERE Id = {id}";
                Assert.Equal($"r{id}", seek.ExecuteScalar());
            }

            // A full table scan and index leaf-chain walk must both reach every row: COUNT(*) (leaf-chain),
            // a non-indexed scan, an indexed range, and SUM all account for all N rows. This catches a wrong
            // leaf next/prev pointer, where Access stops after the first leaf and silently loses the rest.
            long triangular = (long)N * (N + 1) / 2;
            foreach ((string sql, object expected) in new (string, object)[]
            {
                ("SELECT COUNT(*) FROM Big", N),
                ("SELECT COUNT(*) FROM Big WHERE T LIKE 'r%'", N),
                ("SELECT COUNT(*) FROM Big WHERE Id BETWEEN 300 AND 309", 10),
                ("SELECT SUM(Id) FROM Big", triangular),
            })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                Assert.Equal(Convert.ToInt64(expected), Convert.ToInt64(cmd.ExecuteScalar()));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Two structural questions about a multi-level B-tree that only ACE can answer, and a writer has to match
    // or it leaves a tree Access reads differently.
    //
    // A NODE page's prev/next: ACE writes 0 in both, as LibRed does. Measured, and the reason this test does
    // not assert it separately — there is nothing to diverge on.
    //
    // An emptied LEAF: ACE takes it out. Deleting every key in the low leaf of a two-leaf tree leaves the
    // high leaf with prev=0, the node with no separator entry and only its tail pointer, and the emptied page
    // no longer reachable from the root at all. Leaving it linked instead keeps a page in the chain that the
    // leaf-chain walk ACE's COUNT(*) uses must step through for nothing, and keeps a separator in the node
    // pointing at a leaf holding no keys.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_emptied_leaf_leaves_the_tree(bool throughAce)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "btree-empty-leaf-");
        try
        {
            if (throughAce) AceBuild(path); else LibRedBuild(path);

            output.WriteLine($"-- {(throughAce ? "ACE" : "LibRed")} built the tree ({N} rows)");
            List<string> before = Describe(path, "Shape");
            foreach (string line in before) output.WriteLine(line);
            Assert.True(before.Count > 1, "the tree did not split, so there is no leaf to empty");

            // Empty the low leaf outright — a sequential load puts keys 1..~600 in it, so this takes every
            // entry it has and leaves the high leaf holding the rest.
            if (throughAce) AceEmptyLowLeaf(path); else LibRedEmptyLowLeaf(path);
            output.WriteLine("-- after deleting every key below 700, which empties the low leaf");
            List<string> after = Describe(path, "Shape");
            foreach (string line in after) output.WriteLine(line);

            // Exactly one page left the tree, and it keeps its leaf type byte. A released DATA page is stamped
            // 0x09 (page-09), so the natural guess is that an index page is too; it is not, in either engine.
            int Number(string line) => int.Parse(line.Split(' ')[1]);
            int gone = before.Select(Number).Except(after.Select(Number)).Single();
            using (var channel = LibRed.IO.PageChannel.Open(path, readOnly: true))
                Assert.Equal((byte)0x04, channel.ReadPage(gone).ReadByte(0));

            // No leaf reachable from the root holds nothing, and the chain that remains is consistent: the
            // first leaf has no prev, the last no next, and every link points at a leaf still in the tree.
            Assert.DoesNotContain(after, line => line.Contains("type=0x04") && line.Contains("entries=0"));
            AssertChainIsIntact(path, "Shape");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void AceBuild(string path)
    {
        using OleDbConnection connection = OpenOleDb(path);
        Exec(connection, "CREATE TABLE Shape (Id LONG CONSTRAINT pk PRIMARY KEY, T TEXT(60))");
        for (int i = 1; i <= N; i++)
            Exec(connection, $"INSERT INTO Shape (Id, T) VALUES ({i}, '{new string('x', 50)}')");
    }

    private static void AceEmptyLowLeaf(string path)
    {
        using OleDbConnection connection = OpenOleDb(path);
        Exec(connection, "DELETE FROM Shape WHERE Id < 700");
    }

    private static void LibRedBuild(string path)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        database.CreateTable("Shape",
            [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
             new ColumnSpec("T", JetDataType.Text, 120, IsFixedLength: false)],
            primaryKey: ["Id"]);
        var table = database.OpenTable("Shape");
        for (int i = 1; i <= N; i++) table.Insert([i, new string('x', 50)]);
    }

    private static void LibRedEmptyLowLeaf(string path)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        var table = database.OpenTable("Shape");
        foreach ((RowId id, object?[] values) in table.Rows().WithIds().ToList())
        {
            if (Convert.ToInt32(values[0]) >= 700) continue;
            table.Delete(id);
        }
    }

    /// <summary>Walks the leaf chain from the leftmost leaf and checks it covers exactly the leaves the tree
    /// reaches from its root — no page linked in that the root cannot see, and none reachable that the chain
    /// misses.</summary>
    private static void AssertChainIsIntact(string path, string table)
    {
        using var channel = LibRed.IO.PageChannel.Open(path, readOnly: true);
        TableDefinition definition = new LibRed.Catalog.JetCatalog(channel).FindTable(table)!;
        IndexDef def = definition.Indexes.First(i => i.IsPrimaryKey);

        var fromRoot = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(def.RootPage);
        while (pending.Count > 0)
        {
            int number = pending.Pop();
            LibRed.IO.PageBuffer buffer = channel.ReadPage(number);
            if (PageHeader.ReadType(buffer.Span) == PageType.LeafIndexPage) { fromRoot.Add(number); continue; }
            int tail = buffer.ReadInt32(channel.Format.IndexChildTailOffset);
            if (tail > 0) pending.Push(tail);
            foreach ((byte[] _, int child) in LibRed.Storage.IndexTree.DecodeEntries(
                         LibRed.Storage.IndexTree.Read(channel, number, null)))
                pending.Push(child);
        }

        int leftmost = fromRoot.Single(p => LibRed.Storage.IndexTree.ReadSiblings(channel.ReadPage(p).Span, channel.Format).Previous == 0);
        var chain = new List<int>();
        for (int at = leftmost; at != 0; at = LibRed.Storage.IndexTree.ReadSiblings(channel.ReadPage(at).Span, channel.Format).Next) chain.Add(at);

        Assert.Equal(fromRoot.OrderBy(p => p), chain.OrderBy(p => p));
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Every page of the index's B-tree: its type, entry count, and the prev/next/child-tail
    /// pointers, walked from the root so node pages are reported as well as leaves.</summary>
    private static List<string> Describe(string path, string table)
    {
        using var channel = LibRed.IO.PageChannel.Open(path, readOnly: true);
        TableDefinition definition = new LibRed.Catalog.JetCatalog(channel).FindTable(table)!;
        IndexDef def = definition.Indexes.First(i => i.IsPrimaryKey);

        var lines = new List<string>();
        var pending = new Stack<int>();
        var seen = new HashSet<int>();
        pending.Push(def.RootPage);
        while (pending.Count > 0)
        {
            int number = pending.Pop();
            if (!seen.Add(number)) continue;
            LibRed.IO.PageBuffer buffer = channel.ReadPage(number);
            byte type = buffer.ReadByte(0);
            Formats.JetFormatBase format = channel.Format;
            (int prev, int next) = LibRed.Storage.IndexTree.ReadSiblings(buffer.Span, format);
            int tail = buffer.ReadInt32(format.IndexChildTailOffset);
            int entries = 0;
            for (int at = format.IndexEntryMaskOffset; at < format.IndexEntryDataOffset; at++) entries += System.Numerics.BitOperations.PopCount(buffer.ReadByte(at));
            lines.Add($"page {number} type=0x{type:X2} entries={entries} prev={prev} next={next} tail={tail}");

            if (PageHeader.ReadType(buffer.Span) != PageType.IntermediateIndexPage) continue;   // 0x03 = node, 0x04 = leaf
            if (tail > 0) pending.Push(tail);
            foreach ((byte[] _, int child) in LibRed.Storage.IndexTree.DecodeEntries(
                         LibRed.Storage.IndexTree.Read(channel, number, null)))
                pending.Push(child);
        }
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    // A root split rewrites the TDEF's root pointer, and every other open handle has to see it. A catalog
    // loaded before the split keeps the old root — by then only the leftmost leaf — so an insert through it
    // descends into the wrong leaf. With that leaf full, as a sequential load leaves it, the insert splits it
    // as though it were still the root and writes a new root over the real one, cutting every key right of
    // that leaf out of the index: ACE's seeks for them miss. EF keeps several connections open, so this is an
    // ordinary shape.
    [Fact]
    public void Access_seeks_a_row_another_handle_inserted_after_a_root_split()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-splitstale-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Big",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("T", JetDataType.Text, 20, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                db.OpenTable("Big").Insert([1, "r1"]);
            }

            using (var splitter = JetDatabase.Open(path, readOnly: false))
            using (var stale = JetDatabase.Open(path, readOnly: false))
            {
                stale.OpenTable("Big");                          // its catalog loads while the root is one leaf
                var t = splitter.OpenTable("Big");
                for (int i = 2; i <= N; i++) t.Insert([i, $"r{i}"]);   // the root splits
                stale.OpenTable("Big").Insert([5000, "r5000"]);
            }

            using var conn = OpenOleDb(path);
            // Seeks across the whole tree, left leaf to the stale handle's row, and a range the index answers.
            foreach (int id in new[] { 1, 400, 512, 1000, N, 5000 })
            {
                using var seek = conn.CreateCommand();
                seek.CommandText = $"SELECT T FROM Big WHERE Id = {id}";
                Assert.Equal($"r{id}", seek.ExecuteScalar());
            }
            using (var range = conn.CreateCommand())
            {
                range.CommandText = "SELECT COUNT(*) FROM Big WHERE Id BETWEEN 1 AND 5000";
                Assert.Equal(N + 1, Convert.ToInt32(range.ExecuteScalar()));
            }
            using var duplicate = conn.CreateCommand();
            duplicate.CommandText = "INSERT INTO Big (Id, T) VALUES (5000, 'again')";
            Assert.ThrowsAny<OleDbException>(() => duplicate.ExecuteNonQuery());
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}