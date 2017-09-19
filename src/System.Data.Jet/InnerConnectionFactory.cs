using System;
using System.Data.Common;
using System.Data.Jet.ConnectionPooling;
using System.Data.OleDb;

namespace System.Data.Jet
{
    class InnerConnectionFactory : IDisposable
    {

        public static InnerConnectionFactory Instance = new InnerConnectionFactory();

        private InnerConnectionFactory()
        { }

        ConnectionSetCollection _pool = new ConnectionSetCollection();

        public DbConnection OpenConnection(string connectionString)
        {
            if (!JetConfiguration.UseConnectionPooling)
            {
                DbConnection connection = new OleDbConnection(connectionString);
                connection.Open();
                return connection;
            }

            lock (_pool)
            {
                ConnectionSet connectionSet;
                _pool.TryGetValue(connectionString, out connectionSet);
                if (connectionSet == null || connectionSet.ConnectionCount == 0)
                {
                    DbConnection connection = new OleDbConnection(connectionString);
                    connection.Open();
                    return connection;
                }
                return connectionSet.GetConnection();
            }
        }

        public void CloseConnection(string connectionString, DbConnection connection)
        {
            if (!JetConfiguration.UseConnectionPooling)
            {
                connection.Close();
                return;
            }

            if (connection.State != ConnectionState.Open)
                return;

            lock (_pool)
            {
                ConnectionSet connectionSet;
                _pool.TryGetValue(connectionString, out connectionSet);
                if (connectionSet == null)
                {
                    connectionSet = new ConnectionSet(connectionString);
                    _pool.Add(connectionSet);
                }
                connectionSet.AddConnection(connection);
            }

        }

        public void ClearAllPools()
        {
            lock (_pool)
            {
                foreach (ConnectionSet connectionSet in _pool)
                    connectionSet.Dispose();
                _pool.Clear();
            }
        }


        #region IDisposable

        private void ReleaseUnmanagedResources()
        {
            ClearAllPools();
        }

        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~InnerConnectionFactory()
        {
            ReleaseUnmanagedResources();
        }

        #endregion

    }
}
