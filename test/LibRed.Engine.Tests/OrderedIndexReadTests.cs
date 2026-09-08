using LibRed;
using LibRed.Engine;
using LibRed.Engine.Plan;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>TOP n … ORDER BY &lt;indexed&gt;</c> reads the index in key order and stops after n, instead of scanning the
/// table to feed a sort (IndexSelection.OrderedIndexRead → an <see cref="IndexRangeSeekNode"/> with both bounds
/// open). The substitution has to produce the <b>same rows in the same order</b> as the sort did, ties included —
/// so most of these compare it against the unbounded ORDER BY, which never takes the rewrite, and the rest pin
/// the cases it must refuse.
/// </summary>
public class OrderedIndexReadTests : TempDatabaseTest
{
    private const int Rows = 400;

    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ordered-index-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));

        // Id      unique, ascending, the primary key — the shape EF pages with.
        // Grp     8 values over 400 rows, so every ORDER BY on it is 50-way tied.
        // Stamp   temporal, ascending with Id.
        // Nm      indexed TEXT — must NOT take the rewrite (collation is a separate question).
        // Opt     indexed and NULLABLE, a fifth of the rows null, to pin where nulls land.
        // P1, P2  covered ONLY by a composite index, so ordering by P1 alone is a prefix match with no
        //         single-column index to fall back on — the case the exact-match rule has to refuse.
        e.ExecuteNonQuery(
            "CREATE TABLE T (Id LONG PRIMARY KEY, Grp LONG, Stamp DATETIME, Nm TEXT(20), Opt LONG, P1 LONG, P2 LONG)");
        e.ExecuteNonQuery("CREATE INDEX IX_Grp ON T (Grp)");
        e.ExecuteNonQuery("CREATE INDEX IX_Nm ON T (Nm)");
        e.ExecuteNonQuery("CREATE INDEX IX_Opt ON T (Opt)");
        e.ExecuteNonQuery("CREATE INDEX IX_GrpId ON T (Grp, Id)");
        e.ExecuteNonQuery("CREATE INDEX IX_P ON T (P1, P2)");

        e.ExecuteNonQuery("BEGIN TRANSACTION");
        for (int i = 1; i <= Rows; i++)
        {
            string opt = i % 5 == 0 ? "NULL" : $"{(i * 37) % 101}";
            e.ExecuteNonQuery(
                $"INSERT INTO T (Id, Grp, Stamp, Nm, Opt, P1, P2) VALUES "
                + $"({i}, {i % 8}, #2020-01-01#, 'n{i:D4}', {opt}, {i % 4}, {Rows - i})");
        }

        e.ExecuteNonQuery("COMMIT");
        return e;
    }

    private static long[] Ids(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt64(r[0])).ToArray();

    private static bool ReadsAnIndexInOrder(PlanNode node)
        => node is IndexRangeSeekNode { Low: null, High: null } || node.Children.Any(ReadsAnIndexInOrder);

    private static bool ContainsSort(PlanNode node)
        => node is SortNode || node.Children.Any(ContainsSort);

    // --- the rewrite fires, and the answer is unchanged -------------------------------------------------

    [Fact]
    public void Top_n_ordered_by_the_primary_key_reads_the_index_instead_of_sorting()
    {
        PlanNode plan = Seeded().PlanFor("SELECT t.Id FROM T AS t ORDER BY t.Id");
        Assert.True(ContainsSort(plan), "an unbounded ORDER BY must still sort");

        plan = Seeded().PlanFor("SELECT TOP 10 t.Id FROM T AS t ORDER BY t.Id");
        Assert.True(ReadsAnIndexInOrder(plan), "expected an ordered index read");
        Assert.False(ContainsSort(plan), "the sort must be gone, not merely fed differently");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(Rows - 1)]
    [InlineData(Rows)]
    [InlineData(Rows + 50)]  // more than exists: must not invent or drop rows
    public void An_ordered_index_read_agrees_with_the_sort_it_replaces(int take)
    {
        QueryEngine e = Seeded();
        long[] sorted = Ids(e, "SELECT t.Id FROM T AS t ORDER BY t.Id");
        long[] viaIndex = Ids(e, $"SELECT TOP {take} t.Id FROM T AS t ORDER BY t.Id");

        Assert.Equal(sorted.Take(take).ToArray(), viaIndex);
    }

    [Fact]
    public void Ties_come_out_in_the_same_order_as_the_sort_gives_them()
    {
        // Grp is 50-way tied. The index orders equal keys by row pointer (physical position); the sort breaks
        // ties by input position, which a scan feeds in that same physical order. They must agree.
        QueryEngine e = Seeded();
        long[] sorted = Ids(e, "SELECT t.Id FROM T AS t ORDER BY t.Grp");
        long[] viaIndex = Ids(e, "SELECT TOP 120 t.Id FROM T AS t ORDER BY t.Grp");

        Assert.True(ReadsAnIndexInOrder(e.PlanFor("SELECT TOP 120 t.Id FROM T AS t ORDER BY t.Grp")));
        Assert.Equal(sorted.Take(120).ToArray(), viaIndex);
    }

    [Fact]
    public void Nulls_sort_first_exactly_as_the_sort_puts_them()
    {
        // The index stores a null key as 0x00 against 0x7F for a present value, so nulls lead — which is where
        // CompareForSort puts them too. A null-dropping read would be caught by the row count as well as order.
        QueryEngine e = Seeded();
        long[] sorted = Ids(e, "SELECT t.Id FROM T AS t ORDER BY t.Opt");
        long[] viaIndex = Ids(e, $"SELECT TOP {Rows} t.Id FROM T AS t ORDER BY t.Opt");

        Assert.Equal(Rows, viaIndex.Length);
        Assert.Equal(sorted, viaIndex);
    }

    [Fact]
    public void A_composite_index_matched_in_full_is_read_in_order()
    {
        QueryEngine e = Seeded();
        long[] sorted = Ids(e, "SELECT t.Id FROM T AS t ORDER BY t.Grp, t.Id");
        long[] viaIndex = Ids(e, "SELECT TOP 40 t.Id FROM T AS t ORDER BY t.Grp, t.Id");

        Assert.True(ReadsAnIndexInOrder(e.PlanFor("SELECT TOP 40 t.Id FROM T AS t ORDER BY t.Grp, t.Id")));
        Assert.Equal(sorted.Take(40).ToArray(), viaIndex);
    }

    [Fact]
    public void A_temporal_key_is_eligible()
        => Assert.Equal(
            Ids(Seeded(), "SELECT t.Id FROM T AS t ORDER BY t.Stamp").Take(5).ToArray(),
            Ids(Seeded(), "SELECT TOP 5 t.Id FROM T AS t ORDER BY t.Stamp"));

    // --- the cases it must refuse ------------------------------------------------------------------------

    [Theory]
    // Descending: the index is stored ascending and the range seek walks the leaf chain forwards only.
    [InlineData("SELECT TOP 10 t.Id FROM T AS t ORDER BY t.Id DESC")]
    // Text: the key is an NLS sort key, and whether it orders identically to the evaluator's comparison is a
    // separate question that wants checking against ACE first.
    [InlineData("SELECT TOP 10 t.Id FROM T AS t ORDER BY t.Nm")]
    // Only a PREFIX of the one index that covers P1: reading IX_P would order the ties by P2 while the sort
    // leaves them in input order — a legal answer, but a different one, so the rewrite must decline.
    [InlineData("SELECT TOP 10 t.Id FROM T AS t ORDER BY t.P1")]
    // Right leading column, wrong second one: IX_GrpId is (Grp, Id), not (Grp, Stamp).
    [InlineData("SELECT TOP 10 t.Id FROM T AS t ORDER BY t.Grp, t.Stamp")]
    // Not a bare column, so it is not the index's key at all.
    [InlineData("SELECT TOP 10 t.Id FROM T AS t ORDER BY t.Id + 0")]
    // Both columns are indexed, but no index has (Id, Grp) as its key.
    [InlineData("SELECT TOP 10 t.Id FROM T AS t ORDER BY t.Id, t.Grp")]
    public void An_ineligible_ordering_still_sorts(string sql)
    {
        PlanNode plan = Seeded().PlanFor(sql);
        Assert.True(ContainsSort(plan), "expected the sort to be kept");
        Assert.False(ReadsAnIndexInOrder(plan), "expected no ordered index read");
    }

    [Fact]
    public void Refusing_the_rewrite_still_returns_the_right_rows()
    {
        // The refusals above are asserted on the plan; this pins that the answers they fall back to are right,
        // so a future eligibility change cannot quietly break the descending case.
        QueryEngine e = Seeded();
        Assert.Equal([Rows, Rows - 1, Rows - 2], Ids(e, "SELECT TOP 3 t.Id FROM T AS t ORDER BY t.Id DESC"));
        Assert.Equal([1, 2, 3], Ids(e, "SELECT TOP 3 t.Id FROM T AS t ORDER BY t.Nm"));

        // The prefix refusal must still be ordered by P1 alone, with ties in input order — which is what the
        // sort gives and what reading IX_P (ordered by P2 within P1) would not.
        Assert.Equal(
            Ids(e, "SELECT t.Id FROM T AS t ORDER BY t.P1").Take(6).ToArray(),
            Ids(e, "SELECT TOP 6 t.Id FROM T AS t ORDER BY t.P1"));
    }

    [Fact]
    public void An_offset_still_pages_correctly_through_the_ordered_read()
    {
        // The limit above the ordered read does the skipping, and the bound the planner hands the (now absent)
        // sort was skip + take — so paging must land on rows 21-25, not 1-5.
        QueryEngine e = Seeded();
        Assert.Equal(
            [21, 22, 23, 24, 25],
            Ids(e, "SELECT TOP 5 t.Id FROM T AS t ORDER BY t.Id OFFSET 20 ROWS"));
    }
}
