// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.LibRed.FunctionalTests
{
    public class WithConstructorsLibRedTest(WithConstructorsLibRedTest.WithConstructorsLibRedFixture fixture)
        : WithConstructorsTestBase<WithConstructorsLibRedTest.WithConstructorsLibRedFixture>(fixture)
    {
        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());

        public class WithConstructorsLibRedFixture : WithConstructorsFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                base.OnModelCreating(modelBuilder, context);

                modelBuilder.Entity<BlogQuery>().HasNoKey().ToSqlQuery("SELECT * FROM Blog");
            }
        }
    }
}
