using System.Data;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Writing a stored action query of each kind. The test is not that LibRed can write rows — it is that the
// rows are the ones ACE writes for the same statement, and that ACE then reads and runs the query. Storing a
// query the engine will not run is worse than refusing to store it: the file opens, the query is listed, and
// it fails only when someone tries to use it.
[Collection(AceCollection.Name)]
public class StoredActionQueryWriteAccessTests : TempDatabaseTest
{
    private static string Copy() => TemporaryDatabase.CopyPath(
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "action-write-");

    [Theory]
    [InlineData("UPDATE Customers SET ContactTitle = 'Owner' WHERE Country = 'UK'")]
    [InlineData("UPDATE Products SET UnitPrice = UnitPrice * 1.1, Discontinued = True WHERE CategoryID = 1")]
    [InlineData("UPDATE Orders INNER JOIN Customers ON Orders.CustomerID = Customers.CustomerID " +
                "SET Orders.ShipCountry = Customers.Country WHERE Customers.Country = 'UK'")]
    [InlineData("DELETE FROM Shippers WHERE ShipperID > 900")]
    [InlineData("DELETE Shippers.* FROM Shippers WHERE ShipperID > 900")]
    [InlineData("SELECT ShipperID, CompanyName INTO ShipperCopy FROM Shippers WHERE ShipperID > 1")]
    [InlineData("INSERT INTO Shippers (CompanyName, Phone) SELECT CompanyName, Phone FROM Customers WHERE Country = 'UK'")]
    public void A_libred_written_action_query_stores_the_rows_ace_stores(string body)
    {
        string ourPath = Copy(), acePath = Copy();
        try
        {
            using (var db = TemporaryDatabase.OpenTracked(ourPath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery($"CREATE PROCEDURE [P] AS {body}");

            using (var connection = AceTestDatabase.Open(acePath))
            {
                using var create = connection.CreateCommand();
                create.CommandText = $"CREATE PROCEDURE [P] AS {body}";
                create.ExecuteNonQuery();
            }

            Assert.Equal(QueryRows(acePath, "P"), QueryRows(ourPath, "P"));
            Assert.Equal(ObjectFlags(acePath, "P"), ObjectFlags(ourPath, "P"));

            // And the row set is one ACE acts on, not merely one it tolerates: it runs the query.
            using (var connection = AceTestDatabase.Open(ourPath))
            {
                using var run = connection.CreateCommand();
                run.CommandText = "P";
                run.CommandType = CommandType.StoredProcedure;
                run.ExecuteNonQuery();
            }
        }
        finally
        {
            TemporaryDatabase.Delete(ourPath);
            TemporaryDatabase.Delete(acePath);
        }
    }

    [Fact]
    public void A_libred_written_action_query_declares_its_parameters_as_ace_does()
    {
        const string Body =
            "CREATE PROCEDURE [ByCountry] (pTitle Text(50), pCountry Text(20)) AS " +
            "UPDATE Customers SET ContactTitle = pTitle WHERE Country = pCountry";

        string ourPath = Copy(), acePath = Copy();
        try
        {
            using (var db = TemporaryDatabase.OpenTracked(ourPath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(Body);

            using (var connection = AceTestDatabase.Open(acePath))
            {
                using var create = connection.CreateCommand();
                create.CommandText = Body;
                create.ExecuteNonQuery();
            }

            // The parameter rows carry the type code AND the declared length (in LvExtra, which is where
            // Access reads it back from) — a bare Text would be a memo, and a missing length reads as 255.
            Assert.Equal(QueryRows(acePath, "ByCountry"), QueryRows(ourPath, "ByCountry"));

            using var ace = AceTestDatabase.Open(ourPath);
            using var run = ace.CreateCommand();
            run.CommandText = "ByCountry";
            run.CommandType = CommandType.StoredProcedure;
            run.Parameters.Add(new OleDbParameter { OleDbType = OleDbType.VarWChar, Size = 50, Value = "Set by ACE" });
            run.Parameters.Add(new OleDbParameter { OleDbType = OleDbType.VarWChar, Size = 20, Value = "UK" });
            Assert.Equal(7, run.ExecuteNonQuery());   // Northwind has seven UK customers
        }
        finally
        {
            TemporaryDatabase.Delete(ourPath);
            TemporaryDatabase.Delete(acePath);
        }
    }

    /// <summary>A declared parameter's facets ride in its row's <c>LvExtra</c>: a length for text, precision
    /// and scale packed into one value for a decimal, and nothing for the types that record none — a sized
    /// binary included, which is why this cannot just write whatever size was declared.</summary>
    [Theory]
    [InlineData("TEXT(50)")]
    [InlineData("DECIMAL(18,4)")]
    [InlineData("NUMERIC(10,2)")]
    [InlineData("BINARY(10)")]
    [InlineData("LONG")]
    public void A_declared_parameters_facets_are_stored_as_ace_stores_them(string declared)
    {
        const string Body = "AS SELECT CompanyName FROM Shippers WHERE CompanyName = p";
        string ourPath = Copy(), acePath = Copy();
        try
        {
            using (var db = TemporaryDatabase.OpenTracked(ourPath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery($"CREATE PROCEDURE [P] (p {declared}) {Body}");

            using (var connection = AceTestDatabase.Open(acePath))
            {
                using var create = connection.CreateCommand();
                create.CommandText = $"CREATE PROCEDURE [P] (p {declared}) {Body}";
                create.ExecuteNonQuery();
            }

            Assert.Equal(QueryRows(acePath, "P"), QueryRows(ourPath, "P"));
        }
        finally
        {
            TemporaryDatabase.Delete(ourPath);
            TemporaryDatabase.Delete(acePath);
        }
    }

    /// <summary>A stored query whose body has no FROM at all. ACE stores and runs one, and stores it as any
    /// other query minus the table rows — so LibRed's has to be the same rows, and ACE has to run it.</summary>
    [Theory]
    [InlineData("CREATE VIEW [Q] AS SELECT 1 AS n")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT 1 AS n")]
    public void A_from_less_body_is_stored_as_ace_stores_it(string sql)
    {
        string ourPath = Copy(), acePath = Copy();
        try
        {
            using (var db = TemporaryDatabase.OpenTracked(ourPath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(sql);

            using (var connection = AceTestDatabase.Open(acePath))
            {
                using var create = connection.CreateCommand();
                create.CommandText = sql;
                create.ExecuteNonQuery();
            }

            Assert.Equal(QueryRows(acePath, "Q"), QueryRows(ourPath, "Q"));
            Assert.Equal(ObjectFlags(acePath, "Q"), ObjectFlags(ourPath, "Q"));

            // ACE opens the query LibRed wrote and returns its row. It will not use such a query as a
            // SOURCE — `SELECT n FROM [Q]` fails with "Query input must contain at least one table or
            // query" — but it fails that way on its own file too, so the two files behave identically;
            // LibRed's engine is the more permissive of the two, not the odd one out.
            using var ace = AceTestDatabase.Open(ourPath);
            using var run = ace.CreateCommand();
            run.CommandText = "Q";
            run.CommandType = CommandType.StoredProcedure;
            using var reader = run.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(1, Convert.ToInt32(reader.GetValue(0)));
        }
        finally
        {
            TemporaryDatabase.Delete(ourPath);
            TemporaryDatabase.Delete(acePath);
        }
    }

    [Fact]
    public void A_written_action_query_reads_back_as_the_statement_it_was_written_from()
    {
        string path = Copy();
        try
        {
            using var db = TemporaryDatabase.OpenTracked(path, readOnly: false);
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery(
                "CREATE PROCEDURE [P] AS UPDATE Customers SET ContactTitle = 'Owner' WHERE Country = 'UK'");

            // Round trip: what was written is read back as runnable SQL, and running it by name works.
            StoredActionQuery stored = db.Catalog.ActionQueries["P"];
            Assert.Null(stored.UnsupportedReason);
            Assert.Equal("UPDATE [Customers] SET [ContactTitle] = 'Owner' WHERE Country = 'UK'", stored.Sql);
            Assert.Equal(7, engine.ExecuteNonQuery("EXECUTE [P]"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>
    /// Every <c>MSysQueries</c> field of a stored query's rows, ordered so two files compare — a field
    /// neither side sets cannot hide behind a narrower comparison. Two are left out: <c>ObjectId</c>, which
    /// is a per-file identity, and <c>LvExtra</c> on any row that is not a declared parameter, where it holds
    /// nothing. It carries the parameter's declared length on an <c>0x02</c> row and is uninitialised
    /// elsewhere — measured: for the same statement ACE left it null with no parameters declared, and wrote 0
    /// and 226 on the very same rows once one was, while Northwind's designer-authored query has 936840680
    /// throughout.
    /// </summary>
    private static List<string> QueryRows(string path, string queryName)
    {
        using var db = JetDatabase.Open(path);
        TableDef queries = db.Catalog.FindTable("MSysQueries")!;
        int objectIdIndex = Index(queries, "ObjectId");
        int attributeIndex = Index(queries, "Attribute");
        int id = QueryObjectId(db, queryName);

        var rows = db.OpenTable("MSysQueries").Rows()
            .Where(row => Equals(row[objectIdIndex], id))
            .Select(row => string.Join(" | ", queries.Columns
                .Where(c => c.Name != "ObjectId"
                    && (c.Name != "LvExtra" || Equals(row[attributeIndex], (byte)0x02)))
                .Select(c => $"{c.Name}={Text(row[c.Index])}")))
            .ToList();
        rows.Sort(StringComparer.Ordinal);
        return rows;

        static string Text(object? value) => value switch
        {
            null => "-",
            byte[] bytes => Convert.ToHexString(bytes),
            _ => value.ToString()!,
        };
    }

    private static int ObjectFlags(string path, string queryName)
    {
        using var db = JetDatabase.Open(path);
        TableDef objects = db.Catalog.FindTable("MSysObjects")!;
        int flagsIndex = Index(objects, "Flags");
        int idIndex = Index(objects, "Id");
        int id = QueryObjectId(db, queryName);
        return (int)db.OpenTable("MSysObjects").Rows().Single(row => Equals(row[idIndex], id))[flagsIndex]!;
    }

    private static int QueryObjectId(JetDatabase db, string queryName)
    {
        TableDef objects = db.Catalog.FindTable("MSysObjects")!;
        int idIndex = Index(objects, "Id"), nameIndex = Index(objects, "Name");
        return (int)db.OpenTable("MSysObjects").Rows()
            .Single(row => string.Equals(row[nameIndex] as string, queryName, StringComparison.OrdinalIgnoreCase))[idIndex]!;
    }

    private static int Index(TableDef table, string column) => table.FindColumn(column)!.Index;
}
