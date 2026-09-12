using LibRed.Catalog;
using LibRed.Storage.Calculated;
using Xunit;

namespace LibRed.Core.Tests;

// The evaluator for the expression subset ACE permits in a calculated column. These are LibRed-only tests:
// they pin the semantics that must hold before any of it is compared against ACE, and they run on the
// cross-platform suite because nothing here needs an engine.
//
// The value that matters most is Null handling, because a calculated column's stored result is a cache
// neither engine re-derives on read -- a wrong one is invisible until someone compares two databases.
public class CalculatedEvaluatorTests
{
    private static object? Eval(string expression, params (string Column, object? Value)[] row)
    {
        var values = row.ToDictionary(r => r.Column, r => r.Value, StringComparer.OrdinalIgnoreCase);
        return CalculatedEvaluator.Evaluate(
            CalculatedExpression.Parse(expression),
            name => values.TryGetValue(name, out object? v) ? v
                : throw new CalculatedExpressionException($"No column '{name}'."));
    }

    [Theory]
    [InlineData("1+1", 2.0)]
    [InlineData("2*3+1", 7.0)]
    [InlineData("1+2*3", 7.0)]
    [InlineData("(1+2)*3", 9.0)]
    [InlineData("2^3", 8.0)]
    [InlineData("-2^2", -4.0)]          // ^ binds tighter than unary minus, as in VBA
    [InlineData("2^3^2", 512.0)]        // right-associative
    [InlineData("10/4", 2.5)]
    public void Evaluates_arithmetic(string expression, double expected)
        => Assert.Equal(expected, Assert.IsType<double>(Eval(expression)));

    [Theory]
    [InlineData("\"a\" & \"b\"", "ab")]
    [InlineData("\"n=\" & 1", "n=1")]
    [InlineData("\"a\" + \"b\"", "ab")]     // '+' concatenates when BOTH sides are text
    public void Evaluates_concatenation(string expression, string expected)
        => Assert.Equal(expected, Eval(expression));

    // The asymmetry MSDN uses to introduce the feature: '+' propagates Null, '&' swallows it. Reversing
    // them leaves a stray space in the classic full-name expression, which nothing would ever flag.
    [Fact]
    public void Concatenation_swallows_null_but_addition_propagates_it()
    {
        Assert.Equal("ab", Eval("[A] & [B]", ("A", "ab"), ("B", null)));
        Assert.Null(Eval("[A] + [B]", ("A", "ab"), ("B", null)));

        const string fullName = "[First] & \" \" & ([Middle] + \" \") & [Last]";
        Assert.Equal("Ada Lovelace", Eval(fullName, ("First", "Ada"), ("Middle", null), ("Last", "Lovelace")));
        Assert.Equal("Ada B Lovelace", Eval(fullName, ("First", "Ada"), ("Middle", "B"), ("Last", "Lovelace")));
    }

    [Theory]
    [InlineData("[A] + 1")]
    [InlineData("[A] * 2")]
    [InlineData("[A] > 1")]
    [InlineData("Abs([A])")]
    [InlineData("Left([A], 2)")]
    public void Null_propagates_through_arithmetic_comparison_and_functions(string expression)
        => Assert.Null(Eval(expression, ("A", null)));

    // CDbl is the other exception, and it goes the opposite way: the VBA conversions RAISE on Null instead of
    // propagating it, and CDbl is the only conversion a calculated column may contain. ACE caches the failure
    // as VBA error 94 and then cannot read that row back; LibRed refuses to write it, so the unreadable state
    // never reaches the file. Everything else on the whitelist still propagates.
    [Fact]
    public void CDbl_refuses_a_null_rather_than_propagating_it()
    {
        var ex = Assert.Throws<CalculatedExpressionException>(() => Eval("CDbl([A])", ("A", null)));
        Assert.Contains("CDbl cannot convert Null", ex.Message, StringComparison.Ordinal);
        Assert.Contains("IIf(IsNull(", ex.Message, StringComparison.Ordinal);   // and says how to avoid it

        Assert.Equal(7.0, Eval("CDbl([A])", ("A", 7)));                          // still works over a value
        Assert.Null(Eval("Abs([A])", ("A", null)));                              // neighbours unaffected
        Assert.Equal(0.0, Eval("IIf(IsNull([A]), 0, CDbl([A]))", ("A", null)));  // the documented guard
    }

