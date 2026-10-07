using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Reading an index where many rows share one key — ordinary for any non-unique index, and once a real bug.
//
// A full-BMP sweep died at U+4000 with "entry [7, 9) cannot contain its 4-byte trailer", IndexTree
// refusing a page ACE had written. CJK is largely ignorable in General v0, so thousands of rows shared the
// identical key, and once the index outgrew a single leaf the prefix compression became severe enough to
// break the reader's assumptions. 100 rows read fine; 500 and above read NOTHING.
//
// The cause was that the shared prefix covers the whole entry, trailer included — see IndexTree — so
// the stored remainder can be two bytes. These cases now assert, since nothing about them is exotic.
[Collection(AceCollection.Name)]
public class DuplicateIndexKeyProbeTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    [InlineData(4000)]
    public void Probe_reading_an_index_with_many_equal_keys(int rows)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, $"dupkey-{rows}-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Dup (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_Dup ON Dup (K)");
                for (int i = 0; i < rows; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Dup (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", "same");
                    insert.Parameters.AddWithValue("v", i);
                    insert.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("Dup");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Dup");

            int read = 0;
            Exception? failure = null;
            try
            {
                foreach ((byte[] _, RowId _) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
                    read++;
            }
            catch (Exception ex) { failure = ex; }

            output.WriteLine($"{rows} equal keys: read {read} entries" +
                             (failure is null ? " — OK" : $" then {failure.GetType().Name}: {failure.Message}"));
            Assert.Null(failure);
            Assert.Equal(rows, read);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The same leaves, written back: a delete rewrites the leaf at the prefix length ACE stored, and that
    // length reaches past the key into the row pointer.
    [Theory]
    [InlineData(500)]
    [InlineData(2000)]
    public void Deleting_a_row_from_a_leaf_ace_compressed_into_the_row_pointer(int rows)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, $"dupkey-delete-{rows}-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Dup (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_Dup ON Dup (K)");
                for (int i = 0; i < rows; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Dup (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", "same");
                    insert.Parameters.AddWithValue("v", i);
                    insert.ExecuteNonQuery();
                }
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var table = db.OpenTable("Dup");
                IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Dup");
                int keyIndex = table.Definition.FindColumn("K")!.Index;
                int valueIndex = table.Definition.FindColumn("V")!.Index;
                var key = new object?[table.Definition.Columns.Count];
                key[keyIndex] = "same";
                (RowId id, object?[] values) = table.SeekRowsWithIds(index, key)
                    .First(r => Convert.ToInt32(r.Values[valueIndex]) == rows / 2);
                table.Delete(id);
            }

            using (var connection = AceTestDatabase.Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM Dup WHERE K = 'same'";
                Assert.Equal(rows - 1, Convert.ToInt32(command.ExecuteScalar()));
            }

            using var reopened = JetDatabase.Open(path);
            var reread = reopened.OpenTable("Dup");
            IndexDef rereadIndex = reread.Definition.Indexes.Single(i => i.Name == "IX_Dup");
            Assert.Equal(rows - 1, new IndexCursor(reread.Channel, rereadIndex.RootPage).RawEntries().Count());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Dumps the raw bytes of the index root once duplicates have forced a second level, because the entry
    // layout has to be read off the page rather than reasoned about: under the model LibRed implements —
    // key suffix followed by a 4-byte trailer — a 2-byte entry cannot exist, yet ACE wrote one.
    [Fact]
    public void Probe_the_node_page_layout()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dupkey-dump-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Dup (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_Dup ON Dup (K)");
                for (int i = 0; i < 500; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Dup (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", "same");
                    insert.Parameters.AddWithValue("v", i);
                    insert.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("Dup");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Dup");
            var page = table.Channel.ReadPageShared(index.RootPage);
            Formats.JetFormatBase format = db.Format;
            (int prev, int next) = IndexTree.ReadSiblings(page.Span, format);

            output.WriteLine($"root page {index.RootPage}: type 0x{page.ReadByte(0):X2}, " +
                             $"owner {IndexTree.ReadOwner(page.Span, format)}, prev {prev}, " +
                             $"next {next}, tail {page.ReadInt32(format.IndexChildTailOffset)}, " +
                             $"compressed {IndexTree.ReadCompressedByteCount(page.Span, format)}, byte 0x1A 0x{page.ReadByte(format.IndexLevelOffset):X2}");

            var ends = new List<int>();
            ReadOnlySpan<byte> entryMask = page.Slice(format.IndexEntryMaskOffset, format.IndexEntryDataOffset - format.IndexEntryMaskOffset);
            for (int i = 0; i < entryMask.Length && ends.Count < 24; i++)
                for (int bit = 0; bit < 8; bit++)
                    if (BitmapBits.Get(entryMask, i * 8 + bit)) ends.Add(i * 8 + bit);
            output.WriteLine($"first entry ends: {string.Join(", ", ends)}");
            output.WriteLine($"entry data 0x1E0..+64: {Convert.ToHexString(page.Slice(format.IndexEntryDataOffset, 64))}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}