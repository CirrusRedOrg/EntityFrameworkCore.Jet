using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER TABLE ... ALTER COLUMN field type: changes a variable text/binary column's max length (a descriptor
// edit; existing rows are untouched, since variable columns store their own length). Changing the storage type
// throws NotSupported (would need a full column rewrite).
public class AlterColumnTests
{
    private static (QueryEngine Engine, JetDatabase Db) Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"alc-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var db = JetDatabase.Open(path, readOnly: false);
        return (new QueryEngine(db), db);
    }

    private static int Len(JetDatabase db, string table, string col)
        => db.Catalog.UserTables.Single(t => t.Name == table).Columns.Single(c => c.Name == col).Length;

    [Fact]
    public void Widen_a_text_column_keeps_existing_data()
    {
        var (e, db) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, V TEXT(20) )");
        e.ExecuteNonQuery("INSERT INTO T (K, V) VALUES (1, 'short')");
        Assert.Equal(40, Len(db, "T", "V"));   // 20 chars * 2 bytes

        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V TEXT(40)");
        Assert.Equal(80, Len(db, "T", "V"));   // 40 chars * 2

        Assert.Equal("short", e.ExecuteQuery("SELECT V FROM T").Rows.Single()[0]);   // data survives
        // and a longer value now fits
        e.ExecuteNonQuery("INSERT INTO T (K, V) VALUES (2, 'a much longer value than twenty chars')");
    }

    [Fact]
    public void Narrow_a_text_column_is_a_metadata_change()
    {
        var (e, db) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, V TEXT(40) )");
        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V TEXT(10)");
        Assert.Equal(20, Len(db, "T", "V"));
    }

    [Theory]
    [InlineData("ALTER TABLE T ALTER COLUMN V LONG")]       // text -> number (storage change)
    [InlineData("ALTER TABLE T ALTER COLUMN K DOUBLE")]     // numeric type change on a fixed column
    public void Storage_type_change_is_not_supported(string sql)
    {
        var (e, _) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, V TEXT(20) )");
        Assert.Throws<NotSupportedException>(() => e.ExecuteNonQuery(sql));
    }
}
