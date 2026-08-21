using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE (not an assertion of desired LibRed behaviour): what do ACE and LibRed do when a sequential
// AutoNumber (COUNTER) runs off the end of the signed Int32 range?
//
// The counter's on-disk state is a single Int32 — the TDEF high-water at 0x14 — and the next id is
// high-water + increment (0x18). Nothing in the format reserves a "counter exhausted" state, so the
// interesting questions are:
//   1. Does the engine refuse the insert, or does it wrap to the other end of Int32 and carry on?
//   2. If it wraps, what does the high-water become — and is the table then permanently wedged
//      (every subsequent auto id identical, so a duplicate primary key)?
//   3. Is a descending counter (negative increment) symmetric at int.MinValue?
//   4. Does an *explicit* insert of int.MaxValue poison a plain COUNTER the same way?
//
// Every probe logs what actually happened (the id assigned, or the engine's own error text) plus the
// resulting 0x14 high-water read back through LibRed's catalog, so ACE's and LibRed's behaviour sit
// side by side. Assertions are deliberately minimal — they pin only what has been observed.
public class AceAutoNumberOverflowProbeTest(ITestOutputHelper output)
{
    private static OleDbConnection OpenOleDb(string path)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 12; attempt++)
            foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
            {
                try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
                catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { last = ex; Thread.Sleep(40); }
            }
        throw new InvalidOperationException("no provider", last);
    }

    /// <summary>The TDEF AutoNumber high-water (0x14) — the last id handed out. ColumnDef.Seed is the *next* id.</summary>
    private static string HighWater(string path)
    {
        try
        {
            using var db = JetDatabase.Open(path, readOnly: true);
            var col = db.Catalog.FindTable("T")!.Columns.First(c => c.IsAutoNumber);
            return (col.Seed - col.Increment).ToString();
        }
        catch (Exception ex) { return $"<unreadable: {ex.GetType().Name}>"; }
    }

    private static string NewDb(string tag)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{tag}-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        return path;
    }

    // ---------------------------------------------------------------- ACE -----------------------------------------

    /// <summary>Runs one ACE auto insert and logs the id it produced, or the engine's refusal; returns the id, or null if refused.</summary>
    private int? AceInsert(OleDbConnection conn, string label)
    {
        try
        {
            using (var c = conn.CreateCommand()) { c.CommandText = $"INSERT INTO T (V) VALUES ('{label}')"; c.ExecuteNonQuery(); }
            using var q = conn.CreateCommand();
            q.CommandText = $"SELECT Id FROM T WHERE V = '{label}'";
            int id = Convert.ToInt32(q.ExecuteScalar());
            output.WriteLine($"  ACE insert '{label}' -> Id = {id}");
            return id;
        }
        catch (OleDbException ex) { output.WriteLine($"  ACE insert '{label}' -> <error: {ex.Message.Trim()}>"); return null; }
    }

    [Theory]
    // Ascending counter parked two below int.MaxValue: 2147483646, 2147483647, then wraps to int.MinValue.
    [InlineData("ace-max", 2147483646, 1, new[] { 2147483646, 2147483647, -2147483648, -2147483647 })]
    // Descending counter parked two above int.MinValue: mirror image — wraps to int.MaxValue.
    [InlineData("ace-min", -2147483647, -1, new[] { -2147483647, -2147483648, 2147483647, 2147483646 })]
    public void Probe_ace_counter_at_the_int32_boundary(string tag, int seed, int increment, int[] expectedIds)
    {
        string path = NewDb(tag);
        try
        {
            output.WriteLine($"ACE  COUNTER({seed}, {increment})");
            using var conn = OpenOleDb(path);
            try
            {
                using var ddl = conn.CreateCommand();
                ddl.CommandText = $"CREATE TABLE T (Id COUNTER({seed}, {increment}) CONSTRAINT PK PRIMARY KEY, V TEXT(5))";
                ddl.ExecuteNonQuery();
            }
            catch (OleDbException ex)
            {
                output.WriteLine($"  ACE rejected the DDL: {ex.Message.Trim()}");
                return;
            }

            var ids = new List<int?>();
            foreach (string label in new[] { "a", "b", "c", "d" })
            {
                ids.Add(AceInsert(conn, label));
                output.WriteLine($"    high-water (0x14) now {HighWater(path)}");
            }

            using (var dump = conn.CreateCommand())
            {
                dump.CommandText = "SELECT Id, V FROM T ORDER BY V";
                using var r = dump.ExecuteReader();
                while (r.Read()) output.WriteLine($"    row {r[1]} = {r[0]}");
            }

            // Observed: ACE never refuses. It wraps the counter two's-complement style and keeps issuing ids,
            // with the on-disk high-water following the wrapped value.
            Assert.Equal(expectedIds.Cast<int?>(), ids);
            Assert.Equal(expectedIds[^1].ToString(), HighWater(path));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Probe_ace_explicit_max_value_into_a_plain_counter()
    {
        string path = NewDb("ace-explicit");
        try
        {
            output.WriteLine("ACE  plain COUNTER, explicit INSERT of int.MaxValue, then auto inserts");
            // Each step gets its own connection: ACE writes the TDEF page lazily, so 0x14 read from a
            // *second* handle while ACE still holds the file can lag the counter it will actually use.
            void Step(Action<OleDbConnection> act, string label)
            {
                using (var conn = OpenOleDb(path))
                    try { act(conn); }
                    catch (OleDbException ex) { output.WriteLine($"  {label} -> <error: {ex.Message.Trim()}>"); }
                output.WriteLine($"    high-water (0x14) on disk now {HighWater(path)}");
            }

            Step(c => { using var x = c.CreateCommand(); x.CommandText = "CREATE TABLE T (Id COUNTER CONSTRAINT PK PRIMARY KEY, V TEXT(5))"; x.ExecuteNonQuery(); }, "create");
            Step(c => AceInsert(c, "a"), "auto insert 'a'");                                     // auto, so 1
            Step(c => { using var x = c.CreateCommand(); x.CommandText = "INSERT INTO T (Id, V) VALUES (2147483647, 'max')"; x.ExecuteNonQuery(); output.WriteLine("  explicit 2147483647 accepted"); }, "explicit 2147483647");
            Step(c => AceInsert(c, "next"), "auto insert 'next'");
            Step(c => AceInsert(c, "next2"), "auto insert 'next2'");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Probe_ace_seed_at_int32_max_and_a_wrapped_id_that_collides()
    {
        string path = NewDb("ace-collide");
        try
        {
            output.WriteLine("ACE  COUNTER(2147483647, 1) with an existing row already parked on int.MinValue");
            using var conn = OpenOleDb(path);
            void Exec(string sql) { using var c = conn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
            try { Exec("CREATE TABLE T (Id COUNTER(2147483647, 1) CONSTRAINT PK PRIMARY KEY, V TEXT(5))"); }
            catch (OleDbException ex) { output.WriteLine($"  ACE rejected the DDL: {ex.Message.Trim()}"); return; }

            // Park a row on the id the counter will wrap onto, so the wrap lands on an occupied key. The
            // explicit insert drops the counter onto that value (KB 884185 last-inserted rule), so reseed
            // back to int.MaxValue afterwards — otherwise the wrap never happens.
            try { Exec("INSERT INTO T (Id, V) VALUES (-2147483648, 'squat')"); output.WriteLine("  explicit -2147483648 accepted"); }
            catch (OleDbException ex) { output.WriteLine($"  explicit -2147483648 -> <error: {ex.Message.Trim()}>"); }
            try { Exec("ALTER TABLE T ALTER COLUMN Id COUNTER(2147483647, 1)"); output.WriteLine("  reseeded to COUNTER(2147483647, 1)"); }
            catch (OleDbException ex) { output.WriteLine($"  reseed -> <error: {ex.Message.Trim()}>"); }
            output.WriteLine($"    high-water (0x14) now {HighWater(path)}");

            int? a = AceInsert(conn, "a");   // the seed itself
            output.WriteLine($"    high-water (0x14) now {HighWater(path)}");
            int? b = AceInsert(conn, "b");   // the wrap — lands on 'squat'
            output.WriteLine($"    high-water (0x14) now {HighWater(path)}");
            int? c = AceInsert(conn, "c");   // does the table recover afterwards?
            output.WriteLine($"    high-water (0x14) now {HighWater(path)}");

            using (var dump = conn.CreateCommand())
            {
                dump.CommandText = "SELECT Id, V FROM T ORDER BY V";
                using var r = dump.ExecuteReader();
                while (r.Read()) output.WriteLine($"    row {r[1]} = {r[0]}");
            }

            // Observed: the wrap itself is fine; only a wrapped id that is already taken fails, and it fails as
            // an ordinary duplicate-key error. ACE still burns that id (0x14 advances even though the insert
            // was rejected), so the very next insert succeeds — the counter walks past the occupied slot.
            Assert.Equal(int.MaxValue, a);
            Assert.Null(b);
            Assert.Equal(int.MinValue + 1, c);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // -------------------------------------------------------------- LibRed ---------------------------------------

    [Theory]
    [InlineData("lib-max", 2147483646, 1, new[] { 2147483646, 2147483647, -2147483648, -2147483647 })]
    [InlineData("lib-min", -2147483647, -1, new[] { -2147483647, -2147483648, 2147483647, 2147483646 })]
    public void Probe_libred_counter_at_the_int32_boundary(string tag, int seed, int increment, int[] expectedIds)
    {
        string path = NewDb(tag);
        try
        {
            output.WriteLine($"LibRed  COUNTER({seed}, {increment})");
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true, Seed: seed, Increment: increment),
                     new ColumnSpec("V", JetDataType.Text, 5, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                var table = db.OpenTable("T");
                int idIdx = table.Definition.FindColumn("Id")!.Index;
                int vIdx = table.Definition.FindColumn("V")!.Index;

                foreach (string label in new[] { "a", "b", "c", "d" })
                {
                    try
                    {
                        table.Insert([null, label]);
                        var col = db.Catalog.FindTable("T")!.Columns.First(c => c.IsAutoNumber);
                        output.WriteLine($"  LibRed insert '{label}' -> ok; high-water (0x14) now {col.Seed - col.Increment}");
                    }
                    catch (Exception ex) { output.WriteLine($"  LibRed insert '{label}' -> <{ex.GetType().Name}: {ex.Message.Trim()}>"); }
                }

                var ids = new List<int>();
                foreach (object?[] row in table.Rows())
                {
                    output.WriteLine($"    row {row[vIdx]} = {row[idIdx]}");
                    ids.Add(Convert.ToInt32(row[idIdx]));
                }

                // LibRed wraps and carries on exactly as ACE does — the generated id always advances 0x14,
                // including past the boundary, so the counter doesn't wedge on the row after the wrap.
                Assert.Equal(expectedIds, ids);
            }

            // And what does ACE make of the file LibRed left behind? It must continue the wrapped sequence.
            using (var conn = OpenOleDb(path))
            {
                using (var q = conn.CreateCommand())
                {
                    q.CommandText = "SELECT Id, V FROM T ORDER BY V";
                    using var r = q.ExecuteReader();
                    while (r.Read()) output.WriteLine($"    ACE reads row {r[1]} = {r[0]}");
                }
                Assert.Equal(unchecked(expectedIds[^1] + increment), AceInsert(conn, "e"));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Probe_libred_explicit_max_value_into_a_plain_counter()
    {
        string path = NewDb("lib-explicit");
        try
        {
            output.WriteLine("LibRed  plain COUNTER, explicit insert of int.MaxValue, then auto inserts");
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("T",
                [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
                 new ColumnSpec("V", JetDataType.Text, 5, IsFixedLength: false)],
                primaryKey: ["Id"]);
            var table = db.OpenTable("T");
            int idIdx = table.Definition.FindColumn("Id")!.Index;
            int vIdx = table.Definition.FindColumn("V")!.Index;

            void Report(string what, Action act)
            {
                try
                {
                    act();
                    var col = db.Catalog.FindTable("T")!.Columns.First(c => c.IsAutoNumber);
                    output.WriteLine($"  {what} -> ok; high-water (0x14) now {col.Seed - col.Increment}");
                }
                catch (Exception ex) { output.WriteLine($"  {what} -> <{ex.GetType().Name}: {ex.Message.Trim()}>"); }
            }

            Report("auto insert 'a'", () => table.Insert([null, "a"]));
            Report("explicit int.MaxValue", () => table.Insert([int.MaxValue, "max"]));
            Report("auto insert 'next'", () => table.Insert([null, "next"]));
            Report("auto insert 'next2'", () => table.Insert([null, "next2"]));

            var ids = new List<int>();
            foreach (object?[] row in table.Rows())
            {
                output.WriteLine($"    row {row[vIdx]} = {row[idIdx]}");
                ids.Add(Convert.ToInt32(row[idIdx]));
            }

            // Matches ACE: the explicit int.MaxValue takes the counter with it, and the two ids after it are
            // the wrapped continuation rather than a repeated int.MinValue.
            Assert.Equal([1, int.MaxValue, int.MinValue, int.MinValue + 1], ids);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
