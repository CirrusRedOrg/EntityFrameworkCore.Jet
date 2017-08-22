using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model42
{
    class Context : DbContext
    {
        public Context(DbConnection connection) : base(new DbContextOptionsBuilder<Context>().UseJet(connection).Options)
        {
            
        }

        public DbSet<Foo> Foos { get; set; }
        public DbSet<Bar> Bars { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Foo>()
                .HasMany(_ => _.Bars)
                .WithMany(_ => _.Foos);
        }
    }
}
