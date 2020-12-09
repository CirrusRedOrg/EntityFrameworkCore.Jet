using System.Data.Odbc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class ConnectionStringTest
    {
        [TestMethod]
        public void Escape_double_quoted_connection_string()
        {
            var expectedDatabaseName = "Joe's \"Recipes\" Database.accdb";
            var escapedDatabaseName = expectedDatabaseName.Replace("\"", "\"\"");
            
            var connectionString = Helpers.DataAccessProviderFactory is OdbcFactory
                ? $"DBQ=\"{escapedDatabaseName}\""
                : $"Data Source=\"{escapedDatabaseName}\"";

            var csb = Helpers.DataAccessProviderFactory.CreateConnectionStringBuilder();
            csb.ConnectionString = connectionString;
            
            var actualDatabaseName = csb.GetDataSource();
            
            Assert.AreEqual(expectedDatabaseName, actualDatabaseName);
        }
        
        [TestMethod]
        public void Escape_single_quoted_connection_string()
        {
            var expectedDatabaseName = "Joe's \"Recipes\" Database.accdb";
            var escapedDatabaseName = expectedDatabaseName.Replace("'", "''");
            
            var connectionString = Helpers.DataAccessProviderFactory is OdbcFactory
                ? $"DBQ='{escapedDatabaseName}'"
                : $"Data Source='{escapedDatabaseName}'";

            using var connection = new JetConnection(connectionString, Helpers.DataAccessProviderFactory);
            
            var csb = Helpers.DataAccessProviderFactory.CreateConnectionStringBuilder();
            csb.ConnectionString = connectionString;
            
            var actualDatabaseName = csb.GetDataSource();
            
            Assert.AreEqual(expectedDatabaseName, actualDatabaseName);
        }
    }
}