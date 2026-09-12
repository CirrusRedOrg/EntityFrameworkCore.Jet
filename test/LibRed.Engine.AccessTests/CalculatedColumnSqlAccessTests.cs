using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

// The SQL surface for calculated columns, which is LibRed's OWN: Access SQL has no syntax for one, and ACE
// can only create them through DAO's object model. So none of this SQL will run against ACE — but the FILE
// it produces has to, and that is what these tests check.
//
// The type is required, unlike SQL Server's computed column, because the declared type IS the result type
// and decides how the cached value is encoded on disk (docs/format/page-02e-calculated-columns.md).
[Collection(AceCollection.Name)]
public class CalculatedColumnSqlAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Fact]
    public void Creates_and_adds_calculated_columns_ace_can_read()
    {
        string path = TemporaryDatabase.CreatePath("calc-sql-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using (var database = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(database);
                engine.ExecuteNonQuery(
                    "CREATE TABLE T (Id LONG, Qty LONG, Doubled LONG AS ([Qty]*2), "
                    + "Label TEXT(40) AS (\"n=\" & [Qty]))");
                engine.ExecuteNonQuery("INSERT INTO T (Id, Qty) VALUES (1, 7)");
                engine.ExecuteNonQuery("ALTER TABLE T ADD COLUMN Tripled LONG AS ([Qty]*3)");
                engine.ExecuteNonQuery("INSERT INTO T (Id, Qty) VALUES (2, 5)");
            }

            using var check = AceTestDatabase.Open(path);
            using var select = check.CreateCommand();
            select.CommandText = "SELECT Id, Doubled, Label, Tripled FROM T ORDER BY Id";
            using var reader = select.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read())
                rows.Add(string.Join(" | ", Enumerable.Range(0, 4)
                    .Select(i => reader.IsDBNull(i) ? "(null)" : Convert.ToString(reader.GetValue(i)))));
            foreach (string row in rows) output.WriteLine($"  {row}");

            // Row 1 predates the ADD COLUMN, so its Tripled is NULL — an added column reads as null on
            // existing rows whether it is calculated or not, because no row was rewritten.
            Assert.Equal(["1 | 14 | n=7 | (null)", "2 | 10 | n=5 | 15"], rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The exact shape LibRedMigrationsSqlGenerator emits, backticks and all: EF quotes identifiers that way
    // and passes a HasComputedColumnSql string through verbatim, so both quote styles reach the expression.
    // Backticks are translated to brackets on the way in, because ACE's expression service does not know
    // them -- measured, it reads `Qty` as a field literally named "`Qty`" and cannot find it.
    [Fact]
    public void Accepts_the_sql_ef_core_emits_for_a_computed_column()
    {
        string path = TemporaryDatabase.CreatePath("calc-efsql-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using (var database = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(database);
                engine.ExecuteNonQuery("""
                    CREATE TABLE `People` (
                        `Id` counter NOT NULL,
                        `Sum` varchar(255) AS (`X` + `Y`),
                        `X` integer NOT NULL,
                        `Y` integer NOT NULL,
                        CONSTRAINT `PK_People` PRIMARY KEY (`Id`)
                    )
                    """);
                engine.ExecuteNonQuery("INSERT INTO `People` (`X`, `Y`) VALUES (2, 3)");

                // Stored in the bracket form ACE can evaluate, not as written.
                Assert.Equal("[X] + [Y]",
                    database.OpenTable("People").Definition.FindColumn("Sum")!.CalculatedExpression);
            }

            // A forward reference works too: Sum is declared before the columns it reads.
            using var check = AceTestDatabase.Open(path);
            using var select = check.CreateCommand();
            select.CommandText = "SELECT `Sum` FROM `People`";
            output.WriteLine($"  ACE reads: {select.ExecuteScalar()}");
            Assert.Equal("5", Convert.ToString(select.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A CHECK constraint is expression text in LvProp too, evaluated by the same expression service — which
    // does not know SQL's backtick quoting. ACE's DDL parser accepts `CHECK (`Qty` > 0)` and stores it, and
    // then every INSERT fails with "Could not find field '`Qty`'", so the table is created already unusable.
    // LibRed normalises the quoting on the way into the blob, which is what keeps ACE able to write.
    [Fact]
    public void Normalises_identifier_quoting_in_a_check_constraint()
    {
        string path = TemporaryDatabase.CreatePath("check-tick-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using (var database = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(database).ExecuteNonQuery(
                    "CREATE TABLE `T` (`Id` integer, `Qty` integer, CONSTRAINT `ck` CHECK (`Qty` > 0))");

            using (var database = JetDatabase.Open(path, readOnly: true))
            {
                (string Name, string Expression) check =
                    Assert.Single(database.Catalog.FindTable("T")!.CheckConstraints);
                output.WriteLine($"  stored: {check.Name} -> {check.Expression}");
                Assert.Equal("[Qty] > 0", check.Expression);
            }

            // The engine that has to evaluate it gets the last word: ACE writes a passing row and rejects a
            // failing one, which it could not do at all if the constraint still named "`Qty`".
            using var connection = AceTestDatabase.Open(path);
            using (var ok = connection.CreateCommand())
            {
                ok.CommandText = "INSERT INTO T (Id, Qty) VALUES (1, 5)";
                Assert.Equal(1, ok.ExecuteNonQuery());
            }
            using (var bad = connection.CreateCommand())
            {
                bad.CommandText = "INSERT INTO T (Id, Qty) VALUES (2, -1)";
                Exception violation = Assert.ThrowsAny<Exception>(() => bad.ExecuteNonQuery());
                output.WriteLine($"  ACE rejects -1: {violation.Message.Trim()}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // An index over a calculated column is refused on every route. This is not a divergence from Access: its
    // designer will not offer a calculated column in the Indexes dialog and will not let it be the primary
    // key, and the storage engine refuses every INSERT into a table where one is indexed. ACE's SQL layer is
    // the only one that accepts it, and what it builds is a table no row can ever be written to.
    [Theory]
    [InlineData("CREATE INDEX ix ON T (C)")]
    [InlineData("CREATE UNIQUE INDEX ix ON T (C)")]
    [InlineData("CREATE INDEX ix ON T (Qty, C)")]                     // buried in a composite
    public void Refuses_an_index_over_a_calculated_column(string sql)
    {
        string path = TemporaryDatabase.CreatePath("calc-index-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using var database = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(database);
            engine.ExecuteNonQuery("CREATE TABLE T (Id integer, Qty integer, C integer AS ([Qty]*2))");

            var ex = Assert.ThrowsAny<Exception>(() => { engine.ExecuteNonQuery(sql); });
            output.WriteLine($"  {ex.Message}");
            Assert.Contains("calculated column 'C'", ex.Message, StringComparison.Ordinal);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The same refusal on the CREATE TABLE route, where the index arrives as a table-level constraint rather
    // than as an index — the inline `col type PRIMARY KEY` form is already refused at the SQL layer.
    [Theory]
    [InlineData("CREATE TABLE T (Qty integer, C integer AS ([Qty]*2), CONSTRAINT pk PRIMARY KEY (C))")]
    [InlineData("CREATE TABLE T (Qty integer, C integer AS ([Qty]*2), CONSTRAINT u UNIQUE (C))")]
    public void Refuses_a_key_over_a_calculated_column(string sql)
    {
        string path = TemporaryDatabase.CreatePath("calc-key-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using var database = JetDatabase.Open(path, readOnly: false);
            var ex = Assert.ThrowsAny<Exception>(() => { new QueryEngine(database).ExecuteNonQuery(sql); });
            output.WriteLine($"  {ex.Message}");
            Assert.Contains("calculated column 'C'", ex.Message, StringComparison.Ordinal);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Each of these would otherwise produce a column that reads one way in the DDL and behaves another, so
    // they are refused rather than ignored. The key case is measured, not defensive: ACE accepts an index on
    // a calculated column and then refuses every insert into that table.
    [Theory]
    [InlineData("CREATE TABLE T (Qty LONG, C LONG AS ([Qty]*2) PRIMARY KEY)", "cannot be both calculated")]
    [InlineData("CREATE TABLE T (Qty LONG, C LONG AS ([Qty]*2) DEFAULT 1)", "cannot have a DEFAULT")]
    [InlineData("CREATE TABLE T (Qty LONG, C LONG AS ([Qty]*2) NOT NULL)", "cannot be NOT NULL")]
    [InlineData("CREATE TABLE T (Qty LONG, C TEXT(40) AS (\"x\") WITH COMPRESSION)", "WITH COMPRESSION")]
    [InlineData("CREATE TABLE T (Qty LONG, C LONG AS (CInt([Qty])))", "cannot be used")]
    [InlineData("CREATE TABLE T (Qty LONG, C LONG AS ([Missing]*2))", "refers to another table")]
    public void Refuses_a_calculated_column_that_cannot_mean_what_it_says(string sql, string expected)
    {
        string path = TemporaryDatabase.CreatePath("calc-sqlbad-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using var database = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(database);
            var ex = Assert.ThrowsAny<Exception>(() => { engine.ExecuteNonQuery(sql); });
            output.WriteLine($"  {ex.Message}");
            Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
