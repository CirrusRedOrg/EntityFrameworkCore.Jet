using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Associations.OwnedNavigations;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query.Associations.OwnedNavigations;

public class OwnedNavigationsLibRedFixture : OwnedNavigationsRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;
}
