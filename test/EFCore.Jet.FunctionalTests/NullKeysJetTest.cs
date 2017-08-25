using System;
using EntityFramework.Jet.FunctionalTests.Utilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFramework.Jet.FunctionalTests
{
    public class NullKeysJetTest : NullKeysTestBase<NullKeysJetTest.NullKeysJetFixture>
    {
        public NullKeysJetTest(NullKeysJetFixture fixture)
            : base(fixture)
        {
        }

        public class NullKeysJetFixture : NullKeysFixtureBase, IDisposable
        {
            private readonly DbContextOptions _options;
            private readonly JetTestStore _testStore;

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

                _testStore = JetTestStore.GetOrCreateShared(name, EnsureCreated);
            }

            public override DbContext CreateContext()
                => new DbContext(_options);

            public void Dispose() => _testStore.Dispose();
        }
    }
}