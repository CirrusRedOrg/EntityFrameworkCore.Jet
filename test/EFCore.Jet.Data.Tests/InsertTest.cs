using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class InsertTest
    {
        private const string StoreName = nameof(InsertTest) + ".accdb";

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
        public void InsertTestRun()
        {
            var queries = Helpers.GetQueries(Properties.Resources.InsertTestQueries);
            Assert.AreEqual(4, queries.Length);

            for (var index = 0; index < queries.Length - 1; index++)
            {
                var query = queries[index];
                var reader = Helpers.Execute(_connection, query);
                reader?.Dispose();
            }

            Helpers.Execute(_connection, queries[3]);
        }
    }
}