using System.Data.Common;
using Microsoft.EntityFrameworkCore;


namespace EFCore.Jet.Integration.Test.Model79_CantSaveDecimalValue
{
    public class Context : DbContext
    {

        public Context(DbConnection connection) :
            base(TestBase<Context>.GetContextOptions(connection))
        {
            TestBase<Context>.TryCreateTables(this);
        }

        public DbSet<Table> Table { get; set; }
    }
}