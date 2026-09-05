using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

// A LibRed-created view must get the same MSysACEs permission rows Access writes for a query object, or
// Access warns about permissions when opening it (verified against Northwind: owner 0x690C = ACM 0xF00FE,
// admin/users 0x680C = ACM 0xFFEFF — distinct from a table's, where both SIDs get full 0xFFEFF).
public class MSysAcesViewTests
{
    [Fact]
    public void Created_view_gets_the_query_permission_rows_access_writes()
    {
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string path = TemporaryDatabase.CopyPath(northwind, "aces-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(db).ExecuteNonQuery("CREATE VIEW VAces AS SELECT ProductID FROM Products");

            // Find the new query object's id.
            var mo = db.OpenTable("MSysObjects");
            int idIdx = Col(mo, "Id"), typeIdx = Col(mo, "Type"), nameIdx = Col(mo, "Name");
            int viewId = mo.Rows()
                .Where(r => r[nameIdx] as string == "VAces" && Convert.ToInt16(r[typeIdx] ?? (short)0) == 5)
                .Select(r => Convert.ToInt32(r[idIdx]))
                .Single();

            // Collect its MSysACEs rows as (SID-hex, ACM).
            var aces = db.OpenTable("MSysACEs");
            int aObj = Col(aces, "ObjectId"), aSid = Col(aces, "SID"), aAcm = Col(aces, "ACM"), aInh = Col(aces, "FInheritable");
            var rows = aces.Rows()
                .Where(r => r[aObj] is not null && Convert.ToInt32(r[aObj]) == viewId)
                .Select(r => (Sid: Hex(r[aSid] as byte[] ?? []), Acm: Convert.ToInt32(r[aAcm]), Inh: r[aInh]))
                .OrderBy(x => x.Sid)
                .ToList();

            Assert.Equal(2, rows.Count);
            Assert.Equal(("680C", 0xFFEFF), (rows[0].Sid, rows[0].Acm)); // admin/users → full
            Assert.Equal(("690C", 0xF00FE), (rows[1].Sid, rows[1].Acm)); // owner → query mask
            Assert.All(rows, r => Assert.Equal(false, r.Inh));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static int Col(Table t, string name) => t.Definition.FindColumn(name)!.Index;
    private static string Hex(byte[] b) => string.Join("", b.Select(x => x.ToString("X2")));
}
