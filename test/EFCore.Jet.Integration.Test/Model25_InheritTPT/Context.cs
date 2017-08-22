using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model25_InheritTPT
{
    public class Context : DbContext
    {

        public Context(DbContextOptions options) : base (options)
        {
        }

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
