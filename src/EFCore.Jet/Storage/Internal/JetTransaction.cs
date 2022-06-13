using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTransaction : RelationalTransaction
    {
        /// <inheritdoc />
        public JetTransaction(
            [NotNull] IRelationalConnection connection,
            [NotNull] DbTransaction transaction,
            Guid transactionId,
            [NotNull] IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned)
            : base(connection, transaction, transactionId, logger, transactionOwned)
        {
        }
        
        /// <inheritdoc />
        public override bool SupportsSavepoints
            => false;

        /// <inheritdoc />
        public override void CreateSavepoint(string name)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override Task CreateSavepointAsync(string name, CancellationToken cancellationToken = new CancellationToken())
            => throw new NotSupportedException();

        /// <inheritdoc />
        protected override string GetCreateSavepointSql(string name)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override void RollbackToSavepoint(string name)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = new CancellationToken())
            => throw new NotSupportedException();

        /// <inheritdoc />
        protected override string GetRollbackToSavepointSql(string name)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override void ReleaseSavepoint(string name)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <inheritdoc />
        protected override string GetReleaseSavepointSql(string name)
            => throw new NotSupportedException();
    }
}