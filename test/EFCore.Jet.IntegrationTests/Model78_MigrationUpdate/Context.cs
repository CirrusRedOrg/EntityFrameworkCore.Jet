using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model78_MigrationUpdate
{
    public class Context : DbContext
    {

        public Context(DbConnection connection) :
            base(new DbContextOptionsBuilder().UseJet(connection, _ => _.MigrationsAssembly(typeof(Context).Assembly.GetName().Name)).Options)
        {
            TestBase<Context>.TryCreateTables(this);
        }

        public DbSet<Student> Students { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Student>().HasIndex(_ => _.StudentName);
        }
    }
}
