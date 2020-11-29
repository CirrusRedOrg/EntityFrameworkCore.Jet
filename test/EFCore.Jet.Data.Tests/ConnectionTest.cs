using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class OdbcConnectionTest
    {
        private const string StoreName = nameof(OdbcConnectionTest) + ".accdb";

        [TestInitialize]
        public void Setup()
        {
            Helpers.CreateDatabase(StoreName);
        }

        [TestCleanup]
        public void TearDown()
        {
            Helpers.DeleteDatabase(StoreName);
        }

        [TestMethod]
        public void Open_odbc_connection_uppercase_dbq_mdbtools_workaround()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, DataAccessProviderType.Odbc));
            connection.Open();

            Assert.IsTrue(connection.ConnectionString.Contains("DBQ=", StringComparison.Ordinal));
        }
   }
}