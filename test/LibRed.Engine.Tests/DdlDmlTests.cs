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
    public void Memo_column_round_trips_including_null_and_a_long_value()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `M` (`Id` INTEGER PRIMARY KEY, `Note` LONGCHAR NULL)");
            engine.ExecuteNonQuery("INSERT INTO `M` (`Id`, `Note`) VALUES (1, 'hello memo world')");
            engine.ExecuteNonQuery("INSERT INTO `M` (`Id`, `Note`) VALUES (2, NULL)");
            string longText = new string('x', 500); // still inline (fits the page), exercises >255
            engine.ExecuteNonQuery($"INSERT INTO `M` (`Id`, `Note`) VALUES (3, '{longText}')");

            var rows = engine.ExecuteQuery("SELECT `Id`, `Note` FROM `M` ORDER BY `Id`").Rows.ToList();
            Assert.Equal("hello memo world", rows[0][1]);
            Assert.Null(rows[1][1]);
            Assert.Equal(longText, rows[2][1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Ef_generated_create_table_shape_parses_and_round_trips()
    {
        // The exact shape EF Core's Jet migrations generator emits: backtick identifiers, an explicit
        // NULL / NOT NULL nullability marker per column, and a named table-level PRIMARY KEY.
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery(
                "CREATE TABLE `Widgets` (\n" +
                "    `Id` integer NOT NULL,\n" +
                "    `Name` varchar(255) NULL,\n" +
                "    CONSTRAINT `PK_Widgets` PRIMARY KEY (`Id`)\n" +
                ")");
            Assert.Equal(1, engine.ExecuteNonQuery("INSERT INTO `Widgets` (`Id`, `Name`) VALUES (1, 'a')"));

            var rows = engine.ExecuteQuery("SELECT `Id`, `Name` FROM `Widgets`").Rows.ToList();
            Assert.Single(rows);
            Assert.Equal(1, Convert.ToInt32(rows[0][0]));
            Assert.Equal("a", rows[0][1]);

            var pk = Assert.Single(db.Catalog.FindTable("Widgets")!.Indexes, i => i.IsPrimaryKey);
            Assert.Equal(["Id"], pk.Columns.Select(c => c.Column.Name));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Statements_with_a_trailing_semicolon_parse()
    {
        // EF Core terminates each statement with ';'. The grammar accepts an optional trailing one.
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `S` (`Id` INTEGER PRIMARY KEY, `N` TEXT(10));");
            Assert.Equal(1, engine.ExecuteNonQuery("INSERT INTO `S` (`Id`, `N`) VALUES (1, 'a');"));

            var rows = engine.ExecuteQuery("SELECT `Id`, `N` FROM `S`;").Rows.ToList();
            Assert.Single(rows);
            Assert.Equal(1, Convert.ToInt32(rows[0][0]));
            Assert.Equal("a", rows[0][1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Autonumber_is_generated_for_inserts_that_omit_the_column()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `Auto` (`Id` COUNTER PRIMARY KEY, `V` TEXT(20))");
            Assert.Equal(1, engine.ExecuteNonQuery("INSERT INTO `Auto` (`V`) VALUES ('a')")); // -> Id 1
            engine.ExecuteNonQuery("INSERT INTO `Auto` (`V`) VALUES ('b')");                  // -> 2
            engine.ExecuteNonQuery("INSERT INTO `Auto` (`Id`, `V`) VALUES (10, 'ten')");       // explicit, jumps
            engine.ExecuteNonQuery("INSERT INTO `Auto` (`V`) VALUES ('after')");              // -> 11 (continues)

            var ids = engine.ExecuteQuery("SELECT `Id` FROM `Auto` ORDER BY `Id`")
                .Rows.Select(r => Convert.ToInt32(r[0])).ToList();
            Assert.Equal([1, 2, 10, 11], ids);
        }
        finally { File.Delete(path); }
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
    public void Insert_round_trips_nulls_booleans_negative_numbers_and_double_quoted_text()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery(
                "CREATE TABLE `Mixed` (`Id` INTEGER PRIMARY KEY, `Name` TEXT(80), `Qty` INTEGER2, `Price` CURRENCY, `Flag` YESNO)");
            Assert.Equal(1, engine.ExecuteNonQuery(
                "INSERT INTO `Mixed` (`Id`, `Name`, `Qty`, `Price`, `Flag`) VALUES (1, \"O'Brien & Sons\", -12, -42.75, true)"));
            Assert.Equal(1, engine.ExecuteNonQuery(
                "INSERT INTO `Mixed` (`Id`, `Name`, `Flag`) VALUES (2, NULL, false)"));

            var rows = engine.ExecuteQuery(
                "SELECT `Id`, `Name`, `Qty`, `Price`, `Flag`, `Name` & '-checked' AS `Label` FROM `Mixed` ORDER BY `Id`")
                .Rows.ToList();

            Assert.Equal(2, rows.Count);
            Assert.Equal("O'Brien & Sons", rows[0][1]);
            Assert.Equal(-12, Convert.ToInt32(rows[0][2]));
            Assert.Equal(-42.75m, Convert.ToDecimal(rows[0][3]));
            Assert.True((bool)rows[0][4]!);
            Assert.Equal("O'Brien & Sons-checked", rows[0][5]);

            Assert.Null(rows[1][1]);
            Assert.Null(rows[1][2]);
            Assert.Null(rows[1][3]);
            Assert.False((bool)rows[1][4]!);
            Assert.Equal("-checked", rows[1][5]);
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
    public void Boolean_alias_logical1_maps_to_boolean()
    {
        string path = CopyToTemp();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(db).ExecuteNonQuery("CREATE TABLE `B` (`Id` INTEGER, `Flag` LOGICAL1)");
            Assert.Equal(LibRed.Catalog.JetDataType.Boolean, db.Catalog.FindTable("B")!.FindColumn("Flag")!.Type);
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
