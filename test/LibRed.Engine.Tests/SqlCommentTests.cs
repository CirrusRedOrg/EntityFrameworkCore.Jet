using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>SQL comments are skipped by the lexer — EF Core query tags prepend a <c>-- tag</c> line comment
/// to the statement (and block comments can appear too).</summary>
public class SqlCommentTests
{
    private static QueryEngine Engine()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cmt-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id LONG PRIMARY KEY)");
        e.ExecuteNonQuery("INSERT INTO T (Id) VALUES (1)");
        return e;
    }

    [Theory]
    [InlineData("-- my query tag\nSELECT Id FROM T")]
    [InlineData("-- tag one\n-- tag two\nSELECT Id FROM T")]
    [InlineData("/* block */ SELECT Id FROM T")]
    [InlineData("SELECT Id FROM T -- trailing")]
    public void A_comment_is_skipped(string sql)
        => Assert.Equal(1, Convert.ToInt32(Engine().ExecuteQuery(sql).Rows.Single()[0]));
}
