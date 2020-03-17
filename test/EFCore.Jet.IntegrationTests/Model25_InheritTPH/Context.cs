using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model25_InheritTPH
{
    public class Context : DbContext
    {

        public Context(DbContextOptions options) : base (options)
        {
        }

        public DbSet<Derived1Model> M1s { get; set; }
        public DbSet<Derived2Model> M2s { get; set; }
        public DbSet<Derived3Model> M3s { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //modelBuilder.Entity<User>()
            //    .HasMany(u => u.Advertisements)
            //    .WithRequired(x => x.User);

            //modelBuilder.Entity<Advertisement>()
            //    .HasMany(a => a.AdImages)
            //    .WithRequired(x => x.Advertisement);

            //modelBuilder.Entity<AdImage>()
            //    .HasRequired(x => x.Advertisement);

            base.OnModelCreating(modelBuilder);

        }
    }
}
