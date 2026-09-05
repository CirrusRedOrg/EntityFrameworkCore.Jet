using LibRed;
using LibRed.Engine;
using LibRed.Engine.Plan;
using Xunit;

namespace LibRed.Engine.Tests;

public class IndexSelectionRefusalTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "seek-refusal-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE T (Id LONG PRIMARY KEY, A LONG, B LONG, V TEXT(20))");
        engine.ExecuteNonQuery("CREATE INDEX IX_AB ON T (A, B)");
        engine.ExecuteNonQuery("INSERT INTO T (Id, A, B, V) VALUES (1, 10, 20, '10')");
        engine.ExecuteNonQuery("INSERT INTO T (Id, A, B, V) VALUES (2, 11, 21, '11')");
        return engine;
    }

    private static bool ContainsSeek(PlanNode node)
        => node is IndexSeekNode or IndexRangeSeekNode || node.Children.Any(ContainsSeek);

    private static bool ContainsHashJoin(PlanNode node)
        => node is HashJoinNode || node.Children.Any(ContainsHashJoin);

    [Theory]
    [InlineData("SELECT Id FROM T WHERE B = 20")]
    [InlineData("SELECT Id FROM T WHERE A = 10")]
    public void A_partially_constrained_composite_index_is_not_used_as_a_point_seek(string sql)
        => Assert.False(ContainsSeek(Fresh().PlanFor(sql)));

    [Fact]
    public void Fully_constraining_the_composite_index_uses_one_point_seek()
    {
        PlanNode plan = Fresh().PlanFor("SELECT Id FROM T WHERE A = 10 AND B = 20");
        var seek = Assert.IsType<IndexSeekNode>(FindSeek(plan));
        Assert.Equal("IX_AB", seek.Index.Name);
        Assert.Equal(2, seek.Keys.Count);
    }

    [Theory]
    [InlineData("SELECT Id FROM T WHERE A > (SELECT MAX(A) FROM T)")]
    [InlineData("SELECT Id FROM T WHERE A = B")]
    public void A_row_or_subquery_dependent_value_is_not_a_seek_bound(string sql)
        => Assert.False(ContainsSeek(Fresh().PlanFor(sql)));

    [Fact]
    public void A_computed_derived_projection_is_not_assumed_hash_compatible()
    {
        QueryEngine engine = Fresh();
        const string sql =
            "SELECT T.Id FROM T INNER JOIN (SELECT A + 1 AS K FROM T) AS d ON T.A = d.K";
        Assert.False(ContainsHashJoin(engine.PlanFor(sql)));
        Assert.Equal(1, engine.ExecuteQuery(sql).Rows.Count());
    }

    [Fact]
    public void Cross_kind_equality_is_not_hashed()
    {
        QueryEngine engine = Fresh();
        const string sql = "SELECT T.Id FROM T INNER JOIN T AS R ON T.A = R.V";
        Assert.False(ContainsHashJoin(engine.PlanFor(sql)));
        Assert.Equal(2, engine.ExecuteQuery(sql).Rows.Count());
    }

    [Theory]
    [InlineData("BINARY(8)", "0x0102030405060708")]
    [InlineData("GUID", "{00112233-4455-6677-8899-AABBCCDDEEFF}")]
    [InlineData("DATETIME", "#2020-01-02#")]
    public void Same_kind_binary_guid_and_temporal_keys_can_hash(string storeType, string literal)
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery($"CREATE TABLE L (K {storeType})");
        engine.ExecuteNonQuery($"CREATE TABLE R (K {storeType})");
        engine.ExecuteNonQuery($"INSERT INTO L (K) VALUES ({literal})");
        engine.ExecuteNonQuery($"INSERT INTO R (K) VALUES ({literal})");

        const string sql = "SELECT L.K FROM L INNER JOIN R ON L.K = R.K";
        Assert.True(ContainsHashJoin(engine.PlanFor(sql)));
        Assert.Single(engine.ExecuteQuery(sql).Rows);
    }

    [Theory]
    [InlineData("SELECT DISTINCT Id FROM T WHERE Id = 1")]
    [InlineData("SELECT TOP 1 Id FROM T WHERE Id = 1 ORDER BY Id")]
    [InlineData("SELECT Id FROM (SELECT Id FROM T WHERE Id = 1) AS d")]
    public void Row_preserving_wrappers_do_not_hide_a_safe_seek(string sql)
        => Assert.True(ContainsSeek(Fresh().PlanFor(sql)));

    private static PlanNode? FindSeek(PlanNode node)
        => node is IndexSeekNode ? node : node.Children.Select(FindSeek).FirstOrDefault(n => n is not null);
}
