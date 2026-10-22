using System.Globalization;
using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using LibRed.Engine.Execution;
using LibRed.Sql.Binding;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

// IN externaldatabase — a FROM's tables, or a make-table's new table, in another Access file. Every expectation is what
// ACE did with the same statement over the same two files: an
// external file holding ExtT (1 e1, 2 e2), Dup (1 ext1, 2 ext2, 3 ext3) and a query ExtQ, and the statement's own
// file holding a Dup of its own (2 main2, 3 main3, 4 main4).
public class ExternalDatabaseTests : TempDatabaseTest
{
    private static string Northwind => Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    private static (QueryEngine Engine, string External) Fresh() => FreshWithPath() is var f ? (f.Engine, f.External) : default;

    private static (QueryEngine Engine, string External, string Path) FreshWithPath()
    {
        string external = TemporaryDatabase.CopyPath(Northwind, "in-ext-");
        using (var db = JetDatabase.Open(external, readOnly: false))
        {
            var setup = new QueryEngine(db);
            setup.ExecuteNonQuery("CREATE TABLE ExtT (Id LONG, Label TEXT(20))");
            setup.ExecuteNonQuery("INSERT INTO ExtT (Id, Label) VALUES (1, 'e1'), (2, 'e2')");
            setup.ExecuteNonQuery("CREATE TABLE Dup (Id LONG, Label TEXT(20))");
            setup.ExecuteNonQuery("INSERT INTO Dup (Id, Label) VALUES (1, 'ext1'), (2, 'ext2'), (3, 'ext3')");
            setup.ExecuteNonQuery("CREATE PROCEDURE ExtQ AS SELECT Id, Label FROM ExtT WHERE Id = 2");
        }

        string path = TemporaryDatabase.CopyPath(Northwind, "in-main-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE Dup (Id LONG, Label TEXT(20))");
        engine.ExecuteNonQuery("INSERT INTO Dup (Id, Label) VALUES (2, 'main2'), (3, 'main3'), (4, 'main4')");
        return (engine, external, path);
    }

    private static List<string> Rows(QueryEngine engine, string sql) =>
        [.. engine.ExecuteQuery(sql).Rows.Select(r => string.Join(" | ",
            r.Select(v => v is null ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture))))];

    [Theory]
    [InlineData("IN '{0}'")]
    [InlineData("IN \"{0}\"")]
    [InlineData("IN '' [;DATABASE={0}]")]
    [InlineData("IN '' [;DATABASE={0};]")]
    [InlineData("IN \"\" [MS Access;DATABASE={0}]")]
    [InlineData("IN '{0}' 'MS Access;'")]
    [InlineData("IN '{0}' [MS Access;]")]
    [InlineData("IN '{0}' [;]")]
    public void Every_form_ace_takes_reads_the_other_file(string clause)
    {
        (QueryEngine engine, string external) = Fresh();
        Assert.Equal(["1 | e1", "2 | e2"], Rows(engine, $"SELECT * FROM ExtT {string.Format(CultureInfo.InvariantCulture, clause, external)}"));
    }

    [Theory]
    [InlineData("SELECT * FROM ExtT IN '{0}' ''")]
    [InlineData("SELECT * FROM ExtT IN {0}")]
    [InlineData("SELECT * FROM ExtT IN '{0}' AS x")]
    [InlineData("SELECT * FROM ExtT IN '{0}', Dup")]
    [InlineData("SELECT * FROM Dup IN '{0}' IN '{0}'")]
    public void A_form_ace_refuses_is_a_syntax_error(string sql)
    {
        (QueryEngine engine, string external) = Fresh();
        Assert.Throws<SqlParseException>(() => engine.ExecuteQuery(string.Format(CultureInfo.InvariantCulture, sql, external)));
    }

    [Fact]
    public void Every_table_of_the_from_is_in_the_other_file()
    {
        (QueryEngine engine, string external) = Fresh();
        Assert.Equal(["1 | e1"], Rows(engine, $"SELECT * FROM ExtT AS x IN '{external}' WHERE Id = 1"));
        Assert.Equal(
            ["1 | e1 | 1 | ext1", "1 | e1 | 2 | ext2", "1 | e1 | 3 | ext3", "2 | e2 | 1 | ext1", "2 | e2 | 2 | ext2", "2 | e2 | 3 | ext3"],
            Rows(engine, $"SELECT * FROM ExtT, Dup IN '{external}'").Order(StringComparer.Ordinal).ToList());
        Assert.Equal(["1 | e1 | 1 | ext1", "2 | e2 | 2 | ext2"],
            Rows(engine, $"SELECT * FROM ExtT INNER JOIN Dup ON ExtT.Id = Dup.Id IN '{external}'"));
        Assert.Equal(["1 | e1 | 1 | ext1", "2 | e2 | 2 | ext2"],
            Rows(engine, $"SELECT * FROM (ExtT INNER JOIN Dup ON ExtT.Id = Dup.Id) IN '{external}'"));
    }

    [Fact]
    public void A_subquery_elsewhere_reads_the_statements_own_file()
    {
        (QueryEngine engine, string external) = Fresh();
        Assert.Equal(["2 | ext2", "3 | ext3"], Rows(engine, $"SELECT * FROM Dup IN '{external}' WHERE Id IN (SELECT Id FROM Dup)"));
        Assert.Equal(["2 | main2", "3 | main3"], Rows(engine, $"SELECT * FROM Dup WHERE Id IN (SELECT Id FROM Dup IN '{external}')"));
        Assert.All(engine.ExecuteQuery($"SELECT (SELECT Max(Id) FROM Dup) AS M FROM Dup IN '{external}'").Rows,
            r => Assert.Equal(4, Convert.ToInt32(r[0], CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void A_derived_table_with_its_own_in_joins_the_statements_tables()
    {
        (QueryEngine engine, string external) = Fresh();
        Assert.Equal(["1 | ext1", "2 | ext2", "3 | ext3"], Rows(engine, $"SELECT * FROM (SELECT * FROM Dup IN '{external}') AS q"));
        Assert.Equal(["2 | main2 | ext2", "3 | main3 | ext3"],
            Rows(engine, $"SELECT Dup.Id, Dup.Label, q.Label FROM Dup INNER JOIN (SELECT * FROM Dup IN '{external}') AS q ON Dup.Id = q.Id"));
    }

    [Fact]
    public void A_query_of_the_other_file_runs_there()
    {
        (QueryEngine engine, string external) = Fresh();
        Assert.Equal(["2 | e2"], Rows(engine, $"SELECT * FROM ExtQ IN '{external}'"));

        // A make-table reads it the same way, run as a query or not.
        Assert.Empty(engine.ExecuteQuery($"SELECT Id, Label INTO FromQ FROM ExtQ IN '{external}'").Rows);
        Assert.Equal(["2 | e2"], Rows(engine, "SELECT * FROM FromQ"));
    }

    [Fact]
    public void A_table_in_the_other_file_is_sought_through_its_own_indexes()
    {
        (QueryEngine engine, string external) = Fresh();
        // Each file's Dup is indexed on a different column, so a seek chosen from the wrong file's catalog, or run
        // against the wrong file, shows up in the plan or in the rows.
        using (var db = JetDatabase.Open(external, readOnly: false))
            new QueryEngine(db).ExecuteNonQuery("CREATE INDEX IX_Dup_Id ON Dup (Id)");
        engine.ExecuteNonQuery("CREATE INDEX IX_Dup_Label ON Dup (Label)");

        string point = $"SELECT Id, Label FROM Dup IN '{external}' WHERE Id = 2";
        Assert.Contains(Nodes(engine.PlanFor(point)), n => n is Plan.IndexSeekNode { Database: not null, Index.Name: "IX_Dup_Id" });
        Assert.Equal(["2 | ext2"], Rows(engine, point));

        string range = $"SELECT Id, Label FROM Dup IN '{external}' WHERE Id >= 2";
        Assert.Contains(Nodes(engine.PlanFor(range)), n => n is Plan.IndexRangeSeekNode { Database: not null, Index.Name: "IX_Dup_Id" });
        Assert.Equal(["2 | ext2", "3 | ext3"], Rows(engine, range));

        string join = $"SELECT * FROM ExtT INNER JOIN Dup ON ExtT.Id = Dup.Id IN '{external}'";
        Assert.Contains(Nodes(engine.PlanFor(join)), n => n is Plan.IndexSeekNode { Database: not null, Index.Name: "IX_Dup_Id" });
        Assert.Equal(["1 | e1 | 1 | ext1", "2 | e2 | 2 | ext2"], Rows(engine, join));

        static IEnumerable<Plan.PlanNode> Nodes(Plan.PlanNode node) => [node, .. node.Children.SelectMany(Nodes)];
    }

    [Fact]
    public void The_rest_of_the_query_runs_over_the_other_files_rows()
    {
        (QueryEngine engine, string external) = Fresh();
        Assert.Equal(["ext3 | 1", "ext2 | 1", "ext1 | 1"],
            Rows(engine, $"SELECT Label, Count(*) AS N FROM Dup IN '{external}' GROUP BY Label HAVING Count(*) > 0 ORDER BY Label DESC"));
        Assert.Equal(["ext1", "ext2"], Rows(engine, $"SELECT DISTINCT TOP 2 Label FROM Dup IN '{external}' ORDER BY Label"));
        Assert.Equal(["1 | ext1", "2 | ext2", "3 | ext3", "2 | main2", "3 | main3", "4 | main4"],
            Rows(engine, $"SELECT * FROM Dup IN '{external}' UNION ALL SELECT * FROM Dup"));
    }

    [Fact]
    public void The_statements_own_file_named_is_itself()
    {
        (QueryEngine engine, _, string self) = FreshWithPath();
        Assert.Equal(["2 | main2", "3 | main3", "4 | main4"], Rows(engine, $"SELECT * FROM Dup IN '{self}'"));
    }

    [Fact]
    public void A_missing_table_file_or_other_database_type_is_refused()
    {
        (QueryEngine engine, string external) = Fresh();
        var missingTable = Assert.Throws<SqlBindException>(() => engine.ExecuteQuery($"SELECT * FROM Nope IN '{external}'"));
        Assert.Equal(SqlBindException.InputNotFound("Nope"), missingTable.Message);

        var missingFile = Assert.Throws<FileNotFoundException>(() => engine.ExecuteQuery(@"SELECT * FROM Dup IN 'C:\nope\nope.accdb'"));
        Assert.Equal(@"'C:\nope\nope.accdb' is not a valid path. Make sure that the path name is spelled correctly and that you are "
            + "connected to the server on which the file resides.", missingFile.Message);

        // ACE also reaches dBASE, Paradox, Excel and text; LibRed reads Access files only.
        Assert.Throws<NotSupportedException>(() => engine.ExecuteQuery($"SELECT * FROM Dup IN '{external}' 'dBASE IV;'"));
    }

    [Fact]
    public void A_make_table_with_in_makes_the_table_in_the_other_file()
    {
        (QueryEngine engine, string external) = Fresh();
        Assert.Equal(3, engine.ExecuteNonQuery($"SELECT Id INTO NewT IN '{external}' FROM Dup"));
        Assert.Equal(3, engine.ExecuteNonQuery($"SELECT Id, Label INTO NewT2 FROM Dup IN '{external}'"));
        Assert.Equal(3, engine.ExecuteNonQuery($"SELECT Id INTO NewT3 IN '{external}' FROM Dup IN '{external}'"));
        Assert.Equal(3, engine.ExecuteNonQuery($"SELECT Id INTO NewT4 IN '' [;DATABASE={external}] FROM Dup"));
        Assert.Equal(3, engine.ExecuteNonQuery($"SELECT Id INTO NewT5 IN '{external}' 'MS Access;' FROM Dup"));

        var taken = Assert.Throws<SchemaObjectExistsException>(() => engine.ExecuteNonQuery($"SELECT Id INTO Dup IN '{external}' FROM Dup"));
        Assert.Equal("Table 'Dup' already exists.", taken.Message);

        Assert.Equal(["2", "3", "4"], Rows(engine, $"SELECT Id FROM NewT IN '{external}'"));
        Assert.Equal(["1 | ext1", "2 | ext2", "3 | ext3"], Rows(engine, "SELECT * FROM NewT2"));
        Assert.Equal(["1", "2", "3"], Rows(engine, $"SELECT Id FROM NewT3 IN '{external}'"));
        Assert.DoesNotContain(engine.Database.Catalog.Tables, t => t.Name is "NewT" or "NewT3" or "NewT4" or "NewT5");

        // Written, and closed out, as any other statement's writes: the file reads back without the engine.
        engine.Database.Dispose();
        using var other = JetDatabase.Open(external);
        Assert.Equal(["Dup", "ExtT", "NewT", "NewT3", "NewT4", "NewT5"],
            other.Catalog.Tables.Where(t => !t.IsSystem && (t.Name.StartsWith("New", StringComparison.Ordinal) || t.Name is "Dup" or "ExtT"))
                .Select(t => t.Name).Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void An_insert_with_in_appends_to_the_other_file_from_this_one()
    {
        (QueryEngine engine, string external) = Fresh();
        // A FROM with an IN of its own reads the other file too — first, before anything is appended to it.
        Assert.Equal(3, engine.ExecuteNonQuery($"INSERT INTO Dup IN '{external}' SELECT Id + 30 AS Id, Label FROM Dup IN '{external}'"));
        Assert.Equal(3, engine.ExecuteNonQuery($"INSERT INTO Dup (Id, Label) IN '{external}' SELECT Id + 20, Label FROM Dup"));
        Assert.Equal(3, engine.ExecuteNonQuery($"INSERT INTO Dup IN '' [;DATABASE={external}] SELECT Id + 40 AS Id, Label FROM Dup"));
        Assert.Equal(3, engine.ExecuteNonQuery($"INSERT INTO Dup IN '{external}' 'MS Access;' SELECT Id + 50 AS Id, Label FROM Dup"));
        // A subquery reads this file, as the FROM does.
        Assert.Equal(3, engine.ExecuteNonQuery($"INSERT INTO Dup IN '{external}' SELECT Id + 60 AS Id, Label FROM Dup WHERE Id IN (SELECT Id FROM Dup)"));
        Assert.Equal(1, engine.ExecuteNonQuery($"INSERT INTO Dup IN '{external}' VALUES (80, 'v')"));
        Assert.Equal(1, engine.ExecuteNonQuery($"INSERT INTO Dup (Id, Label) IN '{external}' VALUES (81, 'v')"));

        Assert.Equal(["2 | main2", "3 | main3", "4 | main4"], Rows(engine, "SELECT * FROM Dup"));
        Assert.Equal(
            ["1 | ext1", "2 | ext2", "22 | main2", "23 | main3", "24 | main4", "3 | ext3", "31 | ext1", "32 | ext2", "33 | ext3",
             "42 | main2", "43 | main3", "44 | main4", "52 | main2", "53 | main3", "54 | main4", "62 | main2", "63 | main3",
             "64 | main4", "80 | v", "81 | v"],
            Rows(engine, $"SELECT * FROM Dup IN '{external}'").Order(StringComparer.Ordinal).ToList());

        // Without a column list the values fill the columns by name, wherever they stand.
        (engine, external) = Fresh();
        Assert.Equal(3, engine.ExecuteNonQuery($"INSERT INTO Dup IN '{external}' SELECT Label, Id FROM Dup"));
        Assert.Equal(["1 | ext1", "2 | ext2", "2 | main2", "3 | ext3", "3 | main3", "4 | main4"],
            Rows(engine, $"SELECT * FROM Dup IN '{external}'").Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void An_insert_with_in_finds_its_target_in_the_other_file_only()
    {
        (QueryEngine engine, string external) = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE OnlyMain (Id LONG, Label TEXT(20))");
        var missing = Assert.Throws<InvalidOperationException>(
            () => engine.ExecuteNonQuery($"INSERT INTO OnlyMain IN '{external}' SELECT Id, Label FROM Dup"));
        Assert.Equal("Could not find output table 'OnlyMain'.", missing.Message);

        // ACE takes the IN after the column list only.
        Assert.Throws<SqlParseException>(() => engine.ExecuteNonQuery($"INSERT INTO Dup IN '{external}' (Id, Label) VALUES (82, 'v')"));
        Assert.Throws<NotSupportedException>(() => engine.ExecuteNonQuery($"INSERT INTO Dup IN '{external}' 'dBASE IV;' SELECT Id, Label FROM Dup"));
        Assert.Equal(["1 | ext1", "2 | ext2", "3 | ext3"], Rows(engine, $"SELECT * FROM Dup IN '{external}'"));
    }

    [Fact]
    public void An_insert_with_in_reports_the_other_files_new_autonumber()
    {
        (QueryEngine engine, string external) = Fresh();
        // The other file's Shippers numbered on past this one's, so the two ids differ.
        using (var db = JetDatabase.Open(external, readOnly: false))
            new QueryEngine(db).ExecuteNonQuery("INSERT INTO Shippers (CompanyName) VALUES ('pad1'), ('pad2')");

        engine.ExecuteNonQuery($"INSERT INTO Shippers (CompanyName) IN '{external}' VALUES ('x')");
        Assert.Equal(6, engine.ExecuteQuery("SELECT @@IDENTITY").Rows.Single()[0]);
        engine.ExecuteNonQuery($"INSERT INTO Shippers (CompanyName) IN '{external}' SELECT 'y'");
        Assert.Equal(7, engine.ExecuteQuery("SELECT @@IDENTITY").Rows.Single()[0]);
    }

    [Fact]
    public void A_stored_insert_keeps_its_in_and_appends_to_the_other_file()
    {
        (QueryEngine engine, string external) = Fresh();
        engine.ExecuteNonQuery($"CREATE PROCEDURE A1 AS INSERT INTO Dup (Id, Label) IN '{external}' SELECT Id + 20, Label FROM Dup");
        engine.ExecuteNonQuery($"CREATE PROCEDURE A2 AS INSERT INTO Dup IN '{external}' VALUES (80, 'v')");
        engine.ExecuteNonQuery($"CREATE PROCEDURE A3 AS INSERT INTO Dup IN '{external}' SELECT TOP 1 Id + 95 AS Id, Label FROM Dup ORDER BY Id DESC");

        Assert.Equal($"INSERT INTO [Dup] ([Id], [Label]) IN '{external}' SELECT Id + 20, Label FROM [Dup]",
            engine.Database.Catalog.FindQuery("A1")!.Sql);
        Assert.Equal($"INSERT INTO [Dup] IN '{external}' VALUES (80, 'v')", engine.Database.Catalog.FindQuery("A2")!.Sql);
        Assert.Equal(3, engine.ExecuteNonQuery("EXECUTE A1"));
        Assert.Equal(1, engine.ExecuteNonQuery("EXECUTE A2"));
        Assert.Equal(1, engine.ExecuteNonQuery("EXECUTE A3"));

        Assert.Equal(["2 | main2", "3 | main3", "4 | main4"], Rows(engine, "SELECT * FROM Dup"));
        Assert.Equal(["1 | ext1", "2 | ext2", "22 | main2", "23 | main3", "24 | main4", "3 | ext3", "80 | v", "99 | main4"],
            Rows(engine, $"SELECT * FROM Dup IN '{external}'").Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void A_stored_query_keeps_its_in_and_runs_against_the_other_file()
    {
        (QueryEngine engine, string external) = Fresh();
        engine.ExecuteNonQuery($"CREATE VIEW V1 AS SELECT Id, Label FROM Dup IN '{external}'");
        engine.ExecuteNonQuery($"CREATE PROCEDURE P2 AS SELECT Dup.Id FROM Dup IN '' [;DATABASE={external}] WHERE Id > 1");
        engine.ExecuteNonQuery($"CREATE PROCEDURE P3 AS SELECT Id INTO NewP IN '{external}' FROM Dup");
        engine.ExecuteNonQuery($"CREATE PROCEDURE P9 AS SELECT Id INTO NewP9 FROM Dup IN '{external}'");
        engine.ExecuteNonQuery($"CREATE PROCEDURE X1 AS TRANSFORM Count(Id) SELECT Label FROM Dup IN '{external}' GROUP BY Label PIVOT Id");

        Assert.Equal($"SELECT Id, Label FROM [Dup] IN '{external}'", engine.Database.Catalog.FindQuery("V1")!.Sql);
        Assert.Equal(["1 | ext1", "2 | ext2", "3 | ext3"], Rows(engine, "SELECT * FROM V1"));
        Assert.Equal(["2", "3"], Rows(engine, "EXECUTE P2"));
        Assert.Equal(3, engine.ExecuteNonQuery("EXECUTE P3"));
        Assert.Equal(["2", "3", "4"], Rows(engine, $"SELECT Id FROM NewP IN '{external}'"));
        Assert.Equal(3, engine.ExecuteNonQuery("EXECUTE P9"));
        Assert.Equal(["1", "2", "3"], Rows(engine, "SELECT Id FROM NewP9"));
        Assert.Equal(["ext1 | 1 | NULL | NULL", "ext2 | NULL | 1 | NULL", "ext3 | NULL | NULL | 1"], Rows(engine, "SELECT * FROM X1"));
    }
}
