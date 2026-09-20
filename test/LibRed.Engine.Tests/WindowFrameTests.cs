using System.Globalization;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Explicit window frames — <c>ROWS</c>, <c>RANGE</c> and <c>GROUPS</c>, with <c>BETWEEN</c> bounds and <c>EXCLUDE</c>
/// — as the SQL standard defines them. Access has no window functions; this is a LibRed extension.
/// </summary>
public class WindowFrameTests(WindowFrameTests.Database database)
    : TempDatabaseTest, IClassFixture<WindowFrameTests.Database>
{
    // Partition 'a' in V order is Ids 5 (Null), 1, 2 and 3 (tied on 2), 4; D has gaps of 1, 2 and 4 days. K has
    // columns named with the frame clause's words, which are not reserved.
    private static readonly string[] Setup =
    [
        "CREATE TABLE F (Id LONG, G TEXT(10), V LONG, D DATETIME)",
        "INSERT INTO F (Id, G, V, D) VALUES (1, 'a', 1, #2020-01-01#)",
        "INSERT INTO F (Id, G, V, D) VALUES (2, 'a', 2, #2020-01-02#)",
        "INSERT INTO F (Id, G, V, D) VALUES (3, 'a', 2, #2020-01-04#)",
        "INSERT INTO F (Id, G, V, D) VALUES (4, 'a', 5, #2020-01-08#)",
        "INSERT INTO F (Id, G, V, D) VALUES (5, 'a', NULL, NULL)",
        "INSERT INTO F (Id, G, V, D) VALUES (6, 'b', 10, #2020-02-01#)",
        "INSERT INTO F (Id, G, V, D) VALUES (7, 'b', 20, #2020-02-15#)",
        "CREATE TABLE K (Range LONG, Current LONG, Groups LONG, Ties LONG)",
        "INSERT INTO K (Range, Current, Groups, Ties) VALUES (1, 10, 100, 1000)",
        "INSERT INTO K (Range, Current, Groups, Ties) VALUES (2, 20, 200, 2000)",
    ];

    public sealed class Database() : SharedDatabase("window-frame-", Setup);

    private string ById(string expression) => string.Join(" ",
        database.Query($"SELECT Id, {expression} AS r FROM F ORDER BY Id", CultureInfo.InvariantCulture).Rows
            .Select(row => $"{row[0]}:{Convert.ToString(row[1], CultureInfo.InvariantCulture)}"));

    [Theory]
    [InlineData("SUM(V) OVER (ORDER BY Id ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING)", "1:3 2:5 3:9 4:7 5:15 6:30 7:30")]
    [InlineData("SUM(V) OVER (ORDER BY Id ROWS UNBOUNDED PRECEDING)", "1:1 2:3 3:5 4:10 5:10 6:20 7:40")]
    [InlineData("SUM(V) OVER (ORDER BY Id ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING)", "1:40 2:39 3:37 4:35 5:30 6:30 7:20")]
    [InlineData("COUNT(*) OVER (ORDER BY Id ROWS BETWEEN 3 PRECEDING AND 2 PRECEDING)", "1:0 2:0 3:1 4:2 5:2 6:2 7:2")]
    [InlineData("MIN(V) OVER (ORDER BY Id ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING)", "1:1 2:1 3:2 4:2 5:5 6:10 7:10")]
    [InlineData("SUM(V) OVER (ORDER BY Id ROWS BETWEEN 5 FOLLOWING AND 9 FOLLOWING)", "1:30 2:20 3: 4: 5: 6: 7:")]
    [InlineData("COUNT(*) OVER (ORDER BY Id ROWS BETWEEN Id - 1 PRECEDING AND CURRENT ROW)", "1:1 2:2 3:3 4:4 5:5 6:6 7:7")]
    public void Rows_frames_count_rows(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Theory]
    [InlineData("SUM(V) OVER (PARTITION BY G ORDER BY V RANGE BETWEEN 1 PRECEDING AND 1 FOLLOWING)", "1:5 2:5 3:5 4:5 5: 6:10 7:20")]
    [InlineData("COUNT(*) OVER (PARTITION BY G ORDER BY V RANGE BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING)", "1:1 2:2 3:2 4:4 5:1 6:0 7:1")]
    [InlineData("SUM(V) OVER (PARTITION BY G ORDER BY V DESC RANGE BETWEEN CURRENT ROW AND 3 FOLLOWING)", "1:1 2:5 3:5 4:9 5: 6:10 7:20")]
    [InlineData("SUM(V) OVER (ORDER BY V RANGE BETWEEN 0.5 PRECEDING AND 0.5 FOLLOWING)", "1:1 2:4 3:4 4:5 5: 6:10 7:20")]
    [InlineData("COUNT(*) OVER (PARTITION BY G ORDER BY D RANGE 2 PRECEDING)", "1:1 2:2 3:2 4:1 5:1 6:1 7:1")]
    [InlineData("SUM(V) OVER (PARTITION BY G ORDER BY V RANGE CURRENT ROW)", "1:1 2:4 3:4 4:5 5: 6:10 7:20")]
    public void Range_frames_measure_the_order_by_key(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Theory]
    [InlineData("SUM(V) OVER (PARTITION BY G ORDER BY V GROUPS BETWEEN 1 PRECEDING AND CURRENT ROW)", "1:1 2:5 3:5 4:9 5: 6:10 7:30")]
    [InlineData("COUNT(*) OVER (PARTITION BY G ORDER BY V GROUPS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING)", "1:3 2:1 3:1 4:0 5:4 6:1 7:0")]
    public void Groups_frames_count_peer_groups(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    private const string Whole = "PARTITION BY G ORDER BY V ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING";

    [Theory]
    [InlineData("SUM(V) OVER (" + Whole + ")", "1:10 2:10 3:10 4:10 5:10 6:30 7:30")]
    [InlineData("SUM(V) OVER (" + Whole + " EXCLUDE NO OTHERS)", "1:10 2:10 3:10 4:10 5:10 6:30 7:30")]
    [InlineData("SUM(V) OVER (" + Whole + " EXCLUDE CURRENT ROW)", "1:9 2:8 3:8 4:5 5:10 6:20 7:10")]
    [InlineData("SUM(V) OVER (" + Whole + " EXCLUDE GROUP)", "1:9 2:6 3:6 4:5 5:10 6:20 7:10")]
    [InlineData("SUM(V) OVER (" + Whole + " EXCLUDE TIES)", "1:10 2:8 3:8 4:10 5:10 6:30 7:30")]
    [InlineData("NTH_VALUE(Id, 2) OVER (" + Whole + " EXCLUDE CURRENT ROW)", "1:2 2:1 3:1 4:1 5:2 6: 7:")]
    public void Exclusion_leaves_rows_around_the_current_one_out(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Theory]
    [InlineData("LAST_VALUE(Id) OVER (" + Whole + ")", "1:4 2:4 3:4 4:4 5:4 6:7 7:7")]
    [InlineData("FIRST_VALUE(Id) OVER (ORDER BY Id ROWS BETWEEN 2 FOLLOWING AND 3 FOLLOWING)", "1:3 2:4 3:5 4:6 5:7 6: 7:")]
    [InlineData("FIRST_VALUE(Id) OVER (PARTITION BY G ORDER BY V ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING EXCLUDE GROUP)",
        "1:2 2:4 3:4 4: 5:1 6:7 7:")]
    [InlineData("LAST(V) OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING)", "1: 2:1 3:2 4:2 5:5 6: 7:10")]
    public void Value_functions_read_the_frame(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Fact]
    public void The_frame_words_still_name_columns()
    {
        var (_, rows) = database.Query(
            "SELECT Range, SUM(Current) OVER (ORDER BY Range ROWS UNBOUNDED PRECEDING) AS Following, Groups, Ties FROM K ORDER BY Range",
            CultureInfo.InvariantCulture);
        Assert.Equal("1 10 100 1000|2 30 200 2000", string.Join("|",
            rows.Select(row => string.Join(" ", row.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))))));
    }

    [Theory]
    [InlineData("ROWS BETWEEN CURRENT ROW AND 1 PRECEDING")]
    [InlineData("ROWS BETWEEN 1 FOLLOWING AND CURRENT ROW")]
    [InlineData("ROWS 1 FOLLOWING")]
    [InlineData("ROWS BETWEEN UNBOUNDED FOLLOWING AND UNBOUNDED FOLLOWING")]
    [InlineData("ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED PRECEDING")]
    public void A_frame_that_starts_after_it_ends_is_a_syntax_error(string frame) =>
        Assert.Throws<SqlParseException>(() => ById($"SUM(V) OVER (ORDER BY Id {frame})"));

    [Theory]
    [InlineData("ROW_NUMBER() OVER (ORDER BY Id ROWS UNBOUNDED PRECEDING)")]
    [InlineData("LAG(V) OVER (ORDER BY Id ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING)")]
    [InlineData("SUM(V) OVER (GROUPS UNBOUNDED PRECEDING)")]
    [InlineData("SUM(V) OVER (ORDER BY G, V RANGE 1 PRECEDING)")]
    [InlineData("SUM(V) OVER (RANGE BETWEEN CURRENT ROW AND 1 FOLLOWING)")]
    public void A_frame_the_window_cannot_take_is_refused(string expression) =>
        Assert.Throws<InvalidOperationException>(() => ById(expression));

    [Fact]
    public void A_range_offset_over_text_is_a_type_mismatch() =>
        Assert.Throws<InvalidCastException>(() => ById("SUM(V) OVER (ORDER BY G RANGE 1 PRECEDING)"));

    [Theory]
    [InlineData("SUM(V) OVER (ORDER BY Id ROWS -1 PRECEDING)")]
    [InlineData("SUM(V) OVER (ORDER BY Id ROWS NULL PRECEDING)")]
    [InlineData("SUM(V) OVER (ORDER BY Id GROUPS BETWEEN CURRENT ROW AND -2 FOLLOWING)")]
    [InlineData("SUM(V) OVER (ORDER BY V RANGE -0.5 PRECEDING)")]
    public void A_null_or_negative_offset_is_an_invalid_procedure_call(string expression) =>
        Assert.Throws<ArgumentException>(() => ById(expression));
}