    // IIf, Choose and IsNull are the exceptions: they must see the Null rather than become it.
    [Fact]
    public void Selection_functions_do_not_propagate_null()
    {
        Assert.Equal("none", Eval("IIf(IsNull([A]), \"none\", [A])", ("A", null)));
        Assert.Equal("here", Eval("IIf(IsNull([A]), \"none\", [A])", ("A", "here")));
        Assert.Equal(true, Eval("IsNull([A])", ("A", null)));
        Assert.Equal("b", Eval("Choose(2, \"a\", \"b\")"));
        Assert.Null(Eval("Choose(9, \"a\", \"b\")"));
    }

    [Theory]
    [InlineData("Len(\"abc\")", 3.0)]
    [InlineData("Asc(\"A\")", 65.0)]
    [InlineData("Abs(-3)", 3.0)]
    [InlineData("Sgn(-3)", -1.0)]
    [InlineData("Int(-1.5)", -2.0)]     // Int floors
    [InlineData("Fix(-1.5)", -1.0)]     // Fix truncates toward zero
    [InlineData("Round(2.5)", 2.0)]     // banker's rounding, as VBA does
    [InlineData("Round(3.5)", 4.0)]
    // 2.125 is exact in binary, so this tests the rounding rule rather than the representation of the
    // literal -- 2.345 is really 2.34499…, which makes it a test of double parsing, not of Round.
    [InlineData("Round(2.125, 2)", 2.12)]
    public void Evaluates_numeric_functions(string expression, double expected)
        => Assert.Equal(expected, Assert.IsType<double>(Eval(expression)));

    [Theory]
    [InlineData("UCase(\"aB\")", "AB")]
    [InlineData("LCase(\"aB\")", "ab")]
    [InlineData("Trim(\"  a  \")", "a")]
    [InlineData("Left(\"abcdef\", 2)", "ab")]
    [InlineData("Right(\"abcdef\", 2)", "ef")]
    [InlineData("Mid(\"abcdef\", 2, 3)", "bcd")]
    [InlineData("Space(3)", "   ")]
    [InlineData("String(3, \"x\")", "xxx")]
    public void Evaluates_string_functions(string expression, string expected)
        => Assert.Equal(expected, Eval(expression));

    // Access requires every argument of these two even though VBA makes some optional, and reports a
    // missing one as a SYNTAX error rather than a policy refusal. LibRed has to refuse the same shapes,
    // or it would happily write a value ACE would never have produced.
    [Theory]
    [InlineData("Mid(\"abcdef\", 2)")]
    [InlineData("InStr(\"abc\", \"b\")")]
    public void Requires_every_argument_where_ace_does(string expression)
        => Assert.Throws<CalculatedExpressionException>(() => Eval(expression));

    // ...but that is per-function and NOT the general rule the MSDN article states, which is why each one
    // has to be recorded by measurement. These were measured accepted with their optionals left out, so
    // refusing them would be refusing an expression ACE lets you author.
    [Theory]
    [InlineData("Round(2.5)", 2.0)]
    [InlineData("Weekday(#2003-09-29#)", 2.0)]
    [InlineData("Pmt(0.005, 60, 10000)", -193.3280)]
    public void Accepts_an_omitted_optional_where_ace_does(string expression, double expected)
        => Assert.Equal(expected, Assert.IsType<double>(Eval(expression)), 4);

    [Fact]
    public void Evaluates_InStr_in_its_full_form()
    {
        Assert.Equal(2.0, Eval("InStr(1, \"abc\", \"b\", 0)"));
        Assert.Equal(0.0, Eval("InStr(1, \"abc\", \"z\", 0)"));
        Assert.Equal(2.0, Eval("InStr(1, \"aBc\", \"b\", 1)"));   // compare = 1 is case-insensitive
    }

    // ACE takes three or four arguments here and rejects two. With compare omitted it matches
    // case-INSENSITIVELY -- Access's Option Compare Database default, not VBA's binary one -- so the
    // three-argument form is not the same as passing 0.
    [Fact]
    public void Evaluates_InStr_without_its_compare_argument()
    {
        Assert.Equal(1.0, Eval("InStr(1, \"hello\", \"H\")"));
        Assert.Equal(0.0, Eval("InStr(1, \"hello\", \"H\", 0)"));
        Assert.Equal(1.0, Eval("InStr(1, \"hello\", \"H\", 1)"));
        Assert.Equal(0.0, Eval("InStr(1, \"hello\", \"a\")"));
    }

