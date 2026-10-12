using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// An index key has to be built from the value its row stores. The two used to convert separately, and wherever
// the conversions differed the index named a value the row did not hold: a Boolean written to a numeric column
// was stored as Jet's -1 but keyed as .NET's +1, and a short value in a fixed BINARY column was stored padded
// but keyed unpadded. Each test here reads through the index (an equality seek) as well as through a scan, so a
// key that disagrees with its row shows up as the two disagreeing. IndexKeyRowAgreementAccessTests holds ACE's
// side of the same writes.
public class IndexKeyRowAgreementTests : TempDatabaseTest
{
    private static QueryEngine Fresh(string ddl, string index = "CREATE INDEX IX ON T (S)")
    {
        string path = TemporaryDatabase.CreatePath("key-row-");
        JetDatabase.Create(path);
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery(ddl);
        engine.ExecuteNonQuery(index);
        return engine;
    }

    private static int Count(QueryEngine engine, string where) =>
        Convert.ToInt32(engine.ExecuteQuery($"SELECT COUNT(*) FROM T WHERE {where}").Rows.Single()[0]);

    [Theory]
    [InlineData("SHORT")]
    [InlineData("LONG")]
    [InlineData("DOUBLE")]
    [InlineData("CURRENCY")]
    [InlineData("DECIMAL(10,2)")]
    public void True_in_an_indexed_numeric_column_is_keyed_as_the_minus_one_it_stores(string type)
    {
        QueryEngine engine = Fresh($"CREATE TABLE T (ID COUNTER PRIMARY KEY, S {type})");
        engine.ExecuteNonQuery("INSERT INTO T (S) VALUES (True)");

        Assert.Equal(1, Count(engine, "S = -1"));       // the index
        Assert.Equal(1, Count(engine, "S + 0 = -1"));   // a scan
        Assert.Equal(0, Count(engine, "S = 1"));
    }

    [Fact]
    public void A_row_updated_to_True_can_be_updated_and_deleted_again()
    {
        QueryEngine engine = Fresh("CREATE TABLE T (ID COUNTER PRIMARY KEY, S SHORT)");
        engine.ExecuteNonQuery("INSERT INTO T (S) VALUES (5)");
        engine.ExecuteNonQuery("UPDATE T SET S = True");
        Assert.Equal(1, Count(engine, "S = -1"));

        // Moving the entry looks up the old key, which is the one the row holds: a key written as +1 is not found.
        engine.ExecuteNonQuery("UPDATE T SET S = 7");
        Assert.Equal(1, Count(engine, "S = 7"));
        engine.ExecuteNonQuery("DELETE FROM T WHERE S = 7");
        Assert.Equal(0, Count(engine, "S + 0 = 7"));
    }

    [Fact]
    public void A_fixed_binary_value_is_keyed_padded_as_it_is_stored()
    {
        QueryEngine engine = Fresh(
            "CREATE TABLE T (ID COUNTER PRIMARY KEY, S BINARY(4))", "CREATE UNIQUE INDEX IX ON T (S)");
        engine.ExecuteNonQuery("INSERT INTO T (S) VALUES (0x0102)");

        // The row holds 0x01020000, so the same bytes written out in full are a duplicate (ACE refuses them too).
        Assert.Throws<ConstraintViolationException>(
            () => engine.ExecuteNonQuery("INSERT INTO T (S) VALUES (0x01020000)"));
        Assert.Equal(1, Count(engine, "S = 0x01020000"));
    }

    [Fact]
    public void A_number_in_an_indexed_text_column_is_keyed_as_the_text_it_stores()
    {
        QueryEngine engine = Fresh("CREATE TABLE T (ID COUNTER PRIMARY KEY, S TEXT(50))");
        engine.ExecuteNonQuery("INSERT INTO T (S) VALUES (5)");   // ACE stores '5'

        Assert.Equal(1, Count(engine, "S = '5'"));        // the index
        Assert.Equal(1, Count(engine, "S & '' = '5'"));   // a scan
    }
}
