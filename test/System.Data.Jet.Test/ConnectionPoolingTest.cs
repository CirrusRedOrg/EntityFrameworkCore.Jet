using System.Data.OleDb;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class ConnectionPoolingTest
    {
        private const string StoreName = nameof(ConnectionPoolingTest) + ".accdb";
        private const string DummyStoreName = nameof(ConnectionPoolingTest) + "Dummy.accdb";

        [TestInitialize]
        public void Setup()
        {
            using (var connection = Helpers.CreateAndOpenDatabase(StoreName))
            {
                using var command = connection.CreateCommand("CREATE TABLE SimpleTable ( Col varchar(10) )");
                command.ExecuteNonQuery();
            }

            JetConnection.ClearAllPools();
        }

        [TestCleanup]
        public void TearDown()
        {
            Helpers.DeleteDatabase(StoreName);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Open_Connection_Without_Connection_String()
        {
            using var connection = new JetConnection();
            connection.Open();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ExecuteReader_From_Closed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            try
            {
                using var command = connection.CreateCommand("select * from MSysAccessStorage");
                command.ExecuteReader();
            }
            catch (Exception e)
            {
                Assert.AreEqual("\"ExecuteReader\" requires a connection in Open state. Current connection state is Closed", e.Message);
                throw;
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ExecuteNonQuery_From_Closed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            try
            {
                using var command = connection.CreateCommand("select * from MSysAccessStorage");
                command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Assert.AreEqual("\"ExecuteNonQuery\" requires a connection in Open state. Current connection state is Closed", e.Message);
                throw;
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ExecuteScalar_From_Closed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            try
            {
                using var command = connection.CreateCommand("select * from MSysAccessStorage");
                command.ExecuteScalar();
            }
            catch (Exception e)
            {
                Assert.AreEqual("\"ExecuteScalar\" requires a connection in Open state. Current connection state is Closed", e.Message);
                throw;
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Prepare_From_Closed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            try
            {
                using var command = connection.CreateCommand("select * from MSysAccessStorage");
                command.Prepare();
            }
            catch (Exception e)
            {
                Assert.AreEqual("\"Prepare\" requires a connection in Open state. Current connection state is Closed", e.Message);
                throw;
            }
        }

        [TestMethod]
        public void GetDataReader_From_Open_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            var dataReader = connection.CreateCommand("select * from MSysAccessStorage")
                .ExecuteReader();
            while (dataReader.Read())
            {
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void GetTransaction_From_Closed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            using var transaction = connection.BeginTransaction();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Change_Database_From_Closed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.ChangeDatabase("abcd");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Change_Database_From_Open_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            connection.ChangeDatabase("abcd");
        }

        [TestMethod]
        public void Change_ConnectionString_From_Closed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.ConnectionString = DummyStoreName;
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Change_ConnectionString_From_Open_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            connection.ConnectionString = DummyStoreName;
        }

        [TestMethod]
        public void Read_ConnectionString_From_Closed0_Connection()
        {
            var connectionString = JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory);
            using var connection = new JetConnection(connectionString);
            Assert.AreEqual(connectionString, connection.ConnectionString);
        }

        [TestMethod]
        public void Read_ConnectionString_From_Closed1_Connection()
        {
            var connectionString = JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory);
            using var connection = new JetConnection(connectionString);
            connection.Close();
            Assert.AreEqual(connectionString, connection.ConnectionString);
        }

        [TestMethod]
        public void Read_ConnectionString_From_Open_Closed_Connection()
        {
            var connectionString = JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory);
            using var connection = new JetConnection(connectionString);
            connection.Open();
            connection.Close();
            Assert.AreEqual(connectionString, connection.ConnectionString);
        }

        [TestMethod]
        public void Read_ConnectionString_From_Disposed_Connection()
        {
            var connectionString = JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory);
            using var connection = new JetConnection(connectionString);
            connection.Dispose();
            Assert.AreEqual(String.Empty, connection.ConnectionString);
        }

        [TestMethod]
        public void Read_ConnectionString_From_Open_Disposed_Connection()
        {
            var connectionString = JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory);
            using var connection = new JetConnection(connectionString);
            connection.Open();
            connection.Dispose();
            Assert.AreEqual(String.Empty, connection.ConnectionString);
        }

        [TestMethod]
        public void Read_ConnectionString_From_Open_Connection()
        {
            var connectionString = JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory);
            using var connection = new JetConnection(connectionString);
            connection.Open();
            Assert.AreEqual(connectionString, connection.ConnectionString);
        }

        [TestMethod]
        public void Read_Database_From_Closed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            Assert.IsTrue(connection.Database == string.Empty);
        }

        [TestMethod]
        public void Read_Database_From_Open_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            Assert.IsTrue(connection.Database == string.Empty);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Read_ServerVersion_From_Closed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            Assert.IsTrue(connection.ServerVersion == string.Empty);
        }

        [TestMethod]
        public void Read_ServerVersion_From_Open_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            Console.WriteLine(connection.ServerVersion);
        }

        [TestMethod]
        public void Read_Database_From_Disposed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Dispose();
            Assert.IsTrue(connection.Database == string.Empty);
        }

        [TestMethod]
        public void Read_DataSource_From_Closed_Connection()
        {
            var connectionString = JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory);
            
            var dataAccessType = JetConnection.GetDataAccessType(connectionString);
            var dataAccessProviderFactory = JetFactory.Instance.CreateDataAccessProviderFactory(dataAccessType);
            var connectionStringBuilder = dataAccessProviderFactory.CreateConnectionStringBuilder();
            connectionStringBuilder.ConnectionString = connectionString;
           
            var fileName = connectionStringBuilder.GetDataSource();
            using var connection = new JetConnection(connectionString);
            Assert.AreEqual(fileName, connection.DataSource);
        }

        [TestMethod]
        public void Read_DataSource_From_Open_Connection()
        {
            var connectionString = JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory);
            
            var dataAccessType = JetConnection.GetDataAccessType(connectionString);
            var dataAccessProviderFactory = JetFactory.Instance.CreateDataAccessProviderFactory(dataAccessType);
            var connectionStringBuilder = dataAccessProviderFactory.CreateConnectionStringBuilder();
            connectionStringBuilder.ConnectionString = connectionString;

            var fileName = connectionStringBuilder.GetDataSource();
            using var connection = new JetConnection(connectionString);
            connection.Open();
            Assert.AreEqual(fileName, connection.DataSource);
        }

        [TestMethod]
        public void Read_DataSource_From_Disposed_Connection()
        {
            var connectionString = JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory);
            using var connection = new JetConnection(connectionString);
            connection.Dispose();
            Assert.AreEqual(string.Empty, connection.DataSource);
        }

        [TestMethod]
        public void DisposeSeveralTimes()
        {
            var stateChangeCount = 0;
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.StateChange += (sender, args) =>
            {
                Console.WriteLine($"{args.OriginalState} => {args.CurrentState}");
                stateChangeCount++;
            };
            connection.Open();
            connection.Dispose();
            connection.Dispose();
            connection.Dispose();

            Assert.AreEqual(2, stateChangeCount);
        }

        [TestMethod]
        public void DisposeWithoutOpen()
        {
            var stateChangeCount = 0;
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.StateChange += (sender, args) =>
            {
                Console.WriteLine($"{args.OriginalState} => {args.CurrentState}");
                stateChangeCount++;
            };
            connection.Dispose();

            Assert.AreEqual(0, stateChangeCount);
        }

        [TestMethod]
        public void Read_Connection_String_After_Dispose()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            Assert.IsFalse(string.IsNullOrEmpty(connection.ConnectionString));
            connection.Dispose();
            Assert.IsTrue(string.IsNullOrEmpty(connection.ConnectionString));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void OpenSeveralTimes()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            connection.Open();
        }

        [TestMethod]
        public void CloseSeveralTimes()
        {
            var stateChangeCount = 0;
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.StateChange += (sender, args) =>
            {
                Console.WriteLine($"{args.OriginalState} => {args.CurrentState}");
                stateChangeCount++;
            };
            connection.Close();
            connection.Close();
            Assert.AreEqual(0, stateChangeCount);
        }

        [TestMethod]
        public void OpenCloseSeveralTimes()
        {
            var stateChangeCount = 0;
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.StateChange += (sender, args) =>
            {
                Console.WriteLine($"{args.OriginalState} => {args.CurrentState}");
                stateChangeCount++;
            };
            connection.Open();
            connection.Close();
            connection.Close();
            Assert.AreEqual(2, stateChangeCount);
        }

        [TestMethod]
        public void Raise_Events()
        {
            var stateChangeCount = 0;

            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.StateChange += (sender, args) =>
            {
                Console.WriteLine($"{args.OriginalState} => {args.CurrentState}");
                stateChangeCount++;
            };

            connection.Open();
            using var command = connection.CreateCommand("select * from MSysAccessStorage");
            var dataReader = command.ExecuteReader();
            while (dataReader.Read())
            {
            }

            connection.Close();
            connection.Open();
            connection.Dispose();

            Assert.AreEqual(4, stateChangeCount);
        }

        [TestMethod]
        public void GetSchema_From_Open_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            connection.GetSchema();
            connection.Close();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void GetSchema_From_Closed_Connection()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.GetSchema();
        }

        [TestMethod]
        public void Transaction_Execute_Close_Open_Execute()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            using var transaction = connection.BeginTransaction();
            
            using (var command = connection.CreateCommand("select count(*) from MSysAccessStorage"))
            {
                command.Transaction = transaction;
                command.ExecuteScalar();
            }

            connection.Close();
            connection.Open();
            
            using (var command = connection.CreateCommand("select count(*) from MSysAccessStorage"))
            {
                command.ExecuteScalar();
            }
        }

        [TestMethod]
        public void Transaction_Execute_Commit_Close_Open_Execute()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand("select count(*) from MSysAccessStorage"))
            {
                command.Transaction = transaction;
                command.ExecuteScalar();
            }

            transaction.Commit();
            connection.Close();
            connection.Open();

            using (var command = connection.CreateCommand("select count(*) from MSysAccessStorage"))
            {
                command.ExecuteScalar();
            }
        }

        [TestMethod]
        public void Transaction_Execute_Close_Open()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand("INSERT INTO SimpleTable(Col) VALUES ('aaa')"))
            {
                command.Transaction = transaction;
                command.ExecuteScalar();
            }

            connection.Close();
            connection.Open();
            
            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                Assert.AreEqual(0, command.ExecuteScalar());
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Transaction_Execute_Close_Open_Commit()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand("INSERT INTO SimpleTable(Col) VALUES ('aaa')"))
            {
                command.Transaction = transaction;
                command.ExecuteScalar();
            }

            connection.Close();
            JetConnection.ClearAllPools();
            connection.Open();
            transaction.Commit();

            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                Assert.AreEqual(0, command.ExecuteScalar());
            }
        }

        [TestMethod]
        public void Transaction_Execute_Close_Open_Transaction()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            using var transaction1 = connection.BeginTransaction();

            using (var command = connection.CreateCommand("INSERT INTO SimpleTable(Col) VALUES ('aaa')"))
            {
                command.Transaction = transaction1;
                command.ExecuteScalar();
            }

            connection.Close();
            connection.Open();
            using var transaction2 = connection.BeginTransaction();

            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                command.Transaction = transaction2;
                Assert.AreEqual(0, command.ExecuteScalar());
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Transaction_Transaction()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            using var firstTransaction = connection.BeginTransaction();
            using var command = connection.CreateCommand("select count(*) from SimpleTable");
            command.Transaction = firstTransaction;
            command.ExecuteScalar();

            try
            {
                using var secondTransaction = connection.BeginTransaction();
            }
            catch (Exception e)
            {
                Assert.AreEqual("JetConnection does not support parallel transactions", e.Message);
                throw;
            }
        }

        [TestMethod]
        public void Transaction_Commit_Transaction()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            using var firstTransaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                command.Transaction = firstTransaction;
                command.ExecuteScalar();
            }
            firstTransaction.Commit();

            using var secondTransaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                command.Transaction = secondTransaction;
                command.ExecuteScalar();
            }
            connection.Close();

            connection.Open();
            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                command.ExecuteScalar();
            }
        }

        [TestMethod]
        public void Transaction_Rollback_Transaction()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            using var firstTransaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                command.Transaction = firstTransaction;
                command.ExecuteScalar();
            }
            firstTransaction.Rollback();

            using var secondTransaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                command.Transaction = secondTransaction;
                command.ExecuteScalar();
            }
            connection.Close();

            connection.Open();
            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                command.ExecuteScalar();
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Transaction_Execute_Commit_Execute_Transaction()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                command.Transaction = transaction;
                command.ExecuteScalar();
            }
            transaction.Commit();

            using (var command = connection.CreateCommand("select count(*) from SimpleTable"))
            {
                command.Transaction = transaction;
                command.ExecuteScalar();
            }
            transaction.Commit();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Transaction_Execute_Commit_Commit()
        {
            using var connection = new JetConnection(JetConnection.GetConnectionString(StoreName, JetConfiguration.DefaultProviderFactory));
            connection.Open();
            
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand("select count(*) from SimpleTable");
            command.Transaction = transaction;
            command.ExecuteScalar();
            transaction.Commit();
            transaction.Commit();
        }
    }
}