using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model10
{
    public class TestContext(DbConnection connection)
        : DbContext(new DbContextOptionsBuilder<TestContext>().UseJet(connection).Options)
    {
        public DbSet<SomeClass> SomeClasses { get; set; }
        public DbSet<Behavior> Behaviors { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SomeClass>()
                .HasOne(s => s.Behavior)
                .WithOne()
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

        }
    }
}
