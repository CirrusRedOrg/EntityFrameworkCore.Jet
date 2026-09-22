using System.Reflection;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: is IdxFKPrimaryScalar enforced? A multi-value column's flat table carries a UNIQUE index over
// [ownerLink + Value], which would mean one record cannot hold the same value twice. The corpus cannot
// answer it — the duplicate values it contains sit on *different* records, which the composite key allows.
//
// Only DAO can reach a complex column: ACE's OLE DB and ODBC surfaces do not expose the child recordset a
// multi-value field resolves to, so there is no way to add a value over them.
[Collection(AceCollection.Name)]
public class ComplexDuplicateValueProbeTest(ITestOutputHelper output)
{
    private const string Source = @"D:\exampleaccdb\PasesDeSalidas.accdb";
    private const string Table = "PasesDeSalida";
    private const string Column = "Acompaña";

    [Fact]
    public void Probe_whether_a_record_may_hold_the_same_value_twice()
    {
        if (!File.Exists(Source)) { output.WriteLine($"PROBE skipped: {Source} not present."); return; }

        object? engine = CreateDbEngine(out string progId);
        if (engine is null) { output.WriteLine("PROBE skipped: DAO unavailable in this process."); return; }
        output.WriteLine($"PROBE DAO engine: {progId}");

        // ACE writes to anything it opens, so never the source.
        string path = TemporaryDatabase.CopyPath(Source, "complex-dup-probe-");
        try
        {
            object db = Invoke(engine, "OpenDatabase", path)!;
            try
            {
                object rs = Invoke(db, "OpenRecordset", Table)!;
                try
                {
                    // Walk to the first record whose multi-value set already holds something.
                    int scanned = 0;
                    while (!Convert.ToBoolean(Get(rs, "EOF")) && scanned < 200)
                    {
                        scanned++;
                        object child = Get(Fields(rs, Column), "Value")!;
                        var existing = new List<object?>();
                        while (!Convert.ToBoolean(Get(child, "EOF")))
                        {
                            existing.Add(Get(Fields(child, "Value"), "Value"));
                            Invoke(child, "MoveNext");
                        }

                        if (existing.Count == 0) { Invoke(rs, "MoveNext"); continue; }

                        output.WriteLine($"PROBE record #{scanned} holds [{string.Join(", ", existing)}]");
                        object? duplicate = existing[0];

                        output.WriteLine($"PROBE adding a DUPLICATE of {duplicate} …");
                        output.WriteLine("PROBE   " + TryAdd(rs, child, duplicate));

                        object fresh = Convert.ToInt32(duplicate) + 9999;
                        output.WriteLine($"PROBE adding a FRESH value {fresh} (control) …");
                        output.WriteLine("PROBE   " + TryAdd(rs, child, fresh));
                        break;
                    }
                }
                finally { Invoke(rs, "Close"); }
            }
            finally { Invoke(db, "Close"); }

            // What actually landed, read back through LibRed.
            using var lib = JetDatabase.Open(path);
            foreach (TableDef t in lib.Catalog.Tables.Where(t => t.Name.StartsWith("f_", StringComparison.Ordinal)))
            {
                ColumnDef? link = t.Indexes.FirstOrDefault(i => !i.IsUnique && !i.IsPrimaryKey && i.Columns.Count == 1)?.Columns[0].Column;
                ColumnDef? value = t.FindColumn("Value");
                if (link is null || value is null) continue;
                foreach (object?[] r in lib.OpenTable(t.Name).Rows())
                    output.WriteLine($"PROBE flat {t.Name}: ownerLink={r[link.Index]} Value={r[value.Index]}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Adds <paramref name="value"/> to the record's child recordset, reporting what DAO said.</summary>
    private static string TryAdd(object rs, object child, object? value)
    {
        try
        {
            Invoke(rs, "Edit");
            Invoke(child, "AddNew");
            Set(Fields(child, "Value"), "Value", value);
            Invoke(child, "Update");
            Invoke(rs, "Update");
            return "ACCEPTED";
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            return $"REJECTED {inner.GetType().Name}: {inner.Message.ReplaceLineEndings(" ")}";
        }
        catch (Exception ex) { return $"REJECTED {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}"; }
    }

    private static object Fields(object target, string name) =>
        target.GetType().InvokeMember("Fields", BindingFlags.GetProperty, null, target, [name])!;

    private static object? Get(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null);

    private static void Set(object target, string member, object? value) =>
        target.GetType().InvokeMember(member, BindingFlags.SetProperty, null, target, [value]);

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
