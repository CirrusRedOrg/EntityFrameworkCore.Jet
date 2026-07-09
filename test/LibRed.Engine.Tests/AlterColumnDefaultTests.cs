using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER TABLE ... ALTER COLUMN ... SET/DROP DEFAULT through LibRed's engine. An LvProp edit only (no retype):
// the default is applied on an omit-insert, DROP DEFAULT removes it (a later omit-insert reads NULL), and
// SET DEFAULT replaces it. EF Core emits `ALTER COLUMN c DROP DEFAULT` in migrations.
public class AlterColumnDefaultTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"altdef-eng-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY, V long)");
        return e;
    }

    private static object? ReadV(QueryEngine e, int id) =>
        e.ExecuteQuery($"SELECT V FROM T WHERE Id = {id}").Rows.Single()[0];

    [Fact]
    public void Set_then_drop_then_set_default()
    {
        var e = Fresh();

        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V SET DEFAULT 7");
        e.ExecuteNonQuery("INSERT INTO T (Id) VALUES (1)");
        Assert.Equal(7, Convert.ToInt32(ReadV(e, 1)));      // default applied on omit-insert

        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V DROP DEFAULT");
        e.ExecuteNonQuery("INSERT INTO T (Id) VALUES (2)");
        Assert.Null(ReadV(e, 2));                            // default gone → NULL

        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V SET DEFAULT 9");
        e.ExecuteNonQuery("INSERT INTO T (Id) VALUES (3)");
        Assert.Equal(9, Convert.ToInt32(ReadV(e, 3)));      // replacement default applied
    }

    [Fact]
    public void Drop_default_on_a_column_without_one_is_a_noop()
    {
        var e = Fresh();
        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V DROP DEFAULT");   // no default set — must not throw
        e.ExecuteNonQuery("INSERT INTO T (Id, V) VALUES (1, 42)");
        Assert.Equal(42, Convert.ToInt32(ReadV(e, 1)));
    }
}
