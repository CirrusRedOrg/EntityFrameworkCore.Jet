using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A declared parameter of a stored query. ACE stores its name as declared — a bracketed one keeps its brackets,
/// a bare <c>@name</c> loses the <c>@</c> — and LibRed must store the same bytes, and read either engine's back by
/// the name it binds to, rebuilding a query that still parses and runs.
/// </summary>
[Collection(AceCollection.Name)]
public class StoredParameterNameAccessTests
{
    private static string Create(string declared) =>
        $"CREATE PROCEDURE P ({declared} TEXT(50)) AS SELECT CustomerID FROM Customers WHERE ContactName = {declared}";

    [Theory]
    [InlineData("[@firstName]", "@firstName")]
    [InlineData("[firstName]", "firstName")]
    [InlineData("firstName", "firstName")]
    [InlineData("[first name]", "first name")]
    [InlineData("@firstName", "firstName")]
    public void A_parameter_is_stored_as_ace_stores_it_and_reads_back_by_its_name(string declared, string name)
    {
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string acePath = TemporaryDatabase.CopyPath(northwind, "stored-param-ace-");
        string libredPath = TemporaryDatabase.CopyPath(northwind, "stored-param-libred-");
        try
        {
            using (OleDbConnection conn = AceTestDatabase.Open(acePath))
            {
                using OleDbCommand cmd = conn.CreateCommand();
                cmd.CommandText = Create(declared);
                cmd.ExecuteNonQuery();
            }
            using (var db = JetDatabase.Open(libredPath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(Create(declared));

            Assert.Equal(StoredName(acePath), StoredName(libredPath));

            foreach (string path in new[] { acePath, libredPath })
            {
                using var db = JetDatabase.Open(path);
                Assert.Equal([name], db.Catalog.FindQuery("P")!.Parameters.Select(p => p.Name));
                var engine = new QueryEngine(db);
                Assert.Equal(["ALFKI"], engine.ExecuteQuery("EXECUTE P 'Maria Anders'").Rows.Select(r => r[0]));
                // As a table source, bound by the name reported for it — what a consumer reading the schema does.
                Assert.Equal(["ALFKI"], engine.ExecuteQuery("SELECT * FROM [P]",
                    new Dictionary<string, object?> { [name] = "Maria Anders" }).Rows.Select(r => r[0]));
            }
        }
        finally
        {
            TemporaryDatabase.Delete(acePath);
            TemporaryDatabase.Delete(libredPath);
        }
    }

    /// <summary>The raw <c>Name1</c> of query P's parameter row.</summary>
    private static string? StoredName(string path)
    {
        using var db = JetDatabase.Open(path);
        var objects = db.Catalog.FindTable("MSysObjects")!;
        int id = objects.FindColumn("Id")!.Index, objectName = objects.FindColumn("Name")!.Index;
        object procId = db.OpenTable("MSysObjects").Rows().Single(r => r[objectName] as string == "P")[id]!;

        var queries = db.Catalog.FindTable("MSysQueries")!;
        int attr = queries.FindColumn("Attribute")!.Index, name1 = queries.FindColumn("Name1")!.Index,
            owner = queries.FindColumn("ObjectId")!.Index;
        return db.OpenTable("MSysQueries").Rows()
            .Single(r => r[attr] is byte a && a == 2 && Equals(r[owner], procId))[name1] as string;
    }
}