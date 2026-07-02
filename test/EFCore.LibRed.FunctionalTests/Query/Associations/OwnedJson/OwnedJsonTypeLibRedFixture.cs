using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query.Associations.OwnedJson;

public class OwnedJsonTypeLibRedFixture : OwnedJsonRelationalFixtureBase
{
    protected override string StoreName
        => "OwnedJsonTypeRelationshipsQueryTest";

    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;

    // protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    // {
    //     base.OnModelCreating(modelBuilder, context);

    //     modelBuilder.Entity<RootEntity>().OwnsOne(x => x.RequiredTrunk).HasColumnType("json");
    //     modelBuilder.Entity<RootEntity>().OwnsOne(x => x.OptionalTrunk).HasColumnType("json");
    //     modelBuilder.Entity<RootEntity>().OwnsMany(x => x.CollectionTrunk).HasColumnType("json");
    // }
}
