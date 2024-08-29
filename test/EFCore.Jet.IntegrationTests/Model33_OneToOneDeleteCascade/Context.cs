using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model33_OneToOneDeleteCascade
{
    public class TestContext(DbContextOptions options) : DbContext(options)
    {
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
