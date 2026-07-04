using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class StringLiteralEscapeTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"strlit-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    // A doubled apostrophe inside a single-quoted string is an escaped apostrophe (SQL standard) — e.g.
    // Northwind's 'Bon app''' → "Bon app'". Verify it parses and stores the un-doubled value.
    [Theory]
    [InlineData("'Bon app'''", "Bon app'")]
    [InlineData("'O''Brien'", "O'Brien")]
    [InlineData("''''", "'")]
    [InlineData("'a''b''c'", "a'b'c")]
    [InlineData("'plain'", "plain")]
    public void Doubled_quote_is_an_escaped_quote(string literal, string expected)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE S (Id LONG, V TEXT(50))");
            e.ExecuteNonQuery($"INSERT INTO S (Id, V) VALUES (1, {literal})");
            Assert.Equal(expected, e.ExecuteQuery("SELECT V FROM S").Rows.First()[0]);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
