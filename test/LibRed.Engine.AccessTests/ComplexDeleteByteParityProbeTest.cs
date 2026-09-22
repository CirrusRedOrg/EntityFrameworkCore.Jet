using System.Buffers.Binary;
using System.Reflection;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

// PROBE: does LibRed's delete of a record owning complex values leave the same bytes ACE's does?
//
// ACE writes to any file it merely opens, so a straight A-vs-B diff cannot tell its delete from its
// housekeeping. Four copies separate them:
//
//   orig   untouched reference
//   ACE    DAO opens it, deletes the record, closes            = housekeeping + delete
//   noise  DAO opens it and closes, deleting nothing           = housekeeping alone
//   LibRed LibRed deletes the same record                      = delete alone
//
// The pages `noise` changes are ACE's own; the interesting comparison is what ACE and LibRed each did to the
// pages outside that set.
[Collection(AceCollection.Name)]
public class ComplexDeleteByteParityProbeTest(ITestOutputHelper output)
{
    private const string Source = @"D:\exampleaccdb\complex1.accdb";

    [Fact]
    public void Probe_byte_parity_of_a_complex_delete()
    {
        if (!File.Exists(Source)) { output.WriteLine($"PROBE skipped: {Source} not present."); return; }

        string where;
        using (var db = JetDatabase.Open(TemporaryDatabase.CopyPath(Source, "cx-parity-pick-")))
        {
            TableDef table = db.Catalog.FindTable("Table1")!;
            ComplexColumn any = db.Catalog.ComplexColumns.First(c => c.OwnerTable.Name == "Table1");
            int recordId = db.OpenTable(any.FlatTable.Name).Rows()
                .Select(r => Convert.ToInt32(r[any.OwnerLink.Index])).Distinct().First();
            int key = db.OpenTable("Table1").Rows()
                .Where(r => Convert.ToInt32(r[table.FindColumn(any.ColumnName)!.Index]) == recordId)
                .Select(r => Convert.ToInt32(r[table.Columns[0].Index])).First();
            where = $"[{table.Columns[0].Name}] = {key}";
            output.WriteLine($"PROBE complex case: Table1 WHERE {where} (complex id {recordId})");
        }
        Compare(Source, "Table1", where, "complex");
    }

    // The control: the same comparison on a table with NO complex column. If the differences look the same,
    // they belong to LibRed's ordinary delete rather than to anything complex-specific.
    [Fact]
    public void Probe_byte_parity_of_an_ordinary_delete()
    {
        output.WriteLine("PROBE control case: [Order Details] — no complex column");
        Compare(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"),
            "Order Details", "OrderID = 10248 AND ProductID = 11", "control");
    }

    // Does ACE *increment* the per-index total-entry count on insert? It decrements on delete (above), and
    // RowInserter's comment says Access does not maintain the total live. If it only ever decremented, the
    // count would drift down — so the insert side decides whether LibRed should touch it at all.
    [Fact]
    public void Probe_entry_counts_on_insert()
    {
        object? engine = CreateDbEngine(out string progId);
        if (engine is null) { output.WriteLine("PROBE skipped: DAO unavailable."); return; }
        output.WriteLine($"PROBE DAO engine: {progId}");

        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string orig = TemporaryDatabase.CopyPath(northwind, "ins-parity-orig-");
        string aceCopy = TemporaryDatabase.CopyPath(northwind, "ins-parity-ace-");
        string libredCopy = TemporaryDatabase.CopyPath(northwind, "ins-parity-libred-");
        try
        {
            const string Insert = "INSERT INTO [Order Details] (OrderID, ProductID, UnitPrice, Quantity, Discount) "
                                  + "VALUES (10248, 1, 1, 1, 0)";
            object db = Invoke(engine, "OpenDatabase", aceCopy)!;
            try { Invoke(db, "Execute", Insert); }
            finally { Invoke(db, "Close"); }

            using (var d = JetDatabase.Open(libredCopy, readOnly: false))
                new QueryEngine(d).ExecuteNonQuery(Insert);

            int page;
            using (var d = JetDatabase.Open(orig)) page = d.Catalog.FindTable("Order Details")!.DefinitionPage;

            byte[] o = File.ReadAllBytes(orig), a = File.ReadAllBytes(aceCopy), l = File.ReadAllBytes(libredCopy);
            const int PageSize = 4096;
            var so = o.AsSpan(page * PageSize, PageSize);
            var sa = a.AsSpan(page * PageSize, PageSize);
            var sl = l.AsSpan(page * PageSize, PageSize);

            output.WriteLine($"PROBE rowCount(0x10) orig={I(so, 0x10)} ace={I(sa, 0x10)} libred={I(sl, 0x10)}");
            int realIndexes = I(so, 0x33);
            for (int i = 0; i < realIndexes; i++)
            {
                int at = 0x3F + i * 12;
                output.WriteLine($"PROBE   index {i} @0x{at:X2}: total orig={I(so, at)} ace={I(sa, at)} libred={I(sl, at)}"
                    + $" | unique orig={I(so, at + 4)} ace={I(sa, at + 4)} libred={I(sl, at + 4)}");
            }
        }
        finally
        {
            foreach (string f in new[] { orig, aceCopy, libredCopy }) TemporaryDatabase.Delete(f);
        }
    }

