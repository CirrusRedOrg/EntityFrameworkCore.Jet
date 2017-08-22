using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;

namespace System.Data.Jet
{
    class JetTransaction : DbTransaction
    {

        internal DbTransaction WrappedTransaction { get; private set; }
        readonly DbConnection _connection;

        public JetTransaction(DbTransaction wrappedTransaction, DbConnection connection)
        {
            WrappedTransaction = wrappedTransaction;
            _connection = connection;
        }

        public override void Commit()
        {
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
            WrappedTransaction.Rollback();
        }


    }
}
