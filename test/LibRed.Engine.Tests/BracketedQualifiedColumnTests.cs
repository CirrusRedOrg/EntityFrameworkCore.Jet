using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Access's query designer writes a qualified column as one delimited name — <c>[Order Details.UnitPrice]</c>
/// rather than <c>[Order Details].[UnitPrice]</c> — and resolves it as table.column, so every saved query read
/// back out of MSysQueries arrives in that form. The split happens in the parser rather than at lookup time,
/// because the planner reads <c>ColumnReference.Table</c> to decide which source a predicate belongs to: a
/// reference left unqualified would resolve but stop being pushed down or seekable.
/// </summary>
public class BracketedQualifiedColumnTests : TempDatabaseTest
{
    private static QueryEngine Northwind()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "bracket-qualified-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: true));
    }

    private static List<object?> Column(QueryEngine e, string sql) =>
        e.ExecuteQuery(sql).Rows.Select(r => r[0]).ToList();

    [Theory]
    // The bracketed form and the plain one are the same reference, so they must give the same answer.
    [InlineData("SELECT [Customers.CustomerID] FROM Customers", "SELECT Customers.CustomerID FROM Customers")]
    // A table whose name has a space is exactly where the designer's one-name form shows up.
    [InlineData("SELECT [Order Details.UnitPrice] FROM [Order Details]", "SELECT [Order Details].UnitPrice FROM [Order Details]")]
    // In a WHERE, where the reference also has to survive planning.
    [InlineData("SELECT CustomerID FROM Customers WHERE [Customers.City] = 'London'",
                "SELECT CustomerID FROM Customers WHERE Customers.City = 'London'")]
    // Against an alias rather than the table name.
    [InlineData("SELECT [c.CustomerID] FROM Customers AS c WHERE [c.City] = 'London'",
                "SELECT c.CustomerID FROM Customers AS c WHERE c.City = 'London'")]
    // Across a join, which is where a mis-attributed qualifier would show up as a wrong row count.
    [InlineData("SELECT [o.OrderID] FROM Customers AS c INNER JOIN Orders AS o ON [c.CustomerID] = [o.CustomerID]",
                "SELECT o.OrderID FROM Customers AS c INNER JOIN Orders AS o ON c.CustomerID = o.CustomerID")]
    public void Bracketed_qualified_column_matches_the_plain_form(string bracketed, string plain)
    {
        QueryEngine e = Northwind();
        var viaBrackets = Column(e, bracketed);
        Assert.NotEmpty(viaBrackets);
        Assert.Equal(Column(e, plain), viaBrackets);
    }

    [Fact]
    public void A_delimited_name_without_a_dot_is_still_one_name()
    {
        QueryEngine e = Northwind();
        // Ordinary delimited names, and a delimited TABLE name that has a space in it — nothing to split.
        Assert.NotEmpty(Column(e, "SELECT [CustomerID] FROM Customers"));
        Assert.NotEmpty(Column(e, "SELECT [ShipName] FROM Orders"));
        Assert.NotEmpty(Column(e, "SELECT [UnitPrice] FROM [Order Details]"));
    }

    [Fact]
    public void A_bracketed_qualifier_naming_no_source_is_still_an_error()
    {
        QueryEngine e = Northwind();
        var ex = Assert.Throws<InvalidOperationException>(
            () => Column(e, "SELECT [Nope.CustomerID] FROM Customers"));
        Assert.Contains("Nope.CustomerID", ex.Message, StringComparison.Ordinal);
    }
}
