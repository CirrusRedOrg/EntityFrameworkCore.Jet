using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Associations.OwnedJson;

public class OwnedJsonTypeJetFixture : OwnedJsonRelationalFixtureBase
{
    protected override string StoreName
        => "OwnedJsonTypeRelationshipsQueryTest";

    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    // protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    // {
    //     base.OnModelCreating(modelBuilder, context);

    //     modelBuilder.Entity<RootEntity>().OwnsOne(x => x.RequiredTrunk).HasColumnType("json");
    //     modelBuilder.Entity<RootEntity>().OwnsOne(x => x.OptionalTrunk).HasColumnType("json");
    //     modelBuilder.Entity<RootEntity>().OwnsMany(x => x.CollectionTrunk).HasColumnType("json");
    // }
}
