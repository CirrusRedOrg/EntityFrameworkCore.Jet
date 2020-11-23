using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class DdlTest
    {
        private const string StoreName = nameof(DdlTest) + ".accdb";

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
        public void CheckIfTablesExists()
        {
            var queries = Helpers.GetQueries(Properties.Resources.CheckIfTableExistsTestQueries);

            Helpers.Execute(_connection, queries[0]);

            var exists = _connection.TableExists("CheckIfTableExistsTable");
            Assert.IsTrue(exists);

            Helpers.Execute(_connection, queries[1]);
        }
    }
}