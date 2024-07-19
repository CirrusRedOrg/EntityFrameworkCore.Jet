// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Data;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
#nullable disable
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class DefaultValuesTest : IAsyncLifetime
    {
        private readonly IServiceProvider _serviceProvider = new ServiceCollection()
            .AddEntityFrameworkJet()
            .BuildServiceProvider();

        [ConditionalFact]
        public void Can_use_SQL_Server_default_values()
        {
            using (var context = new ChipsContext(_serviceProvider, TestStore.Name))
            {
                context.Database.EnsureCreatedResiliently();

                context.Chippers.Add(
                    new Chipper { Id = "Default" });

                context.SaveChanges();

                var honeyDijon = context.Add(
                    new KettleChips { Name = "Honey Dijon" }).Entity;
                var buffaloBleu = context.Add(
                    new KettleChips { Name = "Buffalo Bleu", BestBuyDate = new DateTime(2111, 1, 11) }).Entity;

                context.SaveChanges();

                Assert.Equal(new DateTime(2035, 9, 25), honeyDijon.BestBuyDate);
                Assert.Equal(new DateTime(2111, 1, 11), buffaloBleu.BestBuyDate);
            }

            using (var context = new ChipsContext(_serviceProvider, TestStore.Name))
            {
                Assert.Equal(new DateTime(2035, 9, 25), context.Chips.Single(c => c.Name == "Honey Dijon").BestBuyDate);
                Assert.Equal(new DateTime(2111, 1, 11), context.Chips.Single(c => c.Name == "Buffalo Bleu").BestBuyDate);
            }
        }

        private class ChipsContext(IServiceProvider serviceProvider, string databaseName) : DbContext
        {
            public DbSet<KettleChips> Chips { get; set; }
            public DbSet<Chipper> Chippers { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder
                    .UseJet(JetTestStore.CreateConnectionString(databaseName), TestEnvironment.DataAccessProviderFactory, b => b.ApplyConfiguration())
                    .UseInternalServiceProvider(serviceProvider);

            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => modelBuilder.Entity<KettleChips>(
                    b =>
                    {
                        b.Property(e => e.BestBuyDate)
                            .ValueGeneratedOnAdd()
                            .HasDefaultValue(new DateTime(2035, 9, 25));

                        b.Property(e => e.ChipperId)
                            .IsRequired()
                            .HasDefaultValue("Default");
                    });
        }

        private class KettleChips
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public DateTime BestBuyDate { get; set; }
            public string ChipperId { get; set; }

            public Chipper Manufacturer { get; set; }
        }

        private class Chipper
        {
            public string Id { get; set; }
        }

        protected JetTestStore TestStore { get; private set; }

        public async Task InitializeAsync()
            => TestStore = await JetTestStore.CreateInitializedAsync("DefaultValuesTest");

        public Task DisposeAsync()
        {
            TestStore.Dispose();
            return Task.CompletedTask;
        }
    }
}
