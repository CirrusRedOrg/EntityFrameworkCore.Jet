using System.Globalization;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>WITH OWNERACCESS OPTION</c>: run a query with its owner's permissions. LibRed has no users, so a statement with it
/// does exactly what it does without it — it is accepted wherever ACE accepts it and refused wherever ACE refuses it
/// (verified). A stored query keeps it; <c>StoredActionQueryWriteAccessTests</c> checks that against ACE.
/// </summary>
public class OwnerAccessTests : TempDatabaseTest
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, N TEXT(10))",
        "INSERT INTO T VALUES (1, 'a')",
        "INSERT INTO T VALUES (2, 'b')",
        "INSERT INTO T VALUES (3, 'b')",
    ];

    private static QueryEngine Fresh() => SharedDatabase.Fresh("owneraccess-", Setup);

    private static string Rows(QueryEngine engine, string sql, IReadOnlyDictionary<string, object?>? parameters = null) =>
        string.Join(" | ", engine.ExecuteQuery(sql, parameters).Rows.Select(row =>
            string.Join(",", row.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)))));

    // Each with the declaration returns what it returns without: it ends a SELECT, a query after its ORDER BY, each
    // arm of a UNION and a subquery.
    [Theory]
    [InlineData("SELECT Id FROM T WITH OWNERACCESS OPTION", "SELECT Id FROM T")]
    [InlineData("SELECT Id FROM T WITH OWNERACCESS OPTION;", "SELECT Id FROM T")]
    [InlineData("SELECT Id FROM T ORDER BY Id DESC WITH OWNERACCESS OPTION", "SELECT Id FROM T ORDER BY Id DESC")]
    [InlineData("SELECT Id FROM T WHERE Id > 1 WITH OWNERACCESS OPTION", "SELECT Id FROM T WHERE Id > 1")]
    [InlineData("SELECT N, COUNT(*) FROM T GROUP BY N HAVING COUNT(*) > 1 WITH OWNERACCESS OPTION",
                "SELECT N, COUNT(*) FROM T GROUP BY N HAVING COUNT(*) > 1")]
    [InlineData("SELECT N FROM T UNION SELECT N FROM T ORDER BY N WITH OWNERACCESS OPTION",
                "SELECT N FROM T UNION SELECT N FROM T ORDER BY N")]
    [InlineData("SELECT N FROM T WITH OWNERACCESS OPTION UNION SELECT N FROM T ORDER BY N",
                "SELECT N FROM T UNION SELECT N FROM T ORDER BY N")]
    [InlineData("SELECT N FROM T WITH OWNERACCESS OPTION UNION SELECT N FROM T WITH OWNERACCESS OPTION",
                "SELECT N FROM T UNION SELECT N FROM T")]
    [InlineData("SELECT N FROM T WITH OWNERACCESS OPTION UNION SELECT N FROM T ORDER BY N WITH OWNERACCESS OPTION",
                "SELECT N FROM T UNION SELECT N FROM T ORDER BY N")]
    [InlineData("SELECT * FROM (SELECT Id FROM T WITH OWNERACCESS OPTION) WITH OWNERACCESS OPTION", "SELECT * FROM (SELECT Id FROM T)")]
    [InlineData("SELECT * FROM (SELECT Id FROM T WITH OWNERACCESS OPTION) ORDER BY Id", "SELECT * FROM (SELECT Id FROM T) ORDER BY Id")]
    [InlineData("SELECT Id FROM T WHERE Id IN (SELECT Id FROM T WHERE N = 'b' WITH OWNERACCESS OPTION)",
                "SELECT Id FROM T WHERE Id IN (SELECT Id FROM T WHERE N = 'b')")]
    [InlineData("SELECT Id FROM T with  owneraccess\r\n option", "SELECT Id FROM T")]
    public void A_query_with_it_returns_what_it_returns_without(string with, string without)
    {
        QueryEngine engine = Fresh();
        Assert.Equal(Rows(engine, without), Rows(engine, with));
    }

    [Fact]
    public void It_follows_a_parameters_clause() =>
        Assert.Equal("2 | 3", Rows(Fresh(), "PARAMETERS p Long; SELECT Id FROM T WHERE Id > p ORDER BY Id WITH OWNERACCESS OPTION",
            new Dictionary<string, object?> { ["p"] = 1 }));

    // An INSERT, UPDATE or DELETE with it changes the rows it changes without.
    [Theory]
    [InlineData("INSERT INTO T (Id, N) VALUES (4, 'c') WITH OWNERACCESS OPTION", 1, "1,a | 2,b | 3,b | 4,c")]
    [InlineData("INSERT INTO T (Id, N) SELECT Id + 10, N FROM T WHERE N = 'b' WITH OWNERACCESS OPTION", 2,
                "1,a | 2,b | 3,b | 12,b | 13,b")]
    [InlineData("UPDATE T SET N = 'z' WHERE Id = 2 WITH OWNERACCESS OPTION", 1, "1,a | 2,z | 3,b")]
    [InlineData("DELETE FROM T WHERE N = 'b' WITH OWNERACCESS OPTION", 2, "1,a")]
    public void A_change_with_it_changes_what_it_changes_without(string sql, int affected, string after)
    {
        QueryEngine engine = Fresh();
        Assert.Equal(affected, engine.ExecuteNonQuery(sql));
        Assert.Equal(after, Rows(engine, "SELECT Id, N FROM T ORDER BY Id"));
    }

    [Fact]
    public void A_make_table_query_takes_it()
    {
        QueryEngine engine = Fresh();
        Assert.Equal(3, engine.ExecuteNonQuery("SELECT Id, N INTO T2 FROM T WITH OWNERACCESS OPTION"));
        Assert.Equal("1,a | 2,b | 3,b", Rows(engine, "SELECT Id, N FROM T2 ORDER BY Id"));
    }

    // ACE refuses each of these: a query's ORDER BY belongs to its last SELECT, so the option ending that SELECT cannot
    // come before it; it ends a query once; it needs all three words; and it is not part of DDL.
    [Theory]
    [InlineData("SELECT Id FROM T WITH OWNERACCESS OPTION ORDER BY Id")]
    [InlineData("SELECT Id FROM T UNION SELECT Id FROM T WITH OWNERACCESS OPTION ORDER BY Id")]
    [InlineData("SELECT Id FROM T WITH OWNERACCESS OPTION WITH OWNERACCESS OPTION")]
    [InlineData("SELECT Id FROM T WITH OWNERACCESS")]
    [InlineData("SELECT Id FROM T WITH OPTION")]
    [InlineData("SELECT Id FROM T OWNERACCESS OPTION")]
    [InlineData("CREATE TABLE T3 (Id LONG) WITH OWNERACCESS OPTION")]
    public void Where_ace_refuses_it_so_does_libred(string sql) =>
        Assert.Throws<SqlParseException>(() => Fresh().ExecuteNonQuery(sql));

    // Neither word is reserved: a column may still be named either, unbracketed.
    [Fact]
    public void Its_words_still_name_columns()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE W (Option LONG, OwnerAccess LONG)");
        engine.ExecuteNonQuery("INSERT INTO W (Option, OwnerAccess) VALUES (1, 2)");
        Assert.Equal("1,2", Rows(engine, "SELECT Option, OwnerAccess FROM W WITH OWNERACCESS OPTION"));
    }
}
