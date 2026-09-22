using System.Buffers.Binary;
using System.Reflection;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: what does ACE do to a complex column's stored values when the record that owns them is deleted?
//
// LibRed refuses to delete a row from a table with a complex column today (the index key encoding throws), so
// nothing depends on the answer yet — but it decides what the delete path must do before that refusal can be
// lifted. Three outcomes are possible and only one is guessable: the flat rows cascade away, they are left
// orphaned, or the id is recycled somehow. A writer that guesses wrong leaves a file ACE disagrees with.
//
// Only DAO can delete such a record: ACE's OLE DB and ODBC surfaces do not expose the complex column.
[Collection(AceCollection.Name)]
public class ComplexDeleteCascadeProbeTest(ITestOutputHelper output)
{
    private const string Source = @"D:\exampleaccdb\complex1.accdb";
    private const string Table = "Table1";

    [Fact]
    public void Probe_what_deleting_the_owning_record_does_to_its_values()
    {
        if (!File.Exists(Source)) { output.WriteLine($"PROBE skipped: {Source} not present."); return; }

        object? engine = CreateDbEngine(out string progId);
        if (engine is null) { output.WriteLine("PROBE skipped: DAO unavailable in this process."); return; }
        output.WriteLine($"PROBE DAO engine: {progId}");

        string path = TemporaryDatabase.CopyPath(Source, "complex-delete-probe-");
        try
        {
            int? deleted = Dump(path, "BEFORE");
            if (deleted is not { } target) { output.WriteLine("PROBE: no record owns any value; nothing to delete."); return; }

            object db = Invoke(engine, "OpenDatabase", path)!;
            try
            {
                object rs = Invoke(db, "OpenRecordset", $"SELECT * FROM [{Table}] WHERE ID = {target}")!;
                try
                {
                    if (Convert.ToBoolean(Get(rs, "EOF"))) { output.WriteLine($"PROBE: ID={target} not found."); return; }
                    output.WriteLine($"PROBE deleting {Table} record ID={target} …");
                    Invoke(rs, "Delete");
                    output.WriteLine("PROBE   deleted");
                }
                finally { Invoke(rs, "Close"); }
            }
            finally { Invoke(db, "Close"); }

            Dump(path, "AFTER");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Prints every complex column's ids, counters and stored values. Returns the ID of a record
    /// that actually owns at least one value, so the delete lands somewhere interesting.</summary>
    private int? Dump(string path, string label)
    {
        using var db = JetDatabase.Open(path);
        output.WriteLine($"PROBE --- {label} ---");

        TableDef owner = db.Catalog.FindTable(Table)!;
        ColumnDef idColumn = owner.Columns[0];
        int? withValues = null;

        foreach (ComplexColumn c in db.Catalog.ComplexColumns.Where(c => c.OwnerTable.Name == Table))
        {
            ReadOnlySpan<byte> ownerPage = db.OpenTable(Table).Channel.ReadPageShared(owner.DefinitionPage).Span;
            ReadOnlySpan<byte> flatPage = db.OpenTable(c.FlatTable.Name).Channel.ReadPageShared(c.FlatTable.DefinitionPage).Span;
            output.WriteLine($"PROBE   {Table}.{c.ColumnName}: owner 0x1C={BinaryPrimitives.ReadInt32LittleEndian(ownerPage[0x1C..])}"
                + $"  flat 0x14={BinaryPrimitives.ReadInt32LittleEndian(flatPage[0x14..])}");

            ColumnDef inRow = owner.FindColumn(c.ColumnName)!;
            var ids = new List<string>();
            foreach (object?[] r in db.OpenTable(Table).Rows())
                ids.Add($"ID={r[idColumn.Index]}:cid={r[inRow.Index] ?? "NULL"}");
            output.WriteLine($"PROBE     records: {string.Join("  ", ids)}");

            int fileName = c.ValueColumns.ToList().FindIndex(v => v.Name == "FileName");
            foreach (object?[] flat in db.OpenTable(c.FlatTable.Name).Rows())
            {
                int link = Convert.ToInt32(flat[c.OwnerLink.Index]);
                string what = fileName >= 0 ? $"{c.ValueColumns[fileName].Name}={flat[c.ValueColumns[fileName].Index]}" : "";
                output.WriteLine($"PROBE     value: ownerLink={link} valueId={flat[c.ValueId.Index]} {what}");

                // Prefer a record that still exists and owns something.
                foreach (object?[] r in db.OpenTable(Table).Rows())
                    if (r[inRow.Index] is int cid && cid == link)
                        withValues ??= Convert.ToInt32(r[idColumn.Index]);
            }
        }
        return withValues;
    }

    private static object? Get(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null);

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
