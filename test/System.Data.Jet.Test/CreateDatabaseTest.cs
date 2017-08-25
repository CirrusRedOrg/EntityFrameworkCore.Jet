using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class CreateDatabaseTest
    {
        [TestMethod]
        public void CreateAndDropDatabaseFromConnection()
        {
            string dbFileName = "C:\\TEMP\\Test.accdb";

            File.Delete(dbFileName);

            var connection = new JetConnection(JetConnection.GetConnectionString(dbFileName));
            connection.CreateEmptyDatabase();
            var command = connection.CreateCommand();
            Assert.IsTrue(File.Exists(dbFileName));
            command.CommandText = "DROP DATABASE " + dbFileName;
            command.ExecuteNonQuery();
        }

        [TestMethod]
        public void CreateAndDropDatabaseWithUnsetConnection()
        {
            string dbFileName = "C:\\TEMP\\Test.accdb";

            File.Delete(dbFileName);

            var connection = new JetConnection();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE DATABASE " + dbFileName;
            command.ExecuteNonQuery();
            Assert.IsTrue(File.Exists(dbFileName));
            command.CommandText = "DROP DATABASE " + dbFileName;
            command.ExecuteNonQuery();
        }

    }
}
