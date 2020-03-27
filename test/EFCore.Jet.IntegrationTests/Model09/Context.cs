using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model09
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base (options)
        { }

        public DbSet<One> Ones { get; set; }
        public DbSet<Two> Twos { get; set; }
        public DbSet<Three> Threes { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfiguration(new OneMap());
            modelBuilder.ApplyConfiguration(new TwoMap());
            modelBuilder.ApplyConfiguration(new ThreeMap());
        }
    }
}