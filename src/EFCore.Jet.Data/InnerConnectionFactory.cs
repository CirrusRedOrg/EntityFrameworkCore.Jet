using System;
using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data.ConnectionPooling;

namespace EntityFrameworkCore.Jet.Data
{
    class InnerConnectionFactory : IDisposable
    {
        public static readonly InnerConnectionFactory Instance = new InnerConnectionFactory();

        private InnerConnectionFactory()
        {
        }

        private readonly ConnectionSetCollection _pool = new ConnectionSetCollection();

        public DbConnection OpenConnection(string connectionString, DbProviderFactory? dataAccessProviderFactory)
        {
            connectionString ??= string.Empty;

            if (!JetConfiguration.UseConnectionPooling)
            {
                var connection = dataAccessProviderFactory?.CreateConnection();
                if (connection == null) throw new ArgumentNullException(nameof(connection));
                connection.ConnectionString = connectionString;
                connection.Open();

                return connection;
            }

            lock (_pool)
            {
                _pool.TryGetValue(connectionString, out var connectionSet);

                if (connectionSet == null || connectionSet.ConnectionCount == 0)
                {
                    var connection = dataAccessProviderFactory?.CreateConnection();
                    if (connection == null) throw new ArgumentNullException(nameof(connection));
                    connection.ConnectionString = connectionString;
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

            connectionString ??= string.Empty;

            // TODO: Add more options to control connection pooling aspects.
            lock (_pool)
            {
                _pool.TryGetValue(connectionString, out var connectionSet);

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