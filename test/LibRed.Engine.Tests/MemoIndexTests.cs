using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A Memo (Long Text) column is indexable in Access; its index key is the text collation key over the first
// 255 characters. Exercise the insert path (RowInserter → IndexKeyEncoder) end-to-end through the engine.
public class MemoIndexTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"memoidx-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE MK (Id long PRIMARY KEY, M memo)");
        e.ExecuteNonQuery("CREATE INDEX IX_M ON MK (M)");
        return e;
    }

    [Fact]
    public void Rows_insert_into_a_memo_indexed_table_and_read_back()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO MK (Id, M) VALUES (1, 'hello')");
        e.ExecuteNonQuery("INSERT INTO MK (Id, M) VALUES (2, 'O''Brien')");   // ignorable apostrophe
        e.ExecuteNonQuery($"INSERT INTO MK (Id, M) VALUES (3, '{new string('z', 300)}')"); // past the 255-char key limit

        Assert.Equal(3, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM MK").Rows.Single()[0]));
        Assert.Equal("hello", e.ExecuteQuery("SELECT M FROM MK WHERE Id = 1").Rows.Single()[0]);
        Assert.Equal("O'Brien", e.ExecuteQuery("SELECT M FROM MK WHERE Id = 2").Rows.Single()[0]);
        Assert.Equal(300, ((string)e.ExecuteQuery("SELECT M FROM MK WHERE Id = 3").Rows.Single()[0]!).Length);
    }

    [Fact]
    public void Two_memos_differing_only_past_255_chars_share_a_key_but_both_insert()
    {
        var e = Fresh();
        // Keys are equal (both truncate to 255 'z'), but a non-unique index must accept both rows.
        e.ExecuteNonQuery($"INSERT INTO MK (Id, M) VALUES (1, '{new string('z', 255)}A')");
        e.ExecuteNonQuery($"INSERT INTO MK (Id, M) VALUES (2, '{new string('z', 255)}B')");
        Assert.Equal(2, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM MK").Rows.Single()[0]));
    }
}
