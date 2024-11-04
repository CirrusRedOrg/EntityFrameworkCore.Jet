using System;
using System.Data.Common;

namespace EntityFrameworkCore.Jet.Data.ConnectionPooling
{
    class ConnectionSet(string connectionString) : IDisposable
    {
        public string ConnectionString { get; } = connectionString;

        private DbConnection[] _connections = new DbConnection[10];
        public int ConnectionCount { get; private set; }

        public void AddConnection(DbConnection connection)
        {
            lock(_connections)
            {
                if (ConnectionCount == _connections.Length)
                {
                    DbConnection[] connections = new DbConnection[ConnectionCount * 2];
                    Array.Copy(_connections, connections, ConnectionCount);
                    _connections = connections;
                }

                _connections[ConnectionCount] = connection;
                ConnectionCount++;
            }
        }

        public DbConnection GetConnection()
        {
            lock (_connections)
            {
                if (ConnectionCount == 0)
                    throw new InvalidOperationException("No connection available in the pool");
                return _connections[--ConnectionCount];
            }
        }


        #region Implement IDisposable

        private void ReleaseUnmanagedResources()
        {
            lock (_connections)
            {
                for (int i = 0; i < ConnectionCount; i++)
                    try
                    {
                        _connections[i].Close();
                    }
                    catch { /* ignored */ }
            }
            ConnectionCount = 0;
        }

        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~ConnectionSet()
        {
            ReleaseUnmanagedResources();
        }

        #endregion
    }
}