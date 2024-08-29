using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model69_PrivateBackingField
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Info> Infos { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new Info.InfoMap());
        }
    }

}
