namespace LibRed.Engine;

/// <summary>
/// Connection-scoped runtime state that outlives a single statement, backing the SQL system
/// variables <c>@@ROWCOUNT</c> and <c>@@IDENTITY</c>. One instance lives on a <see cref="QueryEngine"/>
/// (which is created once per open connection), so the values a statement sets are visible to the next
/// statement on the same connection — exactly what EF Core's insert-then-select round-trip relies on.
/// </summary>
/// <remarks>
/// Unlike SQL Server, Jet/ACE <c>@@IDENTITY</c> is *connection*-scoped, not table-scoped: it holds the
/// last AutoNumber generated on the connection regardless of table, and is overwritten by the next
/// insert that generates one. So it must be read immediately after the insert, before another insert
/// clobbers it — which is exactly why EF emits the guarded SELECT in the same batch.
/// </remarks>
public sealed class SessionState
{
    /// <summary>Rows affected by the most recent DML statement (<c>@@ROWCOUNT</c>).</summary>
    public int RowCount { get; set; }

    /// <summary>The last AutoNumber generated on this connection (<c>@@IDENTITY</c>), or <c>null</c> if
    /// none has been generated yet.</summary>
    public object? LastIdentity { get; set; }

    /// <summary>The 24-bit VBA <c>Rnd()</c> generator seed (connection-scoped, VBA default <c>0x50000</c>). Kept
    /// here so a <c>Rnd()</c> sequence is deterministic across the connection — matching ACE, whose JES has no
    /// <c>Randomize</c> to reseed it.</summary>
    public uint RandSeed { get; set; } = 0x50000;
}
