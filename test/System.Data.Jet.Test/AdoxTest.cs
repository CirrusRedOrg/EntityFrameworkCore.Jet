using System.Data.Common;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class AdoxTest
    {
        private const string StoreName = nameof(AdoxTest) + ".accdb";

        private JetConnection _connection;

        [TestInitialize]
        public void Setup()
        {
            _connection = Helpers.CreateAndOpenDatabase(StoreName);
            
            CreateTable();

            CheckTableExists("tableName");
            CheckColumnExists("tableName", "columnName");
            CheckIndexExists("indexName");
        }

        [TestCleanup]
        public void TearDown()
        {
            _connection?.Close();
            Helpers.DeleteDatabase(StoreName);
        }

        [TestMethod]
        public void CreateDatabase()
        {
            Assert.IsTrue(File.Exists(StoreName));
        }

        [TestMethod]
        public void RenameTableAdox()
        {
            AdoxWrapper.RenameTable(JetConnection.GetConnectionString(StoreName, Helpers.DataAccessProviderFactory), "tableName", "newTableName");
            ReOpenConnection();
            CheckTableExists("newTableName");
        }

        [TestMethod]
        public void RenameColumnAdox()
        {
            AdoxWrapper.RenameColumn(JetConnection.GetConnectionString(StoreName, Helpers.DataAccessProviderFactory), "tableName", "columnName", "newColumnName");
            ReOpenConnection();
            CheckColumnExists("tableName", "newColumnName");
        }

        [TestMethod]
        public void RenameTableQuery()
        {
            _connection.CreateCommand("rename table tableName to newTableName")
                .ExecuteNonQuery();

            ReOpenConnection();

            CheckTableExists("newTableName");
        }

        [TestMethod]
        public void RenameColumnQuery()
        {
            _connection.CreateCommand("rename column tableName.columnName to newColumnName")
                .ExecuteNonQuery();

            ReOpenConnection();

            CheckColumnExists("tableName", "newColumnName");
        }

        private void ReOpenConnection()
        {
            _connection.Close();
            JetConnection.ClearAllPools();
            _connection.Open();
        }

        private void CheckTableExists(string tableName)
        {
            var command = _connection.CreateCommand($"SELECT COUNT(*) FROM (SHOW TABLES) WHERE Name='{tableName}'");
            var result = (int) command.ExecuteScalar();
            command.Dispose();
            if (result != 1)
                throw new Exception($"Table {tableName} not found");
        }

        private void CheckColumnExists(string tableName, string columnName)
        {
            var command = _connection.CreateCommand($"SELECT COUNT(*) FROM (SHOW TABLECOLUMNS) WHERE Table='{tableName}' AND Name='{columnName}'");
            var result = (int) command.ExecuteScalar();
            command.Dispose();
            if (result != 1)
                throw new Exception($"Column {tableName}.{columnName} not found");
        }

        private void CheckIndexExists(string indexName)
        {
            var command = _connection.CreateCommand($"SELECT COUNT(*) FROM (SHOW INDEXES) WHERE Name='{indexName}'");
            var result = (int) command.ExecuteScalar();
            command.Dispose();
            if (result != 1)
                throw new Exception($"Index {indexName} not found");
        }

        private void CreateTable()
        {
            DbCommand command;
            command = _connection.CreateCommand(
                @"
CREATE TABLE tableName (columnName int);
CREATE INDEX indexName ON tableName (columnName);");
            command.ExecuteNonQuery();
            command.Dispose();
        }
    }
}