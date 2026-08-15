// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using EntityFrameworkCore.LibRed.Infrastructure;
using EntityFrameworkCore.LibRed.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.LibRed.FunctionalTests
{
    public class TransactionLibRedTest(TransactionLibRedTest.TransactionLibRedFixture fixture)
        : TransactionTestBase<TransactionLibRedTest.TransactionLibRedFixture>(fixture)
    {
        protected override bool SnapshotSupported => false;
        protected override bool AmbientTransactionsSupported => false;
        protected override bool DirtyReadsOccur => false;

        protected override DbContext CreateContextWithConnectionString()
        {
            var options = Fixture.AddOptions(
                    new DbContextOptionsBuilder()
                        .UseLibRed(
                            TestStore.ConnectionString,
                            TestEnvironment.DataAccessProviderFactory,
                            b => b.ApplyConfiguration().UseShortTextForSystemString().ExecutionStrategy(c => new LibRedExecutionStrategy(c))))
                .UseInternalServiceProvider(Fixture.ServiceProvider);

            return new DbContext(options.Options);
        }

        public class TransactionLibRedFixture : TransactionFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;
            
            public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            {
                new LibRedDbContextOptionsBuilder(
                        base.AddOptions(builder))
                    .ExecutionStrategy(c => new LibRedExecutionStrategy(c));
                // LibRed doesn't support ambient transactions (nor does SQLite). The provider leaves EF's
                // default (Throw) so real users still get it; the enlisted-transaction conformance test needs
                // the warning logged instead so it can read it from the log, so downgrade it here — test-side —
                // exactly as EFCore.Sqlite's TransactionSqliteTest fixture does.
                builder.ConfigureWarnings(w => w.Log(RelationalEventId.AmbientTransactionWarning));
                return builder;
            }
        }
    }
}
