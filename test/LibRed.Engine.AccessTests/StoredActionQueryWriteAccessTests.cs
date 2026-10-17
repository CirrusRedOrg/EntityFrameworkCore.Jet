using System.Data;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using LibRed.Engine.Execution;
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
    [InlineData("SELECT DISTINCT Country INTO CountryCopy FROM Customers")]
    [InlineData("SELECT TOP 5 CompanyName INTO CustomerCopy FROM Customers")]
    [InlineData("SELECT * INTO ShipperCopy FROM Shippers")]
    [InlineData("INSERT INTO Shippers (CompanyName) SELECT DISTINCT Country FROM Customers")]
    [InlineData("INSERT INTO Shippers (CompanyName, Phone) SELECT CompanyName, Phone FROM Customers WHERE Country = 'UK'")]
    [InlineData("INSERT INTO Shippers SELECT CompanyName, Phone FROM Suppliers WHERE SupplierID < 4")]
    [InlineData("INSERT INTO Shippers SELECT ContactTitle AS Phone, Phone AS CompanyName FROM Suppliers WHERE SupplierID < 4")]
    [InlineData("INSERT INTO Shippers SELECT Suppliers.CompanyName FROM Suppliers WHERE SupplierID < 4")]
    [InlineData("INSERT INTO Shippers SELECT * FROM (SELECT CompanyName, Phone FROM Suppliers WHERE SupplierID < 4) AS s")]
    [InlineData("INSERT INTO Shippers VALUES (50, 'Fifty', '555')")]
    [InlineData("INSERT INTO Shippers SELECT 'NoFrom' AS CompanyName")]
    [InlineData("INSERT INTO Shippers (CompanyName) SELECT TOP 1 CompanyName FROM Suppliers ORDER BY SupplierID DESC")]
    [InlineData("INSERT INTO Shippers (CompanyName) SELECT Country FROM Customers GROUP BY Country HAVING Count(*) > 10")]
    [InlineData("SELECT TOP 1 CompanyName INTO TopCopy FROM Customers ORDER BY CustomerID DESC")]
    [InlineData("SELECT Country, Count(*) AS N INTO CountryCount FROM Customers GROUP BY Country HAVING Count(*) > 5 ORDER BY Country DESC")]
    [InlineData("DELETE Shippers.Phone FROM Shippers WHERE ShipperID > 900")]
    [InlineData("DELETE Shippers.Phone, Shippers.CompanyName FROM Shippers WHERE ShipperID > 900")]
    [InlineData("DELETE Phone FROM Shippers WHERE ShipperID > 900")]
    [InlineData("DELETE Shippers.Phone, Shippers.* FROM Shippers WHERE ShipperID > 900")]
    [InlineData("UPDATE [Order Details] SET [Order Details].Discount = 0 WHERE OrderID = 10248")]
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

    /// <summary>WITH OWNERACCESS OPTION is one more option row in a stored query, which ACE writes for a view and for
    /// every kind of action query alike. LibRed acts on nothing it says, but keeps it: the rows are ACE's, ACE runs the
    /// query LibRed stored, and LibRed reads the declaration back and runs the query itself.</summary>
    [Theory]
    [InlineData("CREATE VIEW [Q] AS SELECT CompanyName FROM Shippers WITH OWNERACCESS OPTION")]
    // ACE's CREATE VIEW takes no ORDER BY ("Only simple SELECT queries are allowed in VIEWS"); a procedure does.
    [InlineData("CREATE PROCEDURE [Q] AS SELECT DISTINCT CompanyName FROM Shippers ORDER BY CompanyName WITH OWNERACCESS OPTION")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT TOP 2 CompanyName FROM Shippers ORDER BY CompanyName WITH OWNERACCESS OPTION")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT DISTINCT TOP 2 CompanyName FROM Shippers ORDER BY CompanyName WITH OWNERACCESS OPTION")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT DISTINCT TOP 50 PERCENT CompanyName FROM Shippers ORDER BY CompanyName " +
                "WITH OWNERACCESS OPTION")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT CompanyName FROM Shippers WITH OWNERACCESS OPTION")]
    [InlineData("CREATE PROCEDURE [Q] AS UPDATE Customers SET ContactTitle = 'Owner' WHERE Country = 'UK' WITH OWNERACCESS OPTION")]
    [InlineData("CREATE PROCEDURE [Q] AS DELETE FROM Shippers WHERE ShipperID > 900 WITH OWNERACCESS OPTION")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT ShipperID, CompanyName INTO ShipperCopy FROM Shippers WITH OWNERACCESS OPTION")]
    [InlineData("CREATE PROCEDURE [Q] AS INSERT INTO Shippers (CompanyName, Phone) SELECT CompanyName, Phone FROM Customers " +
                "WHERE Country = 'UK' WITH OWNERACCESS OPTION")]
    [InlineData("CREATE PROCEDURE [Q] AS INSERT INTO Shippers (CompanyName) VALUES ('Owner') WITH OWNERACCESS OPTION")]
    public void An_owneraccess_query_is_stored_as_ace_stores_it(string sql)
    {
        string ourPath = Copy(), acePath = Copy(), readPath = Copy();
        bool action = !sql.Contains(" AS SELECT CompanyName", StringComparison.Ordinal)
            && !sql.Contains(" AS SELECT DISTINCT", StringComparison.Ordinal) && !sql.Contains(" AS SELECT TOP", StringComparison.Ordinal);
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

            // ACE runs the query LibRed stored — on a copy, so an action query leaves the file LibRed reads next alone.
            File.Copy(ourPath, readPath, overwrite: true);
            using (var connection = AceTestDatabase.Open(readPath))
            {
                using var run = connection.CreateCommand();
                run.CommandText = "Q";
                run.CommandType = CommandType.StoredProcedure;
                run.ExecuteNonQuery();
            }

            // LibRed reads the declaration back with the query, and runs it.
            using var ours = TemporaryDatabase.OpenTracked(ourPath, readOnly: false);
            StoredQuery query = ours.Catalog.FindQuery("Q")!;
            Assert.Equal(action, query.IsAction);
            string? stored = query.Sql;
            Assert.EndsWith(" WITH OWNERACCESS OPTION", stored);
            var engine = new QueryEngine(ours);
            if (action) engine.ExecuteStoredActionQuery("Q");
            else Assert.NotEmpty(engine.ExecuteQuery("SELECT * FROM [Q]").Rows);
        }
        finally
        {
            TemporaryDatabase.Delete(ourPath);
            TemporaryDatabase.Delete(acePath);
            TemporaryDatabase.Delete(readPath);
        }
    }

    /// <summary>A stored SELECT's DISTINCT, TOP and PERCENT are bits of one option row, as ACE writes them — and PERCENT
    /// survives the round trip: ACE and LibRed return the same rows from the query LibRed stored.</summary>
    [Theory]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT DISTINCT TOP 2 Country FROM Customers ORDER BY Country")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT TOP 10 PERCENT CompanyName FROM Customers ORDER BY CompanyName")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT DISTINCT TOP 25 PERCENT Country FROM Customers ORDER BY Country")]
    public void A_queries_options_share_one_row_as_ace_writes_them(string sql)
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

            int aceRows = 0;
            using (var connection = AceTestDatabase.Open(ourPath))
            {
                using var run = connection.CreateCommand();
                run.CommandText = "Q";
                run.CommandType = CommandType.StoredProcedure;
                using var reader = run.ExecuteReader();
                while (reader.Read()) aceRows++;
            }
            using var ours = TemporaryDatabase.OpenTracked(ourPath, readOnly: true);
            Assert.Equal(aceRows, new QueryEngine(ours).ExecuteQuery("SELECT * FROM [Q]").Rows.Count());
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
            StoredQuery stored = db.Catalog.FindQuery("P")!;
            Assert.Null(stored.UnsupportedReason);
            Assert.Equal("UPDATE [Customers] SET [ContactTitle] = 'Owner' WHERE Country = 'UK'", stored.Sql);
            Assert.Equal(7, engine.ExecuteNonQuery("EXECUTE [P]"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>A UNION query is stored as ACE stores it — two segments, the text before its last UNION and the term
    /// after it — and returns the same rows whichever engine wrote it and whichever runs it: ACE on LibRed's file, and
    /// LibRed on ACE's and on its own. ACE returns a UNION's rows sorted and LibRed in the order it meets them, so the
    /// rows compare as sets.</summary>
    [Theory]
    [InlineData("SELECT CompanyName FROM Shippers UNION SELECT CompanyName FROM Customers")]
    [InlineData("SELECT CompanyName FROM Shippers UNION ALL SELECT CompanyName FROM Customers")]
    [InlineData("SELECT CompanyName FROM Shippers UNION SELECT CompanyName FROM Customers UNION ALL SELECT CompanyName FROM Suppliers")]
    [InlineData("SELECT CompanyName FROM Shippers UNION ALL SELECT CompanyName FROM Customers UNION SELECT CompanyName FROM Suppliers")]
    [InlineData("SELECT CompanyName FROM Shippers UNION SELECT CompanyName FROM Customers UNION SELECT CompanyName FROM Suppliers " +
                "UNION ALL SELECT ShipName FROM Orders")]
    [InlineData("SELECT CompanyName FROM Shippers UNION SELECT CompanyName FROM Customers ORDER BY CompanyName DESC")]
    [InlineData("SELECT CompanyName AS N FROM Shippers WHERE ShipperID > 1 UNION SELECT ContactName FROM Customers WHERE Country = 'UK'")]
    [InlineData("TABLE Shippers UNION TABLE Shippers")]
    [InlineData("TABLE [Order Details] UNION ALL TABLE [Order Details]")]
    [InlineData("(SELECT CompanyName FROM Shippers) UNION (SELECT CompanyName FROM Customers)")]
    [InlineData("SELECT CompanyName FROM Shippers UNION (SELECT CompanyName FROM Customers UNION ALL SELECT CompanyName FROM Suppliers)")]
    [InlineData("SELECT Orders.OrderID FROM Orders INNER JOIN Customers ON Orders.CustomerID = Customers.CustomerID " +
                "WHERE Customers.Country = 'UK' UNION SELECT OrderID FROM Orders WHERE OrderID < 10250")]
    [InlineData("SELECT TOP 2 CompanyName FROM Shippers UNION SELECT DISTINCT Country FROM Customers")]
    [InlineData("   SELECT   CompanyName\r\n FROM  Shippers   UNION   SELECT CompanyName  FROM Customers")]
    [InlineData("SELECT Country, Count(*) AS N FROM Customers GROUP BY Country UNION SELECT Country, Count(*) FROM Suppliers GROUP BY Country")]
    [InlineData("SELECT CompanyName FROM Shippers UNION SELECT CompanyName FROM Customers WITH OWNERACCESS OPTION")]
    [InlineData("SELECT CompanyName FROM Shippers WITH OWNERACCESS OPTION UNION SELECT CompanyName FROM Customers")]
    [InlineData("SELECT CompanyName FROM Shippers UNION SELECT CompanyName FROM Customers ORDER BY CompanyName WITH OWNERACCESS OPTION")]
    [InlineData("SELECT CompanyName FROM Shippers WHERE ShipperID = 1; UNION ALL SELECT CompanyName FROM Shippers WHERE ShipperID = 2")]
    [InlineData("SELECT CompanyName FROM Shippers WHERE ShipperID = 1;\r\nUNION SELECT CompanyName FROM Shippers WHERE ShipperID = 2; " +
                "UNION SELECT CompanyName FROM Shippers WHERE ShipperID = 3")]
    public void A_union_query_is_stored_as_ace_stores_it_and_runs_alike(string body) =>
        UnionRoundTrip($"CREATE PROCEDURE [Q] AS {body}", parameter: null);

    [Fact]
    public void A_union_query_declares_its_parameters_as_a_select_does() =>
        UnionRoundTrip("CREATE PROCEDURE [Q] (pc Text(20)) AS SELECT CompanyName FROM Customers WHERE Country = pc " +
                       "UNION SELECT CompanyName FROM Shippers", parameter: "UK");

    private static void UnionRoundTrip(string sql, string? parameter)
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

            var aceRows = new List<string>();
            using (var connection = AceTestDatabase.Open(ourPath, readOnly: true))
            {
                using var run = connection.CreateCommand();
                run.CommandText = "Q";
                run.CommandType = CommandType.StoredProcedure;
                if (parameter is not null)
                    run.Parameters.Add(new OleDbParameter { OleDbType = OleDbType.VarWChar, Size = 20, Value = parameter });
                using var reader = run.ExecuteReader();
                var values = new object[reader.FieldCount];
                while (reader.Read())
                {
                    reader.GetValues(values);
                    aceRows.Add(Row(values));
                }
            }
            OleDbConnection.ReleaseObjectPool();
            aceRows.Sort(StringComparer.Ordinal);

            foreach (string path in new[] { acePath, ourPath })
            {
                using var db = TemporaryDatabase.OpenTracked(path, readOnly: true);
                string execute = parameter is null ? "EXECUTE [Q]" : $"EXECUTE [Q] '{parameter}'";
                List<string> ours = [.. new QueryEngine(db).ExecuteQuery(execute).Rows.Select(r => Row(r))];
                ours.Sort(StringComparer.Ordinal);
                Assert.Equal(aceRows, ours);
            }
        }
        finally
        {
            TemporaryDatabase.Delete(ourPath);
            TemporaryDatabase.Delete(acePath);
        }

        static string Row(object?[] values) => string.Join(" | ", values.Select(v =>
            v is null or DBNull ? "NULL" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>A crosstab is stored as ACE stores it — the TRANSFORM value, the headings and the PIVOT in column and
    /// GROUP BY rows of their own flags — and runs alike: ACE on LibRed's file and LibRed on both return the same
    /// columns, named alike, and the same rows in the same order.</summary>
    [Theory]
    [InlineData("TRANSFORM Count(*) SELECT Country FROM Customers GROUP BY Country PIVOT City")]
    [InlineData("TRANSFORM Count(OrderID) SELECT EmployeeID FROM Orders GROUP BY EmployeeID PIVOT ShipVia")]
    [InlineData("TRANSFORM Sum(Freight) AS F SELECT EmployeeID, Sum(Freight) AS Total FROM Orders WHERE Freight > 10 " +
                "GROUP BY EmployeeID ORDER BY EmployeeID DESC PIVOT ShipVia IN (1, 2)")]
    [InlineData("TRANSFORM Count(OrderID) SELECT DISTINCT EmployeeID AS Emp FROM Orders GROUP BY EmployeeID PIVOT ShipVia " +
                "WITH OWNERACCESS OPTION")]
    [InlineData("TRANSFORM Count(OrderID) SELECT EmployeeID, ShipVia FROM Orders GROUP BY EmployeeID, ShipVia PIVOT Year(OrderDate)")]
    [InlineData("TRANSFORM Count(OrderID) SELECT EmployeeID FROM Orders GROUP BY EmployeeID, ShipVia PIVOT ShipVia")]
    [InlineData("TRANSFORM Count(Orders.OrderID) SELECT Customers.Country FROM Orders INNER JOIN Customers " +
                "ON Orders.CustomerID = Customers.CustomerID GROUP BY Customers.Country PIVOT Orders.ShipVia")]
    public void A_crosstab_is_stored_as_ace_stores_it_and_runs_alike(string body) =>
        QueryRoundTrip($"CREATE PROCEDURE [Q] AS {body}", parameter: null);

    [Fact]
    public void A_crosstab_declares_its_parameters_as_a_select_does() =>
        QueryRoundTrip("CREATE PROCEDURE [Q] (y SHORT) AS TRANSFORM Count(OrderID) SELECT EmployeeID FROM Orders " +
                       "WHERE Year(OrderDate) = y GROUP BY EmployeeID PIVOT DatePart('q', OrderDate)", parameter: (short)1997);

    /// <summary>A bare <c>*</c> is the option row's output-all-fields bit beside the rows of the other columns named,
    /// <c>TABLE t</c> is <c>SELECT * FROM t</c>, and DISTINCTROW is its own bit, with TOP and PERCENT or without — stored as
    /// ACE stores them, and run alike, column names included.</summary>
    [Theory]
    [InlineData("SELECT * FROM Shippers")]
    [InlineData("SELECT *, ShipperID AS X FROM Shippers")]
    [InlineData("SELECT Phone, *, CompanyName FROM Shippers")]
    [InlineData("SELECT *, (SELECT Max(OrderID) FROM Orders) AS M FROM Shippers")]
    [InlineData("SELECT *, * FROM Shippers")]
    [InlineData("TABLE Shippers")]
    [InlineData("SELECT DISTINCTROW * FROM Shippers")]
    [InlineData("SELECT DISTINCTROW Customers.CompanyName FROM Customers INNER JOIN Orders ON Customers.CustomerID = Orders.CustomerID " +
                "WHERE Customers.Country = 'UK' ORDER BY Customers.CompanyName")]
    [InlineData("SELECT DISTINCTROW TOP 3 Customers.CompanyName FROM Customers INNER JOIN Orders ON Customers.CustomerID = Orders.CustomerID " +
                "WHERE Customers.Country = 'UK' ORDER BY Customers.CompanyName")]
    [InlineData("SELECT DISTINCTROW TOP 50 PERCENT Customers.CompanyName FROM Customers INNER JOIN Orders ON Customers.CustomerID = Orders.CustomerID " +
                "WHERE Customers.Country = 'UK' ORDER BY Customers.CompanyName")]
    public void A_star_or_distinctrow_query_is_stored_as_ace_stores_it_and_runs_alike(string body) =>
        QueryRoundTrip($"CREATE PROCEDURE [Q] AS {body}", parameter: null);

    /// <summary>A SELECT's GROUP BY, HAVING and ORDER BY are stored as ACE stores them, and the query returns the same
    /// rows in the same order from both engines — an ORDER BY on an aggregate, and a TOP it chooses the rows of,
    /// included.</summary>
    [Theory]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT Country, Count(*) AS N FROM Customers GROUP BY Country HAVING Count(*) > 5 ORDER BY Country DESC")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT TOP 3 Country, Count(*) AS N FROM Customers GROUP BY Country ORDER BY Count(*) DESC, Country")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT Customers.Country, Sum(Orders.Freight) AS F FROM Customers INNER JOIN Orders " +
                "ON Customers.CustomerID = Orders.CustomerID WHERE Orders.OrderID < 10300 GROUP BY Customers.Country " +
                "HAVING Sum(Orders.Freight) > 100 ORDER BY Sum(Orders.Freight) DESC")]
    [InlineData("CREATE VIEW [Q] AS SELECT Country, Count(*) AS N FROM Customers GROUP BY Country HAVING Count(*) > 5")]
    public void A_grouped_or_ordered_query_is_stored_as_ace_stores_it_and_runs_alike(string sql) =>
        QueryRoundTrip(sql, parameter: null);

    private static void QueryRoundTrip(string sql, short? parameter)
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

            var aceRows = new List<string>();
            using (var connection = AceTestDatabase.Open(ourPath, readOnly: true))
            {
                using var run = connection.CreateCommand();
                run.CommandText = "Q";
                run.CommandType = CommandType.StoredProcedure;
                if (parameter is not null)
                    run.Parameters.Add(new OleDbParameter { OleDbType = OleDbType.SmallInt, Value = parameter });
                using var reader = run.ExecuteReader();
                aceRows.Add(string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)));
                var values = new object[reader.FieldCount];
                while (reader.Read())
                {
                    reader.GetValues(values);
                    aceRows.Add(Cells(values));
                }
            }
            OleDbConnection.ReleaseObjectPool();

            foreach (string path in new[] { acePath, ourPath })
            {
                using var db = TemporaryDatabase.OpenTracked(path, readOnly: true);
                StoredQuery stored = db.Catalog.FindQuery("Q")!;
                Assert.False(stored.IsAction);
                ResultSet result = new QueryEngine(db).ExecuteQuery(parameter is null ? "EXECUTE [Q]" : $"EXECUTE [Q] {parameter}");
                Assert.Equal(aceRows, [string.Join(", ", result.ColumnNames), .. result.Rows.Select(r => Cells(r))]);
            }
        }
        finally
        {
            TemporaryDatabase.Delete(ourPath);
            TemporaryDatabase.Delete(acePath);
        }
    }

    /// <summary>A row's values as text, a number as its value whatever its type — ACE's Currency carries four places,
    /// and a Count may come back as either engine's integer type.</summary>
    private static string Cells(object?[] values) => string.Join(" | ", values.Select(v => v switch
    {
        null or DBNull => "NULL",
        decimal or double or float or int or short or long or byte =>
            Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture).ToString("0.############", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
    }));

    /// <summary>A query whose FROM or make-table names another file with IN externaldatabase is stored as ACE stores
    /// it — the FROM's file in a <c>0x04</c> row, a make-table's on its operation row — in each of the forms ACE takes.
    /// ACE runs the query LibRed stored, and a SELECT returns the same rows from both engines on both files.</summary>
    [Theory]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT ShipperID, CompanyName FROM Shippers IN '{0}'")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT * FROM Shippers IN '{0}'")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT Shippers.ShipperID FROM Shippers IN '' [;DATABASE={0}] WHERE ShipperID > 1")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT CompanyName FROM Shippers IN '{0}' 'MS Access;'")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT CompanyName FROM Shippers IN '{0}' [MS Access;]")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT * FROM (SELECT * FROM Shippers IN '{0}') AS q")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT Orders.OrderID, Customers.Country FROM Orders INNER JOIN Customers " +
                "ON Orders.CustomerID = Customers.CustomerID IN '{0}' WHERE Orders.OrderID < 10255")]
    [InlineData("CREATE VIEW [Q] AS SELECT ShipperID, CompanyName FROM Shippers IN '{0}'")]
    [InlineData("CREATE PROCEDURE [Q] AS TRANSFORM Count(OrderID) SELECT ShipVia FROM Orders IN '{0}' GROUP BY ShipVia PIVOT EmployeeID")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT ShipperID INTO ShipCopy IN '{0}' FROM Shippers")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT ShipperID INTO ShipCopy FROM Shippers IN '{0}'")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT Shippers.ShipperID INTO ShipCopy IN '' [;DATABASE={0}] FROM Shippers IN '{0}' WHERE ShipperID > 0")]
    [InlineData("CREATE PROCEDURE [Q] AS SELECT ShipperID INTO ShipCopy IN '{0}' 'MS Access;' FROM Shippers")]
    [InlineData("CREATE PROCEDURE [Q] AS INSERT INTO Shippers (CompanyName, Phone) IN '{0}' SELECT CompanyName, Phone FROM Suppliers WHERE SupplierID < 3")]
    [InlineData("CREATE PROCEDURE [Q] AS INSERT INTO Shippers IN '{0}' SELECT CompanyName, Phone FROM Suppliers WHERE SupplierID < 3")]
    [InlineData("CREATE PROCEDURE [Q] AS INSERT INTO Shippers IN '' [;DATABASE={0}] SELECT CompanyName FROM Suppliers WHERE SupplierID < 3")]
    [InlineData("CREATE PROCEDURE [Q] AS INSERT INTO Shippers IN '{0}' 'MS Access;' SELECT TOP 1 CompanyName FROM Suppliers ORDER BY SupplierID DESC")]
    [InlineData("CREATE PROCEDURE [Q] AS INSERT INTO Shippers IN '{0}' [MS Access;] SELECT CompanyName FROM Shippers IN '{0}' WHERE ShipperID = 1")]
    [InlineData("CREATE PROCEDURE [Q] AS INSERT INTO Shippers (CompanyName) IN '{0}' VALUES ('new')")]
    [InlineData("CREATE PROCEDURE [Q] AS INSERT INTO Shippers IN '{0}' VALUES (50, 'Fifty', '555')")]
    public void An_in_externaldatabase_is_stored_as_ace_stores_it_and_runs_alike(string template)
    {
        string external = Copy(), ourPath = Copy(), acePath = Copy(), runPath = Copy(), runExternal = Copy();
        try
        {
            // The other file's shippers are told apart from the statement's own.
            foreach (string path in new[] { external, runExternal })
                using (var connection = AceTestDatabase.Open(path))
                {
                    using var mark = connection.CreateCommand();
                    mark.CommandText = "UPDATE Shippers SET CompanyName = 'ext ' & CompanyName";
                    mark.ExecuteNonQuery();
                }
            OleDbConnection.ReleaseObjectPool();
            string sql = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, external);

            using (var db = TemporaryDatabase.OpenTracked(ourPath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(sql);
            using (var connection = AceTestDatabase.Open(acePath))
            {
                using var create = connection.CreateCommand();
                create.CommandText = sql;
                try { create.ExecuteNonQuery(); }
                catch (OleDbException e) when (e.Message.StartsWith("Operation is not supported", StringComparison.Ordinal))
                {
                    Assert.Skip("ACE refuses remote tables here: set AllowQueryRemoteTables to 1 to run this test.");
                }
            }

            Assert.Equal(QueryRows(acePath, "Q"), QueryRows(ourPath, "Q"));
            Assert.Equal(ObjectFlags(acePath, "Q"), ObjectFlags(ourPath, "Q"));

            // A make-table or an append writes rather than returns rows.
            bool action = sql.Contains(" INTO ", StringComparison.Ordinal);
            using (var db = TemporaryDatabase.OpenTracked(ourPath, readOnly: true))
                Assert.Contains(" IN '", db.Catalog.FindQuery("Q")!.Sql, StringComparison.Ordinal);

            // ACE runs the query LibRed stored, on a copy pointed at a copy of the other file, so a make-table or an
            // append leaves both files the comparison reads alone.
            File.Copy(ourPath, runPath, overwrite: true);
            using (var db = TemporaryDatabase.OpenTracked(runPath, readOnly: false))
            {
                db.DropQueryObject("Q");
                new QueryEngine(db).ExecuteNonQuery(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, runExternal));
            }
            var aceRows = new List<string>();
            using (var connection = AceTestDatabase.Open(runPath))
            {
                using var run = connection.CreateCommand();
                run.CommandText = "Q";
                run.CommandType = CommandType.StoredProcedure;
                if (action) run.ExecuteNonQuery();
                else
                {
                    using var reader = run.ExecuteReader();
                    var values = new object[reader.FieldCount];
                    while (reader.Read())
                    {
                        reader.GetValues(values);
                        aceRows.Add(Cells(values));
                    }
                }
            }
            OleDbConnection.ReleaseObjectPool();
            if (action) return;

            aceRows.Sort(StringComparer.Ordinal);
            foreach (string path in new[] { acePath, ourPath })
            {
                using var db = TemporaryDatabase.OpenTracked(path, readOnly: true);
                List<string> ours = [.. new QueryEngine(db).ExecuteQuery("EXECUTE [Q]").Rows.Select(r => Cells(r))];
                ours.Sort(StringComparer.Ordinal);
                Assert.Equal(aceRows, ours);
            }
        }
        finally
        {
            foreach (string path in new[] { external, ourPath, acePath, runPath, runExternal })
                TemporaryDatabase.Delete(path);
        }
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
        TableDefinition queries = db.Catalog.FindTable("MSysQueries")!;
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
        TableDefinition objects = db.Catalog.FindTable("MSysObjects")!;
        int flagsIndex = Index(objects, "Flags");
        int idIndex = Index(objects, "Id");
        int id = QueryObjectId(db, queryName);
        return (int)db.OpenTable("MSysObjects").Rows().Single(row => Equals(row[idIndex], id))[flagsIndex]!;
    }

    private static int QueryObjectId(JetDatabase db, string queryName)
    {
        TableDefinition objects = db.Catalog.FindTable("MSysObjects")!;
        int idIndex = Index(objects, "Id"), nameIndex = Index(objects, "Name");
        return (int)db.OpenTable("MSysObjects").Rows()
            .Single(row => string.Equals(row[nameIndex] as string, queryName, StringComparison.OrdinalIgnoreCase))[idIndex]!;
    }

    private static int Index(TableDefinition table, string column) => table.FindColumn(column)!.Index;
}