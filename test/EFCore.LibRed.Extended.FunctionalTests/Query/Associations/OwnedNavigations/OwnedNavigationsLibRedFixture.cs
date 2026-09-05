using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Associations.OwnedNavigations;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query.Associations.OwnedNavigations;

public class OwnedNavigationsLibRedFixture : OwnedNavigationsRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;
}
