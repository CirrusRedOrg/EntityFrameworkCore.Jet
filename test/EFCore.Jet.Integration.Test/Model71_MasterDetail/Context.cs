using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model71_MasterDetail
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options)
            : base(options)
        { }

        public DbSet<Master> Masters { get; set; }
        public DbSet<Detail> Details { get; set; }
    }

}
