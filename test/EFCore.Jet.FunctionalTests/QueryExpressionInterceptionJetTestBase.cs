// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public abstract class QueryExpressionInterceptionJetTestBase : QueryExpressionInterceptionTestBase
{
    protected QueryExpressionInterceptionJetTestBase(InterceptionJetFixtureBase fixture)
        : base(fixture)
    {
    }

    public abstract class InterceptionJetFixtureBase : InterceptionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(serviceCollection.AddEntityFrameworkJet(), injectedInterceptors);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        {
            new JetDbContextOptionsBuilder(base.AddOptions(builder))
                .ExecutionStrategy(d => new JetExecutionStrategy(d));
            return builder;
        }
    }

    public class QueryExpressionInterceptionJetTest
        : QueryExpressionInterceptionJetTestBase, IClassFixture<QueryExpressionInterceptionJetTest.InterceptionJetFixture>
    {
        public QueryExpressionInterceptionJetTest(InterceptionJetFixture fixture)
            : base(fixture)
        {
        }

        public class InterceptionJetFixture : InterceptionJetFixtureBase
        {
            protected override string StoreName
                => "QueryExpressionInterception";

            protected override bool ShouldSubscribeToDiagnosticListener
                => false;
        }
    }

    public class QueryExpressionInterceptionWithDiagnosticsJetTest
        : QueryExpressionInterceptionJetTestBase,
            IClassFixture<QueryExpressionInterceptionWithDiagnosticsJetTest.InterceptionJetFixture>
    {
        public QueryExpressionInterceptionWithDiagnosticsJetTest(InterceptionJetFixture fixture)
            : base(fixture)
        {
        }

        public class InterceptionJetFixture : InterceptionJetFixtureBase
        {
            protected override string StoreName
                => "QueryExpressionInterceptionWithDiagnostics";

            protected override bool ShouldSubscribeToDiagnosticListener
                => true;
        }
    }
}
