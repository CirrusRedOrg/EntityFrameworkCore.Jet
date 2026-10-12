using System.Globalization;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A value stored into a column of another type is converted as ACE converts it: to text as CStr writes it, and from
// text as CDate reads it — both in the current culture, as ACE follows the system's regional settings. Measured
// against ACE under en-AU: INSERT, ALTER COLUMN and a calculated column's cached result all agree with these. They
// used to convert in the invariant culture with .NET's own formatting, storing 1/3 as '0.3333333333333333', a date
// as '02/01/2020 13:45:00' and '01/02/2020' as 2 January.
public class StoredTextConversionTests : TempDatabaseTest
{
    // The regional settings the ACE measurements were taken under: en-AU as Windows sets it, which differs from the
    // culture data .NET ships (d/M/yyyy, lowercase am/pm) — pinned here so the expected text is the same everywhere.
    private static readonly CultureInfo EnAu = RegionalEnAu();
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static CultureInfo RegionalEnAu()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-AU").Clone();
        culture.DateTimeFormat.ShortDatePattern = "d/MM/yyyy";
        culture.DateTimeFormat.LongTimePattern = "h:mm:ss tt";
        culture.DateTimeFormat.AMDesignator = "AM";
        culture.DateTimeFormat.PMDesignator = "PM";
        return culture;
    }

    private static QueryEngine Fresh(params string[] setup)
    {
        string path = TemporaryDatabase.CreatePath("stored-text-");
        JetDatabase.Create(path);
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        foreach (string statement in setup) engine.ExecuteNonQuery(statement);
        return engine;
    }

    private static void Execute(QueryEngine engine, CultureInfo culture, string sql) =>
        SharedDatabase.Under(culture, () => engine.ExecuteNonQuery(sql));

    private static List<object?> Column(QueryEngine engine, string sql) =>
        [.. engine.ExecuteQuery(sql).Rows.Select(r => r[0])];

    [Theory]
    [InlineData("1/3", "0.333333333333333")]
    [InlineData("CDbl(0.1) + CDbl(0.2)", "0.3")]
    [InlineData("True", "-1")]
    [InlineData("#2020-02-01 13:45:00#", "1/02/2020 1:45:00 PM")]
    [InlineData("#2020-02-01#", "1/02/2020")]
    [InlineData("CCur(2.5)", "2.5")]
    public void A_value_inserted_into_text_is_stored_as_CStr_writes_it(string value, string stored)
    {
        QueryEngine engine = Fresh("CREATE TABLE T (Id COUNTER PRIMARY KEY, S TEXT(100))");
        Execute(engine, EnAu, $"INSERT INTO T (S) VALUES ({value})");
        Assert.Equal([stored], Column(engine, "SELECT S FROM T"));
    }

    [Fact]
    public void Text_inserted_into_a_date_is_read_in_the_regional_order()
    {
        QueryEngine engine = Fresh("CREATE TABLE T (Id COUNTER PRIMARY KEY, D DATETIME)");
        Execute(engine, EnAu, "INSERT INTO T (D) VALUES ('01/02/2020')");
        Execute(engine, EnAu, "INSERT INTO T (D) VALUES ('13/02/2020')");
        Execute(engine, EnUs, "INSERT INTO T (D) VALUES ('01/02/2020')");
        Assert.Equal(
            [new DateTime(2020, 2, 1), new DateTime(2020, 2, 13), new DateTime(2020, 1, 2)],
            Column(engine, "SELECT D FROM T ORDER BY Id"));
    }

    [Fact]
    public void Altering_a_column_to_text_writes_each_value_as_CStr_writes_it()
    {
        QueryEngine engine = Fresh(
            "CREATE TABLE T (Id COUNTER PRIMARY KEY, V DOUBLE, D DATETIME, B BIT)",
            "INSERT INTO T (V, D, B) VALUES (1/3, #2020-02-01 13:45:00#, True)",
            "INSERT INTO T (V, D, B) VALUES (2, #13:45:00#, False)");
        foreach (string column in new[] { "V", "D", "B" })
            Execute(engine, EnAu, $"ALTER TABLE T ALTER COLUMN {column} TEXT(100)");

        var rows = engine.ExecuteQuery("SELECT V, D, B FROM T ORDER BY Id").Rows.ToList();
        Assert.Equal(["0.333333333333333", "1/02/2020 1:45:00 PM", "-1"], rows[0]);
        Assert.Equal(["2", "1:45:00 PM", "0"], rows[1]);
    }

    [Fact]
    public void Altering_text_to_a_date_reads_it_in_the_regional_order()
    {
        QueryEngine engine = Fresh(
            "CREATE TABLE T (Id COUNTER PRIMARY KEY, S TEXT(50))",
            "INSERT INTO T (S) VALUES ('01/02/2020')",
            "INSERT INTO T (S) VALUES ('03/04/2021 10:00')");
        Execute(engine, EnAu, "ALTER TABLE T ALTER COLUMN S DATETIME");
        Assert.Equal(
            [new DateTime(2020, 2, 1), new DateTime(2021, 4, 3, 10, 0, 0)],
            Column(engine, "SELECT S FROM T ORDER BY Id"));
    }

    [Fact]
    public void A_calculated_text_column_caches_its_result_as_CStr_writes_it()
    {
        QueryEngine engine = Fresh(
            "CREATE TABLE T (Id COUNTER PRIMARY KEY, A DOUBLE, D DATETIME, F BIT, "
            + "CA TEXT(255) AS ([A] & ''), CD TEXT(255) AS ([D] & ''), CF TEXT(255) AS ([F] & ''))");
        Execute(engine, EnAu, "INSERT INTO T (A, D, F) VALUES (1/3, #2020-02-01 13:45:00#, True)");

        var row = engine.ExecuteQuery("SELECT CA, CD, CF FROM T").Rows.Single();
        Assert.Equal(["0.333333333333333", "1/02/2020 1:45:00 PM", "-1"], row);
    }
}
