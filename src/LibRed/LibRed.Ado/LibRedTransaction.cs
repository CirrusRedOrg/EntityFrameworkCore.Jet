using System.Data;
using System.Data.Common;
using LibRed.IO;

namespace LibRed.Data;

/// <summary>
/// A database transaction over LibRed's deferred-write page overlay. Writes are buffered in the overlay
/// rather than going to disk, so <see cref="Commit"/> is what makes them visible at all, and
/// <see cref="Rollback"/> simply discards the overlay — there is nothing on disk to restore. An uncommitted
/// transaction that is disposed rolls back, which is what gives EF Core's shared-database tests their
/// per-test isolation.
/// </summary>
public sealed class LibRedTransaction : DbTransaction
{
    private LibRedConnection? _connection;
    private bool _completed;

    // Named savepoints opened in this transaction (EF names them for nested SaveChanges). Maps the name to the
    // engine's savepoint handle.
    private readonly Dictionary<string, Savepoint> _savepoints = new(StringComparer.Ordinal);

    internal LibRedTransaction(LibRedConnection connection, IsolationLevel isolationLevel, int openedAtDepth)
    {
        _connection = connection;
        IsolationLevel = isolationLevel;
        OpenedAtDepth = openedAtDepth;
    }

    /// <summary>The engine's nesting depth before this handle opened its level — how far
    /// <see cref="Commit"/> unwinds, so a SQL <c>BEGIN</c> left open inside it cannot strand the
    /// transaction. See <c>LibRedConnection.CommitTransaction</c>.</summary>
    internal int OpenedAtDepth { get; }

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
        Savepoint target = Lookup(savepointName);
        _connection!.RollbackToSavepoint(target);

        // Rolling back discards every frame ABOVE the target, so the names that addressed them are now stale.
        // A Savepoint is a bare frame index with no validity token, and the next Save reuses the freed index —
        // so a stale name silently resolves to a DIFFERENT savepoint rather than failing. Release does drop
        // its own name; this drops the ones the rollback invalidated. EF Core reaches this shape whenever a
        // nested SaveChanges rolls back inside a user transaction and another follows it.
        foreach (string stale in _savepoints
                     .Where(e => e.Value.Index > target.Index)
                     .Select(e => e.Key).ToList())
            _savepoints.Remove(stale);
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

    /// <summary>Marks this handle complete when SQL COMMIT/ROLLBACK closes the physical transaction.</summary>
    internal void CompleteFromSql()
    {
        _completed = true;
        _connection = null;
        _savepoints.Clear();
    }

    // A DbException (not a plain InvalidOperationException): referencing a released/never-opened savepoint is a
    // database-operation error, and callers — EF Core's transaction tests included — expect DbException for it.
    private Savepoint Lookup(string name) =>
        _savepoints.TryGetValue(name, out Savepoint sp)
            ? sp
            : throw new LibRedException($"No savepoint named '{name}' is open in this transaction.", 0);

    public override void Commit()
    {
        if (_completed)
            throw new InvalidOperationException("This transaction has already been committed or rolled back.");
        _connection?.CommitTransaction(this);
        _completed = true;
        _savepoints.Clear();
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
