using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.BulkUpdates.Inheritance;
using Microsoft.EntityFrameworkCore.Query.Inheritance;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query.Inheritance;

public abstract class TPCInheritanceQueryLibRedFixtureBase : TPCInheritanceQueryFixture
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;

    public override bool UseGeneratedKeys
        => false;
}
