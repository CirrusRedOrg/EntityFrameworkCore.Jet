using System.Data.Odbc;
using System.Data.OleDb;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class ConnectionStringTest
    {
        [DataTestMethod]
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

        [DataTestMethod]
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
            const string connectionString = @"driver={Microsoft Access Driver (*.mdb, *.accdb)};dbq=C:\myFolder\myAccessFile.accdb;uid=Admin;pwd=hunter2;systemdb=SysDb";
            var csb = new JetConnectionStringBuilder(DataAccessProviderType.Odbc) { ConnectionString = connectionString };

            Assert.AreEqual(csb.Provider, @"Microsoft Access Driver (*.mdb, *.accdb)");
            Assert.AreEqual(csb.DataSource, @"C:\myFolder\myAccessFile.accdb");
            Assert.AreEqual(csb.UserId, "Admin");
            Assert.AreEqual(csb.Password, "hunter2");
            Assert.AreEqual(csb.SystemDatabase, "SysDb");
            Assert.IsNull(csb.DatabasePassword);
        }

        [TestMethod]
        public void Odbc_connection_string_with_all_properties()
        {
            var csb = new JetConnectionStringBuilder(DataAccessProviderType.Odbc)
            {
                Provider = "Microsoft Access Driver (*.mdb, *.accdb)",
                DataSource = @"C:\myFolder\myAccessFile.accdb",
                UserId = "Admin",
                Password = "hunter2",
                SystemDatabase = "SysDb",
                DatabasePassword = "DbPwd",
            };

            Assert.AreEqual(@"driver=""{Microsoft Access Driver (*.mdb, *.accdb)}"";dbq=C:\myFolder\myAccessFile.accdb;uid=Admin;pwd=DbPwd;systemdb=SysDb", csb.ConnectionString);
        }

        [TestMethod]
        public void Odbc_connection_string_with_all_properties_from_factory()
        {
            var csb = new JetConnection(OdbcFactory.Instance).JetFactory.CreateConnectionStringBuilder() as JetConnectionStringBuilder;
            Assert.IsNotNull(csb);
            csb.Provider = "Microsoft Access Driver (*.mdb, *.accdb)";
            csb.DataSource = @"C:\myFolder\myAccessFile.accdb";
            csb.UserId = "Admin";
            csb.Password = "hunter2";
            csb.SystemDatabase = "SysDb";
            csb.DatabasePassword = "DbPwd";

            Assert.AreEqual(@"driver=""{Microsoft Access Driver (*.mdb, *.accdb)}"";dbq=C:\myFolder\myAccessFile.accdb;uid=Admin;pwd=DbPwd;systemdb=SysDb", csb.ConnectionString);
        }

        [TestMethod]
        public void OleDb_read_connection_string_with_all_properties()
        {
            const string connectionString = @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\myFolder\myAccessFile.accdb;User ID=Admin;Password=hunter2;Jet OLEDB:System Database=SysDb;Jet OLEDB:Database Password=DbPwd";
            var csb = new JetConnectionStringBuilder(DataAccessProviderType.OleDb) { ConnectionString = connectionString };

            Assert.AreEqual(csb.Provider, "Microsoft.ACE.OLEDB.12.0");
            Assert.AreEqual(csb.DataSource, @"C:\myFolder\myAccessFile.accdb");
            Assert.AreEqual(csb.UserId, "Admin");
            Assert.AreEqual(csb.Password, "hunter2");
            Assert.AreEqual(csb.SystemDatabase, "SysDb");
            Assert.AreEqual(csb.DatabasePassword, "DbPwd");
        }

        [TestMethod]
        public void OleDb_connection_string_with_all_properties()
        {
            var csb = new JetConnectionStringBuilder(DataAccessProviderType.OleDb)
            {
                Provider = "Microsoft.ACE.OLEDB.12.0",
                DataSource = @"C:\myFolder\myAccessFile.accdb",
                UserId = "Admin",
                Password = "hunter2",
                SystemDatabase = "SysDb",
                DatabasePassword = "DbPwd",
            };

            Assert.AreEqual(@"provider=Microsoft.ACE.OLEDB.12.0;data source=C:\myFolder\myAccessFile.accdb;user id=Admin;password=hunter2;jet oledb:system database=SysDb;jet oledb:database password=DbPwd", csb.ConnectionString);
        }

        [TestMethod]
        public void OleDb_connection_string_with_all_properties_from_factory()
        {
            var csb = new JetConnection(OleDbFactory.Instance).JetFactory.CreateConnectionStringBuilder() as JetConnectionStringBuilder;
            Assert.IsNotNull(csb);
            csb.Provider = "Microsoft.ACE.OLEDB.12.0";
            csb.DataSource = @"C:\myFolder\myAccessFile.accdb";
            csb.UserId = "Admin";
            csb.Password = "hunter2";
            csb.SystemDatabase = "SysDb";
            csb.DatabasePassword = "DbPwd";

            Assert.AreEqual(@"provider=Microsoft.ACE.OLEDB.12.0;data source=C:\myFolder\myAccessFile.accdb;password=hunter2;user id=Admin;jet oledb:system database=SysDb;jet oledb:database password=DbPwd", csb.ConnectionString);
        }
    }
}