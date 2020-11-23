using System.Data.Common;

namespace System.Data.Jet
{
    internal class JetTransaction : DbTransaction
    {
        private readonly JetConnection _connection;

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
            LogHelper.ShowCommandHeader("--- Commit");
            WrappedTransaction.Commit();
            _connection.ActiveTransaction = null;
        }

        protected override DbConnection DbConnection
            => _connection;

        public override System.Data.IsolationLevel IsolationLevel
            => WrappedTransaction.IsolationLevel;

        public override void Rollback()
        {
            LogHelper.ShowCommandHeader("^^^ Rollback");
            WrappedTransaction.Rollback();
            _connection.ActiveTransaction = null;
        }
    }
}