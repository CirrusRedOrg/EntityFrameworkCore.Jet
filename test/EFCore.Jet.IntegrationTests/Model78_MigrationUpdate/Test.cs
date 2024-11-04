using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model78_MigrationUpdate
{
    [TestClass]
    public abstract class Test
    {
        protected abstract DbConnection GetConnection();
        
        [TestMethod]
        public void MigrationUpdateTest()
        {
            using DbConnection connection = GetConnection();
            using var context = new Context(connection);
            string sql = """
                        
                        UPDATE `Students_78` SET `StudentName` = '2'
                        WHERE `StudentId` = 1;
                        SELECT @@ROWCOUNT; 
                        """;
            context.Database.ExecuteSqlRaw(sql);
        }

    }
}