    // Before any parity question: LibRed reuses page 329 (a MSysNavPaneObjectIDs data page) as a new index
    // leaf where ACE extends the file instead. If that page is NOT actually free, this is data loss, not a
    // difference of allocation policy. Compare every table's rows before and after, on both engines.
    [Fact]
    public void Probe_that_a_reused_page_was_really_free()
    {
        object? engine = CreateDbEngine(out string progId);
        if (engine is null) { output.WriteLine("PROBE skipped: DAO unavailable."); return; }
        output.WriteLine($"PROBE [reuse] DAO engine: {progId}");

        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string orig = TemporaryDatabase.CopyPath(northwind, "reuse-orig-");
        string aceCopy = TemporaryDatabase.CopyPath(northwind, "reuse-ace-");
        string libredCopy = TemporaryDatabase.CopyPath(northwind, "reuse-libred-");
        try
        {
            const string Insert = "INSERT INTO [Order Details] (OrderID, ProductID, UnitPrice, Quantity, Discount) "
                                  + "VALUES (10248, 1, 1, 1, 0)";
            object db = Invoke(engine, "OpenDatabase", aceCopy)!;
            try { Invoke(db, "Execute", Insert); }
            finally { Invoke(db, "Close"); }

            using (var d = JetDatabase.Open(libredCopy, readOnly: false))
                new QueryEngine(d).ExecuteNonQuery(Insert);

            Dictionary<string, int> before = RowCounts(orig), afterAce = RowCounts(aceCopy),
                                    afterLibred = RowCounts(libredCopy);

            int mismatches = 0;
            foreach ((string name, int n) in before.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                afterAce.TryGetValue(name, out int ace);
                afterLibred.TryGetValue(name, out int lib);
                if (n == ace && n == lib) continue;
                mismatches++;
                output.WriteLine($"PROBE   [{name}] rows: orig={n} ace={ace} libred={lib}");
            }
            output.WriteLine(mismatches == 0
                ? $"PROBE   all {before.Count} tables unchanged in row count on both engines"
                : $"PROBE   {mismatches} table(s) differ");

            // And does ACE itself accept the file LibRed wrote?
            object check = Invoke(engine, "OpenDatabase", libredCopy)!;
            try
            {
                object rs = Invoke(check, "OpenRecordset", "SELECT COUNT(*) FROM [MSysNavPaneObjectIDs]")!;
                try { output.WriteLine($"PROBE   ACE reads libred's MSysNavPaneObjectIDs: {Get(Fields(rs, 0), "Value")}"); }
                finally { Invoke(rs, "Close"); }
            }
            finally { Invoke(check, "Close"); }
        }
        finally
        {
            foreach (string f in new[] { orig, aceCopy, libredCopy }) TemporaryDatabase.Delete(f);
        }
    }

