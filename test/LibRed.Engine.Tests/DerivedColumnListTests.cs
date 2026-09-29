using System.Globalization;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A derived table's column list, the standard's <c>(query) AS t(a, b)</c>: it names the table's columns in order, a
/// table value constructor's included, as SQL Server and PostgreSQL take it and EF Core emits it for an inline
/// collection. ACE has no such syntax, so this is a LibRed extension.
/// </summary>
public class DerivedColumnListTests(DerivedColumnListTests.Database database)
    : TempDatabaseTest, IClassFixture<DerivedColumnListTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, K LONG)",
        "INSERT INTO T VALUES (1, 10)",
        "INSERT INTO T VALUES (2, 20)",
        "INSERT INTO T VALUES (7, 70)",
    ];

    public sealed class Database() : SharedDatabase("derived-columns-", Setup);

    private string Run(string sql)
    {
        var result = database.Engine.ExecuteQuery(sql);
        return $"[{string.Join(",", result.ColumnNames)}] " + string.Join(" | ", result.Rows.Select(row =>
            string.Join(",", row.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)))));
    }

    [Theory]
    [InlineData("SELECT * FROM (VALUES (1, 'a'), (2, 'b')) AS v(n, s)", "[n,s] 1,a | 2,b")]
    [InlineData("SELECT v.s FROM (VALUES (1, 'a'), (2, 'b')) AS v(n, s) WHERE v.n = 2", "[s] b")]
    [InlineData("SELECT x FROM (VALUES (1), (2)) v(x) ORDER BY x DESC", "[x] 2 | 1")]
    [InlineData("SELECT t.x FROM (SELECT Id, K FROM T) AS t(x, y) WHERE t.y = 70", "[x] 7")]
    [InlineData("SELECT * FROM (SELECT Id AS a, K AS b FROM T WHERE Id = 1) AS t(c, d)", "[c,d] 1,10")]
    [InlineData("SELECT * FROM (SELECT Id FROM T WHERE Id = 1 UNION ALL VALUES (5)) AS u(n)", "[n] 1 | 5")]
    [InlineData("SELECT a.Id, v.label FROM T AS a INNER JOIN (VALUES (1, 'one'), (7, 'seven')) AS v(id, label) ON a.Id = v.id ORDER BY a.Id",
        "[Id,label] 1,one | 7,seven")]
    public void The_list_names_the_columns_in_order(string sql, string expected) =>
        Assert.Equal(expected, Run(sql));

    [Fact]
    public void The_old_names_are_gone() =>
        Assert.ThrowsAny<Exception>(() => Run("SELECT t.Id FROM (SELECT Id FROM T) AS t(x)"));

    [Fact]
    public void The_list_must_name_every_column() =>
        Assert.Throws<InvalidOperationException>(() => Run("SELECT * FROM (VALUES (1)) AS v(a, b)"));

    [Fact]
    public void A_name_may_appear_once() =>
        Assert.Throws<SqlParseException>(() => Run("SELECT * FROM (VALUES (1, 2)) AS v(a, A)"));

    [Fact]
    public void An_update_can_read_through_one()
    {
        QueryEngine engine = SharedDatabase.Fresh("derived-columns-update-", Setup);
        engine.ExecuteNonQuery("UPDATE T AS a INNER JOIN (VALUES (1, 11), (7, 77)) AS v(id, k) ON a.Id = v.id SET a.K = v.k");
        Assert.Equal([11, 20, 77], engine.ExecuteQuery("SELECT K FROM T ORDER BY Id").Rows.Select(row => (int)row[0]!));
    }

    [Fact]
    public void A_view_cannot_store_one()
    {
        QueryEngine engine = SharedDatabase.Fresh("derived-columns-view-", Setup);
        Assert.Throws<NotSupportedException>(() =>
            engine.ExecuteNonQuery("CREATE VIEW V AS SELECT t.x FROM (SELECT Id FROM T) AS t(x)"));
    }
}
