namespace LibRed.IO;

/// <summary>
/// Coordinates page access between the handles that share one database file — eventually across processes,
/// so LibRed can hold a live <c>.accdb</c> open alongside Access. This is the seam the concurrency work fills
/// in: <see cref="PageChannel"/> acquires a shared lock around every read and an exclusive lock around every
/// write, and the implementation decides what that means.
///
/// <para>Implementations:
/// <list type="bullet">
/// <item><see cref="MonitorLockManager"/> — process-local reader/writer locks (LibRed↔LibRed within one
/// process); the reference behaviour the file-based managers must match.</item>
/// <item>(later) a self-consistent byte-range manager for cross-process LibRed↔LibRed, then a Jet-exact one
/// (the <c>LockFileEx</c> offset bands + page-0 commit-byte table) for live co-residency with Access — see
/// <c>docs/design/transactions.md</c>.</item>
/// </list></para>
///
/// <para>Locks are <b>operation-scoped</b> in this phase: acquired and released around a single page read or
/// write, which prevents a reader from seeing a half-written page. Holding locks to transaction commit (strict
/// two-phase locking, for full isolation) is a later refinement layered on the same seam.</para>
/// </summary>
/// <remarks>The API is <c>Enter</c>/<c>Exit</c> (not a disposable handle) so the hot path — a page read takes
/// and releases a shared lock — allocates nothing; <see cref="PageChannel"/> pairs each Enter with an Exit in a
/// <c>finally</c>. Locks are non-reentrant: a caller never enters the same page twice on one thread.</remarks>
public interface ILockManager
{
    /// <summary>Takes a shared (read) lock on the page. Multiple readers may hold it concurrently, but not while
    /// a writer does. Pair with <see cref="ExitShared"/>.</summary>
    void EnterShared(int page);

    /// <summary>Releases the shared lock taken by <see cref="EnterShared"/>.</summary>
    void ExitShared(int page);

    /// <summary>Takes an exclusive (write) lock on the page, excluding all other readers and writers of it. Pair
    /// with <see cref="ExitExclusive"/>.</summary>
    void EnterExclusive(int page);

    /// <summary>Releases the exclusive lock taken by <see cref="EnterExclusive"/>.</summary>
    void ExitExclusive(int page);
}
