using System.Linq;
using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

// Grammar wiring for window functions: `f(args) OVER (PARTITION BY … ORDER BY …)`. Access has none of these;
// they are a LibRed extension emitted by EF Core's base SQL generator in extended mode.
// Parsing only — the planner node and executor follow in their own step, so a parsed window function still
// fails at execution here.
public class WindowFunctionParsingTests
{
    private static SqlStatement Parse(string sql) => new AntlrSqlParser().ParseStatement(sql);

    private static WindowFunction FirstWindow(string sql) =>
        Assert.IsType<WindowFunction>(Assert.IsType<SelectStatement>(Parse(sql)).Projection[0].Value);

    private static string ColumnName(Expression e) => Assert.IsType<ColumnReference>(e).Column;

    [Fact]
    public void The_shape_EF_Core_emits()
    {
        // Every one of the 410 OVER clauses in the extended suite is exactly this: ROW_NUMBER, always
        // partitioned, always ordered, never framed.
        WindowFunction w = FirstWindow(
            "SELECT ROW_NUMBER() OVER (PARTITION BY `o`.`CustomerID` ORDER BY `o`.`OrderDate`) AS `row` FROM `Orders` AS `o`");

        Assert.Equal("ROW_NUMBER", w.Name);
        Assert.Empty(w.Arguments);
        Assert.Equal(["CustomerID"], w.Over.PartitionBy.Select(ColumnName));
        Assert.Equal(["OrderDate"], w.Over.OrderBy.Select(o => ColumnName(o.Value)));
        Assert.Equal(SortDirection.Ascending, w.Over.OrderBy[0].Direction);
    }

    [Fact]
    public void Several_partition_and_order_keys_with_a_direction()
    {
        WindowFunction w = FirstWindow(
            "SELECT ROW_NUMBER() OVER (PARTITION BY `a`, `b` ORDER BY `c` DESC, `d`) AS `r` FROM `T`");

        Assert.Equal(["a", "b"], w.Over.PartitionBy.Select(ColumnName));
        Assert.Equal(["c", "d"], w.Over.OrderBy.Select(o => ColumnName(o.Value)));
        Assert.Equal(
            [SortDirection.Descending, SortDirection.Ascending],
            w.Over.OrderBy.Select(o => o.Direction));
    }

    [Theory]
    // Both halves of the spec are optional. The standard gives each a default — no PARTITION BY is one
    // partition over the whole input, no ORDER BY makes every row a peer — so a parse error would report the
    // wrong kind of problem for a window function that cannot actually use them.
    [InlineData("OVER ()", 0, 0)]
    [InlineData("OVER (PARTITION BY `a`)", 1, 0)]
    [InlineData("OVER (ORDER BY `a`)", 0, 1)]
    [InlineData("OVER (PARTITION BY `a` ORDER BY `b`)", 1, 1)]
    public void Both_parts_of_the_spec_are_optional(string over, int partitions, int orderings)
    {
        WindowFunction w = FirstWindow($"SELECT ROW_NUMBER() {over} AS `r` FROM `T`");

        Assert.Equal(partitions, w.Over.PartitionBy.Count);
        Assert.Equal(orderings, w.Over.OrderBy.Count);
    }

    [Fact]
    public void A_window_function_is_not_a_FunctionCall()
        // Load-bearing, not tidiness: QueryPlanner.HasAggregate matches any FunctionCall whose name is an
        // aggregate. Were WindowFunction a subtype, SUM(x) OVER (…) would make the query look grouped.
        => Assert.IsNotType<FunctionCall>(FirstWindow("SELECT ROW_NUMBER() OVER () AS `r` FROM `T`"));

    [Fact]
    public void An_aggregate_over_a_window_parses_as_one_too()
    {
        // Nothing emits this yet. It parses because OVER hangs off the call rather than off a list of window
        // function names — the whole point of that choice.
        WindowFunction w = FirstWindow("SELECT SUM(`x`) OVER (PARTITION BY `g`) AS `r` FROM `T`");

        Assert.Equal("SUM", w.Name);
        Assert.Equal(["x"], w.Arguments.Select(ColumnName));
    }

    [Fact]
    public void Without_OVER_it_is_still_an_ordinary_call()
        => Assert.IsType<FunctionCall>(
            Assert.IsType<SelectStatement>(Parse("SELECT COUNT(*) FROM `T`")).Projection[0].Value);

    [Fact]
    public void The_VBA_Partition_function_still_parses()
    {
        // PARTITION had to become a keyword for `PARTITION BY`, but Access has a real
        // Partition(number, start, stop, interval) that LibRed implements — so it is readmitted as a function
        // name. A call is always followed by '(' and `PARTITION BY` never is, so the two cannot collide.
        var f = Assert.IsType<FunctionCall>(
            Assert.IsType<SelectStatement>(Parse("SELECT Partition(`n`, 0, 100, 10) FROM `T`")).Projection[0].Value);

        Assert.Equal("Partition", f.Name);
        Assert.Equal(4, f.Arguments.Count);
    }

    [Theory]
    [InlineData("Over")]
    [InlineData("Partition")]
    public void The_new_keywords_are_reserved_as_column_names(string name)
    {
        // The tax every keyword in this grammar charges (see the FULL comment beside joinType): bracketed or
        // backticked it is a column, bare it is a keyword and the parse fails.
        Assert.Equal(name, ColumnName(
            Assert.IsType<SelectStatement>(Parse($"SELECT `{name}` FROM `T`")).Projection[0].Value));
        Assert.ThrowsAny<Exception>(() => Parse($"SELECT {name} FROM `T`"));
    }
}
