using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model32_Include_NotInclude
{
    public class TestContext : DbContext
    {
        public TestContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Address> Adresses { get; set; }
        public DbSet<Visit> Visits { get; set; }

    }
}
