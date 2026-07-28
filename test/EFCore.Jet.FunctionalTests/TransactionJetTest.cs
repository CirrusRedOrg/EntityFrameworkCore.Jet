// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class TransactionJetTest(TransactionJetTest.TransactionJetFixture fixture)
        : TransactionTestBase<TransactionJetTest.TransactionJetFixture>(fixture)
    {
        protected override bool SnapshotSupported => false;
        protected override bool AmbientTransactionsSupported => false;
        protected override bool DirtyReadsOccur => false;
        protected override bool SavepointsSupported => false;

        protected override DbContext CreateContextWithConnectionString()
        {
            var options = Fixture.AddOptions(
                    new DbContextOptionsBuilder()
                        .UseJet(
                            TestStore.ConnectionString,
                            TestEnvironment.DataAccessProviderFactory,
                            b => b.ApplyConfiguration().UseShortTextForSystemString().ExecutionStrategy(c => new JetExecutionStrategy(c))))
                .UseInternalServiceProvider(Fixture.ServiceProvider);

            return new DbContext(options.Options);
        }

        [Theory(Skip = "Jet does not support savepoints")]
        [InlineData(true)]
        [InlineData(false)]
        public override Task Savepoint_can_be_released(bool async)
        {
            return base.Savepoint_can_be_released(async);
        }

        [Theory(Skip = "Jet does not support savepoints")]
        [InlineData(true)]
        [InlineData(false)]
        public override Task Savepoint_can_be_rolled_back(bool async)
        {
            return base.Savepoint_can_be_rolled_back(async);
        }

        [Theory(Skip = "Jet does not support savepoints")]
        [InlineData(true)]
        [InlineData(false)]
        public override Task Savepoint_name_is_quoted(bool async)
        {
            return base.Savepoint_name_is_quoted(async);
        }

        public class TransactionJetFixture : TransactionFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
            
            public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            {
                new JetDbContextOptionsBuilder(
                        base.AddOptions(builder))
                    .ExecutionStrategy(c => new JetExecutionStrategy(c));
                // Jet doesn't support ambient transactions (like SQLite); the enlisted-transaction conformance
                // test needs the warning logged rather than thrown so it can read it from the log. Configure it
                // here (test-side), not in the provider — matching EFCore.Sqlite's TransactionSqliteTest fixture.
                builder.ConfigureWarnings(w => w.Log(RelationalEventId.AmbientTransactionWarning));
                return builder;
            }
        }
    }
}
