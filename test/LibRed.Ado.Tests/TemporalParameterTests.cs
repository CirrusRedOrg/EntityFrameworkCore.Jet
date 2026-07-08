using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

// Jet has no native TimeSpan/TimeOnly/DateOnly/DateTimeOffset — LibRedCommand.Normalize converts each parameter
// to the DateTime the engine stores and compares against (a time on the 1899-12-30 epoch, a date at midnight),
// exactly as the literal path does. So the engine stays DateTime-only and `WHERE timeCol = @timeSpanParam`
// matches. Regression for BuiltInDataTypes Can_query_using_any_data_type / Can_query_using_DateDiffHour.
public class TemporalParameterTests
{
    private static LibRedConnection OpenTemp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tpar-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var conn = new LibRedConnection($"Data Source={path}");
        conn.Open();
        return conn;
    }

    private static void Exec(LibRedConnection c, string sql, string? name = null, object? value = null)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (name is not null)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
        cmd.ExecuteNonQuery();
    }

    [Theory]
    [MemberData(nameof(TemporalCases))]
    public void A_temporal_parameter_matches_the_stored_value(object stored, object queried)
    {
        using var conn = OpenTemp();
        Exec(conn, "CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `V` DATETIME)");
        Exec(conn, "INSERT INTO `T` (`Id`, `V`) VALUES (1, @v)", "@v", stored);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT `Id` FROM `T` WHERE `V` = @p";
        var p = cmd.CreateParameter();
        p.ParameterName = "@p";
        p.Value = queried;
        cmd.Parameters.Add(p);

        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());   // stored temporal (as an epoch DateTime) equals the queried temporal
        Assert.Equal(1, reader.GetInt32(0));
    }

    public static IEnumerable<object[]> TemporalCases() =>
    [
        [new TimeSpan(10, 9, 8), new TimeSpan(10, 9, 8)],
        [new TimeSpan(0, 10, 9, 8, 7), new TimeSpan(10, 9, 8)],           // sub-second stripped both ways
        [new TimeOnly(12, 30, 45), new TimeOnly(12, 30, 45)],
        [new DateOnly(2020, 3, 1), new DateOnly(2020, 3, 1)],
    ];
}
