using System.Data.OleDb;
using System.Text;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// BYTE-DIFF HARNESS — captures ACE's in-place column-modify delta. Creates T + a row via ACE, snapshots the
// file, has ACE change one column's type, then diffs the two files page-by-page. The reported delta is the
// exact set of bytes LibRed's in-place RewriteColumn must reproduce to be byte-for-byte faithful.
public class AceModifyByteDiffProbe
{
    private const int PageSize = 4096;

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

    private static void Exec(OleDbConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    private static void CopyWithRetry(string src, string dst)
    {
        for (int i = 0; i < 20; i++) { try { File.Copy(src, dst, overwrite: true); return; } catch (IOException) { Thread.Sleep(50); } }
        File.Copy(src, dst, overwrite: true);
    }

    // Verifies LibRed's in-place TDEF edit produces the SAME TDEF-page bytes as ACE's own column-modify.
    // Both start from an identical ACE-created file, so the TDEF page number matches; after each does
    // `B LONG -> DOUBLE`, that page must be byte-identical (rows aren't re-laid by LibRed yet, so only the
    // TDEF page is compared).
    [Fact]
    public void Libred_in_place_tdef_edit_matches_ace_tdef_page()
    {
        string start = Path.Combine(Path.GetTempPath(), $"ip-start-{Guid.NewGuid():N}.accdb");
        string ace = Path.Combine(Path.GetTempPath(), $"ip-ace-{Guid.NewGuid():N}.accdb");
        string lib = Path.Combine(Path.GetTempPath(), $"ip-lib-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, start);
        try
        {
            using (var c = OpenOleDb(start))
            {
                Exec(c, "CREATE TABLE T ( A LONG, B LONG, C LONG )");
                Exec(c, "INSERT INTO T (A, B, C) VALUES (11, 22, 33)");
            }

            int tdefPage;
            using (var db = JetDatabase.Open(start, readOnly: true))
                tdefPage = db.Catalog.FindTable("T")!.DefinitionPage;

            CopyWithRetry(start, ace);
            using (var c = OpenOleDb(ace)) Exec(c, "ALTER TABLE T ALTER COLUMN B DOUBLE");

            CopyWithRetry(start, lib);
            using (var db = JetDatabase.Open(lib, readOnly: false))
                db.AlterColumnTypeInPlaceTdef("T", "B", new ColumnSpec("B", JetDataType.Double, 8, IsFixedLength: true));

            byte[] aceBytes = File.ReadAllBytes(ace);
            byte[] libBytes = File.ReadAllBytes(lib);

            int baseOff = tdefPage * PageSize;
            var diffs = new StringBuilder();
            for (int i = 0; i < PageSize; i++)
                if (aceBytes[baseOff + i] != libBytes[baseOff + i])
                    diffs.AppendLine($"  TDEF +0x{i:X3}: ace={aceBytes[baseOff + i]:X2} lib={libBytes[baseOff + i]:X2}");

            Assert.True(diffs.Length == 0, $"\nTDEF page {tdefPage} differs (ace vs libred in-place):\n{diffs}");
        }
        finally
        {
            foreach (var f in new[] { start, ace, lib }) { try { File.Delete(f); } catch (IOException) { } }
        }
    }

    // Does LibRed's row encoding already match ACE's for an all-fixed table? (Needed before the modify row
    // re-lay can be byte-faithful.) Create T(A,B,C LONG)+row via ACE and via LibRed; dump both data-page rows.
    [Fact]
    public void Diff_libred_vs_ace_all_fixed_row_bytes()
    {
        string aceF = Path.Combine(Path.GetTempPath(), $"rf-ace-{Guid.NewGuid():N}.accdb");
        string libF = Path.Combine(Path.GetTempPath(), $"rf-lib-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, aceF);
        File.Copy(TestDatabases.NorthwindAccdb, libF);
        try
        {
            using (var c = OpenOleDb(aceF))
            {
                Exec(c, "CREATE TABLE T ( A LONG, B LONG, C LONG )");
                Exec(c, "INSERT INTO T (A, B, C) VALUES (11, 22, 33)");
            }
            using (var db = JetDatabase.Open(libF, readOnly: false))
            {
                db.CreateTable("T", [
                    new ColumnSpec("A", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("B", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("C", JetDataType.Int32, 4, IsFixedLength: true)]);
                db.OpenTable("T").Insert([11, 22, 33]);
            }

            string RowHex(string path)
            {
                int tdefPage;
                using (var db = JetDatabase.Open(path, readOnly: true))
                    tdefPage = db.Catalog.FindTable("T")!.DefinitionPage;
                byte[] file = File.ReadAllBytes(path);
                for (int p = 0; p < file.Length / PageSize; p++)
                {
                    int b = p * PageSize;
                    if (file[b] != 0x01) continue;                                   // data page
                    if (BitConverter.ToInt32(file, b + 4) != tdefPage) continue;     // owned by T
                    if (BitConverter.ToUInt16(file, b + 0x0C) == 0) continue;        // has a row
                    int rowOff = BitConverter.ToUInt16(file, b + 0x0E) & 0x1FFF;
                    return Convert.ToHexString(file.AsSpan(b + rowOff, PageSize - rowOff));
                }
                return "(no row found)";
            }
            // LibRed's all-fixed row must be byte-identical to ACE's (var section omitted when numVarCols == 0).
            Assert.Equal(RowHex(aceF), RowHex(libF));
        }
        finally { foreach (var f in new[] { aceF, libF }) { try { File.Delete(f); } catch (IOException) { } } }
    }

    // The whole point: LibRed's full in-place column-modify must match ACE's file byte-for-byte on T's own
    // pages (TDEF + data). Covers fixed & variable columns, fixed & variable targets, and an indexed (PK) table.
    [Theory]
    // create ; insert ; ace-alter ; target col ; new type ; new length ; new-is-fixed
    [InlineData("CREATE TABLE T ( A LONG, B LONG, C LONG )", "INSERT INTO T (A,B,C) VALUES (11,22,33)", "ALTER TABLE T ALTER COLUMN B DOUBLE", "B", JetDataType.Double, 8, true)]
    [InlineData("CREATE TABLE T ( A LONG, B LONG, V TEXT(10) )", "INSERT INTO T (A,B,V) VALUES (11,22,'hi')", "ALTER TABLE T ALTER COLUMN B DOUBLE", "B", JetDataType.Double, 8, true)]
    [InlineData("CREATE TABLE T ( A LONG, V TEXT(10), C LONG )", "INSERT INTO T (A,V,C) VALUES (11,'hi',33)", "ALTER TABLE T ALTER COLUMN V TEXT(50)", "V", JetDataType.Text, 100, false)]
    [InlineData("CREATE TABLE T ( A LONG, B LONG, C LONG )", "INSERT INTO T (A,B,C) VALUES (11,22,33)", "ALTER TABLE T ALTER COLUMN B TEXT(20)", "B", JetDataType.Text, 40, false)]
    [InlineData("CREATE TABLE T ( K LONG PRIMARY KEY, B LONG )", "INSERT INTO T (K,B) VALUES (1,22)", "ALTER TABLE T ALTER COLUMN B DOUBLE", "B", JetDataType.Double, 8, true)]
    [InlineData("CREATE TABLE T ( A LONG, B LONG );CREATE INDEX ixB ON T (B)", "INSERT INTO T (A,B) VALUES (11,22)", "ALTER TABLE T ALTER COLUMN B DOUBLE", "B", JetDataType.Double, 8, true)]
    // edge cases:
    [InlineData("CREATE TABLE T ( A LONG, B LONG, C LONG )", "", "ALTER TABLE T ALTER COLUMN B DOUBLE", "B", JetDataType.Double, 8, true)] // empty table
    [InlineData("CREATE TABLE T ( A LONG, F BIT, B LONG )", "INSERT INTO T (A,F,B) VALUES (11,1,22)", "ALTER TABLE T ALTER COLUMN B DOUBLE", "B", JetDataType.Double, 8, true)] // boolean in table
    [InlineData("CREATE TABLE T ( A LONG, M MEMO, B LONG )", "INSERT INTO T (A,M,B) VALUES (11,'hi',22)", "ALTER TABLE T ALTER COLUMN B DOUBLE", "B", JetDataType.Double, 8, true)] // memo in table
    [InlineData("CREATE TABLE T ( A LONG, B LONG );CREATE INDEX ix ON T (A,B)", "INSERT INTO T (A,B) VALUES (11,22)", "ALTER TABLE T ALTER COLUMN B DOUBLE", "B", JetDataType.Double, 8, true)] // composite index on target
    [InlineData("CREATE TABLE T ( A LONG, B LONG );CREATE INDEX ix1 ON T (B);CREATE INDEX ix2 ON T (B)", "INSERT INTO T (A,B) VALUES (11,22)", "ALTER TABLE T ALTER COLUMN B DOUBLE", "B", JetDataType.Double, 8, true)] // two indexes on target
    public void Libred_in_place_modify_matches_ace_whole_file(string create, string insert, string aceAlter,
        string col, JetDataType newType, int newLen, bool newFixed)
    {
        string start = Path.Combine(Path.GetTempPath(), $"fm-start-{Guid.NewGuid():N}.accdb");
        string ace = Path.Combine(Path.GetTempPath(), $"fm-ace-{Guid.NewGuid():N}.accdb");
        string lib = Path.Combine(Path.GetTempPath(), $"fm-lib-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, start);
        try
        {
            using (var c = OpenOleDb(start)) { foreach (var s in create.Split(';')) Exec(c, s); if (insert.Length > 0) Exec(c, insert); }
            CopyWithRetry(start, ace);
            using (var c = OpenOleDb(ace)) Exec(c, aceAlter);

            CopyWithRetry(start, lib);
            using (var db = JetDatabase.Open(lib, readOnly: false))
                db.AlterColumnTypeInPlace("T", col, new ColumnSpec(col, newType, newLen, IsFixedLength: newFixed));

            byte[] a = File.ReadAllBytes(ace);
            byte[] l = File.ReadAllBytes(lib);
            int pages = Math.Max(a.Length, l.Length) / PageSize;

            // Every page must be byte-identical to ACE EXCEPT the two environmental, non-deterministic spots:
            // page 0 (the database modification counter) and MSysObjects' data page (owner 2) which carries
            // T's DateUpdate wall-clock timestamp. That covers TDEF, data, index B-tree, usage maps and free map.
            var diffs = new StringBuilder();
            for (int p = 1; p < pages; p++)
            {
                int b = p * PageSize;
                bool aIn = b + PageSize <= a.Length, lIn = b + PageSize <= l.Length;
                if (aIn && lIn && BitConverter.ToInt32(a, b + 4) == 2) continue; // MSysObjects data (timestamp)
                if (!aIn || !lIn) { diffs.AppendLine($"  page {p}: present in {(aIn ? "ace" : "lib")} only"); continue; }
                int shown = 0;
                for (int i = 0; i < PageSize && shown < 12; i++)
                    if (a[b + i] != l[b + i]) { diffs.AppendLine($"  page {p} (ace type 0x{a[b]:X2} owner {BitConverter.ToInt32(a, b + 4)}) +0x{i:X3}: ace={a[b + i]:X2} lib={l[b + i]:X2}"); shown++; }
            }
            Assert.True(diffs.Length == 0, $"\nLibRed in-place modify differs from ACE:\n{diffs}");
        }
        finally { foreach (var f in new[] { start, ace, lib }) { try { File.Delete(f); } catch (IOException) { } } }
    }

    // Diagnostic: dump ACE's row/TDEF delta for a given shape (e.g. an indexed target — the remaining case
    // that still needs byte-exact index B-tree rebuild). Kept as a tool; run explicitly when iterating.
    [Theory(Skip = "diagnostic harness — dumps ACE's in-place modify delta for a shape")]
    [InlineData("CREATE TABLE T ( B LONG PRIMARY KEY, A LONG )", "INSERT INTO T (B,A) VALUES (22,11)", "ALTER TABLE T ALTER COLUMN B DOUBLE")]
    [InlineData("CREATE TABLE T ( A LONG, B LONG );CREATE INDEX ixB ON T (B)", "INSERT INTO T (A,B) VALUES (11,22)", "ALTER TABLE T ALTER COLUMN B DOUBLE")]
    public void Diff_ace_modify_shape(string create, string insert, string alter)
    {
        string start = Path.Combine(Path.GetTempPath(), $"sh-{Guid.NewGuid():N}.accdb");
        string after = Path.Combine(Path.GetTempPath(), $"sh2-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, start);
        try
        {
            using (var c = OpenOleDb(start)) { foreach (var s in create.Split(';')) Exec(c, s); Exec(c, insert); }
            int tdefPage;
            string cols;
            using (var db = JetDatabase.Open(start, readOnly: true))
            {
                TableDef t = db.Catalog.FindTable("T")!;
                tdefPage = t.DefinitionPage;
                cols = string.Join(",", t.Columns.Select(x => $"{x.Name}(idx{x.Index},id{x.ColumnId},{x.Type},fix{x.FixedOffset},var{x.VariableIndex},len{x.Length})"));
            }
            CopyWithRetry(start, after);
            using (var c = OpenOleDb(after)) Exec(c, alter);

            byte[] b1 = File.ReadAllBytes(start), b2 = File.ReadAllBytes(after);
            var sb = new StringBuilder($"\n{alter}\nBEFORE cols: {cols}\n");
            for (int p = 0; p < Math.Min(b1.Length, b2.Length) / PageSize; p++)
            {
                int b = p * PageSize;
                bool isT = p == tdefPage || (b1[b] == 0x01 && BitConverter.ToInt32(b1, b + 4) == tdefPage);
                if (!isT) continue;
                sb.AppendLine($"--- page {p} (type 0x{b1[b]:X2}{(p == tdefPage ? " TDEF" : " data")}) ---");
                if (b1[b] == 0x01)
                {
                    sb.AppendLine($"  head before {Convert.ToHexString(b1.AsSpan(b, 16))}");
                    sb.AppendLine($"  head after  {Convert.ToHexString(b2.AsSpan(b, 16))}");
                    sb.AppendLine($"  tail before {Convert.ToHexString(b1.AsSpan(b + 0xFC0, 0x40))}");
                    sb.AppendLine($"  tail after  {Convert.ToHexString(b2.AsSpan(b + 0xFC0, 0x40))}");
                }
                else
                    for (int i = 0; i < PageSize; i++)
                        if (b1[b + i] != b2[b + i]) sb.AppendLine($"  TDEF +0x{i:X3}: {b1[b + i]:X2} -> {b2[b + i]:X2}");
            }
            Assert.Fail(sb.ToString());
        }
        finally { foreach (var f in new[] { start, after }) { try { File.Delete(f); } catch (IOException) { } } }
    }

    // Compares LibRed's in-place modify to ACE across the whole file (except env pages 0 + MSysObjects).
    private static void AssertModifyMatchesAce(string create, string insert, string aceAlter, Action<JetDatabase> libModify)
    {
        string start = Path.Combine(Path.GetTempPath(), $"e-{Guid.NewGuid():N}.accdb");
        string ace = Path.Combine(Path.GetTempPath(), $"e-a-{Guid.NewGuid():N}.accdb");
        string lib = Path.Combine(Path.GetTempPath(), $"e-l-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, start);
        try
        {
            using (var c = OpenOleDb(start)) { foreach (var s in create.Split(';')) Exec(c, s); if (insert.Length > 0) Exec(c, insert); }
            CopyWithRetry(start, ace);
            using (var c = OpenOleDb(ace)) foreach (var s in aceAlter.Split(';')) Exec(c, s);
            CopyWithRetry(start, lib);
            using (var db = JetDatabase.Open(lib, readOnly: false)) libModify(db);

            byte[] a = File.ReadAllBytes(ace), l = File.ReadAllBytes(lib);
            var diffs = new StringBuilder();
            for (int p = 1; p < Math.Max(a.Length, l.Length) / PageSize && diffs.Length < 1500; p++)
            {
                int b = p * PageSize;
                bool aIn = b + PageSize <= a.Length, lIn = b + PageSize <= l.Length;
                if (aIn && lIn && BitConverter.ToInt32(a, b + 4) == 2) continue;
                if (!aIn || !lIn) { diffs.AppendLine($"  page {p}: {(aIn ? "ace" : "lib")}-only"); continue; }
                for (int i = 0; i < PageSize; i++)
                    if (a[b + i] != l[b + i]) { diffs.AppendLine($"  page {p} (0x{a[b]:X2} owner {BitConverter.ToInt32(a, b + 4)}) +0x{i:X3}: ace={a[b + i]:X2} lib={l[b + i]:X2}"); break; }
            }
            Assert.True(diffs.Length == 0, $"\nModify differs from ACE:\n{diffs}");
        }
        finally { foreach (var f in new[] { start, ace, lib }) { try { File.Delete(f); } catch (IOException) { } } }
    }

    // Long-value (MEMO) target falls back to the correct (not byte-exact) rebuild; verify it works: ACE reads
    // the converted value, and modifying it back works. (Byte-exactness for long-value targets is out of scope.)
    [Fact]
    public void Libred_memo_target_modify_is_functional()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mt-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var conn = OpenOleDb(path))
            {
                Exec(conn, "CREATE TABLE T ( A LONG, B LONG )");
                Exec(conn, "INSERT INTO T (A,B) VALUES (11,22)");
            }
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.AlterColumnTypeInPlace("T", "B", new ColumnSpec("B", JetDataType.Memo, 0, IsFixedLength: false));

            using var c = OpenOleDb(path);
            using var q = c.CreateCommand();
            q.CommandText = "SELECT B FROM T WHERE A = 11";
            Assert.Equal("22", Convert.ToString(q.ExecuteScalar()));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Libred_multipage_tdef_modify_matches_ace()
    {
        // 60 columns forces a multi-page TDEF; modify a middle column.
        string cols = string.Join(", ", Enumerable.Range(0, 60).Select(i => $"c{i} LONG"));
        string vals = string.Join(",", Enumerable.Range(0, 60));
        AssertModifyMatchesAce(
            $"CREATE TABLE T ( {cols} )",
            $"INSERT INTO T ({string.Join(",", Enumerable.Range(0, 60).Select(i => $"c{i}"))}) VALUES ({vals})",
            "ALTER TABLE T ALTER COLUMN c30 DOUBLE",
            db => db.AlterColumnTypeInPlace("T", "c30", new ColumnSpec("c30", JetDataType.Double, 8, IsFixedLength: true)));
    }

    [Fact]
    public void Libred_decimal_target_modify_matches_ace()
    {
        AssertModifyMatchesAce(
            "CREATE TABLE T ( A LONG, B LONG )",
            "INSERT INTO T (A,B) VALUES (11,22)",
            "ALTER TABLE T ALTER COLUMN B DECIMAL(10,2)",
            db => db.AlterColumnTypeInPlace("T", "B", new ColumnSpec("B", JetDataType.FixedPoint, 17, IsFixedLength: true, Precision: 10, Scale: 2)));
    }

    // Robustness: 20 columns, a sequence of NON-sequential modifies (incl. re-modifying an already-modified
    // column, fixed↔variable, an indexed column) — accumulating burned ids and dead slots. Must stay
    // byte-identical to ACE through the whole sequence.
    [Fact]
    public void Libred_in_place_multi_modify_matches_ace()
    {
        string start = Path.Combine(Path.GetTempPath(), $"mm-{Guid.NewGuid():N}.accdb");
        string ace = Path.Combine(Path.GetTempPath(), $"mm-a-{Guid.NewGuid():N}.accdb");
        string lib = Path.Combine(Path.GetTempPath(), $"mm-l-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, start);
        try
        {
            string cols = string.Join(", ", Enumerable.Range(0, 20).Select(i => $"c{i} LONG"));
            string vals = string.Join(",", Enumerable.Range(0, 20));
            using (var c = OpenOleDb(start))
            {
                Exec(c, $"CREATE TABLE T ( {cols} )");
                Exec(c, "CREATE INDEX ixc7 ON T (c7)");
                Exec(c, $"INSERT INTO T ({string.Join(",", Enumerable.Range(0, 20).Select(i => $"c{i}"))}) VALUES ({vals})");
            }

            // (column, ace SQL type, libred type, libred length, libred isFixed)
            var steps = new (string Col, string AceType, JetDataType Type, int Len, bool Fixed)[]
            {
                ("c5",  "DOUBLE",   JetDataType.Double, 8,  true),
                ("c12", "TEXT(30)", JetDataType.Text,   60, false),
                ("c3",  "SINGLE",   JetDataType.Single, 4,  true),
                ("c17", "TEXT(10)", JetDataType.Text,   20, false),
                ("c5",  "TEXT(15)", JetDataType.Text,   30, false),   // re-modify an already-burned column
                ("c7",  "DOUBLE",   JetDataType.Double, 8,  true),    // the INDEXED column
                ("c12", "DOUBLE",   JetDataType.Double, 8,  true),    // re-modify c12 (var -> fixed)
            };

            CopyWithRetry(start, ace);
            CopyWithRetry(start, lib);
            using (var ca = OpenOleDb(ace))
                foreach (var s in steps) Exec(ca, $"ALTER TABLE T ALTER COLUMN {s.Col} {s.AceType}");
            using (var db = JetDatabase.Open(lib, readOnly: false))
                foreach (var s in steps) db.AlterColumnTypeInPlace("T", s.Col, new ColumnSpec(s.Col, s.Type, s.Len, IsFixedLength: s.Fixed));

            byte[] a = File.ReadAllBytes(ace), l = File.ReadAllBytes(lib);
            int pages = Math.Max(a.Length, l.Length) / PageSize;
            var diffs = new StringBuilder();
            for (int p = 1; p < pages && diffs.Length < 2000; p++)
            {
                int b = p * PageSize;
                bool aIn = b + PageSize <= a.Length, lIn = b + PageSize <= l.Length;
                if (aIn && lIn && BitConverter.ToInt32(a, b + 4) == 2) continue; // MSysObjects (timestamp)
                if (!aIn || !lIn) { diffs.AppendLine($"  page {p}: present in {(aIn ? "ace" : "lib")} only"); continue; }
                for (int i = 0; i < PageSize; i++)
                    if (a[b + i] != l[b + i]) { diffs.AppendLine($"  page {p} (type 0x{a[b]:X2} owner {BitConverter.ToInt32(a, b + 4)}) +0x{i:X3}: ace={a[b + i]:X2} lib={l[b + i]:X2}"); break; }
            }
            Assert.True(diffs.Length == 0, $"\nMulti-modify differs from ACE:\n{diffs}");

            // And ACE reads the fully-modified LibRed file back correctly.
            using var conn = OpenOleDb(lib);
            using var q = conn.CreateCommand();
            q.CommandText = "SELECT c3, c5, c7, c12, c17 FROM T";
            using var r = q.ExecuteReader();
            Assert.True(r.Read());
        }
        finally { foreach (var f in new[] { start, ace, lib }) { try { File.Delete(f); } catch (IOException) { } } }
    }

    [Fact(Skip = "diagnostic harness — dumps a usage-map page for ACE vs LibRed")]
    public void Dump_usagemap_ace_vs_libred()
    {
        string start = Path.Combine(Path.GetTempPath(), $"um-{Guid.NewGuid():N}.accdb");
        string ace = Path.Combine(Path.GetTempPath(), $"um-a-{Guid.NewGuid():N}.accdb");
        string lib = Path.Combine(Path.GetTempPath(), $"um-l-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, start);
        try
        {
            using (var c = OpenOleDb(start)) { Exec(c, "CREATE TABLE T ( A LONG, B LONG )"); Exec(c, "CREATE INDEX ixB ON T (B)"); Exec(c, "INSERT INTO T (A,B) VALUES (11,22)"); }
            CopyWithRetry(start, ace);
            using (var c = OpenOleDb(ace)) Exec(c, "ALTER TABLE T ALTER COLUMN B DOUBLE");
            CopyWithRetry(start, lib);
            using (var db = JetDatabase.Open(lib, readOnly: false)) db.AlterColumnTypeInPlace("T", "B", new ColumnSpec("B", JetDataType.Double, 8, IsFixedLength: true));

            const int P = 329 * PageSize;
            byte[] s = File.ReadAllBytes(start), a = File.ReadAllBytes(ace), l = File.ReadAllBytes(lib);
            var sb = new StringBuilder("\n");
            foreach (var (nm, f) in new[] { ("start", s), ("ace  ", a), ("lib  ", l) })
            {
                sb.AppendLine($"{nm} head[0x00..0x1F]: {Convert.ToHexString(f.AsSpan(P, 32))}");
                sb.AppendLine($"{nm} rows[0xF00..0xFFF]: {Convert.ToHexString(f.AsSpan(P + 0xF00, 0x100))}");
            }
            Assert.Fail(sb.ToString());
        }
        finally { foreach (var f in new[] { start, ace, lib }) { try { File.Delete(f); } catch (IOException) { } } }
    }

    [Fact(Skip = "diagnostic harness — dumps ACE's full-file delta for an indexed-column modify")]
    public void Diff_ace_indexed_full()
    {
        string start = Path.Combine(Path.GetTempPath(), $"ix-{Guid.NewGuid():N}.accdb");
        string after = Path.Combine(Path.GetTempPath(), $"ix2-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, start);
        try
        {
            using (var c = OpenOleDb(start))
            {
                Exec(c, "CREATE TABLE T ( A LONG, B LONG )");
                Exec(c, "CREATE INDEX ixB ON T (B)");
                Exec(c, "INSERT INTO T (A,B) VALUES (11,22)");
            }
            int tdefPage;
            using (var db = JetDatabase.Open(start, readOnly: true)) tdefPage = db.Catalog.FindTable("T")!.DefinitionPage;
            CopyWithRetry(start, after);
            using (var c = OpenOleDb(after)) Exec(c, "ALTER TABLE T ALTER COLUMN B DOUBLE");

            byte[] b1 = File.ReadAllBytes(start), b2 = File.ReadAllBytes(after);
            int rp1 = -1, rp2 = -1;
            using (var db = JetDatabase.Open(start, readOnly: true)) rp1 = db.Catalog.FindTable("T")!.Indexes[0].RootPage;
            using (var db = JetDatabase.Open(after, readOnly: true)) rp2 = db.Catalog.FindTable("T")!.Indexes[0].RootPage;
            var sb = new StringBuilder($"\nT TDEF page = {tdefPage}; index root before={rp1} after={rp2}; pages before={b1.Length / PageSize} after={b2.Length / PageSize}\n");
            int maxPages = Math.Max(b1.Length, b2.Length) / PageSize;
            for (int p = 0; p < maxPages; p++)
            {
                int b = p * PageSize;
                bool inB1 = b + PageSize <= b1.Length, inB2 = b + PageSize <= b2.Length;
                bool diff = !inB1 || !inB2;
                if (inB1 && inB2) for (int i = 0; i < PageSize && !diff; i++) diff = b1[b + i] != b2[b + i];
                if (!diff) continue;
                byte t2 = inB2 ? b2[b] : (byte)0; int own2 = inB2 ? BitConverter.ToInt32(b2, b + 4) : 0;
                sb.AppendLine($"== page {p}{(!inB1 ? " (NEW)" : "")}: type a=0x{t2:X2} owner a={own2}"
                    + (inB1 ? $"  (type b=0x{b1[b]:X2} owner b={BitConverter.ToInt32(b1, b + 4)})" : ""));
                if (!inB1) { sb.AppendLine($"   new page head: {Convert.ToHexString(b2.AsSpan(b, 32))}"); continue; }
                int shown = 0;
                for (int i = 0; i < PageSize && shown < 30; i++)
                    if (b1[b + i] != b2[b + i]) { sb.AppendLine($"   +0x{i:X3}: {b1[b + i]:X2} -> {b2[b + i]:X2}"); shown++; }
            }
            Assert.Fail(sb.ToString());
        }
        finally { foreach (var f in new[] { start, after }) { try { File.Delete(f); } catch (IOException) { } } }
    }

    // Diagnostic (not an assertion): dumps ACE's in-place column-modify byte delta — the spec target the
    // row re-lay must reproduce. Kept as a tool; run explicitly when iterating.
    [Fact(Skip = "diagnostic harness — dumps ACE's in-place modify delta")]
    public void Diff_ace_in_place_column_modify()
    {
        string before = Path.Combine(Path.GetTempPath(), $"bd-before-{Guid.NewGuid():N}.accdb");
        string after = Path.Combine(Path.GetTempPath(), $"bd-after-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, before);
        try
        {
            // ACE authors T with a row, all three columns non-null.
            using (var c = OpenOleDb(before))
            {
                Exec(c, "CREATE TABLE T ( A LONG, B LONG, C LONG )");
                Exec(c, "INSERT INTO T (A, B, C) VALUES (11, 22, 33)");
            }

            CopyWithRetry(before, after);

            // ACE changes ONE column's type in place on the 'after' copy.
            using (var c = OpenOleDb(after))
                Exec(c, "ALTER TABLE T ALTER COLUMN B DOUBLE");

            // Column layout, read via LibRed, to interpret the diff.
            string cols;
            int tdefPage;
            using (var db = JetDatabase.Open(before, readOnly: true))
            {
                TableDef t = db.Catalog.FindTable("T")!;
                tdefPage = t.DefinitionPage;
                cols = string.Join(",", t.Columns.Select(x => $"{x.Name}(idx{x.Index},id{x.ColumnId},{x.Type},fix{x.FixedOffset},len{x.Length})"));
            }

            byte[] b1 = File.ReadAllBytes(before);
            byte[] b2 = File.ReadAllBytes(after);

            var sb = new StringBuilder();
            sb.AppendLine($"\nBEFORE cols: {cols}");
            sb.AppendLine($"T TDEF page (before) = {tdefPage}");
            sb.AppendLine($"file size before={b1.Length} after={b2.Length}  ({b1.Length / PageSize} vs {b2.Length / PageSize} pages)");

            int pages = Math.Min(b1.Length, b2.Length) / PageSize;
            for (int p = 0; p < pages; p++)
            {
                int baseOff = p * PageSize;
                // find changed byte ranges within this page
                var ranges = new List<(int start, int end)>();
                int i = 0;
                while (i < PageSize)
                {
                    if (b1[baseOff + i] != b2[baseOff + i])
                    {
                        int s = i;
                        while (i < PageSize && b1[baseOff + i] != b2[baseOff + i]) i++;
                        ranges.Add((s, i));
                    }
                    else i++;
                }
                if (ranges.Count == 0) continue;

                byte typeBefore = b1[baseOff];
                sb.AppendLine($"\n--- page {p} (type 0x{typeBefore:X2}){(p == tdefPage ? "  <== T's TDEF" : "")} : {ranges.Count} changed range(s) ---");
                foreach (var (s, e) in ranges)
                {
                    string h1 = Convert.ToHexString(b1.AsSpan(baseOff + s, e - s));
                    string h2 = Convert.ToHexString(b2.AsSpan(baseOff + s, e - s));
                    sb.AppendLine($"  +0x{s:X3}..0x{e - 1:X3}: {h1}  ->  {h2}");
                }

                // For a changed DATA page, dump the header directory + the row-record tail so the row layout
                // (fixed region, var table, null bitmap) can be decoded before/after.
                if (typeBefore == 0x01)
                {
                    sb.AppendLine($"  page {p} head[0x00..0x1F] before: {Convert.ToHexString(b1.AsSpan(baseOff, 32))}");
                    sb.AppendLine($"  page {p} head[0x00..0x1F] after : {Convert.ToHexString(b2.AsSpan(baseOff, 32))}");
                    sb.AppendLine($"  page {p} tail[0xFD0..0xFFF] before: {Convert.ToHexString(b1.AsSpan(baseOff + 0xFD0, 0x30))}");
                    sb.AppendLine($"  page {p} tail[0xFD0..0xFFF] after : {Convert.ToHexString(b2.AsSpan(baseOff + 0xFD0, 0x30))}");
                }
            }

            Assert.Fail(sb.ToString());
        }
        finally
        {
            foreach (var f in new[] { before, after }) { try { File.Delete(f); } catch (IOException) { } }
        }
    }
}
