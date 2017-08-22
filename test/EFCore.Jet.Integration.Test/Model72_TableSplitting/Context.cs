using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model72_TableSplitting
{
    public class Context : DbContext
    {

        public Context(DbContextOptions options) : base (options)
        {
        }

        public DbSet<Product> M1s { get; set; }
        public DbSet<ProductDetails> M2s { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Product>()
                .HasOne(e => e.Details).WithOne(e => e.Product)
                .HasForeignKey<ProductDetails>(e => e.Id);
            modelBuilder.Entity<Product>().ToTable("Products");
            modelBuilder.Entity<ProductDetails>().ToTable("Products");
        }
    }
}
