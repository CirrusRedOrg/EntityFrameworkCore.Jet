using System.Data.Common;
using System.Data.Odbc;
using EntityFrameworkCore.Jet.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model80_LongValuesOverOdbc
{
    [TestClass]
    public class Model80_LongValuesOverOdbc : Test
    {
        // ODBC explicitly: the bug is the Access ODBC driver's, so the test must not follow the default provider.
        protected override DbConnection GetConnection(string storeName)
        {
            var connection = new JetConnection(Helpers.CreateJetDatabase(storeName), OdbcFactory.Instance);
            connection.Open();
            return connection;
        }
    }
}
