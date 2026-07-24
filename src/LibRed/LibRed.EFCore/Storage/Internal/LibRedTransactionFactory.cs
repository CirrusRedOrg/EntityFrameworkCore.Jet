using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

/// <summary>
/// Produces EF Core's base <see cref="RelationalTransaction"/> rather than EFCore.Jet's <c>JetTransaction</c>.
/// <c>JetTransaction</c> overrides every savepoint operation to throw, because ACE/Jet has no savepoints — but
/// LibRed's engine does (<c>PageChannel.CreateSavepoint</c>/<c>RollbackToSavepoint</c>/<c>ReleaseSavepoint</c>)
/// and its ADO <c>LibRedTransaction</c> exposes them (<c>SupportsSavepoints</c> + <c>Save</c>/<c>Rollback</c>/
/// <c>Release</c>). The base <see cref="RelationalTransaction"/> honours savepoints by delegating to the ADO
/// transaction, so LibRed uses it to get real savepoint support (EF nests a SaveChanges inside a user
/// transaction on one).
/// </summary>
public class LibRedTransactionFactory(RelationalTransactionFactoryDependencies dependencies) : IRelationalTransactionFactory
{
    protected virtual RelationalTransactionFactoryDependencies Dependencies { get; } = dependencies;

    public virtual RelationalTransaction Create(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned)
        => new LibRedRelationalTransaction(
            connection, transaction, transactionId, logger, transactionOwned, Dependencies.SqlGenerationHelper);
}
