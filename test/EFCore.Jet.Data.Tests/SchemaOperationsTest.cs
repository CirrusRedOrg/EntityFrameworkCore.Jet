using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class SchemaOperationsTest
    {
        private const string StoreName = nameof(SchemaOperationsTest) + ".accdb";

        private JetConnection _connection;
        private ISchemaOperationsProvider _schemaOperationsProvider;

        [TestInitialize]
        public void Setup()
        {
            _connection = Helpers.CreateAndOpenDatabase(StoreName);

            CreateTable();

            AssertTableExists("tableName");
            AssertColumnExists("tableName", "columnName");
            AssertIndexExists("indexName");
            
            _schemaOperationsProvider = SchemaProvider.CreateInstance(_connection.SchemaProviderType, _connection, false);
        }

        [TestCleanup]
        public void TearDown()
        {
            _schemaOperationsProvider?.Dispose();
            _connection?.Close();
            
            Helpers.DeleteDatabase(StoreName);
        }

        [TestMethod]
        public void CreateDatabase()
        {
            Assert.IsTrue(File.Exists(StoreName));
        }

        [TestMethod]
        public void RenameTable_Provider()
        {
            _schemaOperationsProvider.RenameTable("tableName", "newTableName");
            
            ReOpenConnection();
            AssertTableExists("newTableName");
        }

        [TestMethod]
        public void RenameTable_Query()
        {
            _connection.CreateCommand("rename table tableName to newTableName")
                .ExecuteNonQuery();

            ReOpenConnection();
            AssertTableExists("newTableName");
        }

        [TestMethod]
        public void RenameColumn_Provider()
        {
            _schemaOperationsProvider.RenameColumn("tableName", "columnName", "newColumnName");

            ReOpenConnection();
            AssertColumnExists("tableName", "newColumnName");
        }

        [TestMethod]
        public void RenameColumn_Query()
        {
            _connection.CreateCommand("rename column tableName.columnName to newColumnName")
                .ExecuteNonQuery();

            ReOpenConnection();
            AssertColumnExists("tableName", "newColumnName");
        }

        private void ReOpenConnection()
        {
            _connection.Close();
            JetConnection.ClearAllPools();
            _connection.Open();
        }

        private void AssertTableExists(string tableName)
        {
            using var command = _connection.CreateCommand($"SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = '{tableName}'");
            using var reader = command.ExecuteReader();

            Assert.IsTrue(reader.HasRows, $"Table `{tableName}` not found.");
        }

        private void AssertColumnExists(string tableName, string columnName)
        {
            var command = _connection.CreateCommand($"SELECT * FROM `INFORMATION_SCHEMA.COLUMNS` WHERE `TABLE_NAME` = '{tableName}' AND `COLUMN_NAME` = '{columnName}'");
            using var reader = command.ExecuteReader();

            Assert.IsTrue(reader.HasRows, $"Column `{tableName}`.`{columnName}` not found.");
        }

        private void AssertIndexExists(string indexName)
        {
            var command = _connection.CreateCommand($"SELECT * FROM `INFORMATION_SCHEMA.INDEXES` WHERE `INDEX_NAME` = '{indexName}'");
            using var reader = command.ExecuteReader();

            Assert.IsTrue(reader.HasRows, $"Index `{indexName}` not found.");
        }

        private void CreateTable()
        {
            using var command = _connection.CreateCommand(
                @"
CREATE TABLE tableName (columnName int);
CREATE INDEX indexName ON tableName (columnName);");
            command.ExecuteNonQuery();
        }
    }
}