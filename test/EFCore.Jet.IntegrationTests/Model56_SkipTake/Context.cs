using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model56_SkipTake
{
    public class Context : DbContext
    {
        // For migration test
        public Context()
        { }


        public Context(DbContextOptions options) : base (options)
        { }
        public DbSet<Entity> Entities { get; set; }
    }

}
