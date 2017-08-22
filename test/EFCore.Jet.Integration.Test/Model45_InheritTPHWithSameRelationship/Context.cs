using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model45_InheritTPHWithSameRelationship
{
    public class Context : DbContext
    {

        public Context(DbContextOptions options) : base (options)
        {
        }

        // For TPH
        public DbSet<Base> Bases { get; set; }

        public DbSet<Inherited1> M1s { get; set; }
        public DbSet<Inherited2> M2s { get; set; }
        public DbSet<Type1> T1s { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Inherited1>()
                .HasOne(_ => _.Rel)
                .WithMany()
                .IsRequired()
                .HasForeignKey("Rel_Id");

            modelBuilder.Entity<Inherited2>()
                .HasOne(_ => _.Rel)
                .WithMany()
                .IsRequired()
                .HasForeignKey("Rel_Id");

        }
    }
}
