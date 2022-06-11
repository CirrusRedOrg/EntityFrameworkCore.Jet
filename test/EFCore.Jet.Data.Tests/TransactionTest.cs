using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class TransactionTest
    {
        private const string StoreName = nameof(TransactionTest) + ".accdb";

        private JetConnection _connection;

        [TestInitialize]
        public void Setup()
        {
            _connection = Helpers.CreateAndOpenDatabase(StoreName);

            var script = @"
CREATE TABLE `Cookies` (
    `CookieId` counter NOT NULL,
    `Name` text NULL,
    CONSTRAINT `PK_Cookies` PRIMARY KEY (`CookieId`)
);

INSERT INTO `Cookies` (`Name`) VALUES ('Basic');
INSERT INTO `Cookies` (`Name`) VALUES ('Chocolate Chip');
";
            
            Helpers.ExecuteScript(_connection, script);
        }

        [TestCleanup]
        public void TearDown()
        {
            _connection?.Close();
            Helpers.DeleteDatabase(StoreName);
        }

        [TestMethod]
        public void Delete_rollback_implicit()
        {
            using (var transaction = _connection.BeginTransaction())
            {
                using var deleteCommand = _connection.CreateCommand();
                deleteCommand.CommandText = @"delete * from `Cookies` where `Name` = 'Basic'";
                deleteCommand.Transaction = transaction;
                var affected = deleteCommand.ExecuteNonQuery();

                Assert.AreEqual(1, affected);
            }

            using var verifyCommand = _connection.CreateCommand();
            verifyCommand.CommandText = @"select count(*) as `Count` from `Cookies`";
            var count = verifyCommand.ExecuteScalar();
            
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void Delete_rollback_explicit()
        {
            using (var transaction = _connection.BeginTransaction())
            {
                using var deleteCommand = _connection.CreateCommand();
                deleteCommand.CommandText = @"delete * from `Cookies` where `Name` = 'Basic'";
                deleteCommand.Transaction = transaction;
                var affected = deleteCommand.ExecuteNonQuery();

                transaction.Rollback();
                
                Assert.AreEqual(1, affected);
            }

            using var verifyCommand = _connection.CreateCommand();
            verifyCommand.CommandText = @"select count(*) as `Count` from `Cookies`";
            var count = verifyCommand.ExecuteScalar();
            
            Assert.AreEqual(2, count);
        }
    }
}