using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER TABLE ... ADD CONSTRAINT name UNIQUE (cols): adds a unique (non-primary) index, which LibRed then
// enforces on insert (a duplicate composite key is rejected; NULLs are distinct).
public class AlterAddUniqueTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"auq-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
    }

    [Fact]
    public void Add_unique_constraint_is_enforced()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE tblCustomers ( CustomerID LONG PRIMARY KEY, [Last Name] TEXT(30), [First Name] TEXT(30) )");
        e.ExecuteNonQuery("ALTER TABLE tblCustomers ADD CONSTRAINT UQ_Name UNIQUE ([Last Name], [First Name])");

        e.ExecuteNonQuery("INSERT INTO tblCustomers (CustomerID, [Last Name], [First Name]) VALUES (1, 'Smith', 'John')");
        // different name → fine
        e.ExecuteNonQuery("INSERT INTO tblCustomers (CustomerID, [Last Name], [First Name]) VALUES (2, 'Smith', 'Jane')");
        // duplicate composite → rejected
        Assert.ThrowsAny<Exception>(() =>
            e.ExecuteNonQuery("INSERT INTO tblCustomers (CustomerID, [Last Name], [First Name]) VALUES (3, 'Smith', 'John')"));
    }

    [Fact]
    public void Add_unique_on_a_populated_table_backfills()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, C TEXT(20) )");
        e.ExecuteNonQuery("INSERT INTO T (K, C) VALUES (1, 'a')");
        e.ExecuteNonQuery("INSERT INTO T (K, C) VALUES (2, 'b')");
        e.ExecuteNonQuery("ALTER TABLE T ADD CONSTRAINT UQ_C UNIQUE (C)");
        // the back-filled index enforces new inserts
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO T (K, C) VALUES (3, 'a')"));
        e.ExecuteNonQuery("INSERT INTO T (K, C) VALUES (4, 'c')");
    }
}
