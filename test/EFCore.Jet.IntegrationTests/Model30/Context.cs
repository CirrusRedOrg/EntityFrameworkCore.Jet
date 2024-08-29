using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model30
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Answer> Answers { get; set; }
        public DbSet<Question> Questions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Question>()
                .HasMany(q => q.Answers)
                .WithOne(x => x.Question);

            base.OnModelCreating(modelBuilder);

        }
    }
}
