// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using EntityFrameworkCore.LibRed.Design.Internal;
using System.Reflection;
using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests;

public class DesignTimeLibRedTest(DesignTimeLibRedTest.DesignTimeLibRedFixture fixture)
    : DesignTimeTestBase<DesignTimeLibRedTest.DesignTimeLibRedFixture>(fixture)
{
    protected override Assembly ProviderAssembly
        => typeof(LibRedDesignTimeServices).Assembly;

    public class DesignTimeLibRedFixture : DesignTimeFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => LibRedTestStoreFactory.Instance;
    }
}
