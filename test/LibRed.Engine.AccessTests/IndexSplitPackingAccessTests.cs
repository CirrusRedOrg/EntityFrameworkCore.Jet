using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.IO;
using Xunit;

namespace LibRed.Engine.Tests;

// How full an index's leaf pages end up after ascending inserts, and whether ACE can navigate the result.
//
// ACE does a RIGHT-EDGE split: when the new key is the highest on the page it leaves that page alone and
// starts a fresh one, so a sequentially-loaded index packs its leaves to capacity. LibRed splits down the
// middle, which is the textbook B-tree rule and correct, but leaves every page about half used — 2000
// ascending rows give ACE 4 leaves (three of them full to within a byte) and LibRed 6 at ~56%.
//
// Ascending keys are the ordinary case, not a corner: AutoNumber and identity primary keys produce exactly
// this shape. Nothing here is wrong — ACE reads LibRed's tree and seeks through it correctly, which is the
// assertion that has to hold — but the index costs about 1.8x the pages it needs to.
//
// The packing numbers are asserted rather than merely reported so that changing the split policy fails here
// and this note gets revisited with it.
[Collection(AceCollection.Name)]
public class IndexSplitPackingAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    private const int Rows = 2000;

    [Fact]
    public void Ace_packs_its_leaves_full_and_libred_packs_them_half()
    {
        (int aceLeaves, int aceFullest) = Leaves(AceRun);
        (int libredLeaves, int libredFullest) = Leaves(LibRedRun);

        output.WriteLine($"ACE {aceLeaves} leaves, fullest has {aceFullest} bytes free; "
            + $"LibRed {libredLeaves} leaves, fullest has {libredFullest} bytes free");

        Assert.True(aceFullest < 16, $"ACE should pack a leaf to capacity, got {aceFullest} free");
        Assert.True(libredLeaves > aceLeaves,
            $"the split policies still differ; if LibRed now matches ACE ({libredLeaves} vs {aceLeaves} "
            + "leaves) this test and its note need updating");
    }

    // The assertion that must not regress whatever the packing: ACE has to read the tree LibRed built.
    [Fact]
    public void Ace_seeks_through_a_libred_built_split_tree()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "idxsplit-read-");
        try
        {
            LibRedRun(path);

            using OleDbConnection connection = AceTestDatabase.Open(path);
            using (OleDbCommand count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM W";
                Assert.Equal(Rows, Convert.ToInt32(count.ExecuteScalar()));
            }
            foreach (int i in new[] { 1, 499, 1000, 1501, Rows })
            {
                using OleDbCommand seek = connection.CreateCommand();
                seek.CommandText = $"SELECT B FROM W WHERE A = {i}";
                Assert.Equal($"value {i}", seek.ExecuteScalar());
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void AceRun(string path)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using (OleDbCommand ddl = connection.CreateCommand())
        {
            ddl.CommandText = "CREATE TABLE W (A LONG, B TEXT(40), CONSTRAINT pk PRIMARY KEY (A))";
            ddl.ExecuteNonQuery();
        }
        using OleDbCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO W (A, B) VALUES (?, ?)";
        insert.Parameters.Add("a", OleDbType.Integer);
        insert.Parameters.Add("b", OleDbType.VarWChar, 40);
        for (int i = 1; i <= Rows; i++)
        {
            insert.Parameters[0].Value = i;
            insert.Parameters[1].Value = $"value {i}";
            insert.ExecuteNonQuery();
        }
    }

    private static void LibRedRun(string path)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(database);
        engine.ExecuteNonQuery("CREATE TABLE `W` (`A` LONG, `B` TEXT(40), CONSTRAINT `pk` PRIMARY KEY (`A`))");
        for (int i = 1; i <= Rows; i++)
            engine.ExecuteNonQuery($"INSERT INTO `W` (`A`, `B`) VALUES ({i}, 'value {i}')");
    }

    /// <summary>How many leaf pages the table's index has, and the least free space on any of them.</summary>
    private static (int Count, int Fullest) Leaves(Action<string> run)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "idxsplit-");
        try
        {
            run(path);

            int definitionPage;
            using (var database = JetDatabase.Open(path, readOnly: true))
                definitionPage = database.Catalog.FindTable("W")!.DefinitionPage;

            using var channel = PageChannel.Open(path, readOnly: true);
            var free = new List<int>();
            for (int page = 1; page < channel.PageCount; page++)
            {
                byte[] bytes = channel.ReadPage(page).Span.ToArray();
                if (bytes[0] != 0x04) continue;
                if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)) != definitionPage) continue;
                free.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2)));
            }
            return (free.Count, free.Count == 0 ? int.MaxValue : free.Min());
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
