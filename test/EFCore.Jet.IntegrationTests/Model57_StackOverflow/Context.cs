using System;
using Microsoft.EntityFrameworkCore;

// https://stackoverflow.com/questions/44809921/duplicate-values-in-the-index-primary-key-or-relationship

namespace EntityFrameworkCore.Jet.IntegrationTests.Model57_StackOverflow
{
    public class Context : DbContext
    {
        // For migration test
        public Context()
        { }


        public Context(DbContextOptions options) : base (options)
        { }
        public DbSet<Page> Pages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Page>()
                .HasIndex(_ => _.BookNumber).IsUnique();
        }
    }

}
