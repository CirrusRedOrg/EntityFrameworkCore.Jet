using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model77_DateTimeOffset
{
    class Context : DbContext
    {
        public Context(DbConnection connection) :
            base(TestBase<Context>.GetContextOptions(connection))
        {
            TestBase<Context>.TryCreateTables(this);
        }

        public DbSet<MyEntity> MyEntities { get; set; }

    }
}
