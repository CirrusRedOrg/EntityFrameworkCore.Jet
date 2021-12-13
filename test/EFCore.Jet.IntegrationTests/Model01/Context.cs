using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model01
{
    public class Context : DbContext
    {

        public Context(DbContextOptions options) :
            base(options)
        {
        }

        public DbSet<Student> Students { get; set; }
        public DbSet<Standard> Standards { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Standard>()
                .HasIndex(_ => new {_.StandardName, _.StandardId}).HasDatabaseName("MultipleColumnIndex");
            modelBuilder.Entity<Student>().HasIndex(_ => _.StudentName);
        }
    }
}
