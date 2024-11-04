namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <inheritdoc />
    public class JetTransaction(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned,
        ISqlGenerationHelper sqlGenerationHelper) : RelationalTransaction(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
    {

        /// <inheritdoc />
        public override bool SupportsSavepoints
            => false;

        /// <inheritdoc />
        public override void CreateSavepoint(string name)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override Task CreateSavepointAsync(string name, CancellationToken cancellationToken = new())
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override void RollbackToSavepoint(string name)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = new())
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override void ReleaseSavepoint(string name)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}