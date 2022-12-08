using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using System.Linq;
using EntityFrameworkCore.Jet.IntegrationTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFramework.Jet.FunctionalTests
{
    [TestClass]
    public class DefaultValuesTest : TestBase<ChipsContext>
    {

        [TestMethod]
        public void Can_use_Jet_default_values()
        {
            Context.Chippers.Add(new Chipper { Id = "Default" });

            Context.SaveChanges();

            var honeyDijon = Context.Add(new KettleChips { Name = "Honey Dijon" }).Entity;
            var buffaloBleu = Context.Add(new KettleChips { Name = "Buffalo Bleu", BestBuyDate = new DateTime(2111, 1, 11) }).Entity;

            Context.SaveChanges();

            Assert.AreEqual(new DateTime(2035, 9, 25), honeyDijon.BestBuyDate);
            Assert.AreEqual(new DateTime(2111, 1, 11), buffaloBleu.BestBuyDate);

            base.DisposeContext();
            base.CreateContext();

            Assert.AreEqual(new DateTime(2035, 9, 25), Context.Chips.Single(c => c.Name == "Honey Dijon").BestBuyDate);
            Assert.AreEqual(new DateTime(2111, 1, 11), Context.Chips.Single(c => c.Name == "Buffalo Bleu").BestBuyDate);
        }


        protected override DbConnection GetConnection()
        {
            string connectionString = JetConnection.GetConnectionString("Chips.accdb", Helpers.DataAccessProviderFactory);
            return new JetConnection(connectionString);
        }
    }

    public class ChipsContext : DbContext
    {
        public ChipsContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<KettleChips> Chips { get; set; }
        public DbSet<Chipper> Chippers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<KettleChips>(b =>
            {
                b.Property(e => e.BestBuyDate)
                    .ValueGeneratedOnAdd()
                    .HasDefaultValue(new DateTime(2035, 9, 25));

                b.Property(e => e.ChipperId)
                    .IsRequired()
                    .HasDefaultValue("Default");

            });
    }

    public class KettleChips
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime BestBuyDate { get; set; }
        public string ChipperId { get; set; }

        public Chipper Manufacturer { get; set; }
    }

    public class Chipper
    {
        public string Id { get; set; }
    }


}
