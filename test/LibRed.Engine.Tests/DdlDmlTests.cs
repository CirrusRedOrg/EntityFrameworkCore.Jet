using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class DdlDmlTests
{
    private static string CopyToTemp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-ddl-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    [Fact]
    public void Create_insert_select_round_trips_through_sql()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery(
                "CREATE TABLE `Widget` (`Id` INTEGER PRIMARY KEY, `Name` TEXT(100), `Price` CURRENCY)");

            Assert.Equal(1, engine.ExecuteNonQuery(
                "INSERT INTO `Widget` (`Id`, `Name`, `Price`) VALUES (1, 'Gizmo', 9.99)"));
            Assert.Equal(1, engine.ExecuteNonQuery(
                "INSERT INTO `Widget` (`Id`, `Name`, `Price`) VALUES (2, 'Sprocket', 14.5)"));

            var rs = engine.ExecuteQuery("SELECT `Id`, `Name`, `Price` FROM `Widget` ORDER BY `Id`");
            var rows = rs.Rows.ToList();

            Assert.Equal(["Id", "Name", "Price"], rs.ColumnNames);
            Assert.Equal(2, rows.Count);
            Assert.Equal(1, Convert.ToInt32(rows[0][0]));
            Assert.Equal("Gizmo", rows[0][1]);
            Assert.Equal(9.99m, Convert.ToDecimal(rows[0][2]));
            Assert.Equal("Sprocket", rows[1][1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Insert_with_parameters_round_trips()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `P` (`Id` INTEGER PRIMARY KEY, `Name` TEXT(50))");
            engine.ExecuteNonQuery("INSERT INTO `P` (`Id`, `Name`) VALUES (@id, @name)",
                new Dictionary<string, object?> { ["@id"] = 7, ["@name"] = "param" });

            var only = Assert.Single(engine.ExecuteQuery("SELECT `Name` FROM `P` WHERE `Id` = 7").Rows);
            Assert.Equal("param", only[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Create_without_primary_key_is_allowed()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `NoPk` (`A` INTEGER, `B` TEXT(20))");
            engine.ExecuteNonQuery("INSERT INTO `NoPk` (`A`, `B`) VALUES (1, 'x')");

            var only = Assert.Single(engine.ExecuteQuery("SELECT `A`, `B` FROM `NoPk`").Rows);
            Assert.Equal(1, Convert.ToInt32(only[0]));
            Assert.Equal("x", only[1]);
        }
        finally { File.Delete(path); }
    }
}
