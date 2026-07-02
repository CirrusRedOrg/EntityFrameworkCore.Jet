// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query
{
    public class NullKeysLibRedTest(NullKeysLibRedTest.NullKeysLibRedFixture fixture)
        : NullKeysTestBase<NullKeysLibRedTest.NullKeysLibRedFixture>(fixture)
    {
        public class NullKeysLibRedFixture : NullKeysFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;
        }
    }
}
