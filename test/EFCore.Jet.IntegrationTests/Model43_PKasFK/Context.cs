using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model43_PKasFK
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Parent> Parents { get; set; }
        public DbSet<Child> Children { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            modelBuilder.Entity<Child>()
                .HasKey(_ => new {_.ParentName, _.ChildName});

            modelBuilder.Entity<Parent>()
                .HasMany(_ => _.Children)
                .WithOne(_ => _.Parent)
                .IsRequired()
                .HasForeignKey(_ => _.ParentName);
        }
    }
}
