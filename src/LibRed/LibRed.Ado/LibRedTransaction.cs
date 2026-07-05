using System.Data;
using System.Data.Common;

namespace LibRed.Data;

/// <summary>
/// A database transaction over LibRed's page-level undo log. <see cref="Commit"/> makes the writes
/// permanent (they are already on disk; commit just discards the undo log); <see cref="Rollback"/>
/// restores every page the transaction touched and drops any it allocated. An uncommitted
/// transaction that is disposed rolls back — this is what gives EF Core's shared-database tests
/// their per-test isolation.
/// </summary>
public sealed class LibRedTransaction : DbTransaction
{
    private LibRedConnection? _connection;
    private bool _completed;

    internal LibRedTransaction(LibRedConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection;
        IsolationLevel = isolationLevel;
    }

    public override IsolationLevel IsolationLevel { get; }

    protected override DbConnection? DbConnection => _connection;

    public override void Commit()
    {
        if (_completed)
            throw new InvalidOperationException("This transaction has already been committed or rolled back.");
        _connection?.CommitTransaction(this);
        _completed = true;
    }

    public override void Rollback()
    {
        if (_completed)
            throw new InvalidOperationException("This transaction has already been committed or rolled back.");
        _connection?.RollbackTransaction(this);
        _completed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_completed)
        {
            // Disposed without an explicit Commit → roll back.
            _connection?.RollbackTransaction(this);
            _completed = true;
        }

        _connection = null;
        base.Dispose(disposing);
    }
}
