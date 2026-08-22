using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful: after LibRed widens a text column (ALTER COLUMN path, via AlterColumn), ACE reads the new max
// length and enforces it — a value that fits the new max is accepted, one past it is rejected.
public class AceAlterColumnTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_reads_and_enforces_a_libred_widened_text_column()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "alc-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("V", JetDataType.Text, 40, IsFixedLength: false)],   // TEXT(20)
                    primaryKey: ["K"]);
                db.AlterColumn("T", "V", new ColumnSpec("V", JetDataType.Text, 80, IsFixedLength: false)); // → TEXT(40)
            }

            using var conn = OpenOleDb(path);
            // 40 chars fits the new max
            using (var c = conn.CreateCommand()) { c.CommandText = $"INSERT INTO T (K, V) VALUES (1, '{new string('a', 40)}')"; c.ExecuteNonQuery(); }
            // 41 chars exceeds it → ACE rejects
            using var bad = conn.CreateCommand();
            bad.CommandText = $"INSERT INTO T (K, V) VALUES (2, '{new string('b', 41)}')";
            Assert.ThrowsAny<OleDbException>(() => bad.ExecuteNonQuery());

            string? v; using (var c = conn.CreateCommand()) { c.CommandText = "SELECT V FROM T WHERE K = 1"; v = (string?)c.ExecuteScalar(); }
            Assert.Equal(40, v!.Length);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_applies_a_libred_alter_column_default()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "acd-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("V", JetDataType.Text, 40, IsFixedLength: false)],
                    primaryKey: ["K"]);
                db.SetColumnDefault("T", "V", "'unknown'");   // the ALTER COLUMN ... DEFAULT write path
            }

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K) VALUES (1)"; c.ExecuteNonQuery(); }
            string? v; using (var c = conn.CreateCommand()) { c.CommandText = "SELECT V FROM T WHERE K = 1"; v = (string?)c.ExecuteScalar(); }
            Assert.Equal("unknown", v);   // ACE applied the LibRed-written default
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_sees_a_libred_dropped_default_gone_but_keeps_not_null()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "add-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("N", JetDataType.Int32, 4, IsFixedLength: true, IsNullable: false)],  // NOT NULL
                    primaryKey: ["K"]);
                db.SetColumnDefault("T", "N", "5");
                db.DropColumnDefault("T", "N");   // ALTER COLUMN ... DROP DEFAULT — removes only the default
            }

            using var conn = OpenOleDb(path);
            // Default is gone, so an omit-insert no longer supplies 5 — and N is still NOT NULL, so ACE rejects it.
            using var omit = conn.CreateCommand();
            omit.CommandText = "INSERT INTO T (K) VALUES (1)";
            Assert.ThrowsAny<OleDbException>(() => omit.ExecuteNonQuery());
            // Supplying a value still works (type intact).
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K, N) VALUES (2, 8)"; c.ExecuteNonQuery(); }
            int n; using (var c = conn.CreateCommand()) { c.CommandText = "SELECT N FROM T WHERE K = 2"; n = Convert.ToInt32(c.ExecuteScalar()); }
            Assert.Equal(8, n);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Byte-faithful: ACE recognises a LibRed-written GenGUID() default on a GUID column and applies it on its
    // own insert, generating a fresh Guid per row. (GenGUID() is default-only — ACE rejects it in a SELECT.)
    [Fact]
    public void Access_applies_a_libred_written_genguid_default()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "gg-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("G",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("U", JetDataType.Guid, 16, IsFixedLength: true)],
                    primaryKey: ["Id"]);
                db.SetColumnDefault("G", "U", "GenGUID()");
            }

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO G (Id) VALUES (1)"; c.ExecuteNonQuery(); }
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO G (Id) VALUES (2)"; c.ExecuteNonQuery(); }

            var guids = new List<Guid>();
            using (var q = conn.CreateCommand())
            {
                q.CommandText = "SELECT U FROM G ORDER BY Id";
                using var r = q.ExecuteReader();
                while (r.Read()) guids.Add((Guid)r.GetValue(0));
            }
            Assert.Equal(2, guids.Count);
            Assert.All(guids, g => Assert.NotEqual(Guid.Empty, g));
            Assert.NotEqual(guids[0], guids[1]);   // ACE generated a fresh Guid per row
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_enforces_a_libred_alter_column_made_required()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "areq-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("V", JetDataType.Text, 40, IsFixedLength: false)],   // V nullable
                    primaryKey: ["K"]);
                db.SetColumnRequired("T", "V", true);   // ALTER COLUMN ... NOT NULL
            }

            using (var conn = OpenOleDb(path))
            {
                using var bad = conn.CreateCommand();
                bad.CommandText = "INSERT INTO T (K) VALUES (1)";   // omit V → ACE rejects (now required)
                Assert.ThrowsAny<OleDbException>(() => bad.ExecuteNonQuery());
                using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K, V) VALUES (2, 'x')"; c.ExecuteNonQuery(); }
            }

            // LibRed clears it again; ACE then accepts an omitted V (structurally nullable — no Required property).
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.SetColumnRequired("T", "V", false);
            using (var conn = OpenOleDb(path))
            {
                using var c = conn.CreateCommand();
                c.CommandText = "INSERT INTO T (K) VALUES (3)";
                c.ExecuteNonQuery();
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_enforces_the_relationship_after_a_libred_parent_side_rewrite()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "prw-");
        try
        {
            using (var conn = OpenOleDb(path))
            {
                void Exec(string s) { using var c = conn.CreateCommand(); c.CommandText = s; c.ExecuteNonQuery(); }
                Exec("CREATE TABLE P ( PID LONG PRIMARY KEY, PData LONG )");
                Exec("CREATE TABLE C ( CID LONG PRIMARY KEY, PID LONG, CONSTRAINT FK FOREIGN KEY (PID) REFERENCES P (PID) )");
                Exec("INSERT INTO P (PID, PData) VALUES (1, 100)");
                Exec("INSERT INTO C (CID, PID) VALUES (10, 1)");
            }

            // LibRed rewrites the PARENT's non-relationship column (drops+recreates P, re-adds the child's FK).
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.AlterColumn("P", "PData", new ColumnSpec("PData", JetDataType.Double, 8, IsFixedLength: true));

            using (var conn = OpenOleDb(path))
            {
                // parent data converted
                double pdata; using (var c = conn.CreateCommand()) { c.CommandText = "SELECT PData FROM P WHERE PID = 1"; pdata = Convert.ToDouble(c.ExecuteScalar()); }
                Assert.Equal(100.0, pdata);
                // ACE still enforces the relationship: orphan rejected, valid parent accepted
                using (var bad = conn.CreateCommand()) { bad.CommandText = "INSERT INTO C (CID, PID) VALUES (20, 77)"; Assert.ThrowsAny<OleDbException>(() => bad.ExecuteNonQuery()); }
                using (var ok = conn.CreateCommand()) { ok.CommandText = "INSERT INTO C (CID, PID) VALUES (21, 1)"; ok.ExecuteNonQuery(); }
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Faithful round-trip: changing ONE column's type must not disturb another column's on-disk descriptor.
    // ACE authors the table (so the untouched column's bytes are ACE's, not LibRed's defaults); LibRed retypes a
    // different, same-width column (no layout shift), then the untouched column's 25 descriptor bytes must be
    // byte-identical — the RawDescriptor passthrough, not a rebuild-from-model that would stamp defaults.
    [Fact]
    public void Libred_type_change_preserves_an_untouched_columns_descriptor_bytes()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "pre-");
        try
        {
            using (var conn = OpenOleDb(path))
            {
                void Exec(string s) { using var c = conn.CreateCommand(); c.CommandText = s; c.ExecuteNonQuery(); }
                Exec("CREATE TABLE T ( A LONG, B LONG, C TEXT(20) )");
                Exec("INSERT INTO T (A, B, C) VALUES (1, 2, 'hi')");
            }

            byte[] before;
            using (var db = JetDatabase.Open(path, readOnly: true))
                before = (byte[])db.Catalog.FindTable("T")!.FindColumn("C")!.RawDescriptor!.Clone();

            // Retype A: LONG -> SINGLE (both 4-byte fixed → no row-layout shift, so C is wholly untouched).
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.AlterColumn("T", "A", new ColumnSpec("A", JetDataType.Single, 4, IsFixedLength: true));

            byte[] after;
            using (var db = JetDatabase.Open(path, readOnly: true))
                after = db.Catalog.FindTable("T")!.FindColumn("C")!.RawDescriptor!;

            Assert.Equal(before, after);   // C's descriptor bytes preserved verbatim

            // And ACE still reads the table (A converted, C intact).
            using var conn2 = OpenOleDb(path);
            using var q = conn2.CreateCommand();
            q.CommandText = "SELECT C FROM T";
            Assert.Equal("hi", (string?)q.ExecuteScalar());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_reads_a_libred_full_rewrite_with_converted_values()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "rw-");
        try
        {
            // ACE creates + populates a LONG column.
            using (var conn = OpenOleDb(path))
            {
                void Exec(string s) { using var c = conn.CreateCommand(); c.CommandText = s; c.ExecuteNonQuery(); }
                Exec("CREATE TABLE T ( K LONG PRIMARY KEY, N LONG )");
                Exec("INSERT INTO T (K, N) VALUES (1, 42)");
                Exec("INSERT INTO T (K, N) VALUES (2, 7)");
            }

            // LibRed rewrites N: LONG -> DOUBLE (full column rewrite, converting values).
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.AlterColumn("T", "N", new ColumnSpec("N", JetDataType.Double, 8, IsFixedLength: true));

            // ACE reads the converted data and treats N as a real Double (a fractional insert round-trips).
            using (var conn = OpenOleDb(path))
            {
                using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K, N) VALUES (3, 3.5)"; c.ExecuteNonQuery(); }
                var vals = new List<double>();
                using var q = conn.CreateCommand();
                q.CommandText = "SELECT N FROM T ORDER BY K";
                using var r = q.ExecuteReader();
                while (r.Read()) vals.Add(Convert.ToDouble(r[0]));
                Assert.Equal(new[] { 42.0, 7.0, 3.5 }, vals);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
