using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model20_HiddenBackingField
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base (options)
        { }

        public DbSet<Company> Companies { get; set; }
        public DbSet<Employee> Employees { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new Entity.EntityMap());
        }
    }
}
