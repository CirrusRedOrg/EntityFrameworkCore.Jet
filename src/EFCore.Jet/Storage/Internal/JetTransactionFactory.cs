namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RelationalTransactionFactory" /> class.
    /// </summary>
    /// <param name="dependencies">Parameter object containing dependencies for this service.</param>
    public class JetTransactionFactory(RelationalTransactionFactoryDependencies dependencies) : IRelationalTransactionFactory
    {

        /// <summary>
        ///     Relational provider-specific dependencies for this service.
        /// </summary>
        protected virtual RelationalTransactionFactoryDependencies Dependencies { get; } = dependencies;

        public virtual RelationalTransaction Create(
            IRelationalConnection connection,
            DbTransaction transaction,
            Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned)
            => new JetTransaction(connection, transaction, transactionId, logger, transactionOwned,Dependencies.SqlGenerationHelper);
    }
}