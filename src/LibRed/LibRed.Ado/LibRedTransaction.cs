using System.Data;
using System.Data.Common;
using LibRed.IO;

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

    // Named savepoints opened in this transaction (EF names them for nested SaveChanges). Maps the name to the
    // engine's savepoint handle.
    private readonly Dictionary<string, Savepoint> _savepoints = new(StringComparer.Ordinal);

    internal LibRedTransaction(LibRedConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection;
        IsolationLevel = isolationLevel;
    }

    public override IsolationLevel IsolationLevel { get; }

    protected override DbConnection? DbConnection => _connection;

    /// <summary>LibRed supports savepoints (backed by the transaction's savepoint stack), so EF Core uses them
    /// to make a nested <c>SaveChanges</c> inside a user transaction individually reversible.</summary>
    public override bool SupportsSavepoints => true;

    /// <summary>Opens a savepoint with the given name — a rollback point within this transaction.</summary>
    public override void Save(string savepointName)
    {
        EnsureActive();
        _savepoints[savepointName] = _connection!.CreateSavepoint();
    }

    /// <summary>Rolls back to a named savepoint, undoing writes made since it was opened; the transaction and
    /// the savepoint stay open.</summary>
    public override void Rollback(string savepointName)
    {
        EnsureActive();
        _connection!.RollbackToSavepoint(Lookup(savepointName));
    }

    /// <summary>Releases a named savepoint, merging its writes into the enclosing scope.</summary>
    public override void Release(string savepointName)
    {
        EnsureActive();
        _connection!.ReleaseSavepoint(Lookup(savepointName));
        _savepoints.Remove(savepointName);
    }

    private void EnsureActive()
    {
        if (_completed)
            throw new InvalidOperationException("This transaction has already been committed or rolled back.");
    }

    private Savepoint Lookup(string name) =>
        _savepoints.TryGetValue(name, out Savepoint sp)
            ? sp
            : throw new InvalidOperationException($"No savepoint named '{name}' is open in this transaction.");

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
