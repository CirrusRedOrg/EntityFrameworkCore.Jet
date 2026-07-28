using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class RowInserterTests
{
    private static string CopyToTemp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-insert-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        return path;
    }

    private static object?[] BuildValues(TableDef table, IReadOnlyDictionary<string, object?> byName)
    {
        var values = new object?[table.Columns.Count];
        foreach (ColumnDef column in table.Columns)
            values[column.Index] = byName.TryGetValue(column.Name, out object? v) ? v : null;
        return values;
    }

    private static List<(int Id, string Name, string Phone)> ReadShippers(string path)
    {
        using var db = JetDatabase.Open(path);
        var table = db.OpenTable("Shippers");
        int id = table.Definition.FindColumn("ShipperID")!.Index;
        int name = table.Definition.FindColumn("CompanyName")!.Index;
        int phone = table.Definition.FindColumn("Phone")!.Index;
        return table.Rows()
            .Select(r => (Convert.ToInt32(r[id]), (string)r[name]!, (string)r[phone]!))
            .OrderBy(r => r.Item1)
            .ToList();
    }

    [Fact]
    public void Inserted_row_round_trips_through_our_reader()
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var table = db.OpenTable("Shippers");
                table.Insert(BuildValues(table.Definition, new Dictionary<string, object?>
                {
                    ["ShipperID"] = 4,
                    ["CompanyName"] = "Speedy Express 2",
                    ["Phone"] = "(503) 555-0000",
                }));
            }

            var rows = ReadShippers(path);
            Assert.Equal(4, rows.Count); // 3 original + 1
            Assert.Contains((4, "Speedy Express 2", "(503) 555-0000"), rows);
            // Original rows are intact.
            Assert.Contains((1, "Speedy Express", "(503) 555-9831"), rows);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Index_stays_sorted_across_inserts_including_a_middle_key()
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var table = db.OpenTable("Shippers");
                // Insert 5 (appends), then 4 (must slot between 3 and 5).
                foreach (int id in new[] { 5, 4 })
                    table.Insert(BuildValues(table.Definition, new Dictionary<string, object?>
                    {
                        ["ShipperID"] = id,
                        ["CompanyName"] = $"Shipper {id}",
                        ["Phone"] = "(000) 000-0000",
                    }));
            }

            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Shippers");
                var pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                var ids = new IndexCursor(table.Channel, pk.RootPage)
                    .Entries(pk.Columns)
                    .Select(e => (int)e.Key[0]!)
                    .ToList();
                Assert.Equal([1, 2, 3, 4, 5], ids); // index order, with 4 inserted in the middle
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Insert_matches_access_own_engine_and_is_readable_by_access()
    {
        string ours = CopyToTemp();
        string access = CopyToTemp();
        try
        {
            // Our insert.
            using (var db = JetDatabase.Open(ours, readOnly: false))
            {
                var table = db.OpenTable("Shippers");
                table.Insert(BuildValues(table.Definition, new Dictionary<string, object?>
                {
                    ["ShipperID"] = 4,
                    ["CompanyName"] = "Speedy Express 2",
                    ["Phone"] = "(503) 555-0000",
                }));
            }

            // Access's own insert via OLE DB (the ground truth).
            using (var conn = OpenOleDb(access))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "INSERT INTO Shippers (ShipperID, CompanyName, Phone) VALUES (4, 'Speedy Express 2', '(503) 555-0000')";
                cmd.ExecuteNonQuery();
            }

            // Both databases, read through our reader, must contain the same Shippers rows.
            Assert.Equal(ReadShippers(access), ReadShippers(ours));

            // Access must find the row we wrote via an indexed primary-key seek — which only
            // works because we maintained the PK B-tree, not just the heap.
            using (var conn = OpenOleDb(ours))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT CompanyName, Phone FROM Shippers WHERE ShipperID = 4";
                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("Speedy Express 2", reader.GetString(0));
                Assert.Equal("(503) 555-0000", reader.GetString(1));
            }
        }
        finally
        {
            File.Delete(ours);
            File.Delete(access);
        }
    }

    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try
            {
                var conn = new OleDbConnection($"Provider={provider};Data Source={path}");
                conn.Open();
                return conn;
            }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException)
            {
                // Try the next provider version.
            }
        }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider (12.0/16.0) is available.");
    }
}
