// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.Reflection;
using EntityFrameworkCore.Jet.Design.Internal;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class DesignTimeJetTest : DesignTimeTestBase<DesignTimeJetTest.DesignTimeJetFixture>
{
    public DesignTimeJetTest(DesignTimeJetFixture fixture)
        : base(fixture)
    {
    }

    protected override Assembly ProviderAssembly
        => typeof(JetDesignTimeServices).Assembly;

    public class DesignTimeJetFixture : DesignTimeFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;
    }
}
