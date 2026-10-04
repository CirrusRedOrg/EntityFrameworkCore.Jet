using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Every comparison a query makes orders and equates text in the database's collation (page-02b §3.4), and only
/// by LibRed's own weight tables, so a database answers the same on every platform. Croatian v1 against General v1
/// is the instrument, as in ACE's own measurement: 'č' is a letter after 'c' and 'lj' one after 'l', so General has
/// 'ča' &lt; 'cb' and 'lja' &lt; 'lm' and Croatian the reverse. The expected orders are the ones ACE returns for the
/// same values (CollationQueryProbeTests).
/// </summary>
public class CollationComparisonTests : TempDatabaseTest
{
    private static readonly Collation GeneralV1 = Collation.General;
    private static readonly Collation CroatianV1 = new(CollatingOrder.Croatian, Collation.GeneralVersion);

    private static readonly string[] Values = ["cb", "ča", "ca", "d", "lm", "lja", "l"];

    private static readonly string[] GeneralOrder = ["ca", "ča", "cb", "d", "l", "lja", "lm"];
    private static readonly string[] CroatianOrder = ["ca", "cb", "ča", "d", "l", "lm", "lja"];

    public static TheoryData<string> Orders => ["general", "croatian"];

    private static (QueryEngine Engine, string[] Order) Seeded(string order)
    {
        Collation collation = order == "croatian" ? CroatianV1 : GeneralV1;
        string path = TemporaryDatabase.CreatePath("collation-comparison-");
        JetDatabase.Create(path, collation: collation);
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path));
        e.ExecuteNonQuery("CREATE TABLE T (Id LONG, S TEXT(20))");
        e.ExecuteNonQuery("CREATE TABLE U (K TEXT(20), N LONG)");
        for (int i = 0; i < Values.Length; i++)
        {
            e.ExecuteNonQuery($"INSERT INTO T (Id, S) VALUES ({i}, '{Values[i]}')");
            e.ExecuteNonQuery($"INSERT INTO U (K, N) VALUES ('{Values[i].ToUpperInvariant()}', {i})");
        }
        return (e, order == "croatian" ? CroatianOrder : GeneralOrder);
    }

    private static string[] Read(QueryEngine e, string sql) =>
        [.. e.ExecuteQuery(sql).Rows.Select(r => r[0] is null ? "NULL" : Convert.ToString(r[0], System.Globalization.CultureInfo.InvariantCulture)!)];

    [Theory]
    [MemberData(nameof(Orders))]
    public void Order_by_sorts_in_the_database_collation(string order)
    {
        (QueryEngine e, string[] expected) = Seeded(order);
        Assert.Equal(expected, Read(e, "SELECT S FROM T ORDER BY S"));
        Assert.Equal([.. expected.Reverse()], Read(e, "SELECT S FROM T ORDER BY S DESC"));
        Assert.Equal(expected, Read(e, "SELECT S FROM T ORDER BY S & ''"));
        Assert.Equal(expected.Take(3), Read(e, "SELECT TOP 3 S FROM T ORDER BY S"));
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void Group_by_and_distinct_come_back_in_the_database_collation(string order)
    {
        (QueryEngine e, string[] expected) = Seeded(order);
        Assert.Equal(expected, Read(e, "SELECT S FROM T GROUP BY S"));
        Assert.Equal(expected, Read(e, "SELECT DISTINCT S FROM T ORDER BY S"));
        Assert.Equal(expected, Read(e, "SELECT S FROM T UNION SELECT S FROM T ORDER BY 1"));
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void Min_and_max_are_the_database_collations(string order)
    {
        (QueryEngine e, string[] expected) = Seeded(order);
        Assert.Equal([$"{expected[0]} / {expected[^1]}"], Read(e, "SELECT MIN(S) & ' / ' & MAX(S) FROM T"));
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void A_comparison_against_a_literal_either_way_round_uses_the_database_collation(string order)
    {
        (QueryEngine e, string[] expected) = Seeded(order);
        string[] below = [.. Values.Where(v => Array.IndexOf(expected, v) < Array.IndexOf(expected, "cb"))];
        Assert.Equal(below, Read(e, "SELECT S FROM T WHERE S < 'cb' ORDER BY Id"));
        Assert.Equal(below, Read(e, "SELECT S FROM T WHERE 'cb' > S ORDER BY Id"));
    }

    [Theory]
    [InlineData("general", "general")]
    [InlineData("croatian", "croatian")]
    public void Two_literals_compare_in_the_database_collation(string order, string answer)
    {
        (QueryEngine e, _) = Seeded(order);
        Assert.Equal([answer], Read(e, "SELECT TOP 1 IIF('ča' < 'cb', 'general', 'croatian') FROM T"));
        Assert.Equal([answer], Read(e, "SELECT TOP 1 IIF('lja' < 'lm', 'general', 'croatian') FROM T"));
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void Text_equality_folds_case_in_joins_in_lists_and_lookups(string order)
    {
        (QueryEngine e, _) = Seeded(order);
        // U holds each value upper-cased: an unindexed equi-join is a hash join, whose hash has to agree with '='.
        Assert.Equal(Values.Length, Read(e, "SELECT T.S FROM T INNER JOIN U ON T.S = U.K").Length);
        Assert.Equal(["ča", "lja"], Read(e, "SELECT S FROM T WHERE S IN ('ČA', 'LJA') ORDER BY Id"));
        Assert.Equal(["ča"], Read(e, "SELECT S FROM T WHERE S IN (SELECT K FROM U WHERE N = 1)"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Collation.GeneralVersion)]
    public void Group_by_folds_what_the_collation_folds(byte version)
    {
        // As ACE groups them (CollationQueryProbeTests): 'ß' is 'ss', case and trailing spaces fold, an accent
        // separates — cafe, café, the three spellings of strasse, and x.
        string path = TemporaryDatabase.CreatePath("collation-group-");
        JetDatabase.Create(path, collation: new Collation(CollatingOrder.General, version));
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path));
        e.ExecuteNonQuery("CREATE TABLE G (Id LONG, S TEXT(20))");
        string[] values = ["Straße", "STRASSE", "strasse ", "x", "X", "café", "cafe"];
        for (int i = 0; i < values.Length; i++)
            e.ExecuteNonQuery($"INSERT INTO G (Id, S) VALUES ({i}, '{values[i]}')");

        Assert.Equal(["1", "1", "3", "2"], Read(e, "SELECT COUNT(*) FROM G GROUP BY S"));
        Assert.Equal(["4"], Read(e, "SELECT COUNT(*) FROM (SELECT DISTINCT S FROM G)"));
    }

    [Fact]
    public void The_comparer_for_a_collation_orders_as_that_collation()
    {
        JetTextComparer croatian = JetTextComparer.For(CroatianV1);
        Assert.Same(croatian, JetTextComparer.For(CroatianV1));
        Assert.Equal(-1, croatian.Compare("cb", "ča"));
        Assert.Equal(1, JetTextComparer.For(GeneralV1).Compare("cb", "ča"));
    }
}