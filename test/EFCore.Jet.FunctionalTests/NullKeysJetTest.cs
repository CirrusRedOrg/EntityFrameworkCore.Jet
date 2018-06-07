using System;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFramework.Jet.FunctionalTests
{
    public class NullKeysJetTest : NullKeysTestBase<NullKeysJetTest.NullKeysJetFixture>
    {
        public NullKeysJetTest(NullKeysJetFixture fixture)
            : base(fixture)
        {
        }

        public class NullKeysJetFixture : NullKeysFixtureBase
        {
            private readonly DbContextOptions _options;

            public NullKeysJetFixture()
            {
                var name = "StringsContext";
                var connectionString = JetTestStore.CreateConnectionString(name);

                _options = new DbContextOptionsBuilder()
                    .UseJet(connectionString, b => b.ApplyConfiguration())
                    .UseInternalServiceProvider(new ServiceCollection()
                        .AddEntityFrameworkJet()
                        .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                        .BuildServiceProvider())
                    .Options;

            }

            public override DbContext CreateContext()
                => new DbContext(_options);

            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
        }
    }
}