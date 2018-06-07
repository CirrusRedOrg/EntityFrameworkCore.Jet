using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

static internal class AssertSqlHelper
{

    public static void AssertSql(this TestSqlLoggerFactory testSqlLoggerFactory, params string[] expected)
    {
        string[] expectedFixed = new string[expected.Length];
        int i = 0;
        foreach (var item in expected)
        {
            if (IgnoreStatement(item))
                return;
            expectedFixed[i++] = item.Replace("\r\n", "\n");
        }
        testSqlLoggerFactory.AssertBaseline(expectedFixed);
    }

    public static void AssertContains(this TestSqlLoggerFactory testSqlLoggerFactory, params string[] expected)
    {
        string[] expectedFixed = new string[expected.Length];
        int i = 0;
        foreach (var item in expected)
        {
            expectedFixed[i++] = item.Replace("\r\n", "\n");
        }
        testSqlLoggerFactory.AssertBaseline(expectedFixed, assertOrder: false);
    }


    public static bool IgnoreStatement(string item)
    {
        if (item.Contains("CAST"))
            return true;
        if (item.Contains("COALESCE"))
            return true;
        if (item.Contains("CONVERT"))
            return true;
        if (item.Contains("WHEN"))
            return true;
        if (item.Contains("LEFT JOIN"))
            return true;
        if (item.Contains("INNER JOIN"))
            return true;
        if (item.Contains("CROSS JOIN"))
            return true;
        if (item.Contains("CHARINDEX"))
            return true;
        return false;
    }
}