using System.Linq;
using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

/// <summary>
/// EF's "huge parameter collection" primitive-collection queries render as a UNION ALL with one branch per
/// element (up to 5000). A left-nested set-op tree recurses O(n) deep through the binder/planner/executor and
/// the runtime nested Concat, overflowing the stack (an uncatchable crash that kills the test host). The AST is
/// now built as a balanced tree (O(log n) depth); this guards that a large UNION ALL runs correctly.
/// </summary>
public class LargeUnionAllTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(5000)]
    public void Large_union_all_does_not_overflow_and_returns_every_branch(int n)
    {
        // The query is FROM-less, so isolate it from the suite's shared Northwind fixture. Otherwise parallel
        // test deployment/copying can transiently hold that file exclusively before this connection opens.
        string path = Path.Combine(Path.GetTempPath(), $"libred-union-{Guid.NewGuid():N}.accdb");
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={path}");
            using var conn = new LibRedConnection($"Data Source={path}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = string.Join(" UNION ALL ", Enumerable.Range(1, n).Select(i => $"SELECT {i} AS V"));

            int count = 0;
            long sum = 0;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                count++;
                sum += Convert.ToInt64(reader.GetValue(0));
            }

            Assert.Equal(n, count);
            Assert.Equal((long)n * (n + 1) / 2, sum); // order-preserving concat of 1..n
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
