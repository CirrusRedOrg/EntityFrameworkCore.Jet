using System.Data.Odbc;
using System.Data.OleDb;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class ConnectionStringTest
    {
        [TestMethod]
        [DataRow(DataAccessProviderType.Odbc)]
        [DataRow(DataAccessProviderType.OleDb)]
        public void Escape_double_quoted_connection_string(DataAccessProviderType providerType)
        {
            var expectedDatabaseName = "Joe's \"Recipes\" Database.accdb";
            var escapedDatabaseName = expectedDatabaseName.Replace("\"", "\"\"");

            var connectionString = providerType == DataAccessProviderType.Odbc
                ? $"DBQ=\"{escapedDatabaseName}\""
                : $"Data Source=\"{escapedDatabaseName}\"";

            var csb = new JetConnectionStringBuilder(providerType) { ConnectionString = connectionString };

            Assert.AreEqual(expectedDatabaseName, csb.DataSource);
        }

        [TestMethod]
        [DataRow(DataAccessProviderType.Odbc)]
        [DataRow(DataAccessProviderType.OleDb)]
        public void Escape_single_quoted_connection_string(DataAccessProviderType providerType)
        {
            var expectedDatabaseName = "Joe's \"Recipes\" Database.accdb";
            var escapedDatabaseName = expectedDatabaseName.Replace("'", "''");

            var connectionString = providerType == DataAccessProviderType.Odbc
                ? $"DBQ='{escapedDatabaseName}'"
                : $"Data Source='{escapedDatabaseName}'";

            var csb = new JetConnectionStringBuilder(providerType) { ConnectionString = connectionString };

            Assert.AreEqual(expectedDatabaseName, csb.DataSource);
        }

        [TestMethod]
        public void Odbc_read_connection_string_with_all_properties()
        {
            const string connectionString = @"driver={Microsoft Access Driver (*.mdb, *.accdb)};dbq=ConnectionStringTest.accdb;uid=Admin;pwd=hunter2;systemdb=SysDb";
            var csb = new JetConnectionStringBuilder(DataAccessProviderType.Odbc) { ConnectionString = connectionString };

            Assert.AreEqual(@"Microsoft Access Driver (*.mdb, *.accdb)", csb.Provider);
            Assert.AreEqual(@"ConnectionStringTest.accdb", csb.DataSource);
            Assert.AreEqual("Admin", csb.UserId);
            Assert.AreEqual("hunter2", csb.Password);
            Assert.AreEqual("SysDb", csb.SystemDatabase);
            Assert.IsNull(csb.DatabasePassword);
        }

        [TestMethod]
        public void Odbc_connection_string_with_all_properties()
        {
            var csb = new JetConnectionStringBuilder(DataAccessProviderType.Odbc)
            {
                Provider = "Microsoft Access Driver (*.mdb, *.accdb)",
                DataSource = @"ConnectionStringTest.accdb",
                UserId = "Admin",
                Password = "hunter2",
                SystemDatabase = "SysDb",
                DatabasePassword = "DbPwd",
            };

            Assert.AreEqual("""driver="{Microsoft Access Driver (*.mdb, *.accdb)}";dbq=ConnectionStringTest.accdb;uid=Admin;pwd=DbPwd;systemdb=SysDb""", csb.ConnectionString);
        }

        [TestMethod]
        public void Odbc_connection_string_with_all_properties_from_factory()
        {
            var csb = new JetConnection(OdbcFactory.Instance).JetFactory.CreateConnectionStringBuilder() as JetConnectionStringBuilder;
            Assert.IsNotNull(csb);
            csb.Provider = "Microsoft Access Driver (*.mdb, *.accdb)";
            csb.DataSource = @"ConnectionStringTest.accdb";
            csb.UserId = "Admin";
            csb.Password = "hunter2";
            csb.SystemDatabase = "SysDb";
            csb.DatabasePassword = "DbPwd";

            Assert.AreEqual("""driver="{Microsoft Access Driver (*.mdb, *.accdb)}";dbq=ConnectionStringTest.accdb;uid=Admin;pwd=DbPwd;systemdb=SysDb""", csb.ConnectionString);
        }

        [TestMethod]
        public void OleDb_read_connection_string_with_all_properties()
        {
            const string connectionString = @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=ConnectionStringTest.accdb;User ID=Admin;Password=hunter2;Jet OLEDB:System Database=SysDb;Jet OLEDB:Database Password=DbPwd";
            var csb = new JetConnectionStringBuilder(DataAccessProviderType.OleDb) { ConnectionString = connectionString };

            Assert.AreEqual("Microsoft.ACE.OLEDB.12.0", csb.Provider);
            Assert.AreEqual(@"ConnectionStringTest.accdb", csb.DataSource);
            Assert.AreEqual("Admin", csb.UserId);
            Assert.AreEqual("hunter2", csb.Password);
            Assert.AreEqual("SysDb", csb.SystemDatabase);
            Assert.AreEqual("DbPwd", csb.DatabasePassword);
        }

        [TestMethod]
        public void OleDb_connection_string_with_all_properties()
        {
            var csb = new JetConnectionStringBuilder(DataAccessProviderType.OleDb)
            {
                Provider = "Microsoft.ACE.OLEDB.12.0",
                DataSource = @"ConnectionStringTest.accdb",
                UserId = "Admin",
                Password = "hunter2",
                SystemDatabase = "SysDb",
                DatabasePassword = "DbPwd",
            };

            Assert.AreEqual(@"provider=Microsoft.ACE.OLEDB.12.0;data source=ConnectionStringTest.accdb;user id=Admin;password=hunter2;jet oledb:system database=SysDb;jet oledb:database password=DbPwd", csb.ConnectionString);
        }

        [TestMethod]
        public void OleDb_connection_string_with_all_properties_from_factory()
        {
            var csb = new JetConnection(OleDbFactory.Instance).JetFactory.CreateConnectionStringBuilder() as JetConnectionStringBuilder;
            Assert.IsNotNull(csb);
            csb.Provider = "Microsoft.ACE.OLEDB.12.0";
            csb.DataSource = @"ConnectionStringTest.accdb";
            csb.UserId = "Admin";
            csb.Password = "hunter2";
            csb.SystemDatabase = "SysDb";
            csb.DatabasePassword = "DbPwd";

            Assert.IsTrue(
                csb.ConnectionString is @"provider=Microsoft.ACE.OLEDB.12.0;data source=ConnectionStringTest.accdb;password=hunter2;user id=Admin;jet oledb:system database=SysDb;jet oledb:database password=DbPwd"
                                     or @"provider=Microsoft.ACE.OLEDB.12.0;data source=ConnectionStringTest.accdb;user id=Admin;password=hunter2;jet oledb:system database=SysDb;jet oledb:database password=DbPwd");
        }
    }
}