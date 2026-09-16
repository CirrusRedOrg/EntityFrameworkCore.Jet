using LibRed.Engine;
using LibRed.Engine.Plan;
using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The predicates <c>LIKE</c>, <c>BETWEEN</c>, <c>IN</c> and <c>IS NULL</c>. LIKE takes the ANSI-92 wildcards EF
/// emits, BETWEEN takes its bounds in either order, and IN skips a Null item. The expected values were measured
/// against ACE.
/// </summary>
public class PredicateOperatorTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "predicate-ops-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery(
            "CREATE TABLE T (Id LONG, TN TEXT(60), NT TEXT(60), D DATETIME, G GUID, B BINARY(4), SG REAL, DC DECIMAL(18,4))");
        engine.ExecuteNonQuery("INSERT INTO T (Id, TN, D, SG, DC) VALUES (1, '7', #2020-01-02 12:00:00#, 1.5, 4.5)");
        engine.ExecuteNonQuery("UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}");
        engine.ExecuteNonQuery("UPDATE T SET B = 0x41004200");
        return engine;
    }

    private static object? Query(string sql) => Fresh().ExecuteQuery(sql).Rows.First()[0];

    private static object? Scalar(string expression) => Query($"SELECT {expression} FROM T");

    private static object? Count(string condition) => Query($"SELECT COUNT(*) FROM T WHERE {condition}");

    [Theory]
    [InlineData("'abc' LIKE '%'", true)]
    [InlineData("'' LIKE '%'", true)]
    [InlineData("'abc' LIKE '%%%'", true)]
    [InlineData("'abc' LIKE 'A%'", true)]
    [InlineData("'abc' LIKE '_b_'", true)]
    [InlineData("'ab' LIKE '_%'", true)]
    [InlineData("'abc' LIKE '%b%'", true)]
    [InlineData("'aBBBa' LIKE 'a%a'", true)]
    [InlineData("'abc' LIKE 'a%a'", false)]
    [InlineData("'abc' LIKE ''", false)]
    [InlineData("'' LIKE ''", true)]
    [InlineData("'x_y' LIKE 'x[_]y'", true)]
    [InlineData("'x%y' LIKE 'x[%]y'", true)]
    [InlineData("'_' LIKE '%[_]'", true)]
    public void Percent_and_underscore_are_the_wildcards(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("'abc' LIKE '*'", false)]
    [InlineData("'abc' LIKE 'a*'", false)]
    [InlineData("'a*a' LIKE 'a*a'", true)]
    [InlineData("'aBBBa' LIKE 'a*a'", false)]
    [InlineData("'a1a' LIKE 'a?a'", false)]
    [InlineData("'a?a' LIKE 'a?a'", true)]
    [InlineData("'a1a' LIKE 'a#a'", false)]
    [InlineData("'ab' LIKE '??'", false)]
    public void Star_question_mark_and_hash_are_plain_characters(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("'abc' LIKE '[a-z]%'", true)]
    [InlineData("'E' LIKE '[a-e]'", true)]
    [InlineData("'é' LIKE '[a-e]'", false)]
    [InlineData("'abc' LIKE '[!b]%'", true)]
    [InlineData("'b' LIKE '[!b]%'", false)]
    [InlineData("'a[b' LIKE 'a[[]b'", true)]
    [InlineData("'a]b' LIKE 'a]b'", true)]
    [InlineData("'a]b' LIKE 'a[]]b'", true)]
    [InlineData("'a-b' LIKE 'a[-]b'", true)]
    [InlineData("'-' LIKE '[a-]'", true)]
    [InlineData("'-' LIKE '[-a]'", true)]
    [InlineData("'b' LIKE '[a-c-e]'", true)]
    [InlineData("'-' LIKE '[a-c-e]'", true)]
    [InlineData("'a!b' LIKE 'a[!]b'", true)]
    [InlineData("'!' LIKE '[!!]'", false)]
    [InlineData("'z' LIKE '[!!]'", true)]
    [InlineData("'!' LIKE '[!-a]'", true)]
    [InlineData("'-' LIKE '[!-a]'", false)]
    [InlineData("'abc' LIKE '[]abc'", true)]
    [InlineData("'' LIKE '[]'", true)]
    [InlineData("'b' LIKE '%[]'", true)]
    [InlineData("'[]' LIKE '[[]]'", true)]
    [InlineData("'bill' LIKE 'b[!ae]ll'", true)]
    [InlineData("'bill' LIKE 'b[^ae]ll'", false)]
    [InlineData("'x' LIKE '[^b]%'", false)]
    [InlineData("'b' LIKE '[^b]%'", true)]
    [InlineData("'a^b' LIKE 'a[^]b'", true)]
    [InlineData("'^' LIKE '[!^]'", false)]
    [InlineData("'2' LIKE '[^a-z]'", false)]
    public void Brackets_list_one_character(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("'abc' LIKE 'ABC'", true)]
    [InlineData("'E' LIKE 'e'", true)]
    [InlineData("'é' LIKE 'e'", false)]
    [InlineData("'café' LIKE 'cafe'", false)]
    [InlineData("'É' LIKE '[É]'", true)]
    [InlineData("'é' LIKE '[É]'", true)]
    [InlineData("'aßb' LIKE 'ass%'", true)]
    [InlineData("'aSSb' LIKE 'a[ß]b'", true)]
    [InlineData("'aßb' LIKE 'a[s]sb'", true)]
    [InlineData("'ss' LIKE '[ß]'", true)]
    [InlineData("'Æ' LIKE 'ae'", true)]
    [InlineData("'Æ' LIKE '[æ]'", true)]
    [InlineData("'Æ' LIKE '[a-z]%'", true)]
    [InlineData("'Æ' LIKE '[a-z]'", false)]
    [InlineData("'ß' LIKE '_'", true)]
    [InlineData("'ß' LIKE '__'", false)]
    [InlineData("'aßb' LIKE 'a_b'", true)]
    [InlineData("'ß' LIKE 's_'", true)]
    [InlineData("'ß' LIKE '_s'", false)]
    [InlineData("'ﬁ' LIKE 'fi'", false)]
    public void Case_is_ignored_accents_are_not_and_sharp_s_and_ae_expand(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("TRUE LIKE '-1'", true)]
    [InlineData("123 LIKE 123", true)]
    [InlineData("TN LIKE 7", true)]
    [InlineData("1 + 1 LIKE '2'", true)]
    [InlineData("12.5 LIKE '12.5'", true)]
    [InlineData("'abc' LIKE 1", false)]
    public void Other_values_are_matched_as_their_text(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("NULL LIKE '%'", false)]
    [InlineData("NT LIKE '%'", false)]
    [InlineData("NULL NOT LIKE '%'", true)]
    [InlineData("NOT NULL LIKE '%'", true)]
    public void A_pattern_of_just_percent_is_false_for_null(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("NULL LIKE '%%'")]
    [InlineData("NULL LIKE 'a%'")]
    [InlineData("NULL LIKE '%' & ''")]
    [InlineData("NULL LIKE '['")]
    [InlineData("'abc' LIKE NULL")]
    [InlineData("NT LIKE NT")]
    [InlineData("NULL LIKE 1")]
    public void Otherwise_null_gives_null(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("'abc' LIKE 'a[b'")]
    [InlineData("'z' LIKE '['")]
    [InlineData("'z' LIKE '[!'")]
    [InlineData("'abc' LIKE '[z-a]'")]
    [InlineData("'b' LIKE '%[z-a]'")]
    public void An_unclosed_bracket_or_backwards_range_is_an_invalid_pattern(string expression) =>
        Assert.Throws<ArgumentException>(() => Scalar(expression));

    [Fact]
    public void There_is_no_escape_clause() =>
        Assert.Throws<SqlParseException>(() => Scalar(@"'a%b' LIKE 'a\%b' ESCAPE '\'"));

    [Theory]
    [InlineData("'' LIKE '['")]
    [InlineData("' ' LIKE 'a[b'")]
    [InlineData("'' LIKE '[z-a]'")]
    [InlineData("'' LIKE '%[z-a]'")]
    [InlineData("'z' LIKE 'x[z-a]'")]
    public void An_invalid_pattern_is_only_reported_when_the_match_reaches_it(string expression) =>
        Assert.Equal(false, Scalar(expression));

    [Theory]
    [InlineData("TN LIKE '%'", 1)]
    [InlineData("NT LIKE '%'", 0)]
    [InlineData("NT NOT LIKE '%'", 1)]
    [InlineData("'abc' LIKE 'A%'", 1)]
    [InlineData("'abc' LIKE 'A*'", 0)]
    public void A_where_condition_matches_the_same_way(string condition, int expected) =>
        Assert.Equal(expected, Count(condition));

    [Theory]
    [InlineData("5 BETWEEN 1 AND 10", true)]
    [InlineData("5 BETWEEN 10 AND 1", true)]
    [InlineData("1 BETWEEN 1 AND 1", true)]
    [InlineData("10 BETWEEN 1 AND 10", true)]
    [InlineData("15 BETWEEN 1 AND 10", false)]
    [InlineData("5 NOT BETWEEN 1 AND 10", false)]
    [InlineData("5 NOT BETWEEN 10 AND 1", false)]
    [InlineData("NOT 5 BETWEEN 1 AND 10", false)]
    [InlineData("'b' BETWEEN 'c' AND 'a'", true)]
    [InlineData("'B' BETWEEN 'a' AND 'c'", true)]
    [InlineData("'2' BETWEEN '1' AND '3'", true)]
    [InlineData("D BETWEEN #2020-01-01# AND #2020-01-03#", true)]
    [InlineData("D BETWEEN 43833 AND 43832", true)]
    [InlineData("TRUE BETWEEN -1 AND 0", true)]
    [InlineData("2 BETWEEN TRUE AND 3", true)]
    [InlineData("SG BETWEEN 1.4 AND 1.6", true)]
    [InlineData("B BETWEEN 'A' AND 'B'", true)]
    [InlineData("G BETWEEN 'a' AND '{z'", false)]
    [InlineData("LEFT('7', 1) BETWEEN 1 AND 10", true)]
    [InlineData("5 BETWEEN 1 + 1 AND 2 * 5", true)]
    public void Between_is_inclusive_with_the_bounds_in_either_order(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("NULL BETWEEN 1 AND 2")]
    [InlineData("5 BETWEEN NULL AND 10")]
    [InlineData("5 BETWEEN 1 AND NULL")]
    [InlineData("15 BETWEEN NULL AND 10")]
    [InlineData("0 BETWEEN 1 AND NULL")]
    [InlineData("15 NOT BETWEEN NULL AND 10")]
    public void Between_is_null_when_any_operand_is(string expression) =>
        Assert.Null(Scalar(expression));

    [Fact]
    public void Between_reads_text_that_is_not_a_number_as_a_type_mismatch() =>
        Assert.Throws<InvalidCastException>(() => Scalar("'abc' BETWEEN 1 AND 2"));

    [Theory]
    [InlineData("5 BETWEEN 10 AND 1 AND TRUE", true)]
    [InlineData("5 BETWEEN 10 AND 1 AND 0", false)]
    [InlineData("5 BETWEEN 1 AND 10 AND 1 = 2", false)]
    [InlineData("5 BETWEEN 1 AND 10 OR 1 = 2", true)]
    [InlineData("5 BETWEEN 1 AND 10 AND 2 BETWEEN 3 AND 1", true)]
    [InlineData("5 BETWEEN 1 AND 3 OR 4 AND 5 = 5", true)]
    [InlineData("5 BETWEEN (1 AND 1) AND 10", true)]
    [InlineData("5 BETWEEN 1 AND 10 XOR TRUE", false)]
    [InlineData("NOT 5 BETWEEN 1 AND 10 AND TRUE", false)]
    [InlineData("5 BETWEEN 1 + 1 AND 10 - 1 AND TRUE", true)]
    [InlineData("2 BETWEEN 1 AND 3 = TRUE", false)]
    public void The_and_after_the_lower_bound_belongs_to_between(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Fact]
    public void An_indexed_between_seeks_from_the_lower_literal_bound()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE K (Id LONG PRIMARY KEY, V LONG)");
        for (int i = 1; i <= 10; i++)
            engine.ExecuteNonQuery($"INSERT INTO K (Id, V) VALUES ({i}, {i})");

        const string sql = "SELECT COUNT(*) FROM K WHERE Id BETWEEN 8 AND 3";
        Assert.Equal(6, engine.ExecuteQuery(sql).Rows.Single()[0]);
        IndexRangeSeekNode seek = FindSeek(engine.PlanFor(sql))!;
        Assert.Equal(3, Convert.ToInt32(Assert.IsType<LiteralExpression>(seek.Low).Value));
        Assert.Equal(8, Convert.ToInt32(Assert.IsType<LiteralExpression>(seek.High).Value));

        Assert.Equal(4, engine.ExecuteQuery("SELECT COUNT(*) FROM K WHERE Id NOT BETWEEN 8 AND 3").Rows.Single()[0]);
        Assert.Equal(0, engine.ExecuteQuery("SELECT COUNT(*) FROM K WHERE Id BETWEEN NULL AND 3").Rows.Single()[0]);
        Assert.Null(FindSeek(engine.PlanFor("SELECT COUNT(*) FROM K WHERE Id BETWEEN NULL AND 3")));
        Assert.Equal(1, engine.ExecuteQuery("SELECT COUNT(*) FROM K WHERE Id BETWEEN 1 AND 10 AND V = 2").Rows.Single()[0]);
        Assert.Equal(6, engine.ExecuteQuery(
            "SELECT COUNT(*) FROM K WHERE Id BETWEEN @a AND @b",
            new Dictionary<string, object?> { ["a"] = 8, ["b"] = 3 }).Rows.Single()[0]);

        static IndexRangeSeekNode? FindSeek(PlanNode node) =>
            node as IndexRangeSeekNode ?? node.Children.Select(FindSeek).FirstOrDefault(s => s is not null);
    }

    [Theory]
    [InlineData("5 IN (1, 5)", true)]
    [InlineData("5 IN (5, NULL)", true)]
    [InlineData("5 IN (1, NULL)", false)]
    [InlineData("5 NOT IN (1, NULL)", true)]
    [InlineData("5 NOT IN (1, 2)", true)]
    [InlineData("NOT 5 IN (5)", false)]
    [InlineData("LEFT('7', 1) IN (7)", true)]
    [InlineData("1 IN (TRUE)", false)]
    [InlineData("-1 IN (TRUE)", true)]
    [InlineData("5 IN (TRUE)", false)]
    [InlineData("TRUE IN (5)", false)]
    [InlineData("0 IN (FALSE)", true)]
    [InlineData("'0' IN (FALSE)", true)]
    [InlineData("'A' IN ('a')", true)]
    [InlineData("'a ' IN ('a')", true)]
    [InlineData("D IN (43832.5)", true)]
    [InlineData("D IN (#2020-01-02 12:00#)", true)]
    [InlineData("G IN ('{00112233-4455-6677-8899-AABBCCDDEEFF}')", true)]
    [InlineData("B IN ('AB')", true)]
    [InlineData("1 IN (1.0)", true)]
    [InlineData("SG IN (1.5)", true)]
    [InlineData("CSNG(1.1) IN (1.1)", true)]
    [InlineData("4.5 IN (DC)", true)]
    [InlineData("1.5 IN (DC)", false)]
    [InlineData("5 IN (5, 'abc')", true)]
    [InlineData("1 IN (1, 2) AND 1 = 2", false)]
    public void In_compares_each_item_as_equals_does_and_skips_null_items(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("NULL IN (1)")]
    [InlineData("NULL IN (NULL)")]
    [InlineData("NULL NOT IN (1)")]
    public void In_is_null_when_the_value_is(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("'abc' IN (1)")]
    [InlineData("1 IN ('abc')")]
    [InlineData("5 IN ('abc', 5)")]
    [InlineData("1 IN (G)")]
    public void In_raises_a_type_mismatch_at_the_first_item_that_cannot_be_compared(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("5 IN (1, NULL)", 0)]
    [InlineData("5 NOT IN (1, NULL)", 1)]
    [InlineData("5 IN (5, NULL)", 1)]
    public void A_where_condition_skips_null_items_too(string condition, int expected) =>
        Assert.Equal(expected, Count(condition));

    [Theory]
    [InlineData("NULL IS NULL", true)]
    [InlineData("1 IS NULL", false)]
    [InlineData("'' IS NULL", false)]
    [InlineData("NT IS NULL", true)]
    [InlineData("NT IS NOT NULL", false)]
    [InlineData("NOT NT IS NULL", false)]
    [InlineData("NOT NT IS NOT NULL", true)]
    [InlineData("(NT) IS NULL", true)]
    [InlineData("1 + NULL IS NULL", true)]
    [InlineData("NULL & NULL IS NULL", true)]
    [InlineData("1 IS NULL OR 1 = 1", true)]
    [InlineData("G IS NULL", false)]
    [InlineData("B IS NOT NULL", true)]
    [InlineData("NT IS NULL IS NULL", false)]
    public void Is_null_is_never_null_itself(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));
}
