using System.Reflection;
using EntityFrameworkCore.LibRed.Design.Internal;
using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests;

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
