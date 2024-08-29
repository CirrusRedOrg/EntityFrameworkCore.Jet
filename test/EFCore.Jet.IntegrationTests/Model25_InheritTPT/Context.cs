using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model25_InheritTPT
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfiguration(new CompanyMap());
            modelBuilder.ApplyConfiguration(new SupplierMap());
        }


        public DbSet<Company> Companies { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }

    }
}
