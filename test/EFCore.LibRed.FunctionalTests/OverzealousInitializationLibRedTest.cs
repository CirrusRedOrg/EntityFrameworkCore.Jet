// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests;

public class OverzealousInitializationLibRedTest(
    OverzealousInitializationLibRedTest.OverzealousInitializationLibRedFixture fixture)
    : OverzealousInitializationTestBase<OverzealousInitializationLibRedTest.OverzealousInitializationLibRedFixture>(fixture)
{
    public class OverzealousInitializationLibRedFixture : OverzealousInitializationFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => LibRedTestStoreFactory.Instance;
    }
}
