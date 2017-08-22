using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model47_200
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Dept> Depts { get; set; }
        public DbSet<Emp> Emps { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Dept>()
                .HasOne(_ => _.Manager)
                .WithOne(_ => _.Department);

            modelBuilder.Entity<Dept>()
                .Property(_ => _.Manager).IsRequired();
            modelBuilder.Entity<Emp>()
                .Property(_ => _.Department).IsRequired();

        }
    }
}
