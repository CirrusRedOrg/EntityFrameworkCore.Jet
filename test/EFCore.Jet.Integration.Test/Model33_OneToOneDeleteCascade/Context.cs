using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model33_OneToOneDeleteCascade
{
    public class TestContext : DbContext
    {
        public TestContext(DbContextOptions options) :
            base(options)
        {
        }

        public DbSet<Person> Adresses { get; set; }
        public DbSet<Address> Visits { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            /*
            modelBuilder.Entity<Person>()
                .HasOne(s => s.Address)
                .WithOne(ad => ad.Person)
                .IsRequired()
                .WillCascadeOnDelete(true);
            */
            modelBuilder.Entity<Person>()
                .HasOne(s => s.Address)
                .WithOne(ad => ad.Person)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

        }
    }
}
