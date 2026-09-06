using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.IO;
using Xunit;

namespace LibRed.Engine.Tests;

// How full an index's leaf pages end up, across the insert patterns that exercise the two split rules.
//
// Splitting down the middle is right when keys arrive all over the range: the lower half's free space is
// room for the next key near it. When the new entry is the page's MAXIMUM it is waste — nothing sorts below
// a maximum — so ACE keeps that page full and starts a new one with the new entry alone. LibRed split down
// the middle throughout and so used about 1.8x the leaves on a sequential load, which is the ordinary case
// since AutoNumber and identity keys ascend by construction.
//
// The rule is right-edge ONLY: descending inserts get an ordinary middle split from ACE too, and on random
// keys both engines settle near two-thirds full — the classic B-tree equilibrium. Those two workloads are
// here to prove the special case does not fire where it should not, which is what makes it free.
[Collection(AceCollection.Name)]
public class IndexSplitPackingAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Theory]
    [InlineData("ascending")]
    [InlineData("descending")]
    [InlineData("random")]
    public void Leaf_packing_matches_ace(string workload)
    {
        (int aceLeaves, string aceFree, int aceUsed, string acePrefix) = Leaves(workload, ace: true);
        (int libredLeaves, string libredFree, int libredUsed, string libredPrefix) = Leaves(workload, ace: false);

        output.WriteLine($"{workload}: ACE {aceLeaves} leaves [{aceFree}] using {aceUsed} bytes, prefix {acePrefix}");
        output.WriteLine($"{workload,-11}  LibRed {libredLeaves} leaves [{libredFree}] using {libredUsed}, "
            + $"prefix {libredPrefix}");
        Assert.Equal(aceLeaves, libredLeaves);
    }

    // A sequential load has to pack its leaves, not half-fill them — the point of the right-edge rule.
    [Fact]
    public void An_ascending_load_packs_its_leaves()
    {
        (_, string free, _, _) = Leaves("ascending", ace: false);
        int fullest = free.Split(',').Select(int.Parse).Min();   // page order now, so take the minimum
        Assert.True(fullest < 16, $"expected a leaf packed to capacity, got free space {free}");
    }

    // The assertion that must hold whatever the packing: ACE has to navigate the tree LibRed built.
    [Fact]
    public void Ace_seeks_through_a_libred_built_split_tree()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "idxsplit-read-");
        try
        {
            LibRedRun(path, "ascending");

            using OleDbConnection connection = AceTestDatabase.Open(path);
            using (OleDbCommand count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM W";
                Assert.Equal(Rows, Convert.ToInt32(count.ExecuteScalar()));
            }
            foreach (int i in new[] { 1, 499, 1000, 1200, Rows })
            {
                using OleDbCommand seek = connection.CreateCommand();
                seek.CommandText = $"SELECT B FROM W WHERE A = {i}";
                Assert.Equal($"value {i}", seek.ExecuteScalar());
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private const int Rows = 1500;

    private static IEnumerable<int> Keys(string workload)
    {
        switch (workload)
        {
            case "ascending":
                for (int i = 1; i <= Rows; i++) yield return i;
                break;
            case "descending":
                for (int i = Rows; i >= 1; i--) yield return i;
                break;
            default:
                var keys = Enumerable.Range(1, Rows).ToList();
                var rng = new Random(12345);            // fixed, so both engines see one order
                for (int i = keys.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (keys[i], keys[j]) = (keys[j], keys[i]);
                }
                foreach (int key in keys) yield return key;
                break;
        }
    }

    private static void AceRun(string path, string workload)
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
        foreach (int key in Keys(workload))
        {
            insert.Parameters[0].Value = key;
            insert.Parameters[1].Value = $"value {key}";
            insert.ExecuteNonQuery();
        }
    }

    private static void LibRedRun(string path, string workload)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(database);
        engine.ExecuteNonQuery("CREATE TABLE `W` (`A` LONG, `B` TEXT(40), CONSTRAINT `pk` PRIMARY KEY (`A`))");
        foreach (int key in Keys(workload))
            engine.ExecuteNonQuery($"INSERT INTO `W` (`A`, `B`) VALUES ({key}, 'value {key}')");
    }

    /// <summary>The table's leaf pages: how many, their free space sorted ascending, the total bytes they
    /// actually use, and each page's shared-prefix length (`0x18`) — which is what decides how many entries
    /// a page of a given size holds, and where the two engines differ.</summary>
    private static (int Count, string Free, int Used, string Prefix) Leaves(string workload, bool ace)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "idxsplit-");
        try
        {
            if (ace) AceRun(path, workload); else LibRedRun(path, workload);

            int definitionPage;
            using (var database = JetDatabase.Open(path, readOnly: true))
                definitionPage = database.Catalog.FindTable("W")!.DefinitionPage;

            using var channel = PageChannel.Open(path, readOnly: true);
            var free = new List<int>();
            var prefix = new List<int>();
            int used = 0;
            for (int page = 1; page < channel.PageCount; page++)
            {
                byte[] bytes = channel.ReadPage(page).Span.ToArray();
                if (bytes[0] != 0x04) continue;
                if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)) != definitionPage) continue;

                int pageFree = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2));
                free.Add(pageFree);
                prefix.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x18, 2)));
                used += channel.Format.PageSize - pageFree;
            }
            // Both lists stay in PAGE order. Sorting one and not the other made the two columns disagree
            // about which page was which, which is how "the uncompressed page is the tail" got read off a
            // report that did not say so.
            return (free.Count, string.Join(",", free), used, string.Join(",", prefix));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
