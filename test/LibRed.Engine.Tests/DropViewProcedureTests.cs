using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// DROP VIEW / DROP PROCEDURE through LibRed's engine — both remove a type-5 query object.
public class DropViewProcedureTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dropview-eng-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
    }

    [Fact]
    public void Drop_view_removes_it_and_frees_the_name()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE VIEW V AS SELECT ProductID FROM Products");
        Assert.True(e.Database.Catalog.Views.ContainsKey("V"));

        e.ExecuteNonQuery("DROP VIEW V");
        Assert.False(e.Database.Catalog.Views.ContainsKey("V"));      // gone
        Assert.Throws<LibRed.Sql.Binding.SqlBindException>(() => e.ExecuteQuery("SELECT * FROM V"));
        Assert.Equal(0, e.ExecuteNonQuery("CREATE VIEW V AS SELECT ProductID FROM Products")); // name reusable
    }

    [Fact]
    public void Drop_procedure_removes_it()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE PROCEDURE Pr n LONG AS SELECT ProductID FROM Products WHERE ProductID = n");
        e.ExecuteNonQuery("DROP PROCEDURE Pr");
        Assert.False(e.Database.Catalog.ActionQueries.ContainsKey("Pr"));
        Assert.False(e.Database.Catalog.Views.ContainsKey("Pr"));
    }

    [Fact]
    public void Drop_view_and_procedure_are_interchangeable_and_missing_is_rejected()
    {
        var e = Fresh();
        // ACE lets either statement drop either object; LibRed matches.
        e.ExecuteNonQuery("CREATE VIEW V2 AS SELECT ProductID FROM Products");
        e.ExecuteNonQuery("DROP PROCEDURE V2");                        // drop a view via DROP PROCEDURE
        Assert.False(e.Database.Catalog.Views.ContainsKey("V2"));

        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("DROP VIEW Nope"));
    }
}
