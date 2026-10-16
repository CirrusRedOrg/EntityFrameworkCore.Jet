using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>A value written to a DECIMAL column keeps its fraction, from a parameter or a literal.</summary>
public class DecimalWriteTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "decimal-write-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE Z (Id LONG, V DECIMAL(18,2))");
        return engine;
    }

    [Theory]
    [InlineData("-1234567890.01")]
    [InlineData("4.5")]
    [InlineData("0.01")]
    public void A_parameter_keeps_its_fraction(string text)
    {
        QueryEngine engine = Fresh();
        decimal value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        engine.ExecuteNonQuery("INSERT INTO Z (Id, V) VALUES (1, @p)", new Dictionary<string, object?> { ["@p"] = value });
        Assert.Equal(value, engine.ExecuteQuery("SELECT V FROM Z").Rows.Single()[0]);
        Assert.Equal(value.ToString(System.Globalization.CultureInfo.CurrentCulture).TrimEnd('0').TrimEnd('.'),
            engine.ExecuteQuery("SELECT V & '' FROM Z").Rows.Single()[0]);
    }

    [Fact]
    public void A_literal_keeps_its_fraction()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("INSERT INTO Z (Id, V) VALUES (1, -1234567890.01)");
        Assert.Equal(-1234567890.01m, engine.ExecuteQuery("SELECT V FROM Z").Rows.Single()[0]);
    }

    // A decimal-point literal is a Decimal, so every digit it has reaches a DECIMAL column, and a DOUBLE column
    // takes it as the nearest Double (verified vs ACE on this table, AceDecimalPrecisionProbeTest.Literal_type_probe).
    [Theory]
    [InlineData("-123456789.0123456789", "-123456789.012346")]
    [InlineData("123456789.0123456", "123456789.012346")]
    [InlineData("0.0000000001", "0.0000000001")]
    public void A_literal_keeps_every_digit_a_decimal_column_holds(string literal, string asDouble)
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE L (Id LONG, D DECIMAL(28,10), F DOUBLE)");
        engine.ExecuteNonQuery($"INSERT INTO L (Id, D, F) VALUES (1, {literal}, {literal})");
        Assert.Equal(decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture),
            engine.ExecuteQuery("SELECT D FROM L").Rows.Single()[0]);
        Assert.Equal(asDouble, SharedDatabase.Scalar(engine, "SELECT CStr(F) FROM L", System.Globalization.CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void An_update_keeps_its_fraction()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("INSERT INTO Z (Id, V) VALUES (1, 0)");
        engine.ExecuteNonQuery("UPDATE Z SET V = -1234567890.01");
        Assert.Equal(-1234567890.01m, engine.ExecuteQuery("SELECT V FROM Z").Rows.Single()[0]);
    }

    [Fact]
    public void A_currency_literal_keeps_its_fraction()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE M (Id LONG, V CURRENCY)");
        engine.ExecuteNonQuery("INSERT INTO M (Id, V) VALUES (1, -92233720368.4775)");
        Assert.Equal(-92233720368.4775m, engine.ExecuteQuery("SELECT V FROM M").Rows.Single()[0]);
    }
}
