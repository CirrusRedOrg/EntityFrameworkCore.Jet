using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// LibRed enforces CHECK constraints on INSERT and UPDATE: a row is rejected only when a check evaluates to
// explicitly FALSE (NULL/unknown passes). Checks may reference the row's columns and use subqueries.
public class CheckConstraintEnforcementTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "chk-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    [Fact]
    public void Simple_check_is_enforced_on_insert()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE tblInvoices ( ID LONG PRIMARY KEY, Amount DOUBLE, CONSTRAINT CheckAmount CHECK (Amount > 0) )");
        e.ExecuteNonQuery("INSERT INTO tblInvoices (ID, Amount) VALUES (1, 50)");    // passes
        var ex = Assert.Throws<InvalidOperationException>(() =>
            e.ExecuteNonQuery("INSERT INTO tblInvoices (ID, Amount) VALUES (2, -5)")); // violates
        Assert.Contains("CheckAmount", ex.Message);
    }

    [Fact]
    public void Check_is_enforced_on_update()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE tblInvoices ( ID LONG PRIMARY KEY, Amount DOUBLE, CONSTRAINT CheckAmount CHECK (Amount > 0) )");
        e.ExecuteNonQuery("INSERT INTO tblInvoices (ID, Amount) VALUES (1, 50)");
        var error = Assert.Throws<InvalidOperationException>(() =>
            e.ExecuteNonQuery("UPDATE tblInvoices SET Amount = -1 WHERE ID = 1"));
        Assert.Contains("CheckAmount", error.Message);
        e.ExecuteNonQuery("UPDATE tblInvoices SET Amount = 75 WHERE ID = 1"); // valid update succeeds
        Assert.Equal(75.0, Convert.ToDouble(e.ExecuteQuery("SELECT Amount FROM tblInvoices WHERE ID = 1").Rows.Single()[0]));
    }

    [Fact]
    public void Null_value_passes_the_check()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE tblInvoices ( ID LONG PRIMARY KEY, Amount DOUBLE, CONSTRAINT CheckAmount CHECK (Amount > 0) )");
        // Amount omitted → NULL; (NULL > 0) is unknown, which passes.
        e.ExecuteNonQuery("INSERT INTO tblInvoices (ID) VALUES (1)");
        Assert.Null(e.ExecuteQuery("SELECT Amount FROM tblInvoices WHERE ID = 1").Rows.Single()[0]);
    }

    [Fact]
    public void Subquery_check_is_enforced()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE tblCreditLimit ( Lmt DOUBLE )");
        e.ExecuteNonQuery("INSERT INTO tblCreditLimit VALUES (100)");
        e.ExecuteNonQuery("CREATE TABLE tblCustomers ( CustomerID LONG PRIMARY KEY, CustomerLimit DOUBLE, " +
            "CONSTRAINT LimitRule CHECK (CustomerLimit <= (SELECT Lmt FROM tblCreditLimit)) )");

        e.ExecuteNonQuery("INSERT INTO tblCustomers (CustomerID, CustomerLimit) VALUES (1, 80)");   // 80 <= 100 → ok
        var ex = Assert.Throws<InvalidOperationException>(() =>
            e.ExecuteNonQuery("INSERT INTO tblCustomers (CustomerID, CustomerLimit) VALUES (2, 200)")); // 200 <= 100 → false
        Assert.Contains("LimitRule", ex.Message);

        // The docs scenario: updating above the limit fails, at/below succeeds.
        var updateError = Assert.Throws<InvalidOperationException>(() =>
            e.ExecuteNonQuery("UPDATE tblCustomers SET CustomerLimit = 200 WHERE CustomerID = 1"));
        Assert.Contains("LimitRule", updateError.Message);
        e.ExecuteNonQuery("UPDATE tblCustomers SET CustomerLimit = 100 WHERE CustomerID = 1");
    }

    [Fact]
    public void Check_with_trailing_unparsed_tokens_is_rejected_during_enforcement()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T (ID LONG PRIMARY KEY, Amount DOUBLE, " +
            "CONSTRAINT CheckAmount CHECK (Amount > 0 unexpected))");

        Assert.Throws<LibRed.Sql.Parsing.SqlParseException>(() =>
            e.ExecuteNonQuery("INSERT INTO T (ID, Amount) VALUES (1, 50)"));
    }
}
