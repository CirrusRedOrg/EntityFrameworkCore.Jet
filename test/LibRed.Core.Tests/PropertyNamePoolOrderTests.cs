using System.Buffers.Binary;
using System.Data.OleDb;
using System.Text;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful: the LvProp property-name pool is stored in FIRST-APPEARANCE order, not alphabetical.
// Proven against ACE with three names whose first-appearance order (Required, DefaultValue, CheckConstraints)
// is deliberately not alphabetical (which would be CheckConstraints, DefaultValue, Required). Guards against a
// future "tidy-up" that sorts the pool in PropertyBlob.Write (which uses Distinct() = first appearance).
public class PropertyNamePoolOrderTests
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

    private static List<string> ReadNamePool(byte[] blob)
    {
        var names = new List<string>();
        int pos = 4; // 4-byte signature
        while (pos + 6 <= blob.Length)
        {
            int len = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(pos, 4));
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(pos + 4, 2));
            if (len < 6 || pos + len > blob.Length) break;
            if (type == 0x0080)
            {
                int q = pos + 6, end = pos + len;
                while (q + 2 <= end)
                {
                    int nl = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(q, 2)); q += 2;
                    if (q + nl > end) break;
                    names.Add(Encoding.Unicode.GetString(blob.AsSpan(q, nl))); q += nl;
                }
            }
            pos += len;
        }
        return names;
    }

    [Fact]
    public void Ace_stores_the_name_pool_in_first_appearance_order_not_alphabetical()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pnp-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var conn = OpenOleDb(path))
            {
                void Run(string sql) { using var c = conn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
                // R (NOT NULL) → Required first; D (DEFAULT) → DefaultValue; then a CHECK → CheckConstraints last.
                Run("CREATE TABLE PN (R INT NOT NULL, D INT DEFAULT 7, CONSTRAINT PK_PN PRIMARY KEY (R))");
                Run("ALTER TABLE PN ADD CONSTRAINT CK_PN CHECK (R > 0)");
            }

            List<string> pool;
            using (var db = JetDatabase.Open(path, readOnly: true))
            {
                int page = db.Catalog.FindTable("PN")!.DefinitionPage;
                var msys = db.Catalog.FindTable("MSysObjects")!;
                int idIdx = msys.FindColumn("Id")!.Index;
                int lvIdx = msys.FindColumn("LvProp")!.Index;
                pool = db.OpenTable("MSysObjects").Rows()
                    .Where(r => r[idIdx] is not null && Convert.ToInt32(r[idIdx]) == page)
                    .Select(r => ReadNamePool((byte[])r[lvIdx]!))
                    .First();
            }

            // First-appearance order (what ACE wrote), NOT alphabetical.
            Assert.Equal(["Required", "DefaultValue", "CheckConstraints"], pool);
            Assert.NotEqual(pool.OrderBy(n => n, StringComparer.Ordinal).ToList(), pool);

            // LibRed's writer reproduces that same order from properties supplied in first-appearance order.
            var libred = ReadNamePool(PropertyBlob.Write(
            [
                PropertyBlob.Bool("R", PropertyBlob.RequiredProperty, true),
                new PropertyBlob.Property("D", PropertyBlob.DefaultValueProperty, "7"),
                new PropertyBlob.Property("", PropertyBlob.CheckConstraintsProperty, PropertyBlob.WriteCheckList([("CK_PN", "R > 0")])),
            ]));
            Assert.Equal(pool, libred);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
