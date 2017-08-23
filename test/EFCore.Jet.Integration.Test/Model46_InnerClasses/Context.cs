using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model46_InnerClasses
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base(options)
        {
        }

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
