// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class WithConstructorsJetTest : WithConstructorsTestBase<WithConstructorsJetTest.WithConstructorsJetFixture>
    {
        public WithConstructorsJetTest(WithConstructorsJetFixture fixture)
            : base(fixture)
        {
        }

        [ConditionalFact(Skip = "Issue #16323")]
        public override void Query_with_keyless_type()
            => base.Query_with_keyless_type();

        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());

        public class WithConstructorsJetFixture : WithConstructorsFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                base.OnModelCreating(modelBuilder, context);

                modelBuilder.Entity<BlogQuery>().HasNoKey().ToSqlQuery("SELECT * FROM Blog");
            }
        }
    }
}
