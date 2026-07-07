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

    [Fact]
    public void Change_numeric_type_converts_values()
    {
        var (e, db) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, N LONG )");
        e.ExecuteNonQuery("INSERT INTO T (K, N) VALUES (1, 42)");
        e.ExecuteNonQuery("INSERT INTO T (K, N) VALUES (2, 7)");

        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN N DOUBLE");   // LONG -> DOUBLE, full rewrite

        var col = db.Catalog.UserTables.Single(t => t.Name == "T").Columns.Single(c => c.Name == "N");
        Assert.Equal(LibRed.Catalog.JetDataType.Double, col.Type);
        var rows = e.ExecuteQuery("SELECT K, N FROM T ORDER BY K").Rows
            .Select(r => (Convert.ToInt32(r[0]), Convert.ToDouble(r[1]))).ToArray();
        Assert.Equal([(1, 42.0), (2, 7.0)], rows);
    }

    [Fact]
    public void Change_text_to_number_converts_and_keeps_other_columns()
    {
        var (e, db) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, Label TEXT(20), V TEXT(20) )");
        e.ExecuteNonQuery("INSERT INTO T (K, Label, V) VALUES (1, 'one', '42')");
        e.ExecuteNonQuery("INSERT INTO T (K, Label, V) VALUES (2, 'two', '100')");

        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V LONG");   // TEXT -> LONG

        Assert.Equal(LibRed.Catalog.JetDataType.Int32,
            db.Catalog.UserTables.Single(t => t.Name == "T").Columns.Single(c => c.Name == "V").Type);
        var rows = e.ExecuteQuery("SELECT K, Label, V FROM T ORDER BY K").Rows
            .Select(r => ($"{r[0]}", $"{r[1]}", Convert.ToInt32(r[2]))).ToArray();
        Assert.Equal([("1", "one", 42), ("2", "two", 100)], rows);
    }

    [Fact]
    public void Rewrite_preserves_primary_key_uniqueness()
    {
        var (e, _) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, N SHORT )");
        e.ExecuteNonQuery("INSERT INTO T (K, N) VALUES (1, 5)");
        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN N LONG");    // SHORT -> LONG
        // PK still enforced after the rebuild
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO T (K, N) VALUES (1, 9)"));
        e.ExecuteNonQuery("INSERT INTO T (K, N) VALUES (2, 9)");
    }

    [Fact]
    public void Rewrite_preserves_a_secondary_unique_index()
    {
        var (e, _) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, N SHORT, C TEXT(20) )");
        e.ExecuteNonQuery("CREATE UNIQUE INDEX UQ_C ON T (C)");
        e.ExecuteNonQuery("INSERT INTO T (K, N, C) VALUES (1, 5, 'a')");
        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN N LONG");
        // the unique index on C survives the rebuild
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO T (K, N, C) VALUES (2, 9, 'a')"));
        e.ExecuteNonQuery("INSERT INTO T (K, N, C) VALUES (3, 9, 'b')");
    }

    [Fact]
    public void Rewrite_rejects_a_table_in_a_relationship()
    {
        var (e, _) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE P ( PID LONG PRIMARY KEY, V LONG )");
        e.ExecuteNonQuery("CREATE TABLE C ( CID LONG PRIMARY KEY, PID LONG, CONSTRAINT FK FOREIGN KEY (PID) REFERENCES P (PID) )");
        Assert.Throws<NotSupportedException>(() => e.ExecuteNonQuery("ALTER TABLE P ALTER COLUMN V DOUBLE"));
    }
}
