using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class TextKeyEncodingTests
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

    [Fact]
    public void Encoded_text_keys_match_access_byte_for_byte()
    {
        // Build a real text-PK table via Access, then check our encoder reproduces each stored key.
        string[] values =
        [
            "A", "B", "C", "M", "Z", "AA", "AB", "ABC", "BA",
            "Apple", "Banana", "Cherry", "A B", " A",
            "0", "1", "9", "Order123", "Customer", "Z9",
            "(paren)", "a.b", "x/y", "p+q", "k=v",
        ];

        string path = Path.Combine(Path.GetTempPath(), $"libred-textkey-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var conn = OpenOleDb(path))
            {
                using (var c = conn.CreateCommand())
                { c.CommandText = "CREATE TABLE TKey (K varchar(30) PRIMARY KEY, V int)"; c.ExecuteNonQuery(); }
                for (int i = 0; i < values.Length; i++)
                {
                    using var c = conn.CreateCommand();
                    c.CommandText = "INSERT INTO TKey (K, V) VALUES (?, ?)";
                    c.Parameters.AddWithValue("k", values[i]);
                    c.Parameters.AddWithValue("v", i);
                    c.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("TKey");
            var def = table.Definition;
            IndexDef pk = def.Indexes.Single(i => i.IsPrimaryKey);
            int kIdx = def.FindColumn("K")!.Index;
            var decoder = new RowDecoder(def.Columns, db.Format);

            int checkd = 0;
            foreach (var (accessKey, rowId) in new IndexCursor(table.Channel, pk.RootPage).RawEntries())
            {
                string k = (string)decoder.Decode(db.ReadDataPage(rowId.Page).GetRow(rowId.Row))[kIdx]!;

                var values2 = new object?[def.Columns.Count];
                values2[kIdx] = k;
                byte[] ours = IndexKeyEncoder.Encode(pk.Columns, values2);

                Assert.True(accessKey.AsSpan().SequenceEqual(ours),
                    $"'{k}': access={Convert.ToHexString(accessKey)} ours={Convert.ToHexString(ours)}");
                checkd++;
            }
            Assert.Equal(values.Length, checkd);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
