using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Jet.Data
{
    internal class JetTransaction : DbTransaction
    {
        private JetConnection _connection;
        private bool _disposed;

        internal virtual DbTransaction WrappedTransaction { get; }

        protected JetTransaction()
        {
        }

        public JetTransaction(JetConnection connection, IsolationLevel isolationLevel)
        {
            _connection = connection;
            LogHelper.ShowCommandHeader($"\r\nvvv BeginTransaction ({isolationLevel})");
            WrappedTransaction = connection.InnerConnection.BeginTransaction(isolationLevel);
        }

        public override void Commit()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JetTransaction));
            
            LogHelper.ShowCommandHeader("--- Commit");
            WrappedTransaction.Commit();

            _connection.ActiveTransaction = null;
        }

        protected override DbConnection DbConnection
            => _connection;

        public override IsolationLevel IsolationLevel
            => WrappedTransaction.IsolationLevel;

        public override void Rollback()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JetTransaction));
            
            LogHelper.ShowCommandHeader("^^^ Rollback");
            WrappedTransaction.Rollback();

            _connection.ActiveTransaction = null;
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    if (_connection?.ActiveTransaction == this)
                    {
                        if (Connection.State == ConnectionState.Open)
                        {
                            Rollback();
                        }

                        _connection.ActiveTransaction = null;
                    }
                }
            }
            finally
            {
                _disposed = true;
                _connection = null;

                base.Dispose(disposing);
            }
        }

        public override Task CommitAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JetTransaction));
            
            return base.CommitAsync(cancellationToken);
        }

        public override ValueTask DisposeAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JetTransaction));
            
            return base.DisposeAsync();
        }

        public override Task RollbackAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JetTransaction));
            
            return base.RollbackAsync(cancellationToken);
        }
    }
}