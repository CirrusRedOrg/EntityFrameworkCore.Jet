using LibRed;
using LibRed.Engine;
using LibRed.Engine.Plan;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A table read decodes only the columns its statement names (<c>ColumnPruning</c>, and <c>JoinRows</c> for
/// UPDATE and DELETE), and an undecoded column reads as null. So what needs pinning is that nothing which reads
/// a column goes without it: every place a column can be named, every shape that passes a whole row to the
/// output, and every write — which has to rewrite, check and re-index a row from all of its values, including
/// the ones the statement never mentioned.
/// </summary>
public class ColumnPruningTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "column-pruning-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));

        e.ExecuteNonQuery("CREATE TABLE T (Id LONG PRIMARY KEY, Grp LONG, V LONG, Label TEXT(20), Note TEXT(50))");
        e.ExecuteNonQuery("CREATE INDEX IX_Label ON T (Label)");
        e.ExecuteNonQuery("CREATE INDEX IX_V ON T (V)");
        e.ExecuteNonQuery("CREATE TABLE U (K LONG PRIMARY KEY, X LONG, Descr TEXT(20))");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        for (int i = 1; i <= 12; i++)
            e.ExecuteNonQuery($"INSERT INTO T (Id, Grp, V, Label, Note) VALUES ({i}, {i % 3}, {i * 10}, 'L{i}', 'N{i}')");
        for (int k = 0; k <= 2; k++)
            e.ExecuteNonQuery($"INSERT INTO U (K, X, Descr) VALUES ({k}, {k * 100}, 'D{k}')");
        e.ExecuteNonQuery("COMMIT");
        return e;
    }

    private static List<object?[]> Rows(QueryEngine e, string sql) => [.. e.ExecuteQuery(sql).Rows];

    private static int[] Ints(QueryEngine e, string sql) => [.. Rows(e, sql).Select(r => Convert.ToInt32(r[0]))];

    private static int[] Sorted(QueryEngine e, string sql) => [.. Ints(e, sql).Order()];

    private static object?[] RowOfT(QueryEngine e, int id) => Rows(e, $"SELECT * FROM T WHERE Id = {id}").Single();

    // --- every place a column can be read from, each the only place that names it -----------------------

    [Fact]
    public void A_column_named_only_in_the_where_is_read()
        => Assert.Equal([10, 11, 12], Sorted(Seeded(), "SELECT Id FROM T WHERE V > 90"));

    [Fact]
    public void A_column_named_only_in_the_order_by_is_read()
        => Assert.Equal([12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1], Ints(Seeded(), "SELECT Id FROM T ORDER BY V DESC"));

    [Fact]
    public void A_column_named_only_in_the_group_by_is_read()
        => Assert.Equal([4, 4, 4], Ints(Seeded(), "SELECT COUNT(*) FROM T GROUP BY Grp"));

    [Fact]
    public void A_column_named_only_in_an_aggregate_is_read()
        => Assert.Equal([780], Ints(Seeded(), "SELECT SUM(V) FROM T"));

    [Fact]
    public void A_column_named_only_in_the_having_is_read()
        // Group sums: 0 → 300, 1 → 220, 2 → 260.
        => Assert.Equal([0], Ints(Seeded(), "SELECT Grp FROM T GROUP BY Grp HAVING SUM(V) > 260"));

    [Fact]
    public void Columns_named_only_in_a_join_are_read()
        => Assert.Equal([1, 4, 7, 10], Sorted(Seeded(), "SELECT T.Id FROM T INNER JOIN U ON T.Grp = U.K WHERE U.X = 100"));

    [Fact]
    public void Columns_named_only_in_a_hash_join_and_its_projection_are_read()
    {
        var rows = Rows(Seeded(), "SELECT T.Id, U.Descr FROM T INNER JOIN U ON T.Grp = U.X")
            .Select(r => (Convert.ToInt32(r[0]), (string)r[1]!)).Order().ToArray();
        Assert.Equal([(3, "D0"), (6, "D0"), (9, "D0"), (12, "D0")], rows);
    }

    [Fact]
    public void An_outer_column_named_only_inside_a_correlated_subquery_is_read()
    {
        QueryEngine e = Seeded();
        Assert.Equal([10], Ints(e, "SELECT t.Id FROM T AS t WHERE EXISTS (SELECT 1 FROM U AS u WHERE u.X = t.V)"));

        var rows = Rows(e, "SELECT t.Id, (SELECT u.Descr FROM U AS u WHERE u.K = t.Grp) FROM T AS t WHERE t.Id <= 3")
            .Select(r => (Convert.ToInt32(r[0]), (string)r[1]!)).ToArray();
        Assert.Equal([(1, "D1"), (2, "D2"), (3, "D0")], rows);
    }

    // --- a join passes on only the columns its statement names, and builds its rows that narrow -----------

    [Fact]
    public void An_aggregate_over_a_join_reads_what_it_names()
    {
        // Group sums of V over T.Grp = U.K: 0 → 300, 1 → 220, 2 → 260.
        var rows = Rows(Seeded(), "SELECT U.Descr, SUM(T.V) FROM T INNER JOIN U ON T.Grp = U.K GROUP BY U.Descr")
            .Select(r => ((string)r[0]!, Convert.ToInt32(r[1]))).ToArray();
        Assert.Equal([("D0", 300), ("D1", 220), ("D2", 260)], rows);
    }

    [Fact]
    public void A_narrowed_left_join_pads_its_unmatched_rows()
    {
        // Only V = 100 meets an X; every other T row comes back with a null Descr.
        var rows = Rows(Seeded(), "SELECT T.Id, U.Descr FROM T LEFT JOIN U ON T.V = U.X")
            .ToDictionary(r => Convert.ToInt32(r[0]), r => r[1]);
        Assert.Equal(12, rows.Count);
        Assert.Equal("D1", rows[10]);
        Assert.All(rows.Where(p => p.Key != 10), p => Assert.Null(p.Value));
    }

    [Fact]
    public void A_narrowed_full_join_pads_either_side()
    {
        // U rows 0 and 2 (X 0 and 200) meet no V, so they come back with a null Id; T row 10 meets K 1.
        var rows = Rows(Seeded(), "SELECT T.Id, U.K FROM T FULL JOIN U ON T.V = U.X")
            .Select(r => (r[0] is null ? (int?)null : Convert.ToInt32(r[0]), r[1] is null ? (int?)null : Convert.ToInt32(r[1])))
            .ToList();
        Assert.Equal(14, rows.Count);
        Assert.Contains((10, 1), rows);
        Assert.Contains((null, 0), rows);
        Assert.Contains((null, 2), rows);
    }

    [Fact]
    public void A_name_both_sides_share_is_still_ambiguous_when_the_join_is_narrowed()
        // Every column a reference could mean is kept, so an unqualified Id still finds two.
        => Assert.ThrowsAny<InvalidOperationException>(() =>
            Rows(Seeded(), "SELECT Id FROM T AS a INNER JOIN T AS b ON a.Grp = b.Grp"));

    [Fact]
    public void A_column_named_only_in_an_in_subquery_is_read()
        => Assert.Equal([10], Ints(Seeded(), "SELECT Id FROM T WHERE V IN (SELECT X FROM U)"));

    [Fact]
    public void Columns_named_only_in_a_window_are_read()
    {
        var numbers = Rows(Seeded(), "SELECT Id, ROW_NUMBER() OVER (PARTITION BY Grp ORDER BY V DESC) FROM T")
            .ToDictionary(r => Convert.ToInt32(r[0]), r => Convert.ToInt32(r[1]));
        Assert.Equal(1, numbers[12]);
        Assert.Equal(2, numbers[9]);
        Assert.Equal(4, numbers[3]);
        Assert.Equal(1, numbers[10]);
        Assert.Equal(4, numbers[1]);
    }

    [Fact]
    public void A_derived_star_is_read_for_the_columns_its_outer_query_names()
        => Assert.Equal([11, 12], Sorted(Seeded(), "SELECT d.Id FROM (SELECT * FROM T) AS d WHERE d.V > 100"));

    [Fact]
    public void Seeks_decode_what_their_residual_and_projection_name()
    {
        QueryEngine e = Seeded();
        Assert.Equal([5], Ints(e, "SELECT Id FROM T WHERE Label = 'L5' AND V = 50"));
        Assert.Equal(["L3", "L4", "L5"],
            Rows(e, "SELECT Label FROM T WHERE V BETWEEN 30 AND 50").Select(r => (string)r[0]!).Order().ToArray());
    }

    [Fact]
    public void A_bare_count_reads_no_column_and_still_counts_every_row()
        => Assert.Equal([12], Ints(Seeded(), "SELECT COUNT(*) FROM T"));

    // --- shapes that pass whole rows to the output, which must not be pruned ---------------------------

    [Fact]
    public void Select_star_returns_every_column()
        => Assert.Equal([5, 2, 50, "L5", "N5"], RowOfT(Seeded(), 5).Select(Normalise));

    [Fact]
    public void A_qualified_star_returns_every_column_of_its_table()
    {
        object?[] row = Rows(Seeded(), "SELECT T.*, U.Descr FROM T INNER JOIN U ON T.Grp = U.K WHERE T.Id = 5").Single();
        Assert.Equal([5, 2, 50, "L5", "N5", "D2"], row.Select(Normalise));
    }

    [Fact]
    public void A_union_of_stars_returns_every_column()
    {
        var rows = Rows(Seeded(), "SELECT * FROM T WHERE Id <= 2 UNION SELECT * FROM T WHERE Id <= 2");
        Assert.Equal(["N1", "N2"], rows.Select(r => (string)r[4]!).Order());
    }

    [Fact]
    public void Distinctrow_still_tells_underlying_rows_apart_by_columns_it_does_not_output()
    {
        // Grp 0 joins three U rows, 1 two, 2 one: 24 joined rows over the 12 of T. DISTINCTROW keeps one per T
        // row. Deciding that from Grp alone — all a pruned read would have — collapses them to three.
        Assert.Equal(24, Rows(Seeded(), "SELECT T.Grp FROM T INNER JOIN U ON U.K >= T.Grp").Count);
        Assert.Equal(12, Rows(Seeded(), "SELECT DISTINCTROW T.Grp FROM T INNER JOIN U ON U.K >= T.Grp").Count);
    }

    // --- the plan: which reads are pruned, and to what ---------------------------------------------------

    private static IEnumerable<PlanNode> Nodes(PlanNode node) => [node, .. node.Children.SelectMany(Nodes)];

    private static IReadOnlySet<string>? DecodeOf(PlanNode node) => node switch
    {
        ScanNode s => s.Decode,
        IndexSeekNode s => s.Decode,
        IndexRangeSeekNode s => s.Decode,
        _ => throw new InvalidOperationException($"{node.GetType().Name} is not a table read."),
    };

    private static IEnumerable<PlanNode> Reads(PlanNode plan) =>
        Nodes(plan).Where(n => n is ScanNode or IndexSeekNode or IndexRangeSeekNode);

    [Fact]
    public void A_projection_prunes_its_scan_to_the_names_it_reads()
    {
        IReadOnlySet<string>? decode = DecodeOf(Reads(Seeded().PlanFor("SELECT Id, Label FROM T WHERE Grp = 1")).Single());
        Assert.NotNull(decode);
        Assert.Contains("Id", decode);
        Assert.Contains("Label", decode);
        Assert.Contains("Grp", decode);
        Assert.DoesNotContain("Note", decode);
    }

    private static IReadOnlySet<string>? KeepOf(PlanNode join) => join switch
    {
        JoinNode j => j.Keep,
        HashJoinNode h => h.Keep,
        _ => throw new InvalidOperationException($"{join.GetType().Name} is not a join."),
    };

    [Fact]
    public void A_join_under_an_aggregate_keeps_the_names_it_reads()
    {
        PlanNode join = Nodes(Seeded().PlanFor(
            "SELECT U.Descr, SUM(T.V) FROM T INNER JOIN U ON T.Grp = U.K GROUP BY U.Descr"))
            .Single(n => n is JoinNode or HashJoinNode);
        IReadOnlySet<string>? keep = KeepOf(join);
        Assert.NotNull(keep);
        Assert.Superset(new HashSet<string>(["Descr", "V", "Grp", "K"]), new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain("Note", keep);
    }

    [Theory]
    [InlineData("SELECT * FROM T INNER JOIN U ON T.Grp = U.K")]
    [InlineData("SELECT T.*, U.Descr FROM T INNER JOIN U ON T.Grp = U.K")]
    [InlineData("SELECT DISTINCTROW T.Grp FROM T INNER JOIN U ON U.K >= T.Grp")]
    public void A_join_whose_rows_can_reach_the_output_whole_keeps_every_column(string sql)
        => Assert.All(Nodes(Seeded().PlanFor(sql)).Where(n => n is JoinNode or HashJoinNode),
            join => Assert.Null(KeepOf(join)));

    [Theory]
    [InlineData("SELECT * FROM T")]
    [InlineData("SELECT DISTINCT * FROM T")]
    [InlineData("SELECT * FROM T WHERE Id = 3")]
    [InlineData("SELECT DISTINCTROW T.Grp FROM T INNER JOIN U ON U.K >= T.Grp")]
    public void A_read_whose_rows_can_reach_the_output_whole_is_not_pruned(string sql)
        => Assert.All(Reads(Seeded().PlanFor(sql)), read => Assert.Null(DecodeOf(read)));

    // --- writes: a row is rewritten, checked and re-indexed from every value it has -----------------------

    [Fact]
    public void An_update_keeps_the_columns_it_never_names()
    {
        QueryEngine e = Seeded();
        Assert.Equal(4, e.ExecuteNonQuery("UPDATE T SET V = V + 1 WHERE Grp = 1"));
        Assert.Equal([1, 1, 11, "L1", "N1"], RowOfT(e, 1).Select(Normalise));
        Assert.Equal([2, 2, 20, "L2", "N2"], RowOfT(e, 2).Select(Normalise));
    }

    [Fact]
    public void A_joined_update_keeps_both_tables_columns()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("UPDATE T INNER JOIN U ON T.Grp = U.K SET T.V = U.X WHERE T.Id = 4");
        Assert.Equal([4, 1, 100, "L4", "N4"], RowOfT(e, 4).Select(Normalise));
        Assert.Equal([1, 100, "D1"], Rows(e, "SELECT * FROM U WHERE K = 1").Single().Select(Normalise));
    }

    [Fact]
    public void An_update_moves_the_index_entry_of_a_column_it_only_assigns()
    {
        // Label appears only as a SET target, so the read that finds the row does not decode it; the old index
        // entry can only be removed if the row is read in full before it is rewritten.
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("UPDATE T SET Label = 'Z' WHERE Id = 2");
        Assert.Equal([2], Ints(e, "SELECT Id FROM T WHERE Label = 'Z'"));
        Assert.Empty(Ints(e, "SELECT Id FROM T WHERE Label = 'L2'"));
        Assert.Equal([2, 2, 20, "Z", "N2"], RowOfT(e, 2).Select(Normalise));
    }

    [Fact]
    public void A_delete_removes_the_index_entries_of_columns_it_never_names()
    {
        QueryEngine e = Seeded();
        Assert.Equal(1, e.ExecuteNonQuery("DELETE FROM T WHERE V = 30"));
        Assert.Empty(Ints(e, "SELECT Id FROM T WHERE Label = 'L3'"));
        Assert.Equal([11], Ints(e, "SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void An_update_through_a_derived_table_keeps_the_columns_it_does_not_select()
    {
        QueryEngine e = Seeded();
        Assert.Equal(4, e.ExecuteNonQuery("UPDATE (SELECT Id, V FROM T WHERE Grp = 2) SET V = 0"));
        Assert.Equal([5, 2, 0, "L5", "N5"], RowOfT(e, 5).Select(Normalise));
    }

    [Fact]
    public void Cascades_and_key_checks_read_what_they_need()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("CREATE TABLE Par (Id LONG PRIMARY KEY, Nm TEXT(20))");
        e.ExecuteNonQuery("CREATE TABLE Ch (Id LONG PRIMARY KEY, Pid LONG, Info TEXT(20), " +
            "CONSTRAINT fk FOREIGN KEY (Pid) REFERENCES Par (Id) ON UPDATE CASCADE ON DELETE CASCADE)");
        e.ExecuteNonQuery("INSERT INTO Par (Id, Nm) VALUES (1, 'p1')");
        e.ExecuteNonQuery("INSERT INTO Ch (Id, Pid, Info) VALUES (1, 1, 'c1')");
        e.ExecuteNonQuery("INSERT INTO Ch (Id, Pid, Info) VALUES (2, 1, 'c2')");

        // The parent check reads the key alone, and still finds a missing parent.
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO Ch (Id, Pid, Info) VALUES (3, 99, 'x')"));

        // An ON UPDATE CASCADE rewrites each child from all of its values, not just its key.
        e.ExecuteNonQuery("UPDATE Par SET Id = 10 WHERE Id = 1");
        Assert.Equal([[1, 10, "c1"], [2, 10, "c2"]],
            Rows(e, "SELECT * FROM Ch ORDER BY Id").Select(r => r.Select(Normalise).ToArray()).ToArray());

        e.ExecuteNonQuery("DELETE FROM Par WHERE Id = 10");
        Assert.Empty(Rows(e, "SELECT * FROM Ch"));
    }

    [Fact]
    public void Insert_select_and_select_into_carry_the_columns_they_name()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("CREATE TABLE T2 (Id LONG, Note TEXT(50))");
        e.ExecuteNonQuery("INSERT INTO T2 (Id, Note) SELECT Id, Note FROM T WHERE Grp = 0");
        Assert.Equal(["N12", "N3", "N6", "N9"], Rows(e, "SELECT Note FROM T2").Select(r => (string)r[0]!).Order());

        e.ExecuteNonQuery("SELECT Id, Label INTO T3 FROM T WHERE Grp = 1");
        Assert.Equal(["L1", "L10", "L4", "L7"], Rows(e, "SELECT Label FROM T3").Select(r => (string)r[0]!).Order());
    }

    private static object? Normalise(object? value) => value is int or short or long or byte ? Convert.ToInt32(value) : value;
}
