using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>ORDER BY n</c>, where n is a whole number written as such, sorts by the nth output column, as ACE and SQL-92
/// read it; any other constant sorts nothing. A position that names no column is an error. The expected orders
/// were measured against ACE.
/// </summary>
public class OrderByPositionTests(OrderByPositionTests.Database database)
    : TempDatabaseTest, IClassFixture<OrderByPositionTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, X LONG, Y TEXT(5))",
        "INSERT INTO T (Id, X, Y) VALUES (1, 30, 'b')",
        "INSERT INTO T (Id, X, Y) VALUES (2, 10, 'c')",
        "INSERT INTO T (Id, X, Y) VALUES (3, 20, 'a')",
    ];

    public sealed class Database() : SharedDatabase("order-position-", Setup);

    private string Column(string sql, int column = 0) => string.Join("|",
        database.Query(sql, CultureInfo.InvariantCulture).Rows.Select(row => row[column]));

    [Theory]
    [InlineData("SELECT Id FROM T ORDER BY 1 DESC", "3|2|1")]
    [InlineData("SELECT Id, X FROM T ORDER BY 2", "2|3|1")]
    [InlineData("SELECT Id, X, Y FROM T ORDER BY 3", "3|1|2")]
    [InlineData("SELECT Id, X FROM T ORDER BY 2 DESC, 1", "1|3|2")]
    [InlineData("SELECT Id, X FROM T ORDER BY (2)", "2|3|1")]
    [InlineData("SELECT Id, X AS Z FROM T ORDER BY 2", "2|3|1")]
    [InlineData("SELECT Id, X FROM T ORDER BY 2, X DESC", "2|3|1")]
    [InlineData("SELECT TOP 1 Id, X FROM T ORDER BY 2 DESC", "1")]
    [InlineData("SELECT Id FROM T UNION ALL SELECT 0 FROM T WHERE Id = 1 ORDER BY 1", "0|1|2|3")]
    [InlineData("SELECT X, COUNT(*) FROM T GROUP BY X ORDER BY 1 DESC", "30|20|10")]
    [InlineData("SELECT Id FROM (SELECT Id, X FROM T ORDER BY 2) AS D", "2|3|1")]
    public void A_whole_number_names_an_output_column(string sql, string expected) =>
        Assert.Equal(expected, Column(sql));

    [Fact]
    public void A_star_is_counted_through()
    {
        Assert.Equal("c|a|b", Column("SELECT * FROM T ORDER BY 2", column: 2));
        Assert.Equal("3|1|2", Column("SELECT * FROM T ORDER BY 3"));
    }

    [Theory]
    [InlineData("SELECT Id, X FROM T ORDER BY 1.5")]
    [InlineData("SELECT Id, X FROM T ORDER BY 1 + 1")]
    [InlineData("SELECT Id, X FROM T ORDER BY '2'")]
    public void Any_other_constant_sorts_nothing(string sql) =>
        Assert.Equal("1|2|3", Column(sql));

    [Theory]
    [InlineData("SELECT Id FROM T ORDER BY 2")]
    [InlineData("SELECT Id, X FROM T ORDER BY 0")]
    [InlineData("SELECT Id, X FROM T ORDER BY -1")]
    [InlineData("SELECT * FROM T ORDER BY 4")]
    [InlineData("SELECT Id FROM T UNION ALL SELECT 0 FROM T ORDER BY 2")]
    public void A_position_that_names_no_column_is_an_error(string sql) =>
        Assert.Throws<InvalidOperationException>(() => Column(sql));
}
