// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.LibRed.FunctionalTests;

public class ConcurrencyDetectorDisabledLibRedTest : ConcurrencyDetectorDisabledRelationalTestBase<
    ConcurrencyDetectorDisabledLibRedTest.ConcurrencyDetectorLibRedFixture>
{
    public ConcurrencyDetectorDisabledLibRedTest(ConcurrencyDetectorLibRedFixture fixture)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
    }

    protected override async Task ConcurrencyDetectorTest(Func<ConcurrencyDetectorDbContext, Task<object>> test)
    {
        await base.ConcurrencyDetectorTest(test);

        Assert.NotEmpty(Fixture.TestSqlLoggerFactory.SqlStatements);
    }

    public class ConcurrencyDetectorLibRedFixture : ConcurrencyDetectorFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => LibRedTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => builder.EnableThreadSafetyChecks(enableChecks: false);
    }
}
