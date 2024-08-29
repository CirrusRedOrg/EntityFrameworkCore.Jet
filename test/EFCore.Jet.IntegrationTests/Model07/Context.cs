using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model07
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<EntityA> As { get; set; }
        public DbSet<EntityB> Bs { get; set; }
        public DbSet<EntityC> Cs { get; set; }
        public DbSet<EntityA_Child> Acs { get; set; }


    }
}
