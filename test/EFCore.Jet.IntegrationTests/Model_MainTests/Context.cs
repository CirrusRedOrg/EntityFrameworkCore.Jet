using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model_MainTests
{
    public class TestContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Entity> Entities { get; set; }
    }
}
