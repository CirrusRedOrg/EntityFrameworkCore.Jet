// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class MigrationsJetFixture : MigrationsInfrastructureFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

        public MigrationsJetFixture()
        {
            ((JetTestStore)TestStore).ExecuteNonQuery(
                @"USE master
IF EXISTS(select * from sys.databases where name='TransactionSuppressed')
DROP DATABASE TransactionSuppressed");
        }

        public override MigrationsContext CreateContext()
        {
            var options = AddOptions(
                    new DbContextOptionsBuilder()
                        .UseJet(TestStore.ConnectionString, TestEnvironment.DataAccessProviderFactory, b => b.ApplyConfiguration()))
                .UseInternalServiceProvider(ServiceProvider)
                .Options;
            return new MigrationsContext(options);
        }
    }
}
