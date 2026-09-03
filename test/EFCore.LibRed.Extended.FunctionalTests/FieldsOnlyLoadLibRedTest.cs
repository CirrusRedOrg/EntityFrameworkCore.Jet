// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests;

public class FieldsOnlyLoadLibRedTest(FieldsOnlyLoadLibRedTest.FieldsOnlyLoadLibRedFixture fixture)
    : FieldsOnlyLoadTestBase<FieldsOnlyLoadLibRedTest.FieldsOnlyLoadLibRedFixture>(fixture)
{
    public class FieldsOnlyLoadLibRedFixture : FieldsOnlyLoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => LibRedTestStoreFactory.Instance;
    }
}
