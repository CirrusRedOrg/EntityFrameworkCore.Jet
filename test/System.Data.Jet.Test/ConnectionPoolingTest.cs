using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Query.ResultOperators.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class ConnectionPoolingTest
    {

        private readonly string ConnectionString;
        private readonly string ConnectionStringDummy;
        private const string FileName = "ConnectionPoolingTest.accdb";
        private const string FileNameDummy = "ConnectionPoolingTest.accdb";

        public ConnectionPoolingTest()
        {
            ConnectionString = JetConnection.GetConnectionString(FileName);
            ConnectionStringDummy = JetConnection.GetConnectionString(FileNameDummy);
        }


        [TestInitialize]
        public void Initialize()
        {
            if (!JetConnection.DatabaseExists(ConnectionString))
                JetConnection.CreateEmptyDatabase(ConnectionString);
            using (JetConnection connection = new JetConnection(ConnectionString))
            {
                connection.Open();
                if (!connection.TableExists("SimpleTable"))
                    connection.CreateCommand("CREATE TABLE SimpleTable ( Col varchar(10) )").ExecuteNonQuery();
                connection.CreateCommand("DELETE FROM SimpleTable").ExecuteNonQuery();
            }
            JetConnection.ClearAllPools();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Open_Connection_Without_Connection_String()
        {
            JetConnection connection = new JetConnection();
            connection.Open();
        }


        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ExecuteReader_From_Closed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            DbDataReader dataReader;
            try
            {
                dataReader = connection.CreateCommand("Select * from MSysAccessStorage").ExecuteReader();
            }
            catch (Exception e)
            {
                Assert.AreEqual("\"ExecuteReader\" requires a connection in Open state. Current connection state is Closed", e.Message);
                throw;
            }

            while (dataReader.Read())
            {
                
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ExecuteNonQuery_From_Closed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            try
            {
                var a = connection.CreateCommand("Select * from MSysAccessStorage").ExecuteNonQuery();
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
            JetConnection connection = new JetConnection(ConnectionString);
            try
            { 
                var a = connection.CreateCommand("Select * from MSysAccessStorage").ExecuteScalar();
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
            JetConnection connection = new JetConnection(ConnectionString);
            try
            {
                connection.CreateCommand("Select * from MSysAccessStorage").Prepare();
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
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            var dataReader = connection.CreateCommand("Select * from MSysAccessStorage").ExecuteReader();
            while (dataReader.Read())
            {

            }
        }


        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void GetTransaction_From_Closed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            var transaction = connection.BeginTransaction();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Change_Database_From_Closed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.ChangeDatabase("abcd");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Change_Database_From_Open_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            connection.ChangeDatabase("abcd");
        }

        [TestMethod]
        public void Change_ConnectionString_From_Closed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.ConnectionString = ConnectionStringDummy;
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Change_ConnectionString_From_Open_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            connection.ConnectionString = ConnectionStringDummy;
        }

        [TestMethod]
        public void Read_ConnectionString_From_Closed0_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            Assert.AreEqual(ConnectionString, connection.ConnectionString);
        }

        [TestMethod]
        public void Read_ConnectionString_From_Closed1_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Close();
            Assert.AreEqual(ConnectionString, connection.ConnectionString);
        }


        [TestMethod]
        public void Read_ConnectionString_From_Open_Closed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            connection.Close();
            Assert.AreEqual(ConnectionString, connection.ConnectionString);
        }


        [TestMethod]
        public void Read_ConnectionString_From_Disposed0_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            Assert.AreEqual(ConnectionString, connection.ConnectionString);
        }

        [TestMethod]
        public void Read_ConnectionString_From_Disposed1_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Dispose();
            Assert.AreEqual("", connection.ConnectionString);
        }


        [TestMethod]
        public void Read_ConnectionString_From_Open_Disposed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            connection.Dispose();
            Assert.AreEqual("", connection.ConnectionString);
        }



        [TestMethod]
        public void Read_ConnectionString_From_Open_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            Assert.AreEqual(ConnectionString, connection.ConnectionString);
        }

        [TestMethod]
        public void Read_Database_From_Closed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            Assert.IsTrue(connection.Database == string.Empty);
        }

        [TestMethod]
        public void Read_Database_From_Open_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            Assert.IsTrue(connection.Database == string.Empty);
        }


        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Read_ServerVersion_From_Closed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            Assert.IsTrue(connection.ServerVersion == string.Empty);
        }

        [TestMethod]
        public void Read_ServerVersion_From_Open_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            Console.WriteLine(connection.ServerVersion);
        }


        [TestMethod]
        public void Read_Database_From_Disposed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Dispose();
            Assert.IsTrue(connection.Database == string.Empty);
        }

        [TestMethod]
        public void Read_DataSource_From_Closed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            Assert.AreEqual(FileName, connection.DataSource);
        }

        [TestMethod]
        public void Read_DataSource_From_Open_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            Assert.AreEqual(FileName, connection.DataSource);
        }

        [TestMethod]
        public void Read_DataSource_From_Disposed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Dispose();
            Assert.IsTrue(connection.DataSource == string.Empty);
        }


        [TestMethod]
        public void DisposeSeveralTimes()
        {
            int stateChangeCount = 0;
            JetConnection connection = new JetConnection(ConnectionString);
            connection.StateChange += (sender, args) => {
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
            int stateChangeCount = 0;
            JetConnection connection = new JetConnection(ConnectionString);
            connection.StateChange += (sender, args) => {
                Console.WriteLine($"{args.OriginalState} => {args.CurrentState}");
                stateChangeCount++;
            };
            connection.Dispose();

            Assert.AreEqual(0, stateChangeCount);
        }


        [TestMethod]
        public void Read_Connection_String_After_Dispose()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            Assert.IsFalse(string.IsNullOrEmpty(connection.ConnectionString));
            connection.Dispose();
            Assert.IsTrue(string.IsNullOrEmpty(connection.ConnectionString));
        }


        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void OpenSeveralTimes()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            connection.Open();
        }


        [TestMethod]
        public void CloseSeveralTimes()
        {
            int stateChangeCount = 0;
            JetConnection connection = new JetConnection(ConnectionString);
            connection.StateChange += (sender, args) => {
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
            int stateChangeCount = 0;
            JetConnection connection = new JetConnection(ConnectionString);
            connection.StateChange += (sender, args) => {
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
            int stateChangeCount = 0;

            JetConnection connection = new JetConnection(ConnectionString);
            connection.StateChange += (sender, args) => {
                Console.WriteLine($"{args.OriginalState} => {args.CurrentState}");
                stateChangeCount++;
            };

            connection.Open();
            var dataReader = connection.CreateCommand("Select * from MSysAccessStorage").ExecuteReader();
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
            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            connection.GetSchema();
            connection.Close();
        }


        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void GetSchema_From_Closed_Connection()
        {
            JetConnection connection = new JetConnection(ConnectionString);
            connection.GetSchema();
        }


        [TestMethod]
        public void Transaction_Execute_Close_Open_Execute()
        {
            DbCommand command;

            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            var transaction = connection.BeginTransaction();
            command = connection.CreateCommand("Select count(*) from MSysAccessStorage");
            command.Transaction = transaction;
            command.ExecuteScalar();
            connection.Close();
            connection.Open();
            command = connection.CreateCommand("Select count(*) from MSysAccessStorage");
            command.ExecuteScalar();
        }

        [TestMethod]
        public void Transaction_Execute_Commit_Close_Open_Execute()
        {
            DbCommand command;

            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            var transaction = connection.BeginTransaction();
            command = connection.CreateCommand("Select count(*) from MSysAccessStorage");
            command.Transaction = transaction;
            command.ExecuteScalar();
            transaction.Commit();
            connection.Close();
            connection.Open();
            command = connection.CreateCommand("Select count(*) from MSysAccessStorage");
            command.ExecuteScalar();
        }

        [TestMethod]
        public void Transaction_Execute_Close_Open()
        {
            DbCommand command;

            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            var transaction = connection.BeginTransaction();
            command = connection.CreateCommand("INSERT INTO SimpleTable(Col) VALUES ('aaa')");
            command.Transaction = transaction;
            command.ExecuteScalar();
            connection.Close();
            connection.Open();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            Assert.AreEqual(0, command.ExecuteScalar());
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Transaction_Execute_Close_Open_Commit()
        {
            DbCommand command;

            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            var transaction = connection.BeginTransaction();
            command = connection.CreateCommand("INSERT INTO SimpleTable(Col) VALUES ('aaa')");
            command.Transaction = transaction;
            command.ExecuteScalar();
            connection.Close();
            JetConnection.ClearAllPools();
            connection.Open();
            transaction.Commit();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            Assert.AreEqual(0, command.ExecuteScalar());
        }



        [TestMethod]
        public void Transaction_Execute_Close_Open_Transaction()
        {
            DbCommand command;
            DbTransaction transaction;

            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            transaction = connection.BeginTransaction();
            command = connection.CreateCommand("INSERT INTO SimpleTable(Col) VALUES ('aaa')");
            command.Transaction = transaction;
            command.ExecuteScalar();
            connection.Close();
            connection.Open();
            transaction = connection.BeginTransaction();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.Transaction = transaction;
            Assert.AreEqual(0, command.ExecuteScalar());
        }


        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Transaction_Transaction()
        {
            DbCommand command;
            

            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            DbTransaction firstTransaction = connection.BeginTransaction();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.Transaction = firstTransaction;
            command.ExecuteScalar();

            try
            {
                DbTransaction secondTransaction = connection.BeginTransaction();
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
            DbCommand command;


            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            DbTransaction firstTransaction = connection.BeginTransaction();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.Transaction = firstTransaction;
            command.ExecuteScalar();
            firstTransaction.Commit();

            DbTransaction secondTransaction = connection.BeginTransaction();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.Transaction = secondTransaction;
            command.ExecuteScalar();

            connection.Close();

            connection.Open();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.ExecuteScalar();


        }

        [TestMethod]
        public void Transaction_Rollback_Transaction()
        {
            DbCommand command;


            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            DbTransaction firstTransaction = connection.BeginTransaction();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.Transaction = firstTransaction;
            command.ExecuteScalar();
            firstTransaction.Rollback();

            DbTransaction secondTransaction = connection.BeginTransaction();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.Transaction = secondTransaction;
            command.ExecuteScalar();


            connection.Close();

            connection.Open();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.ExecuteScalar();
        }


        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Transaction_Execute_Commit_Execute_Transaction()
        {
            DbCommand command;

            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            DbTransaction transaction = connection.BeginTransaction();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.Transaction = transaction;
            command.ExecuteScalar();
            transaction.Commit();

            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.Transaction = transaction;
            command.ExecuteScalar();
            transaction.Commit();

            connection.Close();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Transaction_Execute_Commit_Commit()
        {
            DbCommand command;

            JetConnection connection = new JetConnection(ConnectionString);
            connection.Open();
            DbTransaction transaction = connection.BeginTransaction();
            command = connection.CreateCommand("Select count(*) from SimpleTable");
            command.Transaction = transaction;
            command.ExecuteScalar();
            transaction.Commit();
            transaction.Commit();

            connection.Close();
        }


    }
}
