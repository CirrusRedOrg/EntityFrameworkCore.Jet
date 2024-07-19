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
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;

#nullable disable
// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class LoggingJetTest : LoggingRelationalTestBase<JetDbContextOptionsBuilder, JetOptionsExtension>
    {
        [ConditionalFact]
        public virtual void StoredProcedureConcurrencyTokenNotMapped_throws_by_default()
        {
            using var context = new StoredProcedureConcurrencyTokenNotMappedContext(CreateOptionsBuilder(new ServiceCollection()));

            var definition = RelationalResources.LogStoredProcedureConcurrencyTokenNotMapped(CreateTestLogger());
            Assert.Equal(
                CoreStrings.WarningAsErrorTemplate(
                    RelationalEventId.StoredProcedureConcurrencyTokenNotMapped.ToString(),
                    definition.GenerateMessage(nameof(Animal), "Animal_Update", nameof(Animal.Name)),
                    "RelationalEventId.StoredProcedureConcurrencyTokenNotMapped"),
                Assert.Throws<InvalidOperationException>(
                    () => context.Model).Message);
        }

        protected class StoredProcedureConcurrencyTokenNotMappedContext(DbContextOptionsBuilder optionsBuilder) : DbContext(optionsBuilder.Options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => modelBuilder.Entity<Animal>(
                    b =>
                    {
                        b.Ignore(a => a.FavoritePerson);
                        b.Property(e => e.Name).IsRowVersion();
                        b.UpdateUsingStoredProcedure(
                            b =>
                            {
                                b.HasOriginalValueParameter(e => e.Id);
                                b.HasParameter(e => e.Name, p => p.IsOutput());
                                b.HasRowsAffectedReturnValue();
                            });
                    });
        }

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
