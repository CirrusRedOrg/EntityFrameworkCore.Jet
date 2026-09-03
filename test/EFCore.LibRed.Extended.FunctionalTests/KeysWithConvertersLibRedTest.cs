// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests;

public class KeysWithConvertersLibRedTest(KeysWithConvertersLibRedTest.KeysWithConvertersLibRedFixture fixture)
    : KeysWithConvertersTestBase<
        KeysWithConvertersLibRedTest.KeysWithConvertersLibRedFixture>(fixture)
{
    public class KeysWithConvertersLibRedFixture : KeysWithConvertersFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => LibRedTestStoreFactory.Instance;

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => builder.UseLibRed();
    }
}
