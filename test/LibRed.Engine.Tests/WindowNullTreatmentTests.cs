using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>RESPECT NULLS</c> / <c>IGNORE NULLS</c> on <c>LAG</c>, <c>LEAD</c>, <c>FIRST_VALUE</c>, <c>LAST_VALUE</c> and
/// <c>NTH_VALUE</c>, and <c>NTH_VALUE … FROM FIRST</c> / <c>FROM LAST</c>, as the SQL standard defines them. Access
/// has no window functions; this is a LibRed extension.
/// </summary>
public class WindowNullTreatmentTests(WindowNullTreatmentTests.Database database)
    : TempDatabaseTest, IClassFixture<WindowNullTreatmentTests.Database>
{
    // V by Id: 10, Null, Null, 40, Null | Null, 70 — partition 'a' is Ids 1-5, 'b' Ids 6-7. The table named Last and
    // its columns carry the new clauses' words, which are not reserved.
    private static readonly string[] Setup =
    [
        "CREATE TABLE N (Id LONG, G TEXT(10), V LONG)",
        "INSERT INTO N (Id, G, V) VALUES (1, 'a', 10)",
        "INSERT INTO N (Id, G, V) VALUES (2, 'a', NULL)",
        "INSERT INTO N (Id, G, V) VALUES (3, 'a', NULL)",
        "INSERT INTO N (Id, G, V) VALUES (4, 'a', 40)",
        "INSERT INTO N (Id, G, V) VALUES (5, 'a', NULL)",
        "INSERT INTO N (Id, G, V) VALUES (6, 'b', NULL)",
        "INSERT INTO N (Id, G, V) VALUES (7, 'b', 70)",
        "CREATE TABLE Last (Respect LONG, Nulls LONG, Within LONG)",
        "INSERT INTO Last (Respect, Nulls, Within) VALUES (1, 2, 3)",
        "INSERT INTO Last (Respect, Nulls, Within) VALUES (4, 5, 6)",
    ];

    public sealed class Database() : SharedDatabase("window-nulls-", Setup);

    private string ById(string expression) => string.Join(" ",
        database.Query($"SELECT Id, {expression} AS r FROM N ORDER BY Id", CultureInfo.InvariantCulture).Rows
            .Select(row => $"{row[0]}:{Convert.ToString(row[1], CultureInfo.InvariantCulture)}"));

    [Theory]
    [InlineData("LAG(V) OVER (ORDER BY Id)", "1: 2:10 3: 4: 5:40 6: 7:")]
    [InlineData("LAG(V) RESPECT NULLS OVER (ORDER BY Id)", "1: 2:10 3: 4: 5:40 6: 7:")]
    [InlineData("LAG(V) IGNORE NULLS OVER (ORDER BY Id)", "1: 2:10 3:10 4:10 5:40 6:40 7:40")]
    [InlineData("LEAD(V) IGNORE NULLS OVER (ORDER BY Id)", "1:40 2:40 3:40 4:70 5:70 6:70 7:")]
    [InlineData("LAG(V, 2, -1) IGNORE NULLS OVER (ORDER BY Id)", "1:-1 2:-1 3:-1 4:-1 5:10 6:10 7:10")]
    [InlineData("LAG(V, 0) IGNORE NULLS OVER (ORDER BY Id)", "1:10 2: 3: 4:40 5: 6: 7:70")]
    [InlineData("LAG(V) IGNORE NULLS OVER (PARTITION BY G ORDER BY Id)", "1: 2:10 3:10 4:10 5:40 6: 7:")]
    public void Lag_and_lead_can_skip_rows_whose_value_is_null(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Theory]
    [InlineData("FIRST_VALUE(V) OVER (PARTITION BY G ORDER BY Id)", "1:10 2:10 3:10 4:10 5:10 6: 7:")]
    [InlineData("FIRST_VALUE(V) IGNORE NULLS OVER (PARTITION BY G ORDER BY Id)", "1:10 2:10 3:10 4:10 5:10 6: 7:70")]
    [InlineData("LAST_VALUE(V) IGNORE NULLS OVER (ORDER BY Id ROWS UNBOUNDED PRECEDING)", "1:10 2:10 3:10 4:40 5:40 6:40 7:70")]
    [InlineData("FIRST_VALUE(V) IGNORE NULLS OVER (ORDER BY Id ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING)",
        "1:10 2:40 3:40 4:40 5:70 6:70 7:70")]
    [InlineData("NTH_VALUE(V, 2) IGNORE NULLS OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING)",
        "1:40 2:40 3:40 4:40 5:40 6:40 7:40")]
    public void Frame_values_can_skip_rows_whose_value_is_null(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Theory]
    [InlineData("NTH_VALUE(Id, 2) FROM LAST OVER (ORDER BY Id)", "1: 2:1 3:2 4:3 5:4 6:5 7:6")]
    [InlineData("NTH_VALUE(Id, 1) FROM FIRST OVER (ORDER BY Id)", "1:1 2:1 3:1 4:1 5:1 6:1 7:1")]
    [InlineData("NTH_VALUE(Id, 2) FROM LAST OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING)",
        "1:6 2:6 3:6 4:6 5:6 6:6 7:6")]
    [InlineData("NTH_VALUE(V, 1) FROM LAST IGNORE NULLS OVER (ORDER BY Id)", "1:10 2:10 3:10 4:40 5:40 6:40 7:70")]
    [InlineData("NTH_VALUE(V, 2) FROM LAST IGNORE NULLS OVER (ORDER BY Id)", "1: 2: 3: 4:10 5:10 6:10 7:40")]
    public void Nth_value_counts_from_either_end_of_the_frame(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    private const string Whole = "ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING";

    // Ordered by G, Ids 1-5 and 6-7 are peers; EXCLUDE TIES keeps the row itself but drops the rest of its group.
    [Theory]
    [InlineData("FIRST_VALUE(V) IGNORE NULLS OVER (ORDER BY Id " + Whole + " EXCLUDE CURRENT ROW)", "1:40 2:10 3:10 4:10 5:10 6:10 7:10")]
    [InlineData("LAST_VALUE(V) IGNORE NULLS OVER (ORDER BY Id " + Whole + " EXCLUDE CURRENT ROW)", "1:70 2:70 3:70 4:70 5:70 6:70 7:40")]
    [InlineData("FIRST_VALUE(V) IGNORE NULLS OVER (ORDER BY G " + Whole + " EXCLUDE TIES)", "1:10 2:70 3:70 4:40 5:70 6:10 7:10")]
    [InlineData("LAST_VALUE(V) IGNORE NULLS OVER (ORDER BY G " + Whole + " EXCLUDE TIES)", "1:70 2:70 3:70 4:70 5:70 6:40 7:70")]
    public void Skipping_nulls_works_across_an_exclusion(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Theory]
    [InlineData("SUM(V) IGNORE NULLS OVER ()")]
    [InlineData("ROW_NUMBER() RESPECT NULLS OVER (ORDER BY Id)")]
    [InlineData("FIRST_VALUE(V) FROM LAST OVER ()")]
    [InlineData("LAG(V) FROM FIRST OVER (ORDER BY Id)")]
    public void Only_the_functions_the_standard_names_take_the_clauses(string expression) =>
        Assert.Throws<InvalidOperationException>(() => ById(expression));

    [Fact]
    public void The_clauses_words_still_name_tables_and_columns()
    {
        Assert.Equal(4, database.Scalar("SELECT MAX(Respect) FROM Last", CultureInfo.InvariantCulture));
        var (_, rows) = database.Query("SELECT Respect, Nulls, Within FROM Last ORDER BY Respect", CultureInfo.InvariantCulture);
        Assert.Equal([1, 2, 3], rows[0]);
    }
}
