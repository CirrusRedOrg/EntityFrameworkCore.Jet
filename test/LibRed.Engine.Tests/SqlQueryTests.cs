using LibRed;
using LibRed.Engine;
using LibRed.Engine.Execution;
using Xunit;

namespace LibRed.Engine.Tests;

public class SqlQueryTests
{
    private static readonly string Northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    private static ResultSet Query(string sql)
    {
        using var db = JetDatabase.Open(Northwind);
        var rs = new QueryEngine(db).ExecuteQuery(sql);
        // Materialize while the database is open.
        return new ResultSet(rs.ColumnNames, rs.Rows.ToList());
    }

    [Fact]
    public void Select_star_returns_all_columns_and_rows()
    {
        var rs = Query("SELECT * FROM Categories");

        Assert.Equal(["CategoryID", "CategoryName", "Description", "Picture"], rs.ColumnNames);
        Assert.Equal(8, rs.Rows.Count());
    }

    [Fact]
    public void Projection_and_where_equality()
    {
        var rs = Query("SELECT CategoryName FROM Categories WHERE CategoryID = 4");

        Assert.Equal(["CategoryName"], rs.ColumnNames);
        var only = Assert.Single(rs.Rows);
        Assert.Equal("Dairy Products", only[0]);
    }

    [Fact]
    public void Where_with_string_literal()
    {
        var rs = Query("SELECT CompanyName, City FROM Customers WHERE Country = 'Germany'");

        Assert.Equal(11, rs.Rows.Count()); // Northwind has 11 German customers
        Assert.All(rs.Rows, r => Assert.IsType<string>(r[0]));
    }

    [Fact]
    public void Top_limits_rows()
    {
        var rs = Query("SELECT TOP 3 CustomerID FROM Customers WHERE Country = 'Germany'");
        Assert.Equal(3, rs.Rows.Count());
    }

    [Fact]
    public void Boolean_column_and_numeric_comparison_combined()
    {
        var rs = Query("SELECT ProductName, UnitPrice FROM Products WHERE Discontinued = true AND UnitPrice > 30");

        var names = rs.Rows.Select(r => (string)r[0]!).ToList();
        Assert.Contains("Mishi Kobe Niku", names);
        Assert.All(rs.Rows, r => Assert.True(Convert.ToDecimal(r[1]) > 30));
    }

    [Fact]
    public void Or_and_alias()
    {
        var rs = Query("SELECT CategoryName AS Name FROM Categories WHERE CategoryID = 1 OR CategoryID = 8");

        Assert.Equal(["Name"], rs.ColumnNames);
        var names = rs.Rows.Select(r => (string)r[0]!).OrderBy(n => n).ToList();
        Assert.Equal(["Beverages", "Seafood"], names);
    }

    [Fact]
    public void Unknown_table_throws_bind_error()
    {
        using var db = JetDatabase.Open(Northwind);
        var engine = new QueryEngine(db);
        var ex = Assert.Throws<LibRed.Sql.Binding.SqlBindException>(() => engine.ExecuteQuery("SELECT * FROM Nope").Rows.ToList());
        Assert.Contains("Nope", ex.Message);
    }
}
