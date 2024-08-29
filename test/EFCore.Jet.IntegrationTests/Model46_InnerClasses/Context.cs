using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model46_InnerClasses
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<ClassA> ClassAs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ClassA>()
                .OwnsOne(_ => _.B);
            modelBuilder.Entity<ClassA>()
                .OwnsOne(_ => _.C);

        }
    }
}
