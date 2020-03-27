// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data.Jet;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class TransactionJetTest : TransactionTestBase<TransactionJetTest.TransactionJetFixture>
    {
        public TransactionJetTest(TransactionJetFixture fixture)
            : base(fixture)
        {
        }

        protected override bool SnapshotSupported => true;

        protected override bool AmbientTransactionsSupported => true;

        protected override DbContext CreateContextWithConnectionString()
        {
            var options = Fixture.AddOptions(
                    new DbContextOptionsBuilder()
                        .UseJet(
                            TestStore.ConnectionString,
                            TestEnvironment.DataAccessProviderFactory,
                            b => b.ApplyConfiguration().ExecutionStrategy(c => new JetExecutionStrategy(c))))
                .UseInternalServiceProvider(Fixture.ServiceProvider);

            return new DbContext(options.Options);
        }

        public class TransactionJetFixture : TransactionFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            protected override void Seed(PoolableDbContext context)
            {
                base.Seed(context);

                context.Database.ExecuteSqlRaw("ALTER DATABASE [" + StoreName + "] SET ALLOW_SNAPSHOT_ISOLATION ON");
                context.Database.ExecuteSqlRaw("ALTER DATABASE [" + StoreName + "] SET READ_COMMITTED_SNAPSHOT ON");
            }

            public override void Reseed()
            {
                using (var context = CreateContext())
                {
                    context.Set<TransactionCustomer>().RemoveRange(context.Set<TransactionCustomer>());
                    context.SaveChanges();

                    base.Seed(context);
                }
            }

            public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            {
                new JetDbContextOptionsBuilder(
                        base.AddOptions(builder))
                    .ExecutionStrategy(c => new JetExecutionStrategy(c));
                return builder;
            }
        }
    }
}
