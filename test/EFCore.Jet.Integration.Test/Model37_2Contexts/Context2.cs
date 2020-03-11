using System.Data.Common;
using EFCore.Jet.Integration.Test.Model37_2Contexts_2;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EFCore.Jet.Integration.Test.Model37_2Contexts
{
    class Context2 : DbContext
    {
        public Context2(DbConnection connection) :
            base(TestBase<Context2>.GetContextOptions(connection))
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