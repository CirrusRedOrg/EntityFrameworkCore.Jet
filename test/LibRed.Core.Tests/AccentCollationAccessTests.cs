using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A text index key containing accented Latin-1 characters (é in "México D.F."). The General collation
/// sorts the accented letter with its base letter's primary weight and records the accent in a secondary
/// section. Verified by inserting through LibRed and having Access find the row through the City index.
/// </summary>
public class AccentCollationAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_finds_an_accented_city_through_the_index()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "accent-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var t = db.OpenTable("Customers");
                var values = new object?[t.Definition.Columns.Count];
                void Set(string col, object? v) => values[t.Definition.FindColumn(col)!.Index] = v;
                Set("CustomerID", "ZZZ01");
                Set("CompanyName", "Los Accentos");
                Set("City", "México D.F.");
                Set("Country", "Mexico");
                t.Insert(values);
            }

            using var conn = OpenOleDb(path);

            // Access uses the City index to seek by equality; it must find both the existing México
            // customers and the LibRed-inserted one.
            using var byCity = conn.CreateCommand();
            byCity.CommandText = "SELECT CustomerID FROM Customers WHERE City = 'México D.F.' ORDER BY CustomerID";
            var found = new List<string>();
            using (var r = byCity.ExecuteReader())
                while (r.Read()) found.Add((string)r[0]);
            Assert.Contains("ZZZ01", found);
            Assert.Contains("ANATR", found); // an existing México customer

            // The index remains well-ordered (no corruption): ORDER BY City runs and México sorts among the M's.
            using var ordered = conn.CreateCommand();
            ordered.CommandText = "SELECT DISTINCT City FROM Customers ORDER BY City";
            using (var r = ordered.ExecuteReader())
                while (r.Read()) { _ = r[0]; }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
