using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// GenGUID() is Access's GUID generator — the sibling of GenUniqueID(). Like GenUniqueID it is default-only in
// ACE ("Undefined function 'GenGUID' in expression" inside a SELECT) but valid as a GUID column's DEFAULT,
// yielding a fresh Guid per row. EF Core models store-generated Guid keys as HasDefaultValueSql("GenGUID()").
public class GenGuidDefaultTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"gg-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
    }

    [Fact]
    public void Guid_column_defaulted_to_genguid_gets_a_fresh_guid_per_row()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE G (Id LONG CONSTRAINT PK PRIMARY KEY, U GUID DEFAULT GenGUID())");
        e.ExecuteNonQuery("INSERT INTO G (Id) VALUES (1)");   // omit U → default fires
        e.ExecuteNonQuery("INSERT INTO G (Id) VALUES (2)");

        var guids = e.ExecuteQuery("SELECT U FROM G ORDER BY Id").Rows.Select(r => (Guid)r[0]!).ToList();
        Assert.Equal(2, guids.Count);
        Assert.All(guids, g => Assert.NotEqual(Guid.Empty, g));
        Assert.NotEqual(guids[0], guids[1]);   // a fresh Guid per row, not one value reused
    }

    [Fact]
    public void An_explicit_guid_overrides_the_default()
    {
        var e = Fresh();
        var explicitGuid = new Guid("11111111-2222-3333-4444-555555555555");
        e.ExecuteNonQuery("CREATE TABLE G (Id LONG CONSTRAINT PK PRIMARY KEY, U GUID DEFAULT GenGUID())");
        e.ExecuteNonQuery($"INSERT INTO G (Id, U) VALUES (1, {{{explicitGuid}}})");

        Assert.Equal(explicitGuid, (Guid)e.ExecuteQuery("SELECT U FROM G WHERE Id = 1").Rows.Single()[0]!);
    }
}
