﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class FieldsOnlyLoadJetTest(FieldsOnlyLoadJetTest.FieldsOnlyLoadJetFixture fixture)
    : FieldsOnlyLoadTestBase<FieldsOnlyLoadJetTest.FieldsOnlyLoadJetFixture>(fixture)
{
    public class FieldsOnlyLoadJetFixture : FieldsOnlyLoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;
    }
}
