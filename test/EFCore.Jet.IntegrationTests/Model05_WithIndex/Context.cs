using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model05_WithIndex
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Foo> Foos { get; set; }
        public DbSet<Bar> Bars { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Bar>()
                .HasIndex(_ => new {_.parent_FooId, _.order}).IsUnique();
    }
}
}
