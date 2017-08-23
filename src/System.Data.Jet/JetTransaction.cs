using System;
using System.Data.Common;

namespace System.Data.Jet
{
    class JetTransaction : DbTransaction
    {

        internal DbTransaction WrappedTransaction { get; private set; }
        readonly DbConnection _connection;

        public JetTransaction(DbTransaction wrappedTransaction, DbConnection connection)
        {
            LogHelper.ShowCommandHeader("\r\nvvv BeginTransaction (" + wrappedTransaction.IsolationLevel + ")");
            WrappedTransaction = wrappedTransaction;
            _connection = connection;
        }

        public override void Commit()
        {
            LogHelper.ShowCommandHeader("--- Commit");
            WrappedTransaction.Commit();
        }

        protected override DbConnection DbConnection
        {
            get { return _connection; }
        }

        public override System.Data.IsolationLevel IsolationLevel
        {
            get { return WrappedTransaction.IsolationLevel; }
        }

        public override void Rollback()
        {
            LogHelper.ShowCommandHeader("^^^ Rollback");
            WrappedTransaction.Rollback();
        }


    }
}
