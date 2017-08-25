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
    public class FieldMappingJetTest
        : FieldMappingTestBase<JetTestStore, FieldMappingJetTest.FieldMappingJetFixture>
    {
        public FieldMappingJetTest(FieldMappingJetFixture fixture)
            : base(fixture)
        {
        }

        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());

        public class FieldMappingJetFixture : FieldMappingFixtureBase
        {
            private const string DatabaseName = "FieldMapping";

            private readonly IServiceProvider _serviceProvider;

            public FieldMappingJetFixture()
            {
                _serviceProvider = new ServiceCollection()
                    .AddEntityFrameworkJet()
                    .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                    .BuildServiceProvider();
            }

            public override JetTestStore CreateTestStore()
            {
                return JetTestStore.GetOrCreateShared(DatabaseName, () =>
                {
                    var optionsBuilder = new DbContextOptionsBuilder()
                        .UseJet(JetTestStore.CreateConnectionString(DatabaseName), b => b.ApplyConfiguration())
                        .UseInternalServiceProvider(_serviceProvider);

                    using (var context = new FieldMappingContext(optionsBuilder.Options))
                    {
                        context.Database.EnsureClean();
                        Seed(context);
                    }
                });
            }

            public override DbContext CreateContext(JetTestStore testStore)
            {
                var optionsBuilder = new DbContextOptionsBuilder()
                    .UseJet(testStore.Connection, b => b.ApplyConfiguration())
                    .UseInternalServiceProvider(_serviceProvider);

                var context = new FieldMappingContext(optionsBuilder.Options);
                context.Database.UseTransaction(testStore.Transaction);

                return context;
            }
        }
    }
}