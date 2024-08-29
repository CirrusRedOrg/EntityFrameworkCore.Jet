using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model19_1_1
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<ClassA> As { get; set; }
        public DbSet<ClassB> Bs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 1-1 Has Optional - With Optional
            modelBuilder.Entity<ClassB>()
                .HasOne(c => c.ClassA)
                .WithOne(c => c.ClassB)
                .HasForeignKey<ClassB>(c => c.Id);
        }
    }
}