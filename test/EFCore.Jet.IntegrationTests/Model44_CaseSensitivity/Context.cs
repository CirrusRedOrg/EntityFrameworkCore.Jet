using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model44_CaseSensitivity
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Entity> Entities { get; set; }
    }
}
