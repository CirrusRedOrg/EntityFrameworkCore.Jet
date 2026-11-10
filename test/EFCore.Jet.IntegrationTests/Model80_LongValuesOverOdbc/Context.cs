using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model80_LongValuesOverOdbc
{
    public class Context : DbContext
    {
        public Context(DbConnection connection) :
            base(TestBase<Context>.GetContextOptions(connection))
        {
            TestBase<Context>.TryCreateTables(this);
        }

        public DbSet<Table> Table { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Table>(
                b =>
                {
                    b.Property(x => x.Text).HasColumnType("longchar");
                    b.Property(x => x.Binary).HasColumnType("longbinary");
                    b.ComplexProperty(x => x.Document, d => d.ToJson());
                });
        }
    }
}
