using System.Data;
using System.Data.Common;

namespace LibRed.Data;

/// <summary>
/// A database transaction. Jet/ACE supports transactions; the engine wiring for
/// commit/rollback is still to come, so this currently tracks state only.
/// </summary>
public sealed class LibRedTransaction(LibRedConnection connection, IsolationLevel isolationLevel) : DbTransaction
{
    private readonly LibRedConnection _connection = connection;

    public override IsolationLevel IsolationLevel { get; } = isolationLevel;

    protected override DbConnection DbConnection => _connection;

    public override void Commit()
    {
        // TODO: flush buffered page writes atomically.
    }

    public override void Rollback()
    {
        // TODO: discard buffered page writes.
    }
}
