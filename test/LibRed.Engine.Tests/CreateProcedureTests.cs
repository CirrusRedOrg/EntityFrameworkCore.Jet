using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class CreateProcedureTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"proc-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    // A CREATE PROCEDURE with declared parameters (no parens; Access syntax) parses and executes, storing a
    // parameterized query the same way a view is stored plus a parameter row per declared parameter.
    [Fact]
    public void Create_procedure_with_parameters_executes()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(db).ExecuteNonQuery(
                "CREATE PROCEDURE `Orders in range` " +
                "`Beginning Date` DateTime, `Ending Date` DateTime AS " +
                "SELECT Orders.OrderID FROM Orders " +
                "WHERE Orders.OrderDate BETWEEN `Beginning Date` AND `Ending Date`");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // A procedure name, like a view, cannot collide with an existing table.
    [Fact]
    public void Procedure_name_colliding_with_an_object_throws()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            Assert.Throws<InvalidOperationException>(() =>
                new QueryEngine(db).ExecuteNonQuery("CREATE PROCEDURE `Customers` AS SELECT `CustomerID` FROM `Customers`"));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
