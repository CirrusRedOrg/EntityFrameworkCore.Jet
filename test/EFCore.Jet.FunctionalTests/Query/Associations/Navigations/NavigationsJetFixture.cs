using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Associations.Navigations;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Associations.Navigations;

public class NavigationsJetFixture : NavigationsRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;
}
