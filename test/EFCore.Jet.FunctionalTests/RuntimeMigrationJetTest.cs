using EntityFrameworkCore.Jet.Design.Internal;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Reflection;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class RuntimeMigrationJetTest(RuntimeMigrationJetTest.RuntimeMigrationJetFixture fixture)
    : RuntimeMigrationTestBase<RuntimeMigrationJetTest.RuntimeMigrationJetFixture>(fixture)
{
    protected override Assembly ProviderAssembly
        => typeof(JetDesignTimeServices).Assembly;

    public class RuntimeMigrationJetFixture : RuntimeMigrationFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;
    }
}
