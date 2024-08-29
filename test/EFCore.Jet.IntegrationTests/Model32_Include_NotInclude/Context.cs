using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model32_Include_NotInclude
{
    public class TestContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Address> Adresses { get; set; }
        public DbSet<Visit> Visits { get; set; }

    }
}
