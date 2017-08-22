using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model55_Unicode
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
