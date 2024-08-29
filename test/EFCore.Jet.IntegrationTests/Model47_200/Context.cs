using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model47_200
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Dept> Depts { get; set; }
        public DbSet<Emp> Emps { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Dept>()
                .HasOne(_ => _.Manager)
                .WithOne(_ => _.Department)
                .HasForeignKey<Dept>()
                .IsRequired();

        }
    }
}
