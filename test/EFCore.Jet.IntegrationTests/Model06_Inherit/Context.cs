using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model06_Inherit
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Address> Addresses { get; set; }
        public DbSet<User> Users { get; set; }

    }
}
