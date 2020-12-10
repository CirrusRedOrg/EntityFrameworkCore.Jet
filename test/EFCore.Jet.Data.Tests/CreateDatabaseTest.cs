using System;
using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.OleDb;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class CreateDatabaseTest
    {
        private const string StoreName = nameof(CreateDatabaseTest) + ".accdb";
        
        [TestInitialize]
        public void Setup()
        {
            Helpers.DeleteDatabase(StoreName);
        }

        [TestCleanup]
        public void TearDown()
        {
            Helpers.DeleteDatabase(StoreName);
        }

        [TestMethod]
        public void CreateAndDropDatabaseFromConnection()
        {
            using var connection = new JetConnection(StoreName, Helpers.DataAccessProviderFactory);
            
            connection.CreateDatabase();
            Assert.IsTrue(File.Exists(StoreName));

            connection.DropDatabase();
            Assert.IsFalse(File.Exists(StoreName));
        }

        [TestMethod]
        public void CreateAndDropDatabaseWithUnsetConnection()
        {
            using var connection = new JetConnection(Helpers.DataAccessProviderFactory);
            
            var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE '{StoreName}'";
            command.ExecuteNonQuery();
            Assert.IsTrue(File.Exists(StoreName));
            
            command.CommandText = $"DROP DATABASE '{StoreName}'";
            command.ExecuteNonQuery();
            Assert.IsFalse(File.Exists(StoreName));
        }
        
        [TestMethod]
        public void CreateAndDropDatabaseWithUnsetConnectionWithoutDataAccessProviderFactoryThrows()
        {
            using var connection = new JetConnection();

            Assert.ThrowsException<InvalidOperationException>(
                () => { using var command = connection.CreateCommand(); });
        }
        
        [TestMethod]
        public void CreateAndDropDatabaseWithSingleQuotedConnectionString()
        {
            var connectionString = Helpers.DataAccessProviderFactory is OdbcFactory
                ? $"DBQ='{StoreName}'"
                : $"Data Source='{StoreName}'";
            
            using var connection = new JetConnection(connectionString, Helpers.DataAccessProviderFactory);

            connection.CreateDatabase();
            Assert.IsTrue(File.Exists(StoreName));

            connection.DropDatabase();
            Assert.IsFalse(File.Exists(StoreName));
        }
        
        [TestMethod]
        public void CreateAndDropDatabaseWithDoubleQuotedConnectionString()
        {
            var connectionString = Helpers.DataAccessProviderFactory is OdbcFactory
                ? $"DBQ=\"{StoreName}\""
                : $"Data Source=\"{StoreName}\"";
            
            using var connection = new JetConnection(connectionString, Helpers.DataAccessProviderFactory);
            
            connection.CreateDatabase();
            Assert.IsTrue(File.Exists(StoreName));

            connection.DropDatabase();
            Assert.IsFalse(File.Exists(StoreName));
        }
        
        [TestMethod]
        public void CreateDatabaseWithPassword()
        {
            var password = "Joe's password";
            var escapedPassword = password.Replace("'", "''");
            using var connection = new JetConnection(Helpers.DataAccessProviderFactory);
            
            var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE '{StoreName}' PASSWORD '{escapedPassword}'";
            command.ExecuteNonQuery();
            Assert.IsTrue(File.Exists(StoreName));

            var csb = Helpers.DataAccessProviderFactory.CreateConnectionStringBuilder();
            csb.SetDataSource(StoreName);
            csb.SetDatabasePassword(password);
            
            connection.ConnectionString = csb.ConnectionString;
            connection.Open();
            Assert.IsTrue(connection.State == ConnectionState.Open);
        }
        
        [TestMethod]
        public void CreateDatabaseWithWrongPassword()
        {
            using var connection = new JetConnection(Helpers.DataAccessProviderFactory);
            
            var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE '{StoreName}' PASSWORD 'wrong password'";
            command.ExecuteNonQuery();
            Assert.IsTrue(File.Exists(StoreName));

            var csb = Helpers.DataAccessProviderFactory.CreateConnectionStringBuilder();
            csb.SetDataSource(StoreName);
            csb.SetDatabasePassword("right password");
            
            connection.ConnectionString = csb.ConnectionString;

            try
            {
                connection.Open();
            }
            catch (Exception e)
            {
                Assert.IsInstanceOfType(e, typeof(DbException));
            }
        }
    }
}
