using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model44_CaseSensitivity
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Entity> Entities { get; set; }
    }
}
