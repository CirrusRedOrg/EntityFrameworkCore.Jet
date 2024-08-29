using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model31_DoubleReference
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Person> People { get; set; }
        public DbSet<PhoneNumber> PhoneNumbers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PhoneNumber>().HasOne(e => e.Person).WithMany(e => e.PhoneNumbers);
        }
    }
}
