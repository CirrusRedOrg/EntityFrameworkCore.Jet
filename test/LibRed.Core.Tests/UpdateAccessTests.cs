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

    [Fact]
    public void Access_reads_a_libred_relocated_row()
    {
        string path = Path.Combine(Path.GetTempPath(), $"reloc-lr-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        string mid = new('m', 80), big = new('X', 255);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                [
                    new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
                    new("A", JetDataType.Text, 255 * 2, IsFixedLength: false),
                    new("B", JetDataType.Text, 255 * 2, IsFixedLength: false),
                    new("C", JetDataType.Text, 255 * 2, IsFixedLength: false),
                ], primaryKey: ["Id"]);
                var table = db.OpenTable("T");
                for (int i = 0; i < 7; i++) table.Insert([null, mid, mid, mid]); // ~fill a page

                int idIdx = table.Definition.FindColumn("Id")!.Index;
                (RowId id, object?[] old) = table.Rows().WithIds().First(x => Convert.ToInt32(x.Values[idIdx]) == 3);
                var updated = (object?[])old.Clone();
                foreach (var col in new[] { "A", "B", "C" }) updated[table.Definition.FindColumn(col)!.Index] = big;
                table.Update(id, updated); // grows past the page → relocates
            }

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT COUNT(*) FROM T"; Assert.Equal(7, Convert.ToInt32(c.ExecuteScalar())); }
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT A FROM T WHERE Id = 3"; Assert.Equal(big, c.ExecuteScalar()); }        // the relocated row
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT A FROM T WHERE Id = 4"; Assert.Equal(mid, c.ExecuteScalar()); }        // a neighbour
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Libred_reads_an_access_relocated_row()
    {
        string path = Path.Combine(Path.GetTempPath(), $"reloc-ace-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        string mid = new('m', 80), big = new('X', 255);
        try
        {
            using (var conn = OpenOleDb(path))
            {
                Exec(conn, "CREATE TABLE T (Id counter PRIMARY KEY, A text(255), B text(255), C text(255))");
                for (int i = 0; i < 7; i++) Exec(conn, $"INSERT INTO T (A,B,C) VALUES ('{mid}','{mid}','{mid}')");
                Exec(conn, $"UPDATE T SET A='{big}', B='{big}', C='{big}' WHERE Id = 3"); // ACE relocates it
            }

            // LibRed follows ACE's forward pointer and reads every row, including the relocated one.
            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("T");
            int idIdx = table.Definition.FindColumn("Id")!.Index, aIdx = table.Definition.FindColumn("A")!.Index;
            var rows = table.Rows().WithIds().ToList();
            Assert.Equal(7, rows.Count);
            Assert.Equal(big, rows.Single(r => Convert.ToInt32(r.Values[idIdx]) == 3).Values[aIdx]);
            Assert.Equal(mid, rows.Single(r => Convert.ToInt32(r.Values[idIdx]) == 4).Values[aIdx]);

            static void Exec(OleDbConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Access_seeks_a_libred_updated_primary_key()
    {
        string path = Path.Combine(Path.GetTempPath(), $"upd-key-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new("N", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"]);
                var table = db.OpenTable("T");
                for (int i = 1; i <= 5; i++) table.Insert([i, i * 10]);

                var def = table.Definition;
                int idIdx = def.FindColumn("Id")!.Index;
                var pk = def.Indexes.First(i => i.IsPrimaryKey);
                (RowId id, object?[] old) = table.Rows().WithIds().First(x => Convert.ToInt32(x.Values[idIdx]) == 2);

                var updated = (object?[])old.Clone();
                updated[idIdx] = 20;
                table.Update(id, updated);
                table.MoveIndexEntry(pk, old, updated, id);
            }

            // Access seeks the row by its NEW primary key (using the index we moved) and no longer by the old.
            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT N FROM T WHERE Id = 20"; Assert.Equal(20, Convert.ToInt32(c.ExecuteScalar())); }
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT COUNT(*) FROM T WHERE Id = 2"; Assert.Equal(0, Convert.ToInt32(c.ExecuteScalar())); }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
