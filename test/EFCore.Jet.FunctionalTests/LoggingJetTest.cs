// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class LoggingJetTest : LoggingRelationalTestBase<JetDbContextOptionsBuilder, JetOptionsExtension>
    {
        protected override DbContextOptionsBuilder CreateOptionsBuilder(
            IServiceCollection services,
            Action<RelationalDbContextOptionsBuilder<JetDbContextOptionsBuilder, JetOptionsExtension>> relationalAction)
            => new DbContextOptionsBuilder()
                .UseInternalServiceProvider(services.AddEntityFrameworkJet().BuildServiceProvider())
                .UseJet("Data Source=LoggingJetTest.db", relationalAction);

        protected override string ProviderName => "EntityFrameworkCore.Jet";
    }
}
