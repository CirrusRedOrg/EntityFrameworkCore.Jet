using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class TopParameterTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "top-");
        return path;
    }

    [Fact]
    public void Top_accepts_a_parameter()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            int rows = e.ExecuteQuery("SELECT TOP @n CustomerID FROM Customers",
                new Dictionary<string, object?> { ["n"] = 3 }).Rows.Count();
            Assert.Equal(3, rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Top_accepts_a_parameter_expression()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            int rows = e.ExecuteQuery("SELECT TOP @a + @b * FROM Customers",
                new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 }).Rows.Count();
            Assert.Equal(5, rows); // @a + @b, and the SELECT star is not swallowed by the TOP expression
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Top_literal_with_star_still_parses()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            Assert.Equal(4, new QueryEngine(db).ExecuteQuery("SELECT TOP 4 * FROM Customers").Rows.Count());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void View_with_a_parameterized_top_is_rejected()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            Assert.Throws<NotSupportedException>(() => new QueryEngine(db).ExecuteNonQuery(
                "CREATE VIEW `V` AS SELECT TOP @n `CustomerID` FROM `Customers`"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
