static internal class AssertSqlHelper
{
    public static bool IgnoreStatement(string item)
    {
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