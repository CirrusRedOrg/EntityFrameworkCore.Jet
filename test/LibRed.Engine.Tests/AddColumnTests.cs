using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER TABLE ... ADD COLUMN through LibRed's engine. Metadata TDEF edit: the column is appended, existing
// rows read it as NULL, and (on an empty table) subsequent inserts include it.
public class AddColumnTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"addcol-eng-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY, A long)");
        return e;
    }

    [Fact]
    public void Add_columns_to_an_empty_table_then_insert_and_read()
    {
        var e = Fresh();
        e.ExecuteNonQuery("ALTER TABLE T ADD COLUMN B text(20)");   // variable
        e.ExecuteNonQuery("ALTER TABLE T ADD COLUMN C long");        // fixed

        Assert.Equal(["Id", "A", "B", "C"], e.ExecuteQuery("SELECT * FROM T").ColumnNames);
        e.ExecuteNonQuery("INSERT INTO T (Id, A, B, C) VALUES (1, 10, 'bee', 30)");
        var r = e.ExecuteQuery("SELECT A, B, C FROM T WHERE Id = 1").Rows.Single();
        Assert.Equal(10, Convert.ToInt32(r[0]));
        Assert.Equal("bee", r[1]);
        Assert.Equal(30, Convert.ToInt32(r[2]));
    }

    [Fact]
    public void Added_column_reads_null_for_existing_rows()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO T (Id, A) VALUES (1, 10)");
        e.ExecuteNonQuery("INSERT INTO T (Id, A) VALUES (2, 20)");
        e.ExecuteNonQuery("ALTER TABLE T ADD COLUMN Note text(20)");

        var rows = e.ExecuteQuery("SELECT Id, A, Note FROM T ORDER BY Id").Rows.ToList();
        Assert.Equal(new object?[] { 1, 10, null }, [rows[0][0], rows[0][1], rows[0][2]]);
        Assert.Equal(new object?[] { 2, 20, null }, [rows[1][0], rows[1][1], rows[1][2]]);
    }

    [Fact]
    public void Adding_a_duplicate_column_is_rejected()
    {
        var e = Fresh();
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("ALTER TABLE T ADD COLUMN A long")); // exists
    }

    [Fact]
    public void Added_column_default_is_applied_and_required_is_enforced()
    {
        var e = Fresh();
        e.ExecuteNonQuery("ALTER TABLE T ADD COLUMN Qty long DEFAULT 1");
        e.ExecuteNonQuery("ALTER TABLE T ADD COLUMN Name text(20) NOT NULL");

        // DEFAULT applied when the column is omitted on insert.
        e.ExecuteNonQuery("INSERT INTO T (Id, A, Name) VALUES (1, 10, 'x')");
        Assert.Equal(1, Convert.ToInt32(e.ExecuteQuery("SELECT Qty FROM T WHERE Id = 1").Rows.Single()[0]));

        // NOT NULL enforced: omitting the required column (no default) is rejected.
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO T (Id, A) VALUES (2, 20)"));
    }
}
