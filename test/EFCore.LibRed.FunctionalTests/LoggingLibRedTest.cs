// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Diagnostics.Internal;
using System;
using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using EntityFrameworkCore.LibRed.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using EntityFrameworkCore.LibRed.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Reflection;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;

#nullable disable
// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.LibRed.FunctionalTests
{
    public class LoggingLibRedTest : LoggingRelationalTestBase<LibRedDbContextOptionsBuilder, LibRedOptionsExtension>
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
            Action<RelationalDbContextOptionsBuilder<LibRedDbContextOptionsBuilder, LibRedOptionsExtension>> relationalAction)
            => new DbContextOptionsBuilder()
                .UseInternalServiceProvider(services.AddEntityFrameworkLibRed().BuildServiceProvider(validateScopes: true))
                .UseLibRed("Data Source=LoggingLibRedTest.db", TestEnvironment.DataAccessProviderFactory, relationalAction);

        protected override TestLogger CreateTestLogger()
        => new TestLogger<JetLoggingDefinitions>();
        protected override string DefaultOptions => "DataAccessProviderFactory";
        protected override string ProviderName => "EntityFrameworkCore.LibRed";
        protected override string ProviderVersion
            => typeof(LibRedOptionsExtension).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    }
}
