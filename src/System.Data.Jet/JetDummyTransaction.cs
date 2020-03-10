using System.Data.Common;

namespace System.Data.Jet
{
    internal class JetDummyTransaction : JetTransaction
    {
        private readonly JetConnection _connection;

        internal override DbTransaction WrappedTransaction => null;

        public JetDummyTransaction(JetConnection connection, IsolationLevel isolationLevel)
        {
            _connection = connection;
            IsolationLevel = isolationLevel;
            
            LogHelper.ShowCommandHeader($"\r\nvvv BeginTransaction ({isolationLevel}): DUMMY!");
        }

        public override void Commit()
        {
            LogHelper.ShowCommandHeader("--- Commit: DUMMY!");
            _connection.ActiveTransaction = null;
        }

        protected override DbConnection DbConnection
            => _connection;

        public override IsolationLevel IsolationLevel { get; }

        public override void Rollback()
        {
            LogHelper.ShowCommandHeader("^^^ Rollback: DUMMY!");
            _connection.ActiveTransaction = null;
        }
    }
}