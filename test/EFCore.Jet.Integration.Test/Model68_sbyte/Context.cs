using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model68_sbyte
{
    public class Context : DbContext
    {
        public Context()
        { }


        public Context(DbContextOptions options) : base (options)
        { }

        public DbSet<Info> Infos { get; set; }

    }

}
