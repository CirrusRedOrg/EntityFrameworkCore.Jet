using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model06_Inherit
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base (options)
        { }

        public DbSet<Address> Addresses { get; set; }
        public DbSet<User> Users { get; set; }

    }
}
