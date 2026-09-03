// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests
{
    public class MusicStoreLibRedTest(MusicStoreLibRedTest.MusicStoreLibRedFixture fixture)
        : MusicStoreTestBase<MusicStoreLibRedTest.MusicStoreLibRedFixture>(fixture)
    {
        public class MusicStoreLibRedFixture : MusicStoreFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;
        }
    }
}
