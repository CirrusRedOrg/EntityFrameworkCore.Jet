using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class CreateTableDefaultTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "def-");
        return path;
    }

    // EF Core emits column DEFAULT values (which the ACE CREATE TABLE grammar accepts as a field
    // attribute). We parse every common form so the statement no longer fails; the table is created and
    // usable (defaults are captured but not yet persisted — EF supplies values on its own inserts).
    [Theory]
    [InlineData("`N` INTEGER DEFAULT 0")]
    [InlineData("`N` INTEGER DEFAULT -1")]
    [InlineData("`N` INTEGER DEFAULT 0 NOT NULL")]           // DEFAULT before NOT NULL
    [InlineData("`N` INTEGER NOT NULL DEFAULT 5")]           // and after
    [InlineData("`S` VARCHAR(20) DEFAULT 'hi'")]
    [InlineData("`B` SMALLINT DEFAULT TRUE")]
    [InlineData("`D` DOUBLE DEFAULT 3.14")]
    public void Column_default_parses_and_table_is_created(string columnDdl)
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery($"CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, {columnDdl})");
            using (var db = JetDatabase.Open(path))
                Assert.NotNull(db.Catalog.FindTable("T"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A DEFAULT is persisted to the table's LvProp property blob, read back onto the column, and applied
    // when an insert omits the column (EF Core relies on the store default rather than supplying a value).
    [Fact]
    public void Default_persists_reads_back_and_applies_on_insert()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(
                    "CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `Age` INTEGER DEFAULT 42, `Nm` VARCHAR(20) DEFAULT 'hi')");

            // Reopen: the defaults are read back onto the columns.
            using (var db = JetDatabase.Open(path))
            {
                var t = db.Catalog.FindTable("T")!;
                Assert.Equal("42", t.FindColumn("Age")!.DefaultValue);
                Assert.Equal("'hi'", t.FindColumn("Nm")!.DefaultValue);
                Assert.Null(t.FindColumn("Id")!.DefaultValue);
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`) VALUES (1)");            // omits Age, Nm → defaults
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Age`) VALUES (2, 99)"); // Age explicit, Nm default
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Nm`) VALUES (3, NULL)"); // explicit NULL stays NULL

                var r1 = e.ExecuteQuery("SELECT `Age`, `Nm` FROM `T` WHERE `Id` = 1").Rows.Single();
                Assert.Equal(42, Convert.ToInt32(r1[0]));
                Assert.Equal("hi", r1[1]);

                var r2 = e.ExecuteQuery("SELECT `Age`, `Nm` FROM `T` WHERE `Id` = 2").Rows.Single();
                Assert.Equal(99, Convert.ToInt32(r2[0]));
                Assert.Equal("hi", r2[1]);

                var r3 = e.ExecuteQuery("SELECT `Nm` FROM `T` WHERE `Id` = 3").Rows.Single();
                Assert.Null(r3[0]); // explicit NULL, not the default
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // `INSERT INTO t DEFAULT VALUES` (EF emits it for an all-store-generated/all-default row): one row is
    // inserted where the AutoNumber is assigned, DEFAULT columns take their default, and the rest are NULL.
    [Fact]
    public void Insert_default_values_inserts_one_all_defaults_row()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery(
                "CREATE TABLE `Offers` (`Id` counter PRIMARY KEY, `Kind` VARCHAR(20) DEFAULT 'std', `Note` VARCHAR(20))");

            Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO `Offers` DEFAULT VALUES"));
            Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO `Offers` DEFAULT VALUES")); // second row → next AutoNumber

            var rows = e.ExecuteQuery("SELECT `Id`, `Kind`, `Note` FROM `Offers`").Rows.ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal([1, 2], rows.Select(r => Convert.ToInt32(r[0])).OrderBy(x => x).ToArray());
            Assert.All(rows, r => Assert.Equal("std", r[1])); // DEFAULT applied
            Assert.All(rows, r => Assert.Null(r[2]));          // no default → NULL
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A NOT NULL column with no default that an insert leaves unset is rejected — matching Access
    // ("You must enter a value in the 'T.Req' field."), for both an explicit insert and DEFAULT VALUES.
    [Fact]
    public void Insert_leaving_a_required_column_null_throws()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE `T` (`Id` counter PRIMARY KEY, `Req` int NOT NULL, `Opt` int)");

            // Omit the required column.
            var ex = Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO `T` (`Opt`) VALUES (5)"));
            Assert.Contains("T.Req", ex.Message);
            // Explicit NULL into the required column is likewise rejected.
            Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO `T` (`Req`) VALUES (NULL)"));
            // DEFAULT VALUES can't satisfy a required, default-less column either.
            Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO `T` DEFAULT VALUES"));

            // Providing the value succeeds.
            Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO `T` (`Req`) VALUES (7)"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Update_setting_a_required_column_null_throws_without_mutating_the_row()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE `T` (`Id` int PRIMARY KEY, `Req` varchar(20) NOT NULL)");
            e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Req`) VALUES (1, 'kept')");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                e.ExecuteNonQuery("UPDATE `T` SET `Req` = NULL WHERE `Id` = 1"));

            Assert.Contains("T.Req", ex.Message);
            Assert.Equal("kept", e.ExecuteQuery("SELECT `Req` FROM `T` WHERE `Id` = 1").Rows.Single()[0]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // DEFAULT VALUES succeeds when every required column is covered by a DEFAULT (or is the AutoNumber).
    [Fact]
    public void Insert_default_values_succeeds_when_required_columns_have_defaults()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE `T` (`Id` counter PRIMARY KEY, `Kind` int DEFAULT 3 NOT NULL)");
            Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO `T` DEFAULT VALUES"));
            Assert.Equal(3, Convert.ToInt32(e.ExecuteQuery("SELECT `Kind` FROM `T`").Rows.Single()[0]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Creating a table whose name already exists (case-insensitively) is rejected, rather than writing a
    // second MSysObjects row that shadows the existing table.
    [Fact]
    public void Duplicate_table_name_throws()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE `Widget` (`Id` INTEGER PRIMARY KEY)");
            var ex = Assert.Throws<SchemaObjectExistsException>(() =>
                e.ExecuteNonQuery("CREATE TABLE `widget` (`Id` INTEGER PRIMARY KEY)")); // different case
            Assert.Contains("already exists", ex.Message);
            Assert.Equal("widget", ex.ObjectName);
            // An existing Northwind table is also protected.
            Assert.Throws<SchemaObjectExistsException>(() =>
                e.ExecuteNonQuery("CREATE TABLE `Shippers` (`Id` INTEGER PRIMARY KEY)"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Temporary_table_throws_not_supported()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var ex = Assert.Throws<NotSupportedException>(() =>
                new QueryEngine(db).ExecuteNonQuery("CREATE TEMPORARY TABLE `T` (`Id` INTEGER)"));
            Assert.Contains("TEMPORARY", ex.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void With_compression_throws_not_supported()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var ex = Assert.Throws<NotSupportedException>(() =>
                new QueryEngine(db).ExecuteNonQuery("CREATE TABLE `T` (`S` VARCHAR(20) WITH COMPRESSION)"));
            Assert.Contains("COMPRESSION", ex.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
