using System.Data.Common;
using EntityFrameworkCore.Jet.IntegrationTests.Model37_2Contexts_1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model37_2Contexts
{
    class Context1 : DbContext
    {
        public Context1(DbConnection connection) :
            base(TestBase<Context1>.GetContextOptions(connection))
        {
            RelationalDatabaseCreator databaseCreator = (RelationalDatabaseCreator) Database.GetService<IDatabaseCreator>();
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