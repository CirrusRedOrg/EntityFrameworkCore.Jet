using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model55_Unicode
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
