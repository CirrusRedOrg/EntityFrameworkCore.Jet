using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model07
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base (options)
        { }

        public DbSet<EntityA> As { get; set; }
        public DbSet<EntityB> Bs { get; set; }
        public DbSet<EntityC> Cs { get; set; }
        public DbSet<EntityA_Child> Acs { get; set; }


    }
}
