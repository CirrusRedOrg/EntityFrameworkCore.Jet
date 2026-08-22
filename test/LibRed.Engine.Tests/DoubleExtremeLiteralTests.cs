using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// EF Core renders a double literal with 15 significant digits, so double.MinValue becomes
// "-1.79769313486232E+308" — whose magnitude EXCEEDS double.MaxValue (1.7976931348623157E+308), i.e. it is not
// a representable double. .NET's double.Parse clamps such a literal to ±Infinity, and LibRed follows suit, so
// EF's `Math.Xxx(col) > double.MinValue` comparisons (used throughout its math-translation tests) evaluate as
// intended. ACE instead rejects the literal outright ("Syntax error in number"), which is why those tests fail
// on the OLE DB path — NOT because ACE mishandles E+308: ACE round-trips MaxValue/MinValue/Epsilon exactly when
// given a correctly-rounded (17-digit) literal or a parameter.
public class DoubleExtremeLiteralTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "dxl-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE D (Id LONG PRIMARY KEY, V DOUBLE)");
        e.ExecuteNonQuery("INSERT INTO D (Id, V) VALUES (1, 0.5)");
        return e;
    }

    [Fact]
    public void An_out_of_range_double_literal_clamps_to_infinity()
    {
        var e = Fresh();
        Assert.Equal(double.NegativeInfinity, e.ExecuteQuery("SELECT -1.79769313486232E+308 FROM D WHERE Id = 1").Rows.Single()[0]);
        Assert.Equal(double.PositiveInfinity, e.ExecuteQuery("SELECT 1.79769313486232E+308 FROM D WHERE Id = 1").Rows.Single()[0]);
    }

    [Fact]
    public void Comparing_against_efs_min_value_literal_matches_every_finite_row()
    {
        var e = Fresh();
        // EF's `Math.Xxx(col) > double.MinValue` shape: the literal clamps to -Infinity, so every finite row matches.
        Assert.Equal(1, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM D WHERE V > -1.79769313486232E+308").Rows.Single()[0]));
    }

    [Fact]
    public void A_correctly_rounded_double_extreme_round_trips_exactly()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO D (Id, V) VALUES (2, -1.7976931348623157E+308)");
        e.ExecuteNonQuery("INSERT INTO D (Id, V) VALUES (3, 1.7976931348623157E+308)");
        Assert.Equal(double.MinValue, e.ExecuteQuery("SELECT V FROM D WHERE Id = 2").Rows.Single()[0]);
        Assert.Equal(double.MaxValue, e.ExecuteQuery("SELECT V FROM D WHERE Id = 3").Rows.Single()[0]);
    }
}
