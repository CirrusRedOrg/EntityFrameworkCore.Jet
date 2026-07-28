using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using Xunit;

namespace LibRed.Core.Tests;

public class Ace16TypeTests
{
    [Fact]
    public void Database_is_the_newer_ace_format()
    {
        using var db = JetDatabase.Open(TestDatabases.Ace16TypesAccdb);

        // The ACE 16 provider creates the newer format (version byte 0x06).
        Assert.Equal(JetVersion.Version17_2019, db.Format.Version);
        Assert.True(db.Format.IsAccdb);
    }

    [Fact]
    public void Decodes_bigint_columns()
    {
        using var db = JetDatabase.Open(TestDatabases.Ace16TypesAccdb);

        var table = db.OpenTable("T");
        var big = table.Definition.Columns.First(c => c.Name == "Big");
        Assert.Equal(JetDataType.Int64, big.Type);

        var values = table.Rows().Select(r => r[big.Index]).ToList();
        Assert.All(values, v => Assert.IsType<long>(v));
        Assert.Equal([9223372036854775807L, -42L, 0L, 1234567890123456789L], values.Cast<long>());
    }

    [Fact]
    public void Decodes_datetime2_columns_with_subsecond_precision()
    {
        using var db = JetDatabase.Open(TestDatabases.Ace16TypesAccdb);

        var table = db.OpenTable("T");
        var dt = table.Definition.Columns.First(c => c.Name == "Dt");
        Assert.Equal(JetDataType.DateTimeExtended, dt.Type);

        var values = table.Rows().Select(r => (DateTime)r[dt.Index]!).ToList();
        Assert.Equal(
            [
                new DateTime(2020, 6, 15, 13, 45, 30),
                new DateTime(1899, 12, 30, 0, 0, 0),     // the Jet epoch
                new DateTime(2000, 1, 1, 12, 0, 0),
                new DateTime(2021, 3, 4, 9, 8, 7).AddTicks(1234567), // 100-ns precision
            ],
            values);
    }
}
