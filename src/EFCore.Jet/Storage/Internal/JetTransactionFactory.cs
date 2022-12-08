using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTransactionFactory : IRelationalTransactionFactory
    {

        /// <summary>
        ///     Initializes a new instance of the <see cref="RelationalTransactionFactory" /> class.
        /// </summary>
        /// <param name="dependencies">Parameter object containing dependencies for this service.</param>
        public JetTransactionFactory(RelationalTransactionFactoryDependencies dependencies)
        {
            Dependencies = dependencies;
        }

        /// <summary>
        ///     Relational provider-specific dependencies for this service.
        /// </summary>
        protected virtual RelationalTransactionFactoryDependencies Dependencies { get; }

        public virtual RelationalTransaction Create(
            IRelationalConnection connection,
            DbTransaction transaction,
            Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned)
            => new JetTransaction(connection, transaction, transactionId, logger, transactionOwned,Dependencies.SqlGenerationHelper);
    }
}