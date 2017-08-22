using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model10
{
    public class TestContext : DbContext
    {
        public TestContext(DbConnection connection) : base(new DbContextOptionsBuilder<TestContext>().UseJet(connection).Options){}

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
