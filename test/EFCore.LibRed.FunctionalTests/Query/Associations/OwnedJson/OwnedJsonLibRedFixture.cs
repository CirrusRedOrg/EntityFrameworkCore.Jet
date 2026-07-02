using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query.Associations.OwnedJson;

public class OwnedJsonLibRedFixture : OwnedJsonRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
    {
        var options = base.AddOptions(builder);
        return options;
    }
}
