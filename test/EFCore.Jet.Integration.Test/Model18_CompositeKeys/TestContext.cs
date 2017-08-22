using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model18_CompositeKeys
{
    public class TestContext : DbContext
    {
        public TestContext(DbContextOptions options)
            : base(options)
        {}

        public DbSet<Product> Products { get; set; }
        public DbSet<GoodsIssueProcess> Processes { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Product>()
                .HasAlternateKey(_ => _.ArticleNumber);
        }
    }
}
