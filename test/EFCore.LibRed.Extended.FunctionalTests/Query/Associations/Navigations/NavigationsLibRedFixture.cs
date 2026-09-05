using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Associations.Navigations;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query.Associations.Navigations;

public class NavigationsLibRedFixture : NavigationsRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;
}
