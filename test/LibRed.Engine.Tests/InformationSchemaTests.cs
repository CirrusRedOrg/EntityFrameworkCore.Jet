using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>The Jet-flavoured INFORMATION_SCHEMA views are engine-native virtual tables over the catalog, so EF's
/// migration existence checks (<c>SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = '…'</c>) work.</summary>
public class InformationSchemaTests
{
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"is-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE Widget (Id LONG PRIMARY KEY, Name TEXT(50))");
        return e;
    }

    [Fact]
    public void Tables_view_lists_user_tables_and_filters_by_name()
    {
        var e = Seeded();
        int hit = e.ExecuteQuery("SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = 'Widget'").Rows.Count();
        Assert.Equal(1, hit);
        int miss = e.ExecuteQuery("SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = 'Nope'").Rows.Count();
        Assert.Equal(0, miss);
    }

    [Fact]
    public void Columns_view_lists_columns()
    {
        var e = Seeded();
        var rows = e.ExecuteQuery("SELECT `COLUMN_NAME` FROM `INFORMATION_SCHEMA.COLUMNS` WHERE `TABLE_NAME` = 'Widget'").Rows.ToList();
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void All_seven_views_bind_and_execute()
    {
        var e = Seeded();
        foreach (string v in new[] { "TABLES", "COLUMNS", "INDEXES", "INDEX_COLUMNS", "RELATIONS", "RELATION_COLUMNS", "CHECK_CONSTRAINTS" })
            _ = e.ExecuteQuery($"SELECT * FROM `INFORMATION_SCHEMA.{v}`").Rows.ToList(); // must not throw
    }
}

// -- IF [NOT] EXISTS (...) THEN <statement> --
public class IfThenStatementTests
{
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"if-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE Existing (Id LONG PRIMARY KEY)");
        return e;
    }

    private static bool TableExists(QueryEngine e, string t) =>
        e.ExecuteQuery($"SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = '{t}'").Rows.Any();

    [Fact]
    public void If_not_exists_creates_when_absent()
    {
        var e = Seeded();
        e.ExecuteNonQuery("IF NOT EXISTS (SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = 'Fresh') THEN CREATE TABLE `Fresh` (`Id` LONG PRIMARY KEY)");
        Assert.True(TableExists(e, "Fresh"));
    }

    [Fact]
    public void If_not_exists_skips_when_present()
    {
        var e = Seeded();
        // Existing already has a table; the guarded CREATE must be skipped (no "already exists" throw).
        e.ExecuteNonQuery("IF NOT EXISTS (SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = 'Existing') THEN CREATE TABLE `Existing` (`Id` LONG PRIMARY KEY)");
        Assert.True(TableExists(e, "Existing"));
    }

    [Fact]
    public void If_exists_runs_body_when_present()
    {
        var e = Seeded();
        e.ExecuteNonQuery("IF EXISTS (SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = 'Existing') THEN CREATE TABLE `Made` (`Id` LONG PRIMARY KEY)");
        Assert.True(TableExists(e, "Made"));
    }
}
