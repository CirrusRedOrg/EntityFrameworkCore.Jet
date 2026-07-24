using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

/// <summary>
/// EF Core relational transaction for LibRed that implements savepoints by calling the ADO transaction's
/// savepoint API directly, instead of the base <see cref="RelationalTransaction"/>'s SQL path.
/// <para>The base issues savepoint <i>SQL</i> via <c>ISqlGenerationHelper</c>, but the inherited
/// <c>JetSqlGenerationHelper</c> generates an <b>empty</b> statement for every savepoint operation (Jet has no
/// savepoints, and its transaction throws before that SQL is ever reached). Executing that empty SQL is a silent
/// no-op — EF believes a savepoint exists when none does. LibRed's ADO <c>LibRedTransaction</c> already maps
/// <c>Save</c>/<c>Rollback</c>/<c>Release</c> to the engine's real savepoints, so we route straight to it.</para>
/// </summary>
public class LibRedRelationalTransaction : RelationalTransaction
{
    private readonly DbTransaction _transaction;

    public LibRedRelationalTransaction(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned,
        ISqlGenerationHelper sqlGenerationHelper)
        : base(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
        => _transaction = transaction;

    public override void CreateSavepoint(string name) => _transaction.Save(name);

    public override Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
        => _transaction.SaveAsync(name, cancellationToken);

    public override void RollbackToSavepoint(string name) => _transaction.Rollback(name);

    public override Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
        => _transaction.RollbackAsync(name, cancellationToken);

    public override void ReleaseSavepoint(string name) => _transaction.Release(name);

    public override Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
        => _transaction.ReleaseAsync(name, cancellationToken);

    public override bool SupportsSavepoints => true;
}
