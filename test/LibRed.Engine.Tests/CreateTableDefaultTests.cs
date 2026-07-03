using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class CreateTableDefaultTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"def-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
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
        finally { try { File.Delete(path); } catch (IOException) { } }
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
        finally { try { File.Delete(path); } catch (IOException) { } }
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
        finally { try { File.Delete(path); } catch (IOException) { } }
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
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
