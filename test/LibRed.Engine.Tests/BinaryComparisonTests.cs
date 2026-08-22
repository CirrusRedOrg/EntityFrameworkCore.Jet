using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// byte[] (Binary) column comparison in WHERE/ORDER BY. The regression: byte[] has no IComparable and
// isn't numeric/string, so it fell through to ToString() — "System.Byte[]" for every array — making all
// binaries compare equal, so `WHERE k = @p` matched every row ("Sequence contains more than one element"
// in EF's EverythingIsBytes suite). Fixed with a structural, length-sensitive byte compare.
public class BinaryComparisonTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "bin-cmp-");
        return path;
    }

    private static void Run(Action<QueryEngine> act)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            // VARBINARY (variable length) — the test stores byte arrays of differing lengths and checks
            // length-sensitive ordering/equality. (A fixed BINARY(50) would pad every value to 50 bytes.)
            e.ExecuteNonQuery("CREATE TABLE B (Id long PRIMARY KEY, K VARBINARY(50))");
            Ins(e, 1, [1, 2, 3]);
            Ins(e, 2, [1, 2, 3, 4]);
            Ins(e, 3, [2]);
            Ins(e, 4, [1, 2, 3, 4, 5]);
            act(e);
        }
        finally { TemporaryDatabase.Delete(path); }

        static void Ins(QueryEngine e, int id, byte[] k) =>
            e.ExecuteNonQuery("INSERT INTO B (Id, K) VALUES (@id, @k)",
                new Dictionary<string, object?> { ["id"] = id, ["k"] = k });
    }

    [Fact]
    public void Equality_matches_only_the_exact_binary_value()
    {
        Run(e =>
        {
            int[] Match(byte[] p) => e.ExecuteQuery("SELECT Id FROM B WHERE K = @p",
                new Dictionary<string, object?> { ["p"] = p }).Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

            Assert.Equal([2], Match([1, 2, 3, 4]));    // exact 4-byte value → one row (not every row)
            Assert.Equal([1], Match([1, 2, 3]));       // a prefix of others matches only itself
            Assert.Equal([3], Match([2]));
            Assert.Empty(Match([9, 9]));               // no match
        });
    }

    [Fact]
    public void Order_by_binary_sorts_lexicographically_then_by_length()
    {
        Run(e =>
        {
            // {1,2,3} < {1,2,3,4} < {1,2,3,4,5} < {2}  → Ids 1, 2, 4, 3
            int[] ordered = e.ExecuteQuery("SELECT Id FROM B ORDER BY K")
                .Rows.Select(r => Convert.ToInt32(r[0])).ToArray();
            Assert.Equal([1, 2, 4, 3], ordered);
        });
    }
}
