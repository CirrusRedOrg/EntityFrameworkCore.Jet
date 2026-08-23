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
// The IN externaldatabase clause is not implemented: creating a table in another file belongs to the
// linked-database subsystem LibRed does not have.
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

        TableDef source = engine.Database.Catalog.Tables.Single(t => t.Name == "SiSrc");
        TableDef made = engine.Database.Catalog.Tables.Single(t => t.Name == "SiNew");

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

    // An expression column is typed from the expression, since there is no source column to copy.
    [Fact]
    public void An_expression_column_is_typed_from_the_expression()
    {
        QueryEngine engine = WithSource();
        engine.ExecuteNonQuery("SELECT Id, Qty * 2 AS Doubled, Label & '!' AS Shout INTO SiNew FROM SiSrc");

        TableDef made = engine.Database.Catalog.Tables.Single(t => t.Name == "SiNew");
        Assert.Equal(["Id", "Doubled", "Shout"], made.Columns.Select(c => c.Name));
        Assert.Equal(JetDataType.Int32, made.Columns[1].Type);
        Assert.Equal(JetDataType.Text, made.Columns[2].Type);
        Assert.Equal(20, Convert.ToInt32(engine.ExecuteQuery("SELECT Doubled FROM SiNew WHERE Id = 1").Rows.Single()[0]));
        Assert.Equal("one!", engine.ExecuteQuery("SELECT Shout FROM SiNew WHERE Id = 1").Rows.Single()[0]);
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
