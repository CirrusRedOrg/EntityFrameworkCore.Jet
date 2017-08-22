using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model58_TruncateTime
{
    public class Context : DbContext
    {
        // For migration test
        public Context()
        { }


        public Context(DbConnection connection)
            : base(new DbContextOptionsBuilder<Context>().UseJet(connection).Options)
        { }
        public DbSet<Entity> Entities { get; set; }
    }

}
