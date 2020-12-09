﻿using System;
using System.Data.Odbc;
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
    }
}
