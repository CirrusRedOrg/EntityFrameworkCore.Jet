using System;
using System.Data.Common;
using EFCore.Jet.Integration.Test.Model37_2Contexts_1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EFCore.Jet.Integration.Test.Model37_2Contexts
{
    class Context1 : DbContext
    {
        public Context1(DbConnection connection) :
            base(TestBase<Context1>.GetContextOptions(connection))
        {
            RelationalDatabaseCreator databaseCreator = (RelationalDatabaseCreator)this.Database.GetService<IDatabaseCreator>();
            try
            {
                databaseCreator.CreateTables();
            }
            catch
            {
                // ignored
            }
        }

        public DbSet<MyEntity> MyEntities { get; set; }

    }
}
