// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using EntityFrameworkCore.LibRed.Infrastructure;
using EntityFrameworkCore.LibRed.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests;

public abstract class QueryExpressionInterceptionLibRedTestBase(
    QueryExpressionInterceptionLibRedTestBase.InterceptionLibRedFixtureBase fixture)
    : QueryExpressionInterceptionTestBase(fixture)
{
    public abstract class InterceptionLibRedFixtureBase : InterceptionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => LibRedTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(serviceCollection.AddEntityFrameworkLibRed(), injectedInterceptors);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        {
            new LibRedDbContextOptionsBuilder(base.AddOptions(builder))
                .ExecutionStrategy(d => new LibRedExecutionStrategy(d));
            return builder;
        }
    }

    public class QueryExpressionInterceptionLibRedTest(QueryExpressionInterceptionLibRedTest.InterceptionLibRedFixture fixture)
        : QueryExpressionInterceptionLibRedTestBase(fixture),
            IClassFixture<QueryExpressionInterceptionLibRedTest.InterceptionLibRedFixture>
    {
        public class InterceptionLibRedFixture : InterceptionLibRedFixtureBase
        {
            protected override string StoreName
                => "QueryExpressionInterception";

            protected override bool ShouldSubscribeToDiagnosticListener
                => false;
        }
    }

    public class QueryExpressionInterceptionWithDiagnosticsLibRedTest(
        QueryExpressionInterceptionWithDiagnosticsLibRedTest.InterceptionLibRedFixture fixture)
        : QueryExpressionInterceptionLibRedTestBase(fixture),
            IClassFixture<QueryExpressionInterceptionWithDiagnosticsLibRedTest.InterceptionLibRedFixture>
    {
        public class InterceptionLibRedFixture : InterceptionLibRedFixtureBase
        {
            protected override string StoreName
                => "QueryExpressionInterceptionWithDiagnostics";

            protected override bool ShouldSubscribeToDiagnosticListener
                => true;
        }
    }
}
