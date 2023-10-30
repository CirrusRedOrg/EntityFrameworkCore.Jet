// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Reflection;
using EntityFrameworkCore.Jet.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.TestUtilities;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class LoggingJetTest : LoggingRelationalTestBase<JetDbContextOptionsBuilder, JetOptionsExtension>
    {
        protected override DbContextOptionsBuilder CreateOptionsBuilder(
            IServiceCollection services,
            Action<RelationalDbContextOptionsBuilder<JetDbContextOptionsBuilder, JetOptionsExtension>> relationalAction)
            => new DbContextOptionsBuilder()
                .UseInternalServiceProvider(services.AddEntityFrameworkJet().BuildServiceProvider(validateScopes: true))
                .UseJet("Data Source=LoggingJetTest.db", TestEnvironment.DataAccessProviderFactory, relationalAction);

        protected override TestLogger CreateTestLogger()
        => new TestLogger<JetLoggingDefinitions>();
        protected override string DefaultOptions => "DataAccessProviderFactory";
        protected override string ProviderName => "EntityFrameworkCore.Jet";
        protected override string ProviderVersion
            => typeof(JetOptionsExtension).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    }
}
