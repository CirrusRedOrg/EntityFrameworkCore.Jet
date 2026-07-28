using System.Globalization;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Access Format(value, format). Named formats and custom numeric/date/string strings, driven off the current
// culture (as ACE drives them off the OS regional settings). Culture is pinned to en-US here so the
// locale-dependent named date/currency formats are deterministic; the values match what ACE produces on an
// en-US host (and the culture-invariant custom strings match ACE on any host).
public class FormatFunctionTests
{
    private static string EvalEnUs(string expr)
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            string path = Path.Combine(Path.GetTempPath(), $"fmt-{Guid.NewGuid():N}.accdb");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
            var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
            e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
            e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
            string r = e.ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0]?.ToString()!;
            try { File.Delete(path); } catch (IOException) { }
            return r;
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Theory]
    // custom numeric
    [InlineData("Format(1234.5, '0.00')", "1234.50")]
    [InlineData("Format(1234.5, '#,##0.00')", "1,234.50")]
    [InlineData("Format(0.25, '0%')", "25%")]
    [InlineData("Format(5, '000')", "005")]
    [InlineData("Format(-5, '000')", "-005")]
    [InlineData("Format(255, '\\#0')", "#255")]
    // named numeric / boolean
    [InlineData("Format(1234.5, 'Currency')", "$1,234.50")]
    [InlineData("Format(1234.5, 'Fixed')", "1234.50")]
    [InlineData("Format(1234.5, 'Standard')", "1,234.50")]
    [InlineData("Format(0.25, 'Percent')", "25.00%")]
    [InlineData("Format(1234.5, 'Scientific')", "1.23E+03")]
    [InlineData("Format(1234.5, 'General Number')", "1234.5")]
    [InlineData("Format(1, 'Yes/No')", "Yes")]
    [InlineData("Format(0, 'Yes/No')", "No")]
    [InlineData("Format(0, 'True/False')", "False")]
    [InlineData("Format(1, 'On/Off')", "On")]
    // custom date (culture-invariant tokens)
    [InlineData("Format(#2020-06-15 13:05:09#, 'yyyy-mm-dd')", "2020-06-15")]
    [InlineData("Format(#2020-06-15 13:05:09#, 'hh:nn:ss')", "13:05:09")]
    [InlineData("Format(#2020-06-15 13:05:09#, 'mmmm d, yyyy')", "June 15, 2020")]
    [InlineData("Format(#2020-06-15 13:05:09#, 'ddd')", "Mon")]
    [InlineData("Format(#2020-06-15#, 'q')", "2")]
    // named date/time (en-US)
    [InlineData("Format(#2020-06-15 13:05:09#, 'Short Date')", "6/15/2020")]
    [InlineData("Format(#2020-06-15 13:05:09#, 'Long Date')", "Monday, June 15, 2020")]
    [InlineData("Format(#2020-06-15 13:05:09#, 'Medium Time')", "01:05 PM")]
    // string
    [InlineData("Format('hello', '>')", "HELLO")]
    [InlineData("Format('hello', '<')", "hello")]
    public void Format_matches_ace_under_en_us(string expr, string expected)
        => Assert.Equal(expected, EvalEnUs(expr));
}
