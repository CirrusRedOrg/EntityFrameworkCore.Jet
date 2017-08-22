using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model06_Inherit
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base (options)
        { }

        public DbSet<Address> Addresses { get; set; }
        public DbSet<User> Users { get; set; }

    }
}
