// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests
{
    public class ConferencePlannerLibRedTest(ConferencePlannerLibRedTest.ConferencePlannerLibRedFixture fixture)
        : ConferencePlannerTestBase<ConferencePlannerLibRedTest.ConferencePlannerLibRedFixture
        >(fixture)
    {
        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());

        public class ConferencePlannerLibRedFixture : ConferencePlannerFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;
        }
    }
}
