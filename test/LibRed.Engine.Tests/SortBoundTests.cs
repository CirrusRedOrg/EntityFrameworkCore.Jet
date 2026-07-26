using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ORDER BY keys are evaluated once per row rather than inside the comparer, and a `TOP n` above a sort bounds it so
// it keeps only the n smallest instead of ordering everything. Both change HOW rows are ordered internally — the
// second replaced a stable sort with a total order over (keys, input position) — so these pin that the observable
// order is unchanged, in particular that ties still come out in input order.
public class SortBoundTests
{
    private const int Rows = 500;

    private static QueryEngine Ties()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sortb-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));

        // Grp deliberately coarse (5 values over 500 rows) so every ORDER BY on it is 100-way tied, and Id
        // ascending is the insertion order — so "ties in input order" is checkable.
        e.ExecuteNonQuery("CREATE TABLE T ( Id LONG PRIMARY KEY, Grp LONG, Val LONG )");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        for (var i = 1; i <= Rows; i++)
        {
            e.ExecuteNonQuery($"INSERT INTO T (Id, Grp, Val) VALUES ({i}, {i % 5}, {(i * 37) % 101})");
        }

        e.ExecuteNonQuery("COMMIT");
        return e;
    }

    private static long[] Ids(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt64(r[0])).ToArray();

    [Fact]
    public void Ties_keep_input_order()
    {
        // All 100 rows sharing Grp = 1 must appear in ascending Id — the order they were inserted. A sort that
        // reordered ties (an unstable algorithm without the position tiebreak) would scramble these.
        long[] ids = Ids(Ties(), "SELECT t.Id FROM T AS t WHERE t.Grp = 1 ORDER BY t.Grp");
        Assert.Equal(ids.OrderBy(x => x).ToArray(), ids);
    }

    [Theory]
    // A bounded sort must return exactly what ordering everything and taking n returns — including which of the
    // tied rows survive, which is the part a non-total order would get wrong.
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(150)]   // spans several trim cycles (the buffer is trimmed at 2n)
    [InlineData(499)]
    [InlineData(Rows)]  // exactly the row count
    [InlineData(Rows + 50)] // more than exists: the bound must not invent or drop rows
    public void A_bounded_sort_agrees_with_the_full_sort(int take)
    {
        QueryEngine e = Ties();
        long[] full = Ids(e, "SELECT t.Id FROM T AS t ORDER BY t.Grp");
        long[] bounded = Ids(e, $"SELECT TOP {take} t.Id FROM T AS t ORDER BY t.Grp");

        Assert.Equal(full.Take(take).ToArray(), bounded);
    }

    [Fact]
    public void A_bounded_sort_honours_descending_and_multiple_keys()
    {
        QueryEngine e = Ties();
        const string order = "ORDER BY t.Grp DESC, t.Val, t.Id DESC";
        long[] full = Ids(e, $"SELECT t.Id FROM T AS t {order}");
        long[] bounded = Ids(e, $"SELECT TOP 10 t.Id FROM T AS t {order}");

        Assert.Equal(full.Take(10).ToArray(), bounded);
    }

    [Fact]
    public void A_top_over_a_distinct_is_not_bounded_below_it()
    {
        // DISTINCT collapses rows, so the n rows reaching the TOP are not the n the sort would have kept — the
        // planner must leave the sort unbounded here. With 5 distinct Grp values, TOP 3 DISTINCT gives 3 rows;
        // bounding the sort to 3 first would have left only rows sharing the smallest Grp, collapsing to 1.
        QueryEngine e = Ties();
        Assert.Equal(3, e.ExecuteQuery("SELECT DISTINCT TOP 3 t.Grp FROM T AS t ORDER BY t.Grp").Rows.Count());
    }

    [Fact]
    public void A_bounded_sort_still_applies_the_projection_and_filter()
    {
        QueryEngine e = Ties();
        Assert.Equal([1, 6, 11], Ids(e, "SELECT TOP 3 t.Id FROM T AS t WHERE t.Grp = 1 ORDER BY t.Id"));
    }
}
