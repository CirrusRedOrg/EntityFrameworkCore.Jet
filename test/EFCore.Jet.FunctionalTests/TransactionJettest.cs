using System.Threading.Tasks;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class TransactionJetTest : TransactionTestBase<TransactionJetTest.TransactionJetFixture>
    {
        public TransactionJetTest(TransactionJetFixture fixture)
            : base(fixture)
        {
        }

        protected override DbContext CreateContextWithConnectionString()
        {
            var options = Fixture.AddOptions(
                    new DbContextOptionsBuilder()
                        .UseJet(TestStore.ConnectionString, b => b.ApplyConfiguration().CommandTimeout(JetTestStore.CommandTimeout)))
                .UseInternalServiceProvider(Fixture.ServiceProvider);

            return new DbContext(options.Options);
        }

        protected override bool SnapshotSupported => false;


        [Theory(Skip = "Unsupported by JET")]
        public override Task Can_use_open_connection_with_started_transaction(bool a) { return Task.CompletedTask; }
        [Theory(Skip = "Unsupported by JET")]
        public override Task QueryAsync_uses_explicit_transaction(bool a) { return Task.CompletedTask; }
        [Theory(Skip = "Unsupported by JET")]
        public override void Query_uses_explicit_transaction(bool a) { }


        public class TransactionJetFixture : TransactionFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

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
                        base.AddOptions(builder)
                            .ConfigureWarnings(
                                w => w.Log(RelationalEventId.QueryClientEvaluationWarning)
                                    .Log(CoreEventId.FirstWithoutOrderByAndFilterWarning)))
                    .MaxBatchSize(1);
                return builder;
            }
        }

    }
}
