using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// DATETIME column defaults that call Access's date/time functions. Semantics verified against ACE:
//   NOW() / bare Now → current timestamp (date + time)
//   Date()           → today at midnight (date only)
//   Time()           → current time on the Jet epoch 1899-12-30 (time only)
// Bare Date / Time are NOT niladic in Jet SQL (they are reserved type keywords — ACE rejects them; they need
// parentheses), so only Now is recognised without parentheses.
public class DateTimeDefaultTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dtdef-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
    }

    private static DateTime InsertAndRead(string def)
    {
        var e = Fresh();
        e.ExecuteNonQuery($"CREATE TABLE T ( K LONG PRIMARY KEY, V DATETIME DEFAULT {def} )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return Convert.ToDateTime(e.ExecuteQuery("SELECT V FROM T").Rows.Single()[0]);
    }

    [Theory]
    [InlineData("NOW()")]
    [InlineData("Now")]     // niladic, no parentheses — the one bare form ACE accepts
    public void Now_default_is_the_current_timestamp(string def)
    {
        DateTime before = DateTime.Now.AddSeconds(-5);
        DateTime v = InsertAndRead(def);
        DateTime after = DateTime.Now.AddSeconds(5);
        Assert.InRange(v, before, after);
        Assert.NotEqual(v.Date, default);
        Assert.True(v.TimeOfDay > TimeSpan.Zero, "NOW() should carry a time component");
    }

    [Fact]
    public void Date_default_is_today_at_midnight()
    {
        DateTime v = InsertAndRead("Date()");
        Assert.Equal(DateTime.Today, v);
        Assert.Equal(TimeSpan.Zero, v.TimeOfDay);
    }

    [Fact]
    public void Time_default_is_the_current_time_on_the_jet_epoch()
    {
        DateTime v = InsertAndRead("Time()");
        Assert.Equal(new DateTime(1899, 12, 30), v.Date);   // time-only → the Jet epoch date
        Assert.InRange(v.TimeOfDay, DateTime.Now.AddMinutes(-2).TimeOfDay, DateTime.Now.AddMinutes(2).TimeOfDay);
    }

    // A Jet default is a per-row expression, so a compound one works too — LibRed's SQL front-end + evaluator
    // handle string concat (&), literal arithmetic and nested calls, which ACE's OLE DB DDL parser (but not its
    // read-time expression service) rejects. These are the same expressions AceCompoundDefaultTests round-trips
    // through ACE.
    [Theory]
    [InlineData("TEXT(20)", "\"INV-\" & Year(Now())", "INV-2026")]
    [InlineData("TEXT(20)", "'INV-' & Year(Now())", "INV-2026")]
    [InlineData("TEXT(20)", "UCase('hi')", "HI")]
    [InlineData("LONG", "1 + 2", "3")]
    [InlineData("LONG", "Year(Now())", "2026")]
    // Parenthesised forms too — ACE's DDL parser does NOT treat parentheses as an "expression escape" the way
    // SQL Server does (it still rejects (1+2), (Year(Now())), etc.), but LibRed's general expression grammar
    // accepts them uniformly.
    [InlineData("LONG", "(1 + 2)", "3")]
    [InlineData("TEXT(20)", "(\"INV-\" & Year(Now()))", "INV-2026")]
    public void Compound_expression_defaults_are_evaluated(string type, string def, string expected)
    {
        var e = Fresh();
        e.ExecuteNonQuery($"CREATE TABLE T ( K LONG PRIMARY KEY, V {type} DEFAULT {def} )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        object? v = e.ExecuteQuery("SELECT V FROM T").Rows.Single()[0];

        string want = expected.Replace("2026", DateTime.Now.Year.ToString());
        Assert.Equal(want, Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
    }

    // A default cannot reference another column — but only a BRACKETED [A] is a column reference (rejected). A
    // bare word is a literal string (see the bare-word tests), so it never resolves to a column. Verified vs ACE:
    // DEFAULT [A] → "does not recognize the field 'A' ... or the default value".
    [Fact]
    public void A_bracketed_default_cannot_reference_another_column()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, A LONG, B LONG DEFAULT [A] )");
        var ex = Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO T (K, A) VALUES (1, 5)"));
        Assert.Contains("'A' was not found", ex.Message);
    }

    // An unquoted single word in a DEFAULT is a LITERAL STRING (verified vs ACE), never a column reference —
    // DEFAULT Unknown → "Unknown", and DEFAULT K → "K" even when K is a column. The niladic Now is the exception.
    [Theory]
    [InlineData("Unknown", "Unknown")]
    [InlineData("K", "K")]               // a bare word matching a column is still the literal word
    [InlineData("hello", "hello")]
    public void A_bare_word_default_is_a_literal_string(string bareDefault, string expected)
    {
        var e = Fresh();
        e.ExecuteNonQuery($"CREATE TABLE T ( K LONG PRIMARY KEY, V TEXT(50) DEFAULT {bareDefault} )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        Assert.Equal(expected, e.ExecuteQuery("SELECT V FROM T WHERE K = 1").Rows.Single()[0]);
    }

    // A multi-word bare default is a syntax error (matching ACE), rejected at CREATE.
    [Fact]
    public void A_multi_word_bare_default_is_a_syntax_error()
    {
        var e = Fresh();
        Assert.ThrowsAny<Exception>(() =>
            e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, V TEXT(50) DEFAULT this is the default string )"));
    }

    // Aggregate functions (SQL Sum/Count or domain DCount) are not allowed in a default — ACE rejects them as
    // "Unknown function ... in ... default value" (the default evaluator has a restricted function whitelist).
    // LibRed matches by rejecting them: it has no aggregate/DCount case for a scalar (non-grouped) evaluation.
    [Theory]
    [InlineData("Sum(1)")]
    [InlineData("Count(1)")]
    [InlineData("DCount('*', 'MSysObjects')")]
    public void An_aggregate_function_in_a_default_is_rejected(string def)
    {
        var e = Fresh();
        e.ExecuteNonQuery($"CREATE TABLE T ( K LONG PRIMARY KEY, V LONG DEFAULT {def} )");
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)"));
    }

    // A default cannot reference a table/query (subquery) — ACE forbids "references to queries". LibRed rejects
    // it too: the default-expression sub-parser doesn't accept a full SELECT.
    [Theory]
    [InlineData("(SELECT MAX(X) FROM Other)")]
    [InlineData("(SELECT COUNT(*) FROM Other)")]
    public void A_default_cannot_reference_a_table_or_query(string def)
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE Other ( X LONG PRIMARY KEY )");
        e.ExecuteNonQuery($"CREATE TABLE T ( K LONG PRIMARY KEY, V LONG DEFAULT {def} )");
        // Rejected when the default is applied (its text is parsed lazily at insert) — a subquery is never a
        // valid default expression.
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)"));
    }

    [Fact]
    public void A_real_column_named_Now_still_wins_over_the_function()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, [Now] LONG )");
        e.ExecuteNonQuery("INSERT INTO T (K, [Now]) VALUES (1, 42)");
        // The identifier resolves to the column, not the niladic Now function.
        int now = e.ExecuteQuery("SELECT [Now] FROM T").Rows.Single()[0] is { } v ? Convert.ToInt32(v) : -1;
        Assert.Equal(42, now);
    }
}
