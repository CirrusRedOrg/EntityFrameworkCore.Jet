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
