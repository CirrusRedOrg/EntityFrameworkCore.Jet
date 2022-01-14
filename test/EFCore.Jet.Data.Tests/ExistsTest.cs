using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class ExistsTest
    {
        private const string StoreName = nameof(ExistsTest) + ".accdb";

        private JetConnection _connection;

        [TestInitialize]
        public void Setup()
        {
            _connection = Helpers.CreateAndOpenDatabase(StoreName);
        }

        [TestCleanup]
        public void TearDown()
        {
            _connection?.Close();
            Helpers.DeleteDatabase(StoreName);
        }

        [TestMethod]
        public void IfExists()
        {
            using var command = _connection.CreateCommand(
                @"IF NOT EXISTS (SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = '__EFMigrationsHistory') THEN CREATE TABLE `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
)");
            command.ExecuteNonQuery();
        }
    }
}