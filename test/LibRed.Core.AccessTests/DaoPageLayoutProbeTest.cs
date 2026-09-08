using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.Pages;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: what the real engine puts on every page of a freshly created database, and in what order — the page
// budget behind a DAO-created ACE 12 file. Useful for judging how closely LibRed's own bootstrap should
// follow it, and for reading a hex dump of one without guessing.
//
// Every page is labelled from its own header (type byte, and the owning TDEF for data/index pages) and
// cross-referenced against the catalog: each table's TDEF page, its index roots, and the usage-map pages its
// TDEF points at.
public class DaoPageLayoutProbeTest(ITestOutputHelper output)
{
    [Fact]
    public void Probe_dao_created_page_layout()
    {
        object? engine = null;
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { engine = Activator.CreateInstance(type); break; } catch (Exception) { }
        }
        if (engine is null) { output.WriteLine("DAO unavailable."); return; }

        string path = TemporaryDatabase.CreatePath("dao-layout-");
        File.Delete(path);
        try
        {
            object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            Invoke(database, "Close");

            byte[] file = File.ReadAllBytes(path);
            using var db = JetDatabase.Open(path);
            int pageSize = db.Format.PageSize;
            int pages = file.Length / pageSize;

            // Label what the catalog knows about: TDEF pages and index roots.
            var owners = new Dictionary<int, string>();
            var labels = new Dictionary<int, string>();
            foreach (TableDef t in db.Catalog.Tables)
            {
                owners[t.DefinitionPage] = t.Name;
                labels[t.DefinitionPage] = $"TDEF {t.Name}";
                foreach (IndexDef i in t.Indexes)
                    if (i.RootPage > 0) labels[i.RootPage] = $"index root {t.Name}.{i.Name}";
            }

            output.WriteLine($"{pages} pages of {pageSize} bytes ({file.Length:N0} bytes)");
            output.WriteLine("");
            output.WriteLine("page  type                    owner  label");
            for (int p = 0; p < pages; p++)
            {
                ReadOnlySpan<byte> page = file.AsSpan(p * pageSize, pageSize);
                var type = (PageType)page[0];
                // Data, index and usage-map pages carry the owning TDEF page at offset 4.
                int owner = type is PageType.DataPage or PageType.IntermediateIndexPage
                                 or PageType.LeafIndexPage or PageType.PageUsageBitmap
                    ? BinaryPrimitives.ReadInt32LittleEndian(page[4..])
                    : 0;
                string ownerName = owner > 0 && owners.TryGetValue(owner, out string? n) ? $"{owner} {n}" : owner > 0 ? owner.ToString() : "";
                labels.TryGetValue(p, out string? label);
                if (label is null && p == 0) label = "database definition";
                if (label is null && p == 1) label = "global free-pages map";
                output.WriteLine($"{p,4}  {type,-22} {ownerName,-24} {label}");
            }

            // Which pages does each TDEF name as its usage maps? Those are the "unlabelled" data pages.
            output.WriteLine("");
            output.WriteLine("usage-map pointers held in each TDEF (row:page):");
            foreach (TableDef t in db.Catalog.Tables.OrderBy(t => t.DefinitionPage))
            {
                ReadOnlySpan<byte> tdef = file.AsSpan(t.DefinitionPage * pageSize, pageSize);
                // Each pointer is a 1-byte row index then a 3-byte page number.
                int owned = BinaryPrimitives.ReadInt32LittleEndian(tdef[db.Format.TdefOwnedPagesOffset..]);
                int free = BinaryPrimitives.ReadInt32LittleEndian(tdef[db.Format.TdefFreePagesOffset..]);
                output.WriteLine($"   {t.Name,-30} owned=page {owned >> 8} row {owned & 0xFF}   free=page {free >> 8} row {free & 0xFF}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, System.Reflection.BindingFlags.InvokeMethod, null, target, args);
}
