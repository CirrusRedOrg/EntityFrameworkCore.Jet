using EntityFrameworkCore.LibRed.Design.Internal;
using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Reflection;

namespace EntityFrameworkCore.LibRed.FunctionalTests;

public class RuntimeMigrationLibRedTest(RuntimeMigrationLibRedTest.RuntimeMigrationLibRedFixture fixture)
    : RuntimeMigrationTestBase<RuntimeMigrationLibRedTest.RuntimeMigrationLibRedFixture>(fixture)
{
    protected override Assembly ProviderAssembly
        => typeof(LibRedDesignTimeServices).Assembly;

    public class RuntimeMigrationLibRedFixture : RuntimeMigrationFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => LibRedTestStoreFactory.Instance;
    }
}
