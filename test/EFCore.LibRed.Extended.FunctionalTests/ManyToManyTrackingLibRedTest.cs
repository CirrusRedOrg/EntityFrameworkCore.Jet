// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests;

public class ManyToManyTrackingLibRedTest(ManyToManyTrackingLibRedTest.ManyToManyTrackingLibRedFixture fixture)
    : ManyToManyTrackingLibRedTestBase<ManyToManyTrackingLibRedTest.ManyToManyTrackingLibRedFixture>(fixture)
{
    public class ManyToManyTrackingLibRedFixture : ManyToManyTrackingLibRedFixtureBase
    {
        protected override string StoreName
            => "ManyToManyTrackingLibRedTest";
    }
}
