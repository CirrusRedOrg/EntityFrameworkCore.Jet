using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using EntityFrameworkCore.LibRed.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Threading.Tasks;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests;

public abstract class JsonTypesLibRedTestBase(NonSharedFixture fixture) : JsonTypesRelationalTestBase(fixture)
{
    public override Task Can_read_write_collection_of_fixed_length_string_JSON_values(object? storeType)
        => base.Can_read_write_collection_of_fixed_length_string_JSON_values("char(32)");

    public override Task Can_read_write_collection_of_ASCII_string_JSON_values(object? storeType)
        => base.Can_read_write_collection_of_ASCII_string_JSON_values("varchar(255)");

    protected override ITestStoreFactory NonSharedTestStoreFactory
        => LibRedTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder AddNonSharedOptions(DbContextOptionsBuilder builder)
    {
        builder = base.AddNonSharedOptions(builder)
            .ConfigureWarnings(w => w.Ignore(JetEventId.DecimalTypeDefaultWarning));
        return builder;
    }
}
