using System;
using System.Data.Common;
using System.Data.Jet;
using System.Data.OleDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model78_MigrationUpdate
{
    [TestClass]
    public class Test
    {
       
        protected DbConnection GetConnection()
        {
            // ReSharper disable once CollectionNeverUpdated.Local

            OleDbConnectionStringBuilder oleDbConnectionStringBuilder = new OleDbConnectionStringBuilder();
            //oleDbConnectionStringBuilder.Provider = "Microsoft.Jet.OLEDB.4.0";
            //oleDbConnectionStringBuilder.DataSource = @".\Empty.mdb";
            oleDbConnectionStringBuilder.Provider = "Microsoft.ACE.OLEDB.15.0";
            oleDbConnectionStringBuilder.DataSource = Helpers.GetTestDirectory() + "\\Model78_MigrationUpdateJet.accdb";
            return new JetConnection(oleDbConnectionStringBuilder.ToString());
        }

        [TestMethod]
        public void MigrationUpdateTest()
        {
            using (DbConnection connection = GetConnection())
            {
                using (var context = new Context(connection))
                {
                    string sql = @"
UPDATE[Students_78] SET [StudentName] = '2'
WHERE[StudentId] = 1;
SELECT @@ROWCOUNT; ";
                    context.Database.ExecuteSqlCommand(sql);
                }
            }
        }

    }
}
