using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model53_TableSplitting
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Person> Persons { get; set; }
        public DbSet<Address> Addresses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Address>()
                .HasKey(t => t.PersonId);
            modelBuilder.Entity<Address>()
                .HasOne(t => t.City)
                .WithMany()
                .HasForeignKey("CityID");

            modelBuilder.Entity<Person>()
                .HasOne(t => t.Address)
                .WithOne(t => t.Person);

            modelBuilder.Entity<Person>()
                .Property(_ => _.PersonId)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<Person>()
                .HasKey(_ => _.PersonId);
                

            modelBuilder.Entity<Person>()
                .Property<int>("AddressId").IsRequired();

                

            modelBuilder.Entity<Person>().ToTable("TB_PERSON");

            modelBuilder.Entity<Address>().ToTable("TB_PERSON");

            modelBuilder.Entity<City>()
                .HasKey(t => t.CityID);
            modelBuilder.Entity<City>()
                .ToTable("City");
        }
    }
}
