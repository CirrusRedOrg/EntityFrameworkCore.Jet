using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model54_MemoryLeakageTest
{
    public class Context : DbContext
    {
        // For migration test
        public Context()
        { }


        public Context(DbContextOptions options) : base (options)
        { }
        public DbSet<Student> Students { get; set; }
        public DbSet<Standard> Standards { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Student>().HasIndex(_ => _.StudentName);
            modelBuilder.Entity<Standard>()
                .HasIndex(_ => new { _.StandardName, _.StandardId }).HasDatabaseName("MultipleColumnIndex");

        }
    }
}
