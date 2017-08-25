using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFramework.Jet.FunctionalTests
{
    public class TransactionJetFixture : TransactionFixtureBase<JetTestStore>
    {
        private readonly IServiceProvider _serviceProvider;

        public TransactionJetFixture()
        {
            _serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                .BuildServiceProvider();
        }

        public override JetTestStore CreateTestStore()
        {
            var db = JetTestStore.CreateScratch(createDatabase: true);

            using (var context = CreateContext(db))
            {
                context.Database.EnsureClean();
            }

            return db;
        }

        public override DbContext CreateContext(JetTestStore testStore)
        {
            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder
                .UseJet(testStore.Connection.ConnectionString)
                .UseInternalServiceProvider(_serviceProvider);

            return new DbContext(optionsBuilder.Options);
        }

        public override DbContext CreateContext(DbConnection connection)
        {
            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder
                .UseJet(connection)
                .UseInternalServiceProvider(_serviceProvider);

            return new DbContext(optionsBuilder.Options);
        }
    }
}
