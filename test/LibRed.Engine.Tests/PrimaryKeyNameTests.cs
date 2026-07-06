using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// The PRIMARY KEY's CONSTRAINT name is stored as the primary-key index's name (ACE names the PK index after
// the constraint), so it round-trips back through the catalog — this is what the scaffolder reports. When no
// name is given, LibRed picks the stable "PrimaryKey" as its own engine fallback (ACE-via-SQL instead
// generates a random "Index_<hex>" — no fixed value to reproduce, and nothing downstream depends on it).
public class PrimaryKeyNameTests
{
    private static (QueryEngine Engine, JetDatabase Db) Setup()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pkname-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var db = JetDatabase.Open(path, readOnly: false);
        return (new QueryEngine(db), db);
    }

    private static string PkIndexName(JetDatabase db, string table) =>
        db.Catalog.UserTables.Single(t => t.Name == table).Indexes.Single(i => i.IsPrimaryKey).Name;

    [Fact]
    public void Column_level_named_primary_key_keeps_its_constraint_name()
    {
        var (e, db) = Setup();
        e.ExecuteNonQuery("CREATE TABLE T ( Id int CONSTRAINT PK__T PRIMARY KEY )");
        Assert.Equal("PK__T", PkIndexName(db, "T"));
    }

    [Fact]
    public void Table_level_named_primary_key_keeps_its_constraint_name()
    {
        var (e, db) = Setup();
        e.ExecuteNonQuery("CREATE TABLE T ( Id1 int, Id2 int, CONSTRAINT MyPK PRIMARY KEY ( Id2 ) )");
        Assert.Equal("MyPK", PkIndexName(db, "T"));
    }

    [Fact]
    public void Unnamed_primary_key_uses_the_engine_fallback_name()
    {
        var (e, db) = Setup();
        e.ExecuteNonQuery("CREATE TABLE T ( Id int PRIMARY KEY )");
        Assert.Equal("PrimaryKey", PkIndexName(db, "T"));
    }
}