    [Theory]
    [InlineData("1 = 1", true)]
    [InlineData("1 <> 1", false)]
    [InlineData("2 > 1 And 1 < 2", true)]
    [InlineData("2 > 1 Or 1 > 2", true)]
    [InlineData("Not 2 > 1", false)]
    [InlineData("\"abc\" Like \"a*\"", true)]
    [InlineData("\"abc\" Like \"a?c\"", true)]
    [InlineData("\"a1c\" Like \"a#c\"", true)]
    [InlineData("\"abc\" Like \"b*\"", false)]
    [InlineData("2 In (1, 2, 3)", true)]
    [InlineData("9 In (1, 2, 3)", false)]
    public void Evaluates_predicates(string expression, bool expected)
        => Assert.Equal(expected, Eval(expression));

    [Fact]
    public void Evaluates_is_null()
    {
        Assert.Equal(true, Eval("[A] Is Null", ("A", null)));
        Assert.Equal(false, Eval("[A] Is Null", ("A", 1.0)));
        Assert.Equal(true, Eval("[A] Is Not Null", ("A", 1.0)));
    }

    [Fact]
    public void Evaluates_dates()
    {
        Assert.Equal(new DateTime(2003, 9, 30), Eval("[D] + 1", ("D", new DateTime(2003, 9, 29))));
        Assert.Equal(2003.0, Eval("Year([D])", ("D", new DateTime(2003, 9, 29))));
        Assert.Equal(9.0, Eval("Month([D])", ("D", new DateTime(2003, 9, 29))));
        Assert.Equal(new DateTime(2020, 1, 1), Eval("DateSerial(2020, 1, 1)"));
    }

    // IsEmpty asks whether a Variant was never initialised, which a stored column value never is. ACE
    // returns False for a column holding a value and for one holding Null alike, so it is a constant.
    [Fact]
    public void IsEmpty_is_always_false_for_a_column()
    {
        Assert.Equal(false, Eval("IsEmpty([A])", ("A", "hello")));
        Assert.Equal(false, Eval("IsEmpty([A])", ("A", null)));
    }

    // Access's Expression Builder offers the '$' name variants and its designer accepts them, but ACE then
    // fails every insert into the table. Refusing them matches ACE; writing a value would not.
    [Theory]
    [InlineData("Left$([A], 2)")]
    [InlineData("Trim$([A])")]
    [InlineData("UCase$([A])")]
    [InlineData("Mid$([A], 2, 3)")]
    public void Refuses_the_dollar_name_variants(string expression)
    {
        var ex = Assert.Throws<CalculatedExpressionException>(() => Eval(expression, ("A", "hello")));
        Assert.Contains("cannot be evaluated in a calculated column", ex.Message);
    }

    // Every function on ACE's whitelist is now implemented, so what has to throw is one ACE itself refuses.
    // Never guess at it: an unnoticed wrong value is the failure mode a cached column cannot recover from.
    [Theory]
    [InlineData("Replace([A], \"a\", \"b\")")]
    [InlineData("Format([A], \"0.00\")")]
    [InlineData("CInt(1.5)")]
    [InlineData("Nz([A], 0)")]
    [InlineData("DateAdd(\"d\", 1, [A])")]
    public void Refuses_a_function_ace_does_not_allow(string expression)
        => Assert.Throws<CalculatedExpressionException>(() => Eval(expression, ("A", "x")));

    // Renaming a column has to repoint every expression that READS it, or the rename quietly breaks a
    // calculated column elsewhere in the table. The rewrite is textual so the author's spacing and bracketing
    // survive, which means it has to know where a name is NOT a column reference.
    [Theory]
    [InlineData("[Qty]*2", "[Amount]*2")]
    [InlineData("Qty*2", "Amount*2")]                              // a bare reference stays bare
    [InlineData("[Qty] & \"Qty\"", "[Amount] & \"Qty\"")]          // not inside a string literal
    [InlineData("[Quantity]*2", "[Quantity]*2")]                   // not a prefix match
    [InlineData("[qty]*2", "[Amount]*2")]                          // Access names are case-insensitive
    [InlineData("Left([Qty], 2)", "Left([Amount], 2)")]            // the function name is left alone
    [InlineData("[Qty]+[Qty]", "[Amount]+[Amount]")]
    [InlineData("[D1] > #2003-09-29#", "[D1] > #2003-09-29#")]
    public void Renames_only_real_references_to_a_column(string expression, string expected)
        => Assert.Equal(expected, CalculatedExpression.RenameColumnReference(expression, "Qty", "Amount"));

