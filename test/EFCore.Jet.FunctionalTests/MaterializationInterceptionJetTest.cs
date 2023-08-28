// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

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

public class MaterializationInterceptionJetTest : MaterializationInterceptionTestBase,
    IClassFixture<MaterializationInterceptionJetTest.MaterializationInterceptionJetFixture>
{
    public MaterializationInterceptionJetTest(MaterializationInterceptionJetFixture fixture)
        : base(fixture)
    {
    }

    public class MaterializationInterceptionJetFixture : SingletonInterceptorsFixtureBase
    {
        protected override string StoreName
            => "MaterializationInterception";

        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<ISingletonInterceptor> injectedInterceptors)
            => base.InjectInterceptors(serviceCollection.AddEntityFrameworkJet(), injectedInterceptors);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        {
            new JetDbContextOptionsBuilder(base.AddOptions(builder))
                .ExecutionStrategy(d => new JetExecutionStrategy(d));
            return builder;
        }
    }
}
