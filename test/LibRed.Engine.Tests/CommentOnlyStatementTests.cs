using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Text that holds no statement — blank, or nothing but comments — runs as a no-op instead of raising a parse
// error. The grammar skips WS/LINE_COMMENT/BLOCK_COMMENT outright, so such input produces no tokens and the
// statement rule (which has no empty production) used to fail with "mismatched input '<EOF>'".
//
// This is ordinary EF Core output: migrationBuilder.Sql("--Before") sends a command that is only a comment,
// and it has to succeed silently. Found via MigrationsInfrastructureLibRedTest, which died on exactly that
// before reaching anything it was actually testing.
public class CommentOnlyStatementTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "cmt-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    [Theory]
    [InlineData("--Before")]
    [InlineData("-- a line comment")]
    [InlineData("/* a block comment */")]
    [InlineData("  \t\r\n ")]
    [InlineData("-- one\r\n-- two\r\n")]
    [InlineData("/* mixed */ -- kinds\r\n")]
    public void Statementless_text_is_a_no_op(string sql)
    {
        QueryEngine e = Fresh();
        Assert.Equal(0, e.ExecuteNonQuery(sql));
        Assert.Empty(e.ExecuteQuery(sql).Rows);
    }

    // The check must come from the lexer, not a scan for '--': here the dashes are inside a string literal,
    // so this is a real statement and must still run.
    [Fact]
    public void Dashes_inside_a_string_literal_are_not_a_comment()
    {
        QueryEngine e = Fresh();
        Assert.Equal("--", e.ExecuteQuery("SELECT '--' FROM `Shippers`").Rows.First()[0]);
        Assert.False(e.IsStatementless("SELECT '--' FROM `Shippers`"));
    }

    // A comment attached to a real statement is unaffected — this is how EF Core query tags arrive.
    [Fact]
    public void A_comment_preceding_a_statement_still_runs_it()
    {
        QueryEngine e = Fresh();
        Assert.NotEmpty(e.ExecuteQuery("-- a query tag\r\nSELECT `CompanyName` FROM `Shippers`").Rows);
    }

    // A statement that is genuinely malformed must still report a parse error rather than being swallowed as
    // a no-op — the short-circuit is for absent statements, not broken ones.
    [Fact]
    public void A_malformed_statement_still_throws()
    {
        QueryEngine e = Fresh();
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("SELECT FROM"));
    }
}
