using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// ORDER BY is a STABLE sort: rows whose ORDER BY keys are equal keep their input order. EF's reference and SQL
/// Server behave this way, so an ORDER BY that does not fully disambiguate (e.g. several orders per customer,
/// ordered only by customer) must return the tied rows in scan/insertion order — a List.Sort would not.
/// </summary>
public class StableSortTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "stable-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id LONG PRIMARY KEY, K TEXT(5), Seq LONG)");
        // Same key K='A' for several rows, inserted in a known Seq order; a stable ORDER BY K keeps that order.
        (int id, string k, int seq)[] rows =
            [(1, "A", 10), (2, "B", 20), (3, "A", 11), (4, "B", 21), (5, "A", 12), (6, "C", 30)];
        foreach (var (id, k, seq) in rows) e.ExecuteNonQuery($"INSERT INTO T (Id, K, Seq) VALUES ({id}, '{k}', {seq})");
        return e;
    }

    [Fact]
    public void Order_by_a_tied_key_preserves_input_order()
    {
        var e = Seeded();
        var seqs = e.ExecuteQuery("SELECT Seq FROM T ORDER BY K").Rows.Select(r => Convert.ToInt64(r[0])).ToList();
        // K ascending: A,A,A,B,B,C — and within each key, insertion (Id/Seq) order.
        Assert.Equal(new long[] { 10, 11, 12, 20, 21, 30 }, seqs);
    }
}
