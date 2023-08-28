// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class ManyToManyTrackingJetTest
    : ManyToManyTrackingJetTestBase<ManyToManyTrackingJetTest.ManyToManyTrackingJetFixture>
{
    public ManyToManyTrackingJetTest(ManyToManyTrackingJetFixture fixture)
        : base(fixture)
    {
    }

    public class ManyToManyTrackingJetFixture : ManyToManyTrackingJetFixtureBase
    {
        protected override string StoreName
            => "ManyToManyTrackingJetTest";
    }
}
