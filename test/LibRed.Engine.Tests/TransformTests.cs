using System.Globalization;
using LibRed.Engine;
using LibRed.Engine.Execution;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

// TRANSFORM … PIVOT, the crosstab query. Every expectation is what ACE returned for the same statement over the same
// table and rows: the column names, their order, the row order and each cell.
public class TransformTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "xtab-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE PivT (G LONG, K TEXT(10), N LONG, B BIT, F DOUBLE, V LONG)");
        foreach (string row in new[]
        {
            "1, 'a', 9, True, 1.5, 1",
            "1, 'A', 10, False, 2, 2",
            "2, 'b', 9, True, 1.5, 3",
            "2, NULL, NULL, NULL, NULL, 4",
            "1, '10', 100, False, 0.1, 5",
            "3, '9', -1, True, -2.5, 6",
        })
            engine.ExecuteNonQuery($"INSERT INTO PivT (G, K, N, B, F, V) VALUES ({row})");
        return engine;
    }

    /// <summary>A result as text: the column names, then each row, Null written as NULL.</summary>
    private static List<string> Run(QueryEngine engine, string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        ResultSet result = engine.ExecuteQuery(sql, parameters);
        return [string.Join(", ", result.ColumnNames), .. result.Rows.Select(r => string.Join(" | ",
            r.Select(v => v is null ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture))))];
    }

    [Fact]
    public void A_row_per_group_and_a_column_per_pivot_value_in_ascending_order()
    {
        Assert.Equal(
            [
                "EmployeeID, 1, 2, 3",
                "1 | 38 | 44 | 41", "2 | 35 | 36 | 25", "3 | 36 | 45 | 46", "4 | 46 | 70 | 40", "5 | 14 | 15 | 13",
                "6 | 23 | 25 | 19", "7 | 20 | 24 | 28", "8 | 27 | 48 | 29", "9 | 10 | 19 | 14",
            ],
            Run(Fresh(), "TRANSFORM Count(OrderID) SELECT EmployeeID FROM Orders GROUP BY EmployeeID PIVOT ShipVia"));
    }

    [Theory]
    // Text in the database's collation, case folded together; Null first, as <>.
    [InlineData("K", "G, <>, 10, 9, a, b", "1 | NULL | 5 | NULL | 3 | NULL", "2 | 4 | NULL | NULL | NULL | 3", "3 | NULL | NULL | 6 | NULL | NULL")]
    // Numbers in numeric order.
    [InlineData("N", "G, <>, -1, 9, 10, 100", "1 | NULL | NULL | 1 | 2 | 5", "2 | 4 | NULL | 3 | NULL | NULL", "3 | NULL | 6 | NULL | NULL | NULL")]
    // A Boolean is its VARIANT_BOOL number. A Yes/No column holds no Null — the one inserted is stored as False.
    [InlineData("B", "G, -1, 0", "1 | 1 | 7", "2 | 3 | 4", "3 | 6 | NULL")]
    // A point is not a name: it becomes an underscore.
    [InlineData("F", "G, <>, -2_5, 0_1, 1_5, 2", "1 | NULL | NULL | 5 | 1 | 2", "2 | 4 | NULL | NULL | 3 | NULL", "3 | NULL | 6 | NULL | NULL | NULL")]
    [InlineData("K & 'x'", "G, 10x, 9x, ax, bx, x", "1 | 5 | NULL | 3 | NULL | NULL", "2 | NULL | NULL | NULL | 3 | 4", "3 | NULL | 6 | NULL | NULL | NULL")]
    [InlineData("N > 9", "G, <>, -1, 0", "1 | NULL | 7 | 1", "2 | 4 | NULL | 3", "3 | NULL | NULL | 6")]
    public void Pivot_columns_are_named_and_ordered_by_value(string pivot, string header, string row1, string row2, string row3)
    {
        Assert.Equal([header, row1, row2, row3], Run(Fresh(), $"TRANSFORM Sum(V) SELECT G FROM PivT GROUP BY G PIVOT {pivot}"));
    }

    [Theory]
    // Fixed headings: in their order, a heading the data lacks still a column, a value no heading names no column.
    [InlineData("K IN ('a', 'b', 'zz', '10')", "G, a, b, zz, 10", "1 | 3 | NULL | NULL | 5", "2 | NULL | 3 | NULL | NULL", "3 | NULL | NULL | NULL | NULL")]
    [InlineData("N IN (9, 100, 7)", "G, 9, 100, 7", "1 | 1 | 5 | NULL", "2 | 3 | NULL | NULL", "3 | NULL | NULL | NULL")]
    // A heading takes the values = calls equal: text against numbers, and either case.
    [InlineData("N IN ('9', '100')", "G, 9, 100", "1 | 1 | 5", "2 | 3 | NULL", "3 | NULL | NULL")]
    [InlineData("K IN ('A')", "G, A", "1 | 3", "2 | NULL", "3 | NULL")]
    [InlineData("K IN (Null)", "G, <>", "1 | NULL", "2 | 4", "3 | NULL")]
    // A heading is named as it is written, not by the value it matches.
    [InlineData("B IN (True, False)", "G, True, False", "1 | 1 | 7", "2 | 3 | 4", "3 | 6 | NULL")]
    [InlineData("F IN (1.5, 2)", "G, 1_5, 2", "1 | 1 | 2", "2 | 3 | NULL", "3 | NULL | NULL")]
    public void A_pivot_in_list_fixes_the_columns(string pivot, string header, string row1, string row2, string row3)
    {
        Assert.Equal([header, row1, row2, row3], Run(Fresh(), $"TRANSFORM Sum(V) SELECT G FROM PivT GROUP BY G PIVOT {pivot}"));
    }

    [Fact]
    public void A_row_total_counts_the_whole_group_whatever_the_headings()
    {
        Assert.Equal(["G, Total, a, b", "1 | 8 | 3 | NULL", "2 | 7 | NULL | 3", "3 | 6 | NULL | NULL"],
            Run(Fresh(), "TRANSFORM Sum(V) SELECT G, Sum(V) AS Total FROM PivT GROUP BY G PIVOT K IN ('a', 'b')"));
    }

    [Fact]
    public void The_order_by_orders_the_rows_and_an_alias_names_a_heading()
    {
        Assert.Equal(["Grp, <>, 10, 9, a, b", "3 | NULL | NULL | 6 | NULL | NULL", "2 | 4 | NULL | NULL | NULL | 3", "1 | NULL | 5 | NULL | 3 | NULL"],
            Run(Fresh(), "TRANSFORM Sum(V) AS S SELECT G AS Grp FROM PivT GROUP BY G ORDER BY G DESC PIVOT K"));
    }

    [Fact]
    public void Every_group_by_key_makes_its_own_row()
    {
        Assert.Equal(["G, a, b", "1 | 1 | NULL", "1 | NULL | 2", "2 | 3 | NULL", "2 | NULL | 4", "3 | NULL | 5"],
            Run(FreshDistinct(), "TRANSFORM Sum(V) SELECT G FROM PivT GROUP BY G, K PIVOT K"));

        static QueryEngine FreshDistinct()
        {
            string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "xtab-");
            var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
            engine.ExecuteNonQuery("CREATE TABLE PivT (G LONG, K TEXT(10), V LONG)");
            foreach (string row in new[] { "1, 'a', 1", "1, 'b', 2", "2, 'a', 3", "2, 'b', 4", "3, 'b', 5" })
                engine.ExecuteNonQuery($"INSERT INTO PivT (G, K, V) VALUES ({row})");
            return engine;
        }
    }

    [Fact]
    public void A_cell_is_the_value_for_its_group_and_pivot_and_null_where_there_is_none()
    {
        QueryEngine engine = Fresh();
        // COUNT(K) of the group's Null pivot rows is 0, and Null where the group has no such row.
        Assert.Equal(["G, <>, -1, 9, 10, 100", "1 | NULL | NULL | 1 | 1 | 1", "2 | 0 | NULL | 1 | NULL | NULL", "3 | NULL | 1 | NULL | NULL | NULL"],
            Run(engine, "TRANSFORM Count(K) SELECT G FROM PivT GROUP BY G PIVOT N"));
        Assert.Equal(["G, <>, 10, 9, a, b", "1 | NULL | 10 | NULL | 6 | NULL", "2 | 8 | NULL | NULL | NULL | 6", "3 | NULL | NULL | 12 | NULL | NULL"],
            Run(engine, "TRANSFORM Sum(V) * 2 SELECT G FROM PivT GROUP BY G PIVOT K"));
    }

    [Fact]
    public void No_rows_leave_only_the_fixed_columns()
    {
        QueryEngine engine = Fresh();
        Assert.Equal(["G"], Run(engine, "TRANSFORM Sum(V) SELECT G FROM PivT WHERE V > 100 GROUP BY G PIVOT K"));
        Assert.Equal(["G, a"], Run(engine, "TRANSFORM Sum(V) SELECT G FROM PivT WHERE V > 100 GROUP BY G PIVOT K IN ('a')"));
    }

    [Fact]
    public void A_name_drops_its_spaces_and_the_characters_a_name_cannot_hold()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE OddT (G LONG, K TEXT(10), V LONG)");
        foreach (string row in new[] { "1, 'x.y', 1", "1, 'a!b', 2", "1, '[q]', 3", "1, ' lead', 4", "1, '', 5", "1, 'a`b', 6", "1, 'tail ', 7", "2, 'x.y', 8" })
            engine.ExecuteNonQuery($"INSERT INTO OddT (G, K, V) VALUES ({row})");

        Assert.Equal(["G, <>, lead, _q_, a_b, a`b, tail, x_y", "1 | 5 | 4 | 3 | 2 | 6 | 7 | 1", "2 | NULL | NULL | NULL | NULL | NULL | NULL | 8"],
            Run(engine, "TRANSFORM Sum(V) SELECT G FROM OddT GROUP BY G PIVOT K"));
    }

    [Fact]
    public void A_declared_parameter_binds_as_in_a_select()
    {
        Assert.Equal(
            [
                "EmployeeID, 1, 2, 3, 4",
                "1 | 15 | 8 | 13 | 19", "2 | 11 | 9 | 8 | 13", "3 | 15 | 19 | 16 | 21", "4 | 21 | 21 | 18 | 21",
                "5 | 4 | 5 | 6 | 3", "6 | 7 | 6 | 6 | 14", "7 | 7 | 11 | 12 | 6", "8 | 14 | 11 | 14 | 15", "9 | 3 | 3 | 4 | 9",
            ],
            Run(Fresh(), "PARAMETERS y SHORT; TRANSFORM Count(OrderID) SELECT EmployeeID FROM Orders " +
                         "WHERE Year(OrderDate) = y GROUP BY EmployeeID PIVOT DatePart('q', OrderDate)",
                new Dictionary<string, object?> { ["y"] = (short)1997 }));
    }

    [Theory]
    [InlineData("TRANSFORM Sum(V) SELECT G FROM PivT GROUP BY G HAVING Sum(V) > 3 PIVOT K", "Syntax error in TRANSFORM statement.")]
    [InlineData("TRANSFORM Sum(V) SELECT 1 AS One FROM PivT PIVOT K", "Syntax error in TRANSFORM statement.")]
    [InlineData("TRANSFORM Sum(V) SELECT * FROM PivT GROUP BY G PIVOT K", "Cannot use '*' in crosstab query.")]
    [InlineData("TRANSFORM Sum(V) SELECT TOP 1 G FROM PivT GROUP BY G ORDER BY G PIVOT K",
        "Values specified in a TOP clause are not allowed in delete queries or reports.")]
    [InlineData("TRANSFORM Sum(V) SELECT G FROM PivT GROUP BY G PIVOT K IN ('a', 'a')",
        "Cannot specify fixed column heading 'a' in a crosstab query more than once.")]
    [InlineData("TRANSFORM Sum(V) SELECT G FROM PivT GROUP BY G PIVOT N IN (9+1)", "Data type mismatch in criteria expression.")]
    public void What_ace_refuses_is_refused(string sql, string message)
    {
        var error = Assert.Throws<SqlParseException>(() => Fresh().ExecuteQuery(sql));
        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData("TRANSFORM V SELECT G FROM PivT GROUP BY G PIVOT K")]
    [InlineData("TRANSFORM Sum(V) SELECT G FROM PivT GROUP BY G ORDER BY Sum(V) DESC PIVOT K")]
    public void A_value_or_order_that_is_not_one_ace_takes_is_refused(string sql) =>
        Assert.Throws<InvalidOperationException>(() => Fresh().ExecuteQuery(sql).Rows.ToList());

    [Fact]
    public void A_stored_crosstab_runs_by_name_and_as_a_source()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE PROCEDURE Xt AS TRANSFORM Sum(V) SELECT G FROM PivT GROUP BY G PIVOT K");

        List<string> expected = ["G, <>, 10, 9, a, b", "1 | NULL | 5 | NULL | 3 | NULL", "2 | 4 | NULL | NULL | NULL | 3", "3 | NULL | NULL | 6 | NULL | NULL"];
        Assert.Equal(expected, Run(engine, "EXECUTE Xt"));
        Assert.Equal(expected, Run(engine, "SELECT * FROM Xt"));
        Assert.Equal(["G, b", "2 | 3", "3 | NULL"], Run(engine, "SELECT G, b FROM Xt WHERE G > 1"));
    }
}
