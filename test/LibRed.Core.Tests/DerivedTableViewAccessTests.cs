using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A view whose FROM is a derived table (subquery), including a UNION — the shape of Northwind's
/// "Customer and Suppliers by City". Access stores such a source as an <c>Attribute=5</c> row with the
/// inner subquery SQL in <c>Expression</c> and the alias in <c>Name2</c> (no <c>Name1</c>). This checks
/// LibRed writes it that way and that Access opens the file and runs the view.
/// </summary>
public class DerivedTableViewAccessTests
{
    private const string Subquery =
        "SELECT City, CompanyName, ContactName, 'Customers' AS Relationship FROM Customers " +
        "UNION SELECT City, CompanyName, ContactName, 'Suppliers' FROM Suppliers";

    // The ACE OLE DB provider is intermittently unstable on x64 (a spurious "Cannot open database …
    // may be corrupt"; see the ace-provider-crash-flakiness note), so retry the open a few times.
    private static OleDbConnection OpenOleDb(string path)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
            {
                try
                {
                    var conn = new OleDbConnection($"Provider={provider};Data Source={path};OLE DB Services=-4;");
                    conn.Open();
                    return conn;
                }
                catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { last = ex; }
            }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider opened the database.", last);
    }

    private static void CreateView(string path, string name)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateView(name, new ViewSpec(
            Distinct: false,
            Columns: [new ViewColumnSpec("u.City", null), new ViewColumnSpec("u.CompanyName", null), new ViewColumnSpec("u.ContactName", null), new ViewColumnSpec("u.Relationship", null)],
            Tables: [new ViewTableSpec(Table: null, Alias: "u", SubquerySql: Subquery)],
            Joins: [],
            Where: null));
    }

    [Fact]
    public void Stores_the_derived_table_source_the_access_way()
    {
        string path = Path.Combine(Path.GetTempPath(), $"derived-view-store-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            CreateView(path, "CSbyCity2");

            // One Attribute=5 table row with Expression (subquery) + Name2 (alias), no Name1; 4 column rows.
            using var ch = PageChannel.Open(path, readOnly: true);
            var (attr, expr, n1, n2) = QueryRowsFor(ch, "CSbyCity2");
            var tableRows = attr.Select((a, i) => (a, i)).Where(x => x.a == 5).ToList();
            Assert.Single(tableRows);
            int t = tableRows[0].i;
            Assert.Equal(Subquery, expr[t]);
            Assert.Null(n1[t]);
            Assert.Equal("u", n2[t]);
            Assert.Equal(4, attr.Count(a => a == 6));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // The long subquery Expression (> 64 bytes) is written to an LVAL page, so Access can run the view.
    [Fact]
    public void Access_runs_a_derived_table_union_view()
    {
        string path = Path.Combine(Path.GetTempPath(), $"derived-view-run-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            CreateView(path, "CSbyCity2");

            using var conn = OpenOleDb(path);
            using var count = conn.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM CSbyCity2";
            Assert.True(Convert.ToInt32(count.ExecuteScalar()) > 0);

            using var seek = conn.CreateCommand();
            seek.CommandText = "SELECT Relationship FROM CSbyCity2 WHERE City = 'London'";
            Assert.NotNull(seek.ExecuteScalar()); // the view resolves and returns rows
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private static (List<byte> Attr, List<string?> Expr, List<string?> Name1, List<string?> Name2)
        QueryRowsFor(PageChannel ch, string viewName)
    {
        var cat = new JetCatalog(ch);
        var obj = cat.FindTable("MSysObjects")!;
        var mq = cat.FindTable("MSysQueries")!;
        int oId = Col(obj, "Id"), oName = Col(obj, "Name");
        int id = new Table(ch, obj).Rows()
            .First(r => string.Equals(r[oName] as string, viewName, StringComparison.OrdinalIgnoreCase))[oId] is int i ? i : 0;

        int qObj = Col(mq, "ObjectId"), qAttr = Col(mq, "Attribute"),
            qExpr = Col(mq, "Expression"), qN1 = Col(mq, "Name1"), qN2 = Col(mq, "Name2");
        (List<byte>, List<string?>, List<string?>, List<string?>) acc = ([], [], [], []);
        foreach (var r in new Table(ch, mq).Rows())
        {
            if (Convert.ToInt32(r[qObj]) != id) continue;
            acc.Item1.Add(Convert.ToByte(r[qAttr]));
            acc.Item2.Add(r[qExpr] as string);
            acc.Item3.Add(r[qN1] as string);
            acc.Item4.Add(r[qN2] as string);
        }
        return acc;
    }

    private static int Col(TableDef t, string name) =>
        t.Columns.First(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).Index;
}
