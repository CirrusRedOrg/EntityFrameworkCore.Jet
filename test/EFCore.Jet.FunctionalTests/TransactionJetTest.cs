// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
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
                            b => b.ApplyConfiguration().ExecutionStrategy(c => new JetExecutionStrategy(c))))
                .UseInternalServiceProvider(Fixture.ServiceProvider);

            return new DbContext(options.Options);
        }

        public class TransactionJetFixture : TransactionFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
            
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