    // A new name that is not a plain identifier has to gain brackets, even where the old one had none.
    [Fact]
    public void Brackets_a_renamed_reference_when_the_new_name_needs_them()
        => Assert.Equal("[Order Qty]*2", CalculatedExpression.RenameColumnReference("Qty*2", "Qty", "Order Qty"));

    [Theory]
    [InlineData("MonthName(1)", "January")]
    [InlineData("MonthName(3, True)", "Mar")]
    [InlineData("WeekdayName(1)", "Sunday")]
    [InlineData("WeekdayName(2, True)", "Mon")]
    [InlineData("WeekdayName(1, False, 2)", "Monday")]     // firstDayOfWeek = vbMonday
    public void Evaluates_the_name_functions(string expression, string expected)
        => Assert.Equal(expected, Eval(expression));

    // The ten financial functions, ported from LibRed.Engine's evaluator so that SQL and a calculated column
    // cannot give two answers for one formula. The expectations are the standard annuity and depreciation
    // results, and they cover both the rate = 0 branch and the general one.
    [Theory]
    [InlineData("Pmt(0.005, 60, 10000, 0, 0)", -193.3280)]
    [InlineData("Pmt(0, 10, 1000, 0, 0)", -100.0)]
    [InlineData("FV(0.01, 10, -100, 0, 0)", 1046.2213)]
    [InlineData("PV(0.01, 10, -100, 0, 0)", 947.1305)]
    [InlineData("NPer(0, -100, 1000, 0, 0)", 10.0)]
    [InlineData("Rate(60, -193.3280, 10000, 0, 0, 0.1)", 0.005)]
    [InlineData("IPmt(0.005, 1, 60, 10000, 0, 0)", -50.0)]
    [InlineData("PPmt(0.005, 1, 60, 10000, 0, 0)", -143.3280)]
    [InlineData("SLN(10000, 1000, 5)", 1800.0)]
    [InlineData("SYD(10000, 1000, 5, 1)", 3000.0)]
    [InlineData("DDB(10000, 1000, 5, 2, 2)", 2400.0)]
    public void Evaluates_the_financial_functions(string expression, double expected)
        => Assert.Equal(expected, Assert.IsType<double>(Eval(expression)), 4);

    [Theory]
    [InlineData("[A] +")]
    [InlineData("(1 + 2")]
    [InlineData("\"unterminated")]
    [InlineData("1 ? 2")]
    public void Refuses_malformed_expressions(string expression)
        => Assert.Throws<CalculatedExpressionException>(() => Eval(expression, ("A", 1.0)));

    [Fact]
    public void Reports_the_columns_an_expression_reads()
    {
        var referenced = CalculatedExpression.ReferencedColumns(
            CalculatedExpression.Parse("IIf([Qty] > 1, [A] & [B], Left([A], 2))"));
        Assert.Equal(["A", "B", "Qty"], referenced.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(JetDataType.Int32, 7.0, 7)]
    [InlineData(JetDataType.Int16, 7.0, (short)7)]
    [InlineData(JetDataType.Byte, 7.0, (byte)7)]
    [InlineData(JetDataType.Boolean, 1.0, true)]
    [InlineData(JetDataType.Boolean, 0.0, false)]
    [InlineData(JetDataType.Double, 7.0, 7.0)]
    [InlineData(JetDataType.Single, 7.0, 7f)]
    [InlineData(JetDataType.Text, 7.0, "7")]
    public void Coerces_a_result_to_the_stored_result_type(JetDataType type, object value, object expected)
        => Assert.Equal(expected, CalculatedEvaluator.Coerce(value, type));

    [Fact]
    public void Coercion_keeps_null_as_null()
        => Assert.Null(CalculatedEvaluator.Coerce(null, JetDataType.Int32));
}
