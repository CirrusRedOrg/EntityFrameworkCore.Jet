using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model36_DetachedScenario
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Holder> Holders { get; set; }
        public DbSet<Thing> Things { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Holder>()
                .HasOne(_ => _.Thing)
                .WithMany(_ => _.Holders)
                .HasForeignKey(_ => new[] {"ThingId"});
        }
    }
}
