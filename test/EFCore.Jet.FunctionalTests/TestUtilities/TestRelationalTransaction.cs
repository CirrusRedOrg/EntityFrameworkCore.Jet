// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using System.Threading.Tasks;
using System.Threading;
using EntityFrameworkCore.Jet.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class TestRelationalTransactionFactory(RelationalTransactionFactoryDependencies dependencies)
        : IRelationalTransactionFactory
    {
        protected virtual RelationalTransactionFactoryDependencies Dependencies { get; } = dependencies;

        public RelationalTransaction Create(
            IRelationalConnection connection,
            DbTransaction transaction,
            Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned)
            => new TestRelationalTransaction(connection, transaction, logger, transactionOwned, Dependencies.SqlGenerationHelper);
    }

    public class TestRelationalTransaction(
        IRelationalConnection connection,
        DbTransaction transaction,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned,
        ISqlGenerationHelper sqlGenerationHelper)
        : RelationalTransaction(connection, transaction, new Guid(), logger, transactionOwned, sqlGenerationHelper)
    {
        private readonly TestJetConnection _testConnection = (TestJetConnection)connection;
        private readonly Func<int, Guid?, DbException> _createExceptionFunc = TestEnvironment.DataAccessProviderType == DataAccessProviderType.OleDb
            ? OleDbExceptionFactory.CreateException
            : OdbcExceptionFactory.CreateException;

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

        public override async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_testConnection.CommitFailures.Count > 0)
            {
                var fail = _testConnection.CommitFailures.Dequeue();
                if (fail.HasValue)
                {
                    if (fail.Value)
                    {
                        await this.GetDbTransaction().RollbackAsync(cancellationToken);
                    }
                    else
                    {
                        await this.GetDbTransaction().CommitAsync(cancellationToken);
                    }

                    await _testConnection.DbConnection.CloseAsync();
                    throw _createExceptionFunc(_testConnection.ErrorNumber, _testConnection.ConnectionId);
                }
            }

            await base.CommitAsync(cancellationToken);
        }
    }
}
