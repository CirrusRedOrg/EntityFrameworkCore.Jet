using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A Memo (Long Text) column is indexable in Access; its index key is the text collation key over the first
// 255 characters. Exercise the write paths (RowInserter → IndexKeyEncoder) end-to-end through the engine.
public class MemoIndexTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "memoidx-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
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

    // Deleting asks each index whether the row being removed held the last copy of its key, which means
    // encoding that key from the row. The decode behind that question was the one on the table path without a
    // long-value reader, so the Memo came back as its 12-byte on-disk descriptor and the key encoder's text
    // path cast a byte[] to string — deleting ANY row of a memo-indexed table threw.
    [Theory]
    [InlineData(5)]      // inline: the value sits in the row beside its descriptor
    [InlineData(4000)]   // chained onto its own long-value pages
    public void A_row_deletes_from_a_memo_indexed_table(int length)
    {
        string memo = new('m', length);
        var e = Fresh();
        e.ExecuteNonQuery($"INSERT INTO MK (Id, M) VALUES (1, '{memo}')");
        e.ExecuteNonQuery("INSERT INTO MK (Id, M) VALUES (2, 'second')");

        Assert.Equal(1, e.ExecuteNonQuery("DELETE FROM MK WHERE Id = 1"));

        Assert.Equal(2, Convert.ToInt32(e.ExecuteQuery("SELECT Id FROM MK").Rows.Single()[0]));
        // The survivor is still reachable through the memo index, and the deleted row is not — so the delete
        // maintained the index rather than leaving an entry behind.
        Assert.Single(e.ExecuteQuery("SELECT Id FROM MK WHERE M = 'second'").Rows);
        Assert.Empty(e.ExecuteQuery($"SELECT Id FROM MK WHERE M = '{memo}'").Rows);
    }

    // The same question on the UPDATE path, and the harder half of it: moving the index entry needs the OLD
    // key as well as the new one, so the old row's Memo has to be resolved rather than read as the 12-byte
    // descriptor standing in for it. Both storage forms, because the descriptor is all the row holds either
    // way and only the reader knows the difference.
    [Theory]
    [InlineData(5, 7)]          // inline to inline
    [InlineData(5, 4000)]       // inline to chained
    [InlineData(4000, 5)]       // chained to inline
    [InlineData(4000, 3000)]    // chained to chained
    public void A_memo_indexed_row_updates_and_keeps_its_index(int before, int after)
    {
        string old = new('m', before), replacement = new('n', after);
        var e = Fresh();
        e.ExecuteNonQuery($"INSERT INTO MK (Id, M) VALUES (1, '{old}')");
        e.ExecuteNonQuery("INSERT INTO MK (Id, M) VALUES (2, 'second')");

        Assert.Equal(1, e.ExecuteNonQuery($"UPDATE MK SET M = '{replacement}' WHERE Id = 1"));

        Assert.Equal(replacement, e.ExecuteQuery("SELECT M FROM MK WHERE Id = 1").Rows.Single()[0]);
        // The index moved with the value: the new one is reachable through it and the old one is gone.
        Assert.Single(e.ExecuteQuery($"SELECT Id FROM MK WHERE M = '{replacement}'").Rows);
        Assert.Empty(e.ExecuteQuery($"SELECT Id FROM MK WHERE M = '{old}'").Rows);
        Assert.Single(e.ExecuteQuery("SELECT Id FROM MK WHERE M = 'second'").Rows);
    }
}
