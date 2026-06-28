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
    public void Width_suffixed_and_two_word_type_aliases_work()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            // integer4 = Int32, integer2 = Int16, integer1 = Byte, and the two-word CHARACTER VARYING.
            engine.ExecuteNonQuery(
                "CREATE TABLE `Aliased` (`Id` INTEGER4 PRIMARY KEY, `Small` INTEGER2, `Tiny` INTEGER1, `Label` CHARACTER VARYING(40))");
            engine.ExecuteNonQuery(
                "INSERT INTO `Aliased` (`Id`, `Small`, `Tiny`, `Label`) VALUES (1, 200, 7, 'hi')");

            var only = Assert.Single(engine.ExecuteQuery("SELECT `Id`, `Small`, `Tiny`, `Label` FROM `Aliased`").Rows);
            Assert.Equal(1, Convert.ToInt32(only[0]));
            Assert.Equal(200, Convert.ToInt32(only[1]));
            Assert.Equal(7, Convert.ToInt32(only[2]));
            Assert.Equal("hi", only[3]);

            var def = db.Catalog.FindTable("Aliased")!;
            Assert.Equal(LibRed.Catalog.JetDataType.Int16, def.FindColumn("Small")!.Type);
            Assert.Equal(LibRed.Catalog.JetDataType.Byte, def.FindColumn("Tiny")!.Type);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Memo_column_fails_with_a_clear_message()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);
            var ex = Assert.Throws<NotSupportedException>(() =>
                engine.ExecuteNonQuery("CREATE TABLE `M` (`Id` INTEGER, `Body` MEMO)"));
            Assert.Contains("long values", ex.Message);
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
