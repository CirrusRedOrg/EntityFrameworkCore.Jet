using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful: a Memo (Long Text) column IS indexable in Access, and its index key is the ordinary text
// collation key over only the value's first 255 characters. Verified against keys Access itself wrote,
// ascending and descending, including truncation and an "ignorable" character (apostrophe).
public class MemoKeyEncodingTests
{
    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider available.");
    }

    private static readonly string[] Values =
    [
        "",
        "a",
        "hello",
        "O'Brien",                 // ignorable apostrophe
        new string('x', 100),
        new string('y', 255),      // exactly the limit
        new string('z', 256),      // one past → key equals the 255-char prefix
        new string('w', 300),      // well past
    ];

    private static void AssertKeysMatchAccess(string indexDdl)
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-memokey-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var conn = OpenOleDb(path))
            {
                using (var c = conn.CreateCommand()) { c.CommandText = "CREATE TABLE MK (Id LONG CONSTRAINT PK PRIMARY KEY, M MEMO)"; c.ExecuteNonQuery(); }
                using (var c = conn.CreateCommand()) { c.CommandText = indexDdl; c.ExecuteNonQuery(); }
                for (int i = 0; i < Values.Length; i++)
                {
                    using var c = conn.CreateCommand();
                    c.CommandText = "INSERT INTO MK (Id, M) VALUES (?, ?)";
                    c.Parameters.AddWithValue("i", i);
                    c.Parameters.AddWithValue("m", Values[i]);
                    c.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("MK");
            var def = table.Definition;
            var mcol = def.FindColumn("M")!;
            IndexDef index = def.Indexes.Single(i => !i.IsPrimaryKey && i.Columns.Any(c => c.Column.Name == "M"));
            var decoder = new RowDecoder(def.Columns, db.Format, new LongValueReader(table.Channel));

            int checkedKeys = 0;
            foreach (var (accessKey, rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            {
                var s = (string?)decoder.Decode(db.ReadDataPage(rowId.Page).GetRow(rowId.Row))[mcol.Index] ?? "";

                var values = new object?[def.Columns.Count];
                values[mcol.Index] = s;
                byte[] ours = IndexKeyEncoder.Encode(index.Columns, values);

                Assert.True(accessKey.AsSpan().SequenceEqual(ours),
                    $"len {s.Length}: access={Convert.ToHexString(accessKey)} ours={Convert.ToHexString(ours)}");
                checkedKeys++;
            }

            Assert.Equal(Values.Length, checkedKeys);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Encoded_memo_keys_match_access_byte_for_byte_ascending()
        => AssertKeysMatchAccess("CREATE INDEX IX_M ON MK (M)");

    [Fact]
    public void Encoded_memo_keys_match_access_byte_for_byte_descending()
        => AssertKeysMatchAccess("CREATE INDEX IX_M ON MK (M DESC)");
}
