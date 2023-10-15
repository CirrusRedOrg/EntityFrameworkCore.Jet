// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class TestRelationalTransactionFactory : IRelationalTransactionFactory
    {
        public TestRelationalTransactionFactory(RelationalTransactionFactoryDependencies dependencies)
        {
            Dependencies = dependencies;
        }

        protected virtual RelationalTransactionFactoryDependencies Dependencies { get; }

        public RelationalTransaction Create(
            IRelationalConnection connection,
            DbTransaction transaction,
            Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned)
            => new TestRelationalTransaction(connection, transaction, logger, transactionOwned, Dependencies.SqlGenerationHelper);
    }

    public class TestRelationalTransaction : RelationalTransaction
    {
        private readonly TestJetConnection _testConnection;
        private readonly Func<int, Guid?, DbException> _createExceptionFunc;

        public TestRelationalTransaction(
            IRelationalConnection connection,
            DbTransaction transaction,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned,
            ISqlGenerationHelper sqlGenerationHelper)
            : base(connection, transaction, new Guid(), logger, transactionOwned, sqlGenerationHelper)
        {
            _testConnection = (TestJetConnection)connection;

            _createExceptionFunc = TestEnvironment.DataAccessProviderType == DataAccessProviderType.OleDb
                ? OleDbExceptionFactory.CreateException
                : OdbcExceptionFactory.CreateException;
        }

        public override void Commit()
        {
            if (_testConnection.CommitFailures.Count > 0)
            {
                var fail = _testConnection.CommitFailures.Dequeue();
                if (fail.HasValue)
                {
                    if (fail.Value)
                    {
                        this.GetDbTransaction().Rollback();
                    }
                    else
                    {
                        this.GetDbTransaction().Commit();
                    }

                    _testConnection.DbConnection.Close();
                    throw _createExceptionFunc(_testConnection.ErrorNumber, _testConnection.ConnectionId);
                }
            }

            base.Commit();
        }
    }
}
