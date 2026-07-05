using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// LibRed rewrites a row in place (row id preserved) and Access reads the updated values back — including a
/// memo that grew onto an LVAL page and a variable-text column that grew the row (page repacked in place).
/// </summary>
public class UpdateAccessTests
{
    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        }
        throw new InvalidOperationException("No provider");
    }

    [Fact]
    public void Access_reads_a_libred_in_place_update_including_memo_growth()
    {
        string path = Path.Combine(Path.GetTempPath(), $"upd-ace-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        string big = new('x', 5000);
        try
        {
            RowId before, after;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                [
                    new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
                    new("N", JetDataType.Int32, 4, IsFixedLength: true),
                    new("S", JetDataType.Text, 200 * 2, IsFixedLength: false),
                    new("M", JetDataType.Memo, 0, IsFixedLength: false),
                ], primaryKey: ["Id"]);

                var table = db.OpenTable("T");
                for (int i = 0; i < 3; i++) table.Insert([null, i, $"short{i}", $"m{i}"]);

                var def = table.Definition;
                int idIdx = def.FindColumn("Id")!.Index;
                (before, var values) = table.Rows().WithIds().First(r => Convert.ToInt32(r.Values[idIdx]) == 2);

                var updated = (object?[])values.Clone();
                updated[def.FindColumn("N")!.Index] = 101;
                updated[def.FindColumn("S")!.Index] = "a considerably longer string value that grows the row well past its original size";
                updated[def.FindColumn("M")!.Index] = big;
                table.Update(before, updated);

                // Re-read: the row id (page + slot) is unchanged — an in-place rewrite.
                after = table.Rows().WithIds().First(r => Convert.ToInt32(r.Values[idIdx]) == 2).Id;
            }
            Assert.Equal(before, after);

            // Access opens the file and reads the updated values, memo included.
            using var conn = OpenOleDb(path);
            using var c = conn.CreateCommand();
            c.CommandText = "SELECT N, S, M FROM T WHERE Id = 2";
            using var r = c.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(101, Convert.ToInt32(r[0]));
            Assert.StartsWith("a considerably longer", (string)r[1]);
            Assert.Equal(big, (string)r[2]);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
