using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: the exact shape of the complex-type system tables in a database the real engine created, so
// DatabaseCreator can reproduce them rather than approximate them — page numbers, column layout, indexes,
// and the MSysObjects catalog rows.
//
// Complex columns (multi-value / attachment) arrived with Access 2007 / ACE 12, so these tables exist only
// from version byte 0x02 up; a Jet 4 (.mdb) database has none of them.
public class ComplexSystemTableLayoutProbeTest(ITestOutputHelper output)
{
    [Fact]
    public void Probe_complex_system_table_layout()
    {
        object? engine = null;
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { engine = Activator.CreateInstance(type); break; } catch (Exception) { }
        }
        if (engine is null) { output.WriteLine("DAO unavailable."); return; }

        string path = TemporaryDatabase.CreatePath("complex-layout-");
        File.Delete(path);
        try
        {
            object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            Invoke(database, "Close");

            using var db = JetDatabase.Open(path);
            output.WriteLine($"file: {new FileInfo(path).Length / 4096} pages of 4096");
            output.WriteLine("");

            output.WriteLine("page  table                          rows  indexes");
            foreach (TableDef t in db.Catalog.Tables.OrderBy(t => t.DefinitionPage))
                output.WriteLine($"{t.DefinitionPage,4}  {t.Name,-30} {db.OpenTable(t.Name).Rows().Count(),4}  " +
                                 $"{string.Join(", ", t.Indexes.Select(i => $"{i.Name}({string.Join("+", i.Columns.Select(c => c.Column.Name))}{(i.IsUnique ? ",unique" : "")}{(i.IsPrimaryKey ? ",pk" : "")})"))}");

            output.WriteLine("");
            foreach (TableDef t in db.Catalog.Tables.Where(t => t.Name.StartsWith("MSysComplex", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(t => t.DefinitionPage))
            {
                output.WriteLine($"== {t.Name} (page {t.DefinitionPage})");
                foreach (ColumnDef c in t.Columns.OrderBy(c => c.Index))
                    output.WriteLine($"     {c.Index}  {c.Name,-22} {c.Type,-12} len={c.Length,-4} fixed={c.IsFixedLength,-5} " +
                                     $"nullable={c.IsNullable,-5} id={c.ColumnId} auto={c.IsAutoNumber}");
                foreach (IndexDef i in t.Indexes)
                    output.WriteLine($"     index {i.Name,-24} root={i.RootPage} unique={i.IsUnique} pk={i.IsPrimaryKey} " +
                                     $"required={i.Required} ignoreNulls={i.IgnoreNulls} cols={string.Join(",", i.Columns.Select(c => $"{c.Column.Name}{(c.Ascending ? "" : " DESC")}"))}");
            }

            output.WriteLine("");
            output.WriteLine("MSysObjects rows for the complex tables (Id, ParentId, Name, Type, Flags):");
            var objects = db.OpenTable("MSysObjects");
            var def = objects.Definition;
            int idIdx = def.FindColumn("Id")!.Index, parentIdx = def.FindColumn("ParentId")!.Index;
            int nameIdx = def.FindColumn("Name")!.Index, typeIdx = def.FindColumn("Type")!.Index;
            int flagsIdx = def.FindColumn("Flags")!.Index;
            foreach (object?[] row in objects.Rows())
            {
                string name = (string?)row[nameIdx] ?? "";
                if (!name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)) continue;
                output.WriteLine($"     Id={Convert.ToInt32(row[idIdx]),6}  ParentId=0x{Convert.ToInt32(row[parentIdx]):X8}  " +
                                 $"{name,-30} Type={row[typeIdx]}  Flags=0x{Convert.ToInt32(row[flagsIdx] ?? 0):X8}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, System.Reflection.BindingFlags.InvokeMethod, null, target, args);
}
