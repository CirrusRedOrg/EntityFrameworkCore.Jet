using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model11
{
    public class TestContext : DbContext
    {
        public TestContext(DbConnection connection) : base(new DbContextOptionsBuilder<TestContext>().UseJet(connection).Options) { }

        public DbSet<InternCode> InternCodes { get; set; }
        public DbSet<Model> Models { get; set; }
        public DbSet<Version> Versions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new VersionMap());
        }
    }
}
