// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests
{
    public class CompositeKeyEndToEndLibRedTest(CompositeKeyEndToEndLibRedTest.CompositeKeyEndToEndLibRedFixture fixture)
        : CompositeKeyEndToEndTestBase<
            CompositeKeyEndToEndLibRedTest.CompositeKeyEndToEndLibRedFixture>(fixture)
    {
        public class CompositeKeyEndToEndLibRedFixture : CompositeKeyEndToEndFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;
        }
    }
}
