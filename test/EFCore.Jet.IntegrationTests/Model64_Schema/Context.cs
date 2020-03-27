using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model64_Schema
{
    public class Context : DbContext
    {
        public Context()
        { }


        public Context(DbContextOptions options) : base (options)
        { }

        public DbSet<Item> Items { get; set; }

    }

}