    private static Dictionary<string, int> RowCounts(string path)
    {
        using var db = JetDatabase.Open(path);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TableDef t in db.Catalog.Tables)
        {
            try { counts[t.Name] = db.OpenTable(t.Name).Rows().Count(); }
            catch (Exception ex) { counts[t.Name] = -1; Console.WriteLine($"{t.Name}: {ex.Message}"); }
        }
        return counts;
    }

    private static object Fields(object target, object name) =>
        target.GetType().InvokeMember("Fields", BindingFlags.GetProperty, null, target, [name])!;

    private static object? Get(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null);

    private void Compare(string source, string table, string where, string label)
    {
        object? engine = CreateDbEngine(out string progId);
        if (engine is null) { output.WriteLine("PROBE skipped: DAO unavailable."); return; }
        output.WriteLine($"PROBE [{label}] DAO engine: {progId}");

        string orig = TemporaryDatabase.CopyPath(source, $"{label}-parity-orig-");
        string aceCopy = TemporaryDatabase.CopyPath(source, $"{label}-parity-ace-");
        string noiseCopy = TemporaryDatabase.CopyPath(source, $"{label}-parity-noise-");
        string libredCopy = TemporaryDatabase.CopyPath(source, $"{label}-parity-libred-");
        try
        {
            DaoDelete(engine, aceCopy, table, where);
            DaoOpenClose(engine, noiseCopy, table);
            using (var db = JetDatabase.Open(libredCopy, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery($"DELETE FROM [{table}] WHERE {where}");

            Report(label, File.ReadAllBytes(orig), File.ReadAllBytes(aceCopy),
                File.ReadAllBytes(noiseCopy), File.ReadAllBytes(libredCopy), Owners(orig));
        }
        finally
        {
            foreach (string f in new[] { orig, aceCopy, noiseCopy, libredCopy }) TemporaryDatabase.Delete(f);
        }
    }

    // The same four-copy comparison for an INSERT. Only the TDEF entry counters had been compared before,
    // which says nothing about the data or index pages the insert writes.
    [Fact]
    public void Probe_byte_parity_of_an_ordinary_insert()
    {
        object? engine = CreateDbEngine(out string progId);
        if (engine is null) { output.WriteLine("PROBE skipped: DAO unavailable."); return; }
        output.WriteLine($"PROBE [insert] DAO engine: {progId}");

        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string orig = TemporaryDatabase.CopyPath(northwind, "insp-orig-");
        string aceCopy = TemporaryDatabase.CopyPath(northwind, "insp-ace-");
        string noiseCopy = TemporaryDatabase.CopyPath(northwind, "insp-noise-");
        string libredCopy = TemporaryDatabase.CopyPath(northwind, "insp-libred-");
        try
        {
            const string Insert = "INSERT INTO [Order Details] (OrderID, ProductID, UnitPrice, Quantity, Discount) "
                                  + "VALUES (10248, 1, 1, 1, 0)";
            object db = Invoke(engine, "OpenDatabase", aceCopy)!;
            try { Invoke(db, "Execute", Insert); }
            finally { Invoke(db, "Close"); }

            DaoOpenClose(engine, noiseCopy, "Order Details");

            using (var d = JetDatabase.Open(libredCopy, readOnly: false))
                new QueryEngine(d).ExecuteNonQuery(Insert);

            Report("insert", File.ReadAllBytes(orig), File.ReadAllBytes(aceCopy),
                File.ReadAllBytes(noiseCopy), File.ReadAllBytes(libredCopy), Owners(orig));
        }
        finally
        {
            foreach (string f in new[] { orig, aceCopy, noiseCopy, libredCopy }) TemporaryDatabase.Delete(f);
        }
    }

    /// <summary>Maps each table's TDEF page to its name, so a data/index page can be named by its owner
    /// (bytes <c>0x04</c> of a `0x01`/`0x03`/`0x04` page point at the owning TDEF).</summary>
    private static Dictionary<int, string> Owners(string path)
    {
        using var db = JetDatabase.Open(path);
        var map = new Dictionary<int, string>();
        foreach (TableDef t in db.Catalog.Tables) map[t.DefinitionPage] = t.Name;
        return map;
    }

    private static string Describe(byte[] file, int page, Dictionary<int, string> owners)
    {
        const int PageSize = 4096;
        if ((page + 1) * PageSize > file.Length) return "(absent)";
        var span = file.AsSpan(page * PageSize, PageSize);
        byte type = span[0];
        string kind = type switch
        {
            0x00 => "db-header", 0x01 => "data", 0x02 => "TDEF", 0x03 => "index-node",
            0x04 => "index-leaf", 0x05 => "usage-map", 0x08 => "long-value", _ => $"?0x{type:X2}",
        };
        if (type is 0x01 or 0x03 or 0x04)
        {
            int tdef = BinaryPrimitives.ReadInt32LittleEndian(span[0x04..]);
            string name = owners.TryGetValue(tdef, out string? t) ? t : $"tdef@{tdef}";
            return $"{kind} of [{name}]";
        }
        if (type == 0x02 && owners.TryGetValue(page, out string? own)) return $"{kind} [{own}]";
        if (span.TrimStart((byte)0).IsEmpty) return "all-zero";
        return kind;
    }

    private void Report(string label, byte[] o, byte[] a, byte[] n, byte[] l, Dictionary<int, string> owners)
    {
        {
            output.WriteLine($"PROBE [{label}] sizes: orig={o.Length} ace={a.Length} noise={n.Length} libred={l.Length}"
                + $"  (pages: orig={o.Length / 4096} ace={a.Length / 4096} libred={l.Length / 4096})");

            const int PageSize = 4096;
            HashSet<int> acePages = ChangedPages(o, a, PageSize), noisePages = ChangedPages(o, n, PageSize),
                         libredPages = ChangedPages(o, l, PageSize);

            output.WriteLine($"PROBE pages changed: ace={acePages.Count} noise={noisePages.Count} libred={libredPages.Count}");
            output.WriteLine($"PROBE   ace-only (minus housekeeping): [{Join(acePages.Except(noisePages))}]");
            output.WriteLine($"PROBE   libred:                                [{Join(libredPages)}]");
            output.WriteLine($"PROBE   housekeeping (noise):                  [{Join(noisePages)}]");

            var aceReal = acePages.Except(noisePages).ToHashSet();
            output.WriteLine($"PROBE   libred touched but ACE did not: [{Join(libredPages.Except(aceReal))}]");
            output.WriteLine($"PROBE   ACE touched but libred did not: [{Join(aceReal.Except(libredPages))}]");

            // What every page in play actually is, on each side — a page number alone says nothing.
            output.WriteLine("PROBE   page inventory (orig -> ace / libred):");
            foreach (int page in aceReal.Union(libredPages).Order())
                output.WriteLine($"PROBE     {page,5}: was {Describe(o, page, owners),-28}"
                    + $" ace={Describe(a, page, owners),-28} libred={Describe(l, page, owners)}");

            // For the pages both changed, are the resulting bytes identical — and where not, what exactly?
            foreach (int page in aceReal.Intersect(libredPages).Order())
            {
                var origSpan = o.AsSpan(page * PageSize, PageSize);
                var aceSpan = a.AsSpan(page * PageSize, PageSize);
                var libSpan = l.AsSpan(page * PageSize, PageSize);
                if (aceSpan.SequenceEqual(libSpan)) { output.WriteLine($"PROBE   page {page}: IDENTICAL"); continue; }

                // Branch on what the page BECAME, not what it was: a recycled page changes type, and the
                // interesting structure is the new one.
                byte type = aceSpan[0];
                output.WriteLine($"PROBE   page {page}: was=0x{origSpan[0]:X2} now=0x{type:X2}"
                    + $"  ace-vs-libred differs in {Ranges(aceSpan, libSpan).Split(' ').Length} run(s)");

                if (type == 0x02) // TDEF — the per-index entry counters
                {
                    int realIndexes = BinaryPrimitives.ReadInt32LittleEndian(origSpan[0x33..]);
                    output.WriteLine($"PROBE     rowCount(0x10) orig={I(origSpan, 0x10)} ace={I(aceSpan, 0x10)} libred={I(libSpan, 0x10)}");
                    for (int i = 0; i < realIndexes && 0x3F + i * 12 + 8 <= PageSize; i++)
                    {
                        int at = 0x3F + i * 12;
                        if (I(origSpan, at) == I(aceSpan, at) && I(origSpan, at + 4) == I(aceSpan, at + 4)
                            && I(origSpan, at) == I(libSpan, at) && I(origSpan, at + 4) == I(libSpan, at + 4)) continue;
                        output.WriteLine($"PROBE     index {i} @0x{at:X2}: total orig={I(origSpan, at)} ace={I(aceSpan, at)} libred={I(libSpan, at)}"
                            + $" | unique orig={I(origSpan, at + 4)} ace={I(aceSpan, at + 4)} libred={I(libSpan, at + 4)}");
                    }
                }
                else if (type is 0x03 or 0x04) // index page — free space, and where each side starts to diverge
                {
                    output.WriteLine($"PROBE     freeSpace(0x02) orig={U(origSpan, 0x02)} ace={U(aceSpan, 0x02)} libred={U(libSpan, 0x02)}");
                    output.WriteLine($"PROBE     first diff vs orig:  ace @0x{FirstDiff(origSpan, aceSpan):X3}"
                        + $"  libred @0x{FirstDiff(origSpan, libSpan):X3}   ace-vs-libred @0x{FirstDiff(aceSpan, libSpan):X3}");
                    output.WriteLine($"PROBE     last  diff vs orig:  ace @0x{LastDiff(origSpan, aceSpan):X3}"
                        + $"  libred @0x{LastDiff(origSpan, libSpan):X3}   ace-vs-libred @0x{LastDiff(aceSpan, libSpan):X3}");
                    int at = Math.Max(0, FirstDiff(aceSpan, libSpan) - 8);
                    output.WriteLine($"PROBE     @0x{at:X3} orig   {Hex(origSpan, at, 40)}");
                    output.WriteLine($"PROBE     @0x{at:X3} ace    {Hex(aceSpan, at, 40)}");
                    output.WriteLine($"PROBE     @0x{at:X3} libred {Hex(libSpan, at, 40)}");

                    // The decisive question for tail handling: past each side's own last live byte, does the
                    // page still hold what was there before, or has it been cleared?
                    int aceEnd = PageSize - U(aceSpan, 0x02), libEnd = PageSize - U(libSpan, 0x02);
                    output.WriteLine($"PROBE     live ends: ace @0x{aceEnd:X3} libred @0x{libEnd:X3}"
                        + $"   tail preserved from orig? ace={origSpan[aceEnd..].SequenceEqual(aceSpan[aceEnd..])}"
                        + $" libred={origSpan[libEnd..].SequenceEqual(libSpan[libEnd..])}"
                        + $"   tail all-zero? ace={aceSpan[aceEnd..].TrimStart((byte)0).IsEmpty}"
                        + $" libred={libSpan[libEnd..].TrimStart((byte)0).IsEmpty}");
                }
            }
        }
    }

    private static string Join(IEnumerable<int> pages) => string.Join(", ", pages.Order());

    private static int FirstDiff(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return i;
        return -1;
    }

    private static int LastDiff(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        for (int i = left.Length - 1; i >= 0; i--) if (left[i] != right[i]) return i;
        return -1;
    }

    private static string Hex(ReadOnlySpan<byte> page, int at, int count) =>
        Convert.ToHexString(page.Slice(Math.Max(0, at), Math.Min(count, page.Length - Math.Max(0, at))));

    private static int I(ReadOnlySpan<byte> page, int at) => BinaryPrimitives.ReadInt32LittleEndian(page[at..]);
    private static ushort U(ReadOnlySpan<byte> page, int at) => BinaryPrimitives.ReadUInt16LittleEndian(page[at..]);

    /// <summary>The byte ranges where two pages differ, as <c>0xSTART-0xEND(length)</c>.</summary>
    private static string Ranges(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var spans = new List<string>();
        int start = -1;
        for (int i = 0; i <= left.Length; i++)
        {
            bool differs = i < left.Length && left[i] != right[i];
            if (differs && start < 0) start = i;
            else if (!differs && start >= 0) { spans.Add($"0x{start:X3}-0x{i - 1:X3}({i - start})"); start = -1; }
        }
        return spans.Count == 0 ? "(none)" : string.Join(" ", spans);
    }

    private static HashSet<int> ChangedPages(byte[] left, byte[] right, int pageSize)
    {
        var changed = new HashSet<int>();
        int pages = Math.Min(left.Length, right.Length) / pageSize;
        for (int p = 0; p < pages; p++)
            if (!left.AsSpan(p * pageSize, pageSize).SequenceEqual(right.AsSpan(p * pageSize, pageSize)))
                changed.Add(p);
        for (int p = pages; p < Math.Max(left.Length, right.Length) / pageSize; p++) changed.Add(p);
        return changed;
    }

    private static void DaoDelete(object engine, string path, string table, string where)
    {
        object db = Invoke(engine, "OpenDatabase", path)!;
        try
        {
            object rs = Invoke(db, "OpenRecordset", $"SELECT * FROM [{table}] WHERE {where}")!;
            try { Invoke(rs, "Delete"); }
            finally { Invoke(rs, "Close"); }
        }
        finally { Invoke(db, "Close"); }
    }

    private static void DaoOpenClose(object engine, string path, string table)
    {
        object db = Invoke(engine, "OpenDatabase", path)!;
        try
        {
            object rs = Invoke(db, "OpenRecordset", $"SELECT * FROM [{table}]")!;
            Invoke(rs, "Close");
        }
        finally { Invoke(db, "Close"); }
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);

    private static object? CreateDbEngine(out string progId)
    {
        AceTestDatabase.ReleaseAbandonedComObjects();
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            progId = $"DAO.DBEngine.{n}";
            Type? type = Type.GetTypeFromProgID(progId);
            if (type is null) continue;
            try { return Activator.CreateInstance(type); }
            catch (Exception) { /* registered but not instantiable in this bitness */ }
        }
        progId = "(none)";
        return null;
    }
}
