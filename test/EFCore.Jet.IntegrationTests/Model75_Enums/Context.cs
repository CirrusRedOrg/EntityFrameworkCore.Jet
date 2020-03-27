using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model75_Enums
{
    public class TestContext : DbContext
    {
        public TestContext(DbContextOptions options) : base(options) { }

        public DbSet<Entity> Entities { get; set; }
    }
}
