using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER TABLE ... ADD CONSTRAINT name CHECK (expr): persists the check to LvProp; the engine then enforces it
// on insert/update (like a CREATE TABLE check). Merges with any existing checks.
public class AlterAddCheckTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "aac-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    [Fact]
    public void Alter_add_check_is_persisted_and_enforced()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE tblInvoices ( ID LONG PRIMARY KEY, Amount DOUBLE )");
        e.ExecuteNonQuery("ALTER TABLE tblInvoices ADD CONSTRAINT CheckAmount CHECK (Amount > 0)");

        e.ExecuteNonQuery("INSERT INTO tblInvoices (ID, Amount) VALUES (1, 50)");  // passes
        var ex = Assert.Throws<InvalidOperationException>(() =>
            e.ExecuteNonQuery("INSERT INTO tblInvoices (ID, Amount) VALUES (2, -5)"));
        Assert.Contains("CheckAmount", ex.Message);
    }

    [Fact]
    public void Alter_add_check_merges_with_an_existing_check()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( ID LONG PRIMARY KEY, A DOUBLE, B DOUBLE, CONSTRAINT CkA CHECK (A > 0) )");
        e.ExecuteNonQuery("ALTER TABLE T ADD CONSTRAINT CkB CHECK (B < 100)");

        // both checks now enforced
        e.ExecuteNonQuery("INSERT INTO T (ID, A, B) VALUES (1, 5, 50)");            // both pass
        AssertCheckViolation(e, "INSERT INTO T (ID, A, B) VALUES (2, -1, 50)", "CkA");
        AssertCheckViolation(e, "INSERT INTO T (ID, A, B) VALUES (3, 5, 200)", "CkB");
    }

    [Fact]
    public void Drop_check_constraint_stops_enforcement()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( ID LONG PRIMARY KEY, Amount LONG )");
        e.ExecuteNonQuery("ALTER TABLE T ADD CONSTRAINT CK_T_Amount CHECK (Amount > 0)");
        AssertCheckViolation(e, "INSERT INTO T (ID, Amount) VALUES (1, -5)", "CK_T_Amount");

        e.ExecuteNonQuery("ALTER TABLE T DROP CONSTRAINT CK_T_Amount");
        e.ExecuteNonQuery("INSERT INTO T (ID, Amount) VALUES (2, -5)");   // no longer enforced
        Assert.Equal(-5, Convert.ToInt32(e.ExecuteQuery("SELECT Amount FROM T WHERE ID = 2").Rows.Single()[0]));
    }

    [Fact]
    public void Drop_check_constraint_leaves_a_sibling_check_intact()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( ID LONG PRIMARY KEY, A LONG, B LONG, CONSTRAINT CkA CHECK (A > 0) )");
        e.ExecuteNonQuery("ALTER TABLE T ADD CONSTRAINT CkB CHECK (B < 100)");
        e.ExecuteNonQuery("ALTER TABLE T DROP CONSTRAINT CkA");

        e.ExecuteNonQuery("INSERT INTO T (ID, A, B) VALUES (1, -1, 50)");   // CkA gone → A=-1 allowed
        AssertCheckViolation(e, "INSERT INTO T (ID, A, B) VALUES (2, 5, 200)", "CkB");
    }

    [Fact]
    public void Docs_credit_limit_scenario_via_alter()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE tblCreditLimit ( Lmt DOUBLE )");
        e.ExecuteNonQuery("INSERT INTO tblCreditLimit VALUES (100)");
        e.ExecuteNonQuery("CREATE TABLE tblCustomers ( CustomerID LONG PRIMARY KEY )");
        e.ExecuteNonQuery("ALTER TABLE tblCustomers ADD COLUMN CustomerLimit DOUBLE");
        e.ExecuteNonQuery("ALTER TABLE tblCustomers ADD CONSTRAINT LimitRule CHECK (CustomerLimit <= (SELECT Lmt FROM tblCreditLimit))");
        e.ExecuteNonQuery("INSERT INTO tblCustomers (CustomerID, CustomerLimit) VALUES (1, 50)");

        // Update above the limit fails; at/below the limit succeeds.
        AssertCheckViolation(e,
            "UPDATE tblCustomers SET CustomerLimit = 200 WHERE CustomerID = 1", "LimitRule");
        e.ExecuteNonQuery("UPDATE tblCustomers SET CustomerLimit = 100 WHERE CustomerID = 1");
        Assert.Equal(100.0, Convert.ToDouble(e.ExecuteQuery("SELECT CustomerLimit FROM tblCustomers WHERE CustomerID = 1").Rows.Single()[0]));
    }

    private static void AssertCheckViolation(QueryEngine engine, string sql, string constraintName)
    {
        var error = Assert.Throws<InvalidOperationException>(() => engine.ExecuteNonQuery(sql));
        Assert.Contains(constraintName, error.Message);
        Assert.Contains("validation rule", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
