using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using EntityFrameworkCore.LibRed.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Threading.Tasks;

namespace EntityFrameworkCore.LibRed.FunctionalTests;

public abstract class JsonTypesLibRedTestBase(NonSharedFixture fixture) : JsonTypesRelationalTestBase(fixture)
{
    public override Task Can_read_write_collection_of_fixed_length_string_JSON_values(object? storeType)
        => base.Can_read_write_collection_of_fixed_length_string_JSON_values("nchar(32)");

    public override Task Can_read_write_collection_of_ASCII_string_JSON_values(object? storeType)
        => base.Can_read_write_collection_of_ASCII_string_JSON_values("varchar(max)");

    protected override ITestStoreFactory NonSharedTestStoreFactory
        => LibRedTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder AddNonSharedOptions(DbContextOptionsBuilder builder)
    {
        builder = base.AddNonSharedOptions(builder)
            .ConfigureWarnings(w => w.Ignore(JetEventId.DecimalTypeDefaultWarning));
        return builder;
    }
}
