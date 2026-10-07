using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Filter, projection, sort, grouping and aggregation evaluate every row through one scope and evaluator,
/// rebound per row, instead of allocating a pair per row. The rebind is only sound if nothing outlives its row
/// and no two enumerations share a scope, so these pin both: a result enumerated twice, or two enumerations
/// interleaved, each see their own rows, and a correlated subquery sees the row it was asked about.
/// </summary>
public class RowScopeReuseTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "row-scope-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));

        e.ExecuteNonQuery("CREATE TABLE T (Id LONG PRIMARY KEY, Grp LONG, V LONG)");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        for (int i = 1; i <= 12; i++)
            e.ExecuteNonQuery($"INSERT INTO T (Id, Grp, V) VALUES ({i}, {i % 3}, {i * 10})");
        e.ExecuteNonQuery("COMMIT");
        return e;
    }

    private const string FilteredProjection = "SELECT Id, V + Id FROM T WHERE V + Id > 40";

    private static readonly (int Id, int Sum)[] Expected =
        [.. Enumerable.Range(4, 9).Select(i => (i, i * 11))];

    private static (int, int) Read(object?[] row) => (Convert.ToInt32(row[0]), Convert.ToInt32(row[1]));

    [Fact]
    public void A_result_enumerated_twice_gives_the_same_rows()
    {
        IEnumerable<object?[]> rows = Seeded().ExecuteQuery(FilteredProjection).Rows;
        Assert.Equal(Expected, rows.Select(Read).ToArray());
        Assert.Equal(Expected, rows.Select(Read).ToArray());
    }

    [Fact]
    public void Interleaved_enumerations_do_not_move_each_others_row()
    {
        IEnumerable<object?[]> rows = Seeded().ExecuteQuery(FilteredProjection).Rows;
        using IEnumerator<object?[]> ahead = rows.GetEnumerator();
        using IEnumerator<object?[]> behind = rows.GetEnumerator();

        var fromAhead = new List<(int, int)>();
        var fromBehind = new List<(int, int)>();
        Assert.True(ahead.MoveNext());
        fromAhead.Add(Read(ahead.Current));
        while (true)
        {
            bool a = ahead.MoveNext();
            if (a) fromAhead.Add(Read(ahead.Current));
            bool b = behind.MoveNext();
            if (b) fromBehind.Add(Read(behind.Current));
            if (!a && !b) break;
        }

        Assert.Equal(Expected, fromAhead);
        Assert.Equal(Expected, fromBehind);
    }

    [Fact]
    public void A_correlated_subquery_sees_the_row_being_filtered_and_projected()
    {
        var rows = Seeded().ExecuteQuery(
            "SELECT t.Id, (SELECT COUNT(*) FROM T AS i WHERE i.Grp = t.Grp AND i.Id < t.Id) FROM T AS t " +
            "WHERE (SELECT MAX(i.Id) FROM T AS i WHERE i.Grp = t.Grp) > t.Id").Rows
            .Select(Read).ToArray();

        // Ids 1..9 each have a later row in their group; each is preceded by (Id - 1) / 3 earlier ones.
        Assert.Equal([.. Enumerable.Range(1, 9).Select(i => (i, (i - 1) / 3))], rows);
    }

    [Fact]
    public void Aggregates_evaluate_each_row_of_their_own_group()
    {
        var rows = Seeded().ExecuteQuery(
            "SELECT Grp, SUM(V), FIRST(Id), LAST(Id), COUNT(*) FILTER (WHERE V > 60) FROM T GROUP BY Grp").Rows
            .Select(r => r.Select(Convert.ToInt32).ToArray()).ToArray();

        Assert.Equal(
            [
                [0, 300, 3, 12, 2],   // 3, 6, 9, 12
                [1, 220, 1, 10, 2],   // 1, 4, 7, 10
                [2, 260, 2, 11, 2],   // 2, 5, 8, 11
            ],
            rows);
    }
}
