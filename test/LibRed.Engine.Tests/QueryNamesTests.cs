using LibRed;
using LibRed.Engine;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// How LibRed reads the names in a query as Access writes them: the bang, where <c>Customers!CustomerID</c> is
/// <c>Customers.CustomerID</c> and a longer chain such as <c>Forms!frmMenu!cmbGroup</c> names a form control, which a
/// query reaches only as a parameter it declares; a reserved word naming a column after a period or a bang; a name in
/// any script written unbracketed; and Yes, No, On and Off as True and False. ACE's own answers are in
/// <c>QueryNamesAccessTests</c>.
/// </summary>
public class QueryNamesTests : TempDatabaseTest
{
    private static QueryEngine Northwind(bool readOnly = true)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "names-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly));
    }

    /// <summary>Northwind with a table K whose columns are named by reserved words and constants, and a table Árú
    /// named, like its columns, outside ASCII.</summary>
    private static QueryEngine Seeded()
    {
        QueryEngine engine = Northwind(readOnly: false);
        foreach (string sql in new[]
        {
            "CREATE TABLE K ([Key] LONG, [Select] LONG, [Yes] LONG, [No] LONG, [On] LONG, [Off] LONG)",
            "INSERT INTO K VALUES (1, 2, 3, 4, 5, 6)",
            "CREATE TABLE [Árú] ([Név] TEXT(10), [Номер] LONG, [名前] TEXT(10), [ΑΒΓ] LONG)",
            "INSERT INTO [Árú] VALUES ('x', 2, 'y', 3)",
        })
            engine.ExecuteNonQuery(sql);
        return engine;
    }

    private static List<object?> Column(QueryEngine e, string sql, IReadOnlyDictionary<string, object?>? parameters = null) =>
        e.ExecuteQuery(sql, parameters).Rows.Select(r => r[0]).ToList();

    private static object?[] Row(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.Single();

    [Theory]
    [InlineData("SELECT Customers!CustomerID FROM Customers", "SELECT Customers.CustomerID FROM Customers")]
    [InlineData("SELECT [Customers]![City] FROM Customers", "SELECT Customers.City FROM Customers")]
    [InlineData("SELECT `Customers`!`City` FROM Customers", "SELECT Customers.City FROM Customers")]
    [InlineData("SELECT c!CustomerID FROM Customers AS c WHERE c!City = 'London'",
                "SELECT c.CustomerID FROM Customers AS c WHERE c.City = 'London'")]
    [InlineData("SELECT City FROM Customers GROUP BY Customers!City HAVING Customers!City > 'M' ORDER BY Customers!City DESC",
                "SELECT City FROM Customers GROUP BY Customers.City HAVING Customers.City > 'M' ORDER BY Customers.City DESC")]
    [InlineData("SELECT o!OrderID FROM Customers AS c INNER JOIN Orders AS o ON c!CustomerID = o!CustomerID",
                "SELECT o.OrderID FROM Customers AS c INNER JOIN Orders AS o ON c.CustomerID = o.CustomerID")]
    [InlineData("SELECT (SELECT COUNT(*) FROM Orders WHERE Orders!CustomerID = Customers!CustomerID) FROM Customers",
                "SELECT (SELECT COUNT(*) FROM Orders WHERE Orders.CustomerID = Customers.CustomerID) FROM Customers")]
    [InlineData("SELECT d!X FROM (SELECT CustomerID AS X FROM Customers) AS d", "SELECT d.X FROM (SELECT CustomerID AS X FROM Customers) AS d")]
    public void Two_parts_joined_by_a_bang_are_table_and_column(string bang, string dot)
    {
        QueryEngine e = Northwind();
        var viaBang = Column(e, bang);
        Assert.NotEmpty(viaBang);
        Assert.Equal(Column(e, dot), viaBang);
    }

    [Theory]
    [InlineData("SELECT Customers!CustomerID FROM Customers", "Customers!CustomerID")]
    [InlineData("SELECT [Customers]![CustomerID] FROM Customers", "Customers]![CustomerID")]
    [InlineData("SELECT Customers!CustomerID AS X FROM Customers", "X")]
    [InlineData("SELECT Customers.CustomerID FROM Customers", "CustomerID")]
    public void An_unaliased_bang_column_is_named_as_written(string sql, string name) =>
        Assert.Equal(name, Northwind().ExecuteQuery(sql).ColumnNames.Single());

    // The name is the column's, so an outer query reaches it by that name and not by the column's own.
    [Fact]
    public void A_derived_tables_bang_column_goes_by_its_written_name()
    {
        QueryEngine e = Northwind();
        const string inner = "(SELECT Customers!City FROM Customers WHERE CustomerID = 'ALFKI')";
        Assert.Equal(["Berlin"], Column(e, $"SELECT [Customers!City] FROM {inner}"));
        Assert.Throws<InvalidOperationException>(() => Column(e, $"SELECT City FROM {inner}"));
    }

    [Theory]
    [InlineData("SELECT Customers ! CustomerID FROM Customers")]
    [InlineData("SELECT Customers !CustomerID FROM Customers")]
    [InlineData("SELECT Customers! CustomerID FROM Customers")]
    public void A_bang_takes_no_space_either_side(string sql) =>
        Assert.Throws<SqlParseException>(() => Northwind().ExecuteQuery(sql));

    // A form control is nothing a query can reach unless the query declares it.
    [Fact]
    public void An_undeclared_form_control_is_not_found()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Column(Northwind(), "SELECT CustomerID FROM Customers WHERE CustomerID = Forms!frmMenu!cmbCustomer"));
        Assert.Contains("Forms!frmMenu!cmbCustomer", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PARAMETERS [Forms]![frmMenu]![cmbCustomer] Text(5); SELECT CustomerID FROM Customers WHERE CustomerID = Forms!frmMenu!cmbCustomer",
                "Forms!frmMenu!cmbCustomer")]
    [InlineData("PARAMETERS Forms!frmMenu!cmbCustomer Text(5); SELECT CustomerID FROM Customers WHERE CustomerID = [Forms]![frmMenu]![cmbCustomer]",
                "Forms!frmMenu!cmbCustomer")]
    // A period in the chain counts as a bang, as the Form property of a subform control is written.
    [InlineData("PARAMETERS [Forms]![frmMenu]![sub].[Form]![cmbCustomer] Text(5); SELECT CustomerID FROM Customers WHERE CustomerID = Forms!frmMenu!sub!Form!cmbCustomer",
                "Forms!frmMenu!sub!Form!cmbCustomer")]
    public void A_declared_form_control_is_a_parameter(string sql, string name) =>
        Assert.Equal(["ALFKI"], Column(Northwind(), sql, new Dictionary<string, object?> { [name] = "ALFKI" }));

    // A two-part control reads as a table and a column, and its declaration wins over a real column of that name.
    [Theory]
    [InlineData("PARAMETERS Forms!txtCity Text(20); SELECT CustomerID FROM Customers WHERE City = Forms!txtCity", "Forms!txtCity")]
    [InlineData("PARAMETERS Forms!txtCity Text(20); SELECT CustomerID FROM Customers WHERE City = Forms.txtCity", "Forms!txtCity")]
    [InlineData("PARAMETERS Customers!City Text(20); SELECT CustomerID FROM Customers WHERE City = Customers!City", "Customers!City")]
    public void A_declared_two_part_control_is_a_parameter(string sql, string name) =>
        Assert.Equal(["ALFKI"], Column(Northwind(), sql, new Dictionary<string, object?> { [name] = "Berlin" }));

    [Fact]
    public void An_update_may_set_a_column_written_with_a_bang()
    {
        QueryEngine e = Northwind(readOnly: false);
        Assert.Equal(1, e.ExecuteNonQuery("UPDATE Customers SET Customers!City = 'Ankh-Morpork' WHERE Customers!CustomerID = 'ALFKI'"));
        Assert.Equal(["Ankh-Morpork"], Column(e, "SELECT City FROM Customers WHERE CustomerID = 'ALFKI'"));
    }

    // Access stores a declared form control as written, brackets and all, and binds the stored query's EXECUTE
    // arguments to it by position.
    [Fact]
    public void A_stored_query_declaring_a_form_control_executes()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "names-proc-");
        using (var db = JetDatabase.Open(path, readOnly: false))
            new QueryEngine(db).ExecuteNonQuery(
                "CREATE PROCEDURE ByCity [Forms]![frmMenu]![txtCity] Text(20) AS " +
                "SELECT CustomerID FROM Customers WHERE City = Forms!frmMenu!txtCity");

        using (var db = JetDatabase.Open(path, readOnly: true))
        {
            Assert.Equal("Forms!frmMenu!txtCity", db.Catalog.FindQuery("ByCity")!.Parameters.Single().Name);
            Assert.Equal(["ALFKI"], Column(new QueryEngine(db), "EXECUTE ByCity 'Berlin'"));
        }
    }

    [Fact]
    public void A_reserved_word_names_a_column_after_a_period_or_a_bang() =>
        Assert.Equal([1, 2, 1, 2], Row(Seeded(), "SELECT K.Key, K.Select, K!Key, K!Select FROM K"));

    [Fact]
    public void A_name_in_any_script_needs_no_brackets()
    {
        QueryEngine e = Seeded();
        Assert.Equal(["x", 2, "y", 3], Row(e, "SELECT Név, Номер, 名前, ΑΒΓ FROM Árú"));
        Assert.Equal(["x"], Row(e, "SELECT a.Név FROM Árú AS a WHERE a.ΑΒΓ = 3"));
    }

    [Fact]
    public void Yes_no_on_and_off_are_true_and_false() =>
        Assert.Equal([true, false, true, false], Row(Seeded(), "SELECT Yes, No, On, Off FROM K"));

    // Unqualified, the constant wins over a column of that name; qualified, it is the column.
    [Fact]
    public void A_qualified_yes_is_the_column() =>
        Assert.Equal([true, 3, false, 4, true, 5, false, 6],
            Row(Seeded(), "SELECT Yes, K.Yes, No, K.No, On, K.On, Off, K!Off FROM K"));
}