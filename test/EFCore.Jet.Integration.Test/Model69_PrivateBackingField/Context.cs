using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model69_PrivateBackingField
{
    public class Context : DbContext
    {

        public Context(DbContextOptions options)
            : base(options)
        { }

        public DbSet<Info> Infos { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new Info.InfoMap());
        }
    }

}
