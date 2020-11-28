using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class UpdateTest
    {
        private const string StoreName = nameof(UpdateTest) + ".accdb";

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
        public void UpdateTestRun()
        {
            var queries = Helpers.GetQueries(Properties.Resources.UpdateTestQueries);
            Assert.AreEqual(6, queries.Length);

            DbDataReader reader;
            for (var index = 0; index < queries.Length - 2; index++)
            {
                var query = queries[index];
                reader = Helpers.Execute(_connection, query);
                reader?.Dispose();
            }

            reader = Helpers.Execute(_connection, queries[4]);
            reader.Read();
            Assert.AreEqual(1, reader.GetInt32(0));
            reader.Dispose();

            Helpers.Execute(_connection, queries[5]);
        }

        [TestMethod]
        public void UpdateTestWithTransactionsRun()
        {
            var queries = Helpers.GetQueries(Properties.Resources.UpdateTestQueries);
            Assert.AreEqual(6, queries.Length);

            DbDataReader reader;
            for (var index = 0; index < queries.Length - 2; index++)
            {
                var transaction = _connection.BeginTransaction();
                var query = queries[index];
                reader = Helpers.Execute(_connection, transaction, query);
                reader?.Dispose();
                transaction.Commit();
            }

            reader = Helpers.Execute(_connection, queries[4]);
            reader.Read();
            Assert.AreEqual(1, reader.GetInt32(0));
            reader.Dispose();

            Helpers.Execute(_connection, queries[5]);
        }
    }
}