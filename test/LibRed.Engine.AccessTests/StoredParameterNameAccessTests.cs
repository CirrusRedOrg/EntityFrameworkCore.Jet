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

    // A parameter declares no storage, so a type the file is too old for neither raises it nor is refused: ACE stores
    // it with its type on an ACE 12 file, leaves the version byte at 0x02, and reads it back with its type name.
    [Theory]
    [InlineData("CREATE PROCEDURE P (p BIGINT) AS SELECT K FROM T WHERE K = p")]
    [InlineData("CREATE PROCEDURE P (d DATETIME2) AS SELECT K FROM T WHERE D = d")]
    [InlineData("CREATE PROCEDURE P (p BIGINT) AS DELETE FROM T WHERE K = p")]
    public void A_parameter_of_a_newer_type_is_stored_on_an_older_file_as_ace_stores_it(string sql)
    {
        string newerType = sql.Contains("DATETIME2", StringComparison.Ordinal) ? "DATETIME2" : "BIGINT";
        Assert.SkipUnless(
            AceTestDatabase.SupportsColumnType(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), newerType),
            AceTestDatabase.UnsupportedColumnTypeReason(newerType));

        string basePath = TemporaryDatabase.CreatePath("stored-param-type-base-");
        string acePath = TemporaryDatabase.CreatePath("stored-param-type-ace-");
        string libredPath = TemporaryDatabase.CreatePath("stored-param-type-libred-");
        try
        {
            JetDatabase.Create(basePath, version: 0x02);   // ACE 12
            using (var db = JetDatabase.Open(basePath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery("CREATE TABLE T (K LONG, D DATETIME)");
            File.Copy(basePath, acePath, overwrite: true);
            File.Copy(basePath, libredPath, overwrite: true);

            using (OleDbConnection conn = AceTestDatabase.Open(acePath))
            {
                using OleDbCommand cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            OleDbConnection.ReleaseObjectPool();
            using (var db = JetDatabase.Open(libredPath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(sql);

            (byte Version, string Parameters, string? Sql) Stored(string path)
            {
                using var db = JetDatabase.Open(path);
                var query = db.Catalog.FindQuery("P")!;
                return (db.DefinitionPage.JetVersion,
                    string.Join(", ", query.Parameters.Select(p => $"{p.Name} {p.Type}")), query.Sql);
            }

            var ace = Stored(acePath);
            Assert.Equal(0x02, ace.Version);
            Assert.Equal(ace, Stored(libredPath));
        }
        finally
        {
            TemporaryDatabase.Delete(basePath);
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