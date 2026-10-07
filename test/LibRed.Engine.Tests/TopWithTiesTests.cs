using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>TOP n [PERCENT] WITH TIES</c> and <c>FETCH … WITH TIES</c>: the n rows, and every further row whose ORDER BY keys
/// equal the last one's. ACE's own <c>TOP n</c> always keeps the ties, and the rows each case expects here are the ones
/// ACE's plain TOP returned over the same data (verified; <c>TopWithTiesAccessTests</c> checks it). LibRed's plain TOP
/// stays exactly n.
/// </summary>
public class TopWithTiesTests(TopWithTiesTests.Database database) : TempDatabaseTest, IClassFixture<TopWithTiesTests.Database>
{
    // K ties three ways at 2 and two ways at 3 and at Null (which sorts first); K2 breaks some of those ties.
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, K LONG, K2 TEXT(10))",
        "INSERT INTO T VALUES (1, 1, 'a')",
        "INSERT INTO T VALUES (2, 2, 'b')",
        "INSERT INTO T VALUES (3, 2, 'a')",
        "INSERT INTO T VALUES (4, 2, 'b')",
        "INSERT INTO T VALUES (5, 3, 'a')",
        "INSERT INTO T VALUES (6, 3, 'a')",
        "INSERT INTO T VALUES (7, 10, 'c')",
        "INSERT INTO T VALUES (8, NULL, NULL)",
        "INSERT INTO T VALUES (9, NULL, 'z')",
    ];

    public sealed class Database() : SharedDatabase("ties-", Setup);

    private string Ids(string sql) =>
        string.Join(",", database.Query(sql, CultureInfo.InvariantCulture).Rows.Select(row => Convert.ToString(row[0], CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("SELECT TOP 1 WITH TIES Id FROM T ORDER BY K", "8,9")]
    [InlineData("SELECT TOP 2 WITH TIES Id FROM T ORDER BY K", "8,9")]
    [InlineData("SELECT TOP 3 WITH TIES Id FROM T ORDER BY K", "8,9,1")]
    [InlineData("SELECT TOP 4 WITH TIES Id FROM T ORDER BY K", "8,9,1,2,3,4")]
    [InlineData("SELECT TOP 6 WITH TIES Id FROM T ORDER BY K", "8,9,1,2,3,4")]
    [InlineData("SELECT TOP 3 WITH TIES Id FROM T ORDER BY K, K2", "8,9,1")]
    [InlineData("SELECT TOP 1 WITH TIES Id FROM T ORDER BY K DESC", "7")]
    [InlineData("SELECT TOP 2 WITH TIES Id FROM T ORDER BY K DESC", "7,5,6")]
    [InlineData("SELECT TOP 5 WITH TIES Id FROM T ORDER BY K2", "8,1,3,5,6")]
    [InlineData("SELECT TOP 2 WITH TIES Id FROM T ORDER BY K MOD 2", "8,9")]
    [InlineData("SELECT TOP 2 WITH TIES Id FROM T WHERE K IS NOT NULL ORDER BY K", "1,2,3,4")]
    [InlineData("SELECT TOP 30 PERCENT WITH TIES Id FROM T ORDER BY K", "8,9,1")]
    [InlineData("SELECT TOP 50 PERCENT WITH TIES Id FROM T ORDER BY K", "8,9,1,2,3,4")]
    [InlineData("SELECT TOP 0 WITH TIES Id FROM T ORDER BY K", "")]
    public void The_ties_of_the_last_row_are_kept(string sql, string expected) =>
        Assert.Equal(expected, Ids(sql));

    [Fact]
    public void Plain_top_stays_exactly_n() =>
        Assert.Equal("8,9,1,2", Ids("SELECT TOP 4 Id FROM T ORDER BY K"));

    [Fact]
    public void Ties_are_cut_across_a_join() =>
        Assert.Equal("5,6", Ids("SELECT TOP 1 WITH TIES a.Id FROM T AS a INNER JOIN T AS b ON a.Id = b.Id WHERE a.K < 10 ORDER BY a.K DESC"));

    // Groups by K2: a has 4 rows, b 2, and c, z and Null 1 each — so the third place is a three-way tie.
    [Fact]
    public void A_grouped_query_ties_its_groups()
    {
        var rows = database.Query(
            "SELECT TOP 3 WITH TIES K2, COUNT(*) AS N FROM T GROUP BY K2 ORDER BY COUNT(*) DESC", CultureInfo.InvariantCulture).Rows;
        Assert.Equal(["a:4", "b:2"], rows.Take(2).Select(row => $"{row[0]}:{row[1]}"));
        Assert.Equal(["(null):1", "c:1", "z:1"], rows.Skip(2).Select(row => $"{row[0] ?? "(null)"}:{row[1]}").Order());
    }

    [Theory]
    [InlineData("SELECT Id FROM T ORDER BY K FETCH FIRST 3 ROWS WITH TIES", "8,9,1")]
    [InlineData("SELECT Id FROM T ORDER BY K OFFSET 2 ROWS FETCH NEXT 2 ROWS WITH TIES", "1,2,3,4")]
    [InlineData("SELECT Id FROM T ORDER BY K OFFSET 2 ROWS FETCH NEXT 1 ROW WITH TIES", "1")]
    [InlineData("SELECT Id FROM T UNION ALL SELECT Id FROM T ORDER BY Id FETCH FIRST 1 ROWS WITH TIES", "1,1")]
    public void Fetch_takes_with_ties_as_well(string sql, string expected) =>
        Assert.Equal(expected, Ids(sql));

    [Fact]
    public void With_ties_needs_an_order_by() =>
        Assert.Throws<InvalidOperationException>(() => Ids("SELECT TOP 2 WITH TIES Id FROM T"));

    [Fact]
    public void With_ties_is_refused_beside_distinct() =>
        Assert.Throws<NotSupportedException>(() => Ids("SELECT DISTINCT TOP 2 WITH TIES K FROM T ORDER BY K"));

    [Fact]
    public void A_view_cannot_store_with_ties()
    {
        QueryEngine engine = SharedDatabase.Fresh("ties-view-", Setup);
        Assert.Throws<NotSupportedException>(() =>
            engine.ExecuteNonQuery("CREATE VIEW V AS SELECT TOP 2 WITH TIES Id FROM T ORDER BY K"));
    }
}
