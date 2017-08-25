using System;
using EntityFramework.Jet.FunctionalTests.Utilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFramework.Jet.FunctionalTests
{
    public abstract class GraphUpdatesJetTestBase<TFixture> : GraphUpdatesTestBase<JetTestStore, TFixture>
        where TFixture : GraphUpdatesJetTestBase<TFixture>.GraphUpdatesJetFixtureBase, new()
    {
        protected GraphUpdatesJetTestBase(TFixture fixture)
            : base(fixture)
        {
        }

        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());

        public abstract class GraphUpdatesJetFixtureBase : GraphUpdatesFixtureBase
        {
            private readonly IServiceProvider _serviceProvider;
            private DbContextOptions _options;

            protected GraphUpdatesJetFixtureBase()
            {
                _serviceProvider = new ServiceCollection()
                    .AddEntityFrameworkJet()
                    .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                    .BuildServiceProvider();
            }

            protected abstract string DatabaseName { get; }

            public override JetTestStore CreateTestStore()
            {
                var testStore = JetTestStore.CreateScratch(true);

                _options = new DbContextOptionsBuilder()
                    .UseJet(testStore.Connection, b => b.ApplyConfiguration())
                    .UseInternalServiceProvider(_serviceProvider)
                    .Options;

                using (var context = new GraphUpdatesContext(_options))
                {
                    context.Database.EnsureClean();
                    Seed(context);
                }

                return testStore;
            }

            public override DbContext CreateContext(JetTestStore testStore)
                => new GraphUpdatesContext(_options);
        }
    }
}