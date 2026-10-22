using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using LibRed.Engine.Execution;
using Xunit;

namespace LibRed.Engine.Tests;

// Access's make-table query:
//   SELECT field1[, field2[, …]] INTO newtable [IN externaldatabase] FROM source
//
// Every expectation here was measured from ACE first (SelectIntoShapeProbeTest), because most of them could
// reasonably have gone the other way: a make-table copies DATA AND COLUMN TYPES ONLY — no primary key, no
// indexes — which is easy to assume otherwise when the operation is described as copying a table.
//
// The IN externaldatabase clause, which makes the table in another file, is ExternalDatabaseTests'.
public class SelectIntoTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "selinto-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    private static QueryEngine WithSource()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE SiSrc (Id LONG PRIMARY KEY, Label TEXT(30), Qty LONG)");
        engine.ExecuteNonQuery("CREATE INDEX IX_SiSrc_Label ON SiSrc (Label)");
        engine.ExecuteNonQuery("INSERT INTO SiSrc (Id, Label, Qty) VALUES (1, 'one', 10)");
        engine.ExecuteNonQuery("INSERT INTO SiSrc (Id, Label, Qty) VALUES (2, 'two', 20)");
        return engine;
    }

    [Fact]
    public void Creates_the_table_and_copies_the_rows()
    {
        QueryEngine engine = WithSource();

        int affected = engine.ExecuteNonQuery("SELECT Id, Label INTO SiNew FROM SiSrc");

        Assert.Equal(2, affected);
        Assert.Equal(2, Convert.ToInt32(engine.ExecuteQuery("SELECT COUNT(*) FROM SiNew").Rows.Single()[0]));
        Assert.Equal("two", engine.ExecuteQuery("SELECT Label FROM SiNew WHERE Id = 2").Rows.Single()[0]);
    }

    // The finding most worth pinning: a make-table copies data and types, NOT the key or the indexes. An
    // archive of a keyed table comes back unkeyed.
    [Fact]
    public void Does_not_copy_the_primary_key_or_indexes()
    {
        QueryEngine engine = WithSource();
        engine.ExecuteNonQuery("SELECT * INTO SiNew FROM SiSrc");

        TableDefinition source = engine.Database.Catalog.Tables.Single(t => t.Name == "SiSrc");
        TableDefinition made = engine.Database.Catalog.Tables.Single(t => t.Name == "SiNew");

        Assert.NotEmpty(source.Indexes);                       // the source has a PK and an index
        Assert.Empty(made.Indexes);                            // the copy has neither
        Assert.Equal(
            source.Columns.Select(c => c.Name),
            made.Columns.Select(c => c.Name));                 // the columns do come across
    }

    [Fact]
    public void Applies_the_where_clause()
    {
        QueryEngine engine = WithSource();

        int affected = engine.ExecuteNonQuery("SELECT Id, Label INTO SiNew FROM SiSrc WHERE Id = 2");

        Assert.Equal(1, affected);
        Assert.Equal(2, Convert.ToInt32(engine.ExecuteQuery("SELECT Id FROM SiNew").Rows.Single()[0]));
    }

    // An empty result still creates the table — measured, and the opposite is just as plausible.
    [Fact]
    public void An_empty_result_still_creates_the_table()
    {
        QueryEngine engine = WithSource();

        int affected = engine.ExecuteNonQuery("SELECT Id, Label INTO SiNew FROM SiSrc WHERE Id > 99");

        Assert.Equal(0, affected);
        Assert.Contains(engine.Database.Catalog.Tables, t => t.Name == "SiNew");
        Assert.Equal(0, Convert.ToInt32(engine.ExecuteQuery("SELECT COUNT(*) FROM SiNew").Rows.Single()[0]));
    }

    // "If newtable is the same as the name of an existing table, a trappable error occurs" — ACE reports
    // "Table 'X' already exists".
    [Fact]
    public void An_existing_target_is_an_error()
    {
        QueryEngine engine = WithSource();
        engine.ExecuteNonQuery("SELECT Id INTO SiNew FROM SiSrc");

        var error = Assert.Throws<SchemaObjectExistsException>(
            () => engine.ExecuteNonQuery("SELECT Id INTO SiNew FROM SiSrc"));
        Assert.Contains("already exists", error.Message);
        Assert.Equal("SiNew", error.ObjectName);
    }

    // A query's name is taken too: tables and queries share the Tables container's name space.
    [Fact]
    public void A_query_named_like_the_target_is_an_error()
    {
        QueryEngine engine = WithSource();
        engine.ExecuteNonQuery("CREATE VIEW SiView AS SELECT Id FROM SiSrc");

        var error = Assert.Throws<SchemaObjectExistsException>(
            () => engine.ExecuteNonQuery("SELECT Id INTO SiView FROM SiSrc"));
        Assert.Equal("Table 'SiView' already exists.", error.Message);
    }

    // An expression column is typed from the expression, since there is no source column to copy.
    [Fact]
    public void An_expression_column_is_typed_from_the_expression()
    {
        QueryEngine engine = WithSource();
        engine.ExecuteNonQuery("SELECT Id, Qty * 2 AS Doubled, Label & '!' AS Shout INTO SiNew FROM SiSrc");

        TableDefinition made = engine.Database.Catalog.Tables.Single(t => t.Name == "SiNew");
        Assert.Equal(["Id", "Doubled", "Shout"], made.Columns.Select(c => c.Name));
        Assert.Equal(JetDataType.Int32, made.Columns[1].Type);
        Assert.Equal(JetDataType.Text, made.Columns[2].Type);
        Assert.Equal(20, Convert.ToInt32(engine.ExecuteQuery("SELECT Doubled FROM SiNew WHERE Id = 1").Rows.Single()[0]));
        Assert.Equal("one!", engine.ExecuteQuery("SELECT Shout FROM SiNew WHERE Id = 1").Rows.Single()[0]);
    }

    private static QueryEngine WithNumbers()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE T (C CURRENCY, D DECIMAL(10,2), F DOUBLE, S REAL, L LONG, I SHORT, B BYTE, Y BIT, "
            + "E DECIMAL(18,4), G DECIMAL(4,1), H DECIMAL(28,6), N BIGINT, X TEXT(10), W DATETIME, K DECIMAL(20,10))");
        engine.ExecuteNonQuery("INSERT INTO T (C, D, F, S, L, I, B, Y, E, G, H, N, X, W, K) "
            + "VALUES (1.1, 12.34, 0.1, 0.1, 3, 2, 4, TRUE, 1.2345, 1.5, 1.123456, 7, '5', #2020-01-02#, 1.5)");
        return engine;
    }

    // A computed Decimal is a variable DECIMAL of the result's precision and scale, its length the bytes the largest
    // number of that precision takes and a sign byte (verified vs ACE on this table,
    // AceDecimalPrecisionProbeTest.Make_table_column_type_probe).
    [Theory]
    [InlineData("D * 2", 12, 2, 6, "24.68")]
    [InlineData("E * D", 18, 4, 9, "15.2337")]
    [InlineData("G * D", 10, 2, 6, "18.51")]
    [InlineData("G * I", 6, 1, 4, "3.0")]
    [InlineData("G * B", 11, 1, 6, "6.0")]
    [InlineData("G * G * G", 4, 1, 3, "3.3")]
    [InlineData("G * K", 20, 10, 10, "2.2500000000")]
    [InlineData("K + E", 24, 10, 11, "2.7345000000")]
    [InlineData("G * 0.5", 28, 1, 13, "0.7")]
    [InlineData("D + C", 28, 4, 13, "13.4400")]
    [InlineData("SUM(G)", 28, 1, 13, "1.5")]
    public void A_computed_decimal_is_a_decimal_of_its_precision_and_scale(
        string expression, int precision, int scale, int length, string value)
    {
        QueryEngine engine = WithNumbers();
        engine.ExecuteNonQuery($"SELECT {expression} AS R INTO M FROM T");

        ColumnDef made = engine.Database.Catalog.FindTable("M")!.FindColumn("R")!;
        Assert.Equal(JetDataType.FixedPoint, made.Type);
        Assert.False(made.IsFixedLength);
        Assert.Equal((precision, scale, length), (made.Precision, made.Scale, made.Length));
        Assert.Equal(value, Assert.IsType<decimal>(engine.ExecuteQuery("SELECT R FROM M").Rows.Single()[0])
            .ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // Verified vs ACE on this table, as above: past 28 digits a Double, a Currency stays one, and a BigInt is one.
    [Theory]
    [InlineData("D * 2.5", JetDataType.Double)]
    [InlineData("C * 2", JetDataType.Currency)]
    [InlineData("N * 2", JetDataType.Int64)]
    public void A_computed_number_past_a_decimal_keeps_its_type(string expression, JetDataType type)
    {
        QueryEngine engine = WithNumbers();
        engine.ExecuteNonQuery($"SELECT {expression} AS R INTO M FROM T");
        ColumnDef made = engine.Database.Catalog.FindTable("M")!.FindColumn("R")!;
        Assert.Equal((type, 8), (made.Type, made.Length));
    }

    // Not ACE's, which makes it a Double: LibRed's Round keeps its operand's type, so the table holds what the SELECT
    // returns.
    [Fact]
    public void A_round_of_a_decimal_is_a_decimal_of_its_operand()
    {
        QueryEngine engine = WithNumbers();
        engine.ExecuteNonQuery("SELECT ROUND(D, 1) AS R INTO M FROM T");
        ColumnDef made = engine.Database.Catalog.FindTable("M")!.FindColumn("R")!;
        Assert.Equal((JetDataType.FixedPoint, (byte)10, (byte)2), (made.Type, made.Precision, made.Scale));
        Assert.Equal(12.30m, engine.ExecuteQuery("SELECT R FROM M").Rows.Single()[0]);
    }

    // After a Decimal, every later column but a text one carries that Decimal's precision and scale as two words in
    // the bytes a collation takes — read back as collating order 12, sort id 2 after a DECIMAL(12,2). A text column
    // keeps its own and does not stop the carrying, and the next statement starts afresh (verified vs ACE on this
    // table, AceDecimalPrecisionProbeTest.Make_table_descriptor_union_probe).
    [Fact]
    public void Columns_after_a_decimal_carry_its_precision_and_scale()
    {
        QueryEngine engine = WithNumbers();
        engine.ExecuteNonQuery("SELECT D * 2 AS A, F * 2 AS B, L * 2 AS C, X & '' AS S, F * 3 AS E, W + 1 AS H, Y AS I, "
            + "C * 2 AS J, N * 2 AS K INTO M1 FROM T");
        engine.ExecuteNonQuery("SELECT F * 2 AS B INTO M2 FROM T");
        engine.ExecuteNonQuery("SELECT F * 2 AS B, G * 2 AS A, F * 3 AS E INTO M4 FROM T");

        Collation database = engine.Database.Collation;
        Collation Of(string table, string column) => engine.Database.Catalog.FindTable(table)!.FindColumn(column)!.Collation;
        var afterDecimal12x2 = new Collation((CollatingOrder)12, Version: 0, SortId: 2);
        foreach (string column in (string[])["B", "C", "E", "H", "I", "J", "K"])
            Assert.Equal(afterDecimal12x2, Of("M1", column));
        Assert.Equal(database, Of("M1", "S"));
        Assert.Equal(database, Of("M2", "B"));
        Assert.Equal(database, Of("M4", "B"));
        Assert.Equal(new Collation((CollatingOrder)11, Version: 0, SortId: 1), Of("M4", "E"));
    }

    [Fact]
    public void An_aggregate_can_be_made_into_a_table()
    {
        QueryEngine engine = WithSource();
        engine.ExecuteNonQuery("SELECT COUNT(*) AS N, SUM(Qty) AS Total INTO SiNew FROM SiSrc");

        object?[] row = engine.ExecuteQuery("SELECT N, Total FROM SiNew").Rows.Single();
        Assert.Equal(2, Convert.ToInt32(row[0]));
        Assert.Equal(30, Convert.ToInt32(row[1]));
    }

    // A make-table returns no rows: it is an action query, and asking for its rows gets an empty result
    // rather than the source's.
    [Fact]
    public void Returns_no_rows_to_the_caller()
    {
        QueryEngine engine = WithSource();

        ResultSet result = engine.ExecuteQuery("SELECT Id, Label INTO SiNew FROM SiSrc");

        Assert.Empty(result.Rows);
        Assert.Equal(2, Convert.ToInt32(engine.ExecuteQuery("SELECT COUNT(*) FROM SiNew").Rows.Single()[0]));
    }
}