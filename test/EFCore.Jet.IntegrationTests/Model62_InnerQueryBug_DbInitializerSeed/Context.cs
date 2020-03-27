using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model62_InnerQueryBug_DbInitializerSeed
{
    public class Context : DbContext
    {
        public Context()
        { }


        public Context(DbConnection connection)
            : base(new DbContextOptionsBuilder<Context>().UseJet(connection).Options)
        { }

        public DbSet<Item> Items { get; set; }
        public DbSet<Sale> Sales { get; set; }
        public DbSet<SaleDetail> SaleDetails { get; set; }

    }

}
