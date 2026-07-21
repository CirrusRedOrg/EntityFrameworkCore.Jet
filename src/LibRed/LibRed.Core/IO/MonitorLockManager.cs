using System.Collections.Concurrent;

namespace LibRed.IO;

/// <summary>
/// Process-local page coordination: a <see cref="ReaderWriterLockSlim"/> per page, so the handles a single
/// process holds on one file (e.g. EF's several connections) don't read a page mid-write. Shared per file path
/// via <see cref="ForPath"/> so every <see cref="PageChannel"/> on the same file coordinates through one map.
///
/// <para>This is the reference reader/writer behaviour; the cross-process byte-range managers (self-consistent,
/// then Jet-exact) must reproduce it against the same seam. It provides no cross-process coordination and no
/// coordination with Access — that is the later, file-based work (<c>docs/design/transactions.md</c>).</para>
/// </summary>
public sealed class MonitorLockManager : ILockManager
{
    private static readonly Dictionary<string, MonitorLockManager> ByPath = new(StringComparer.OrdinalIgnoreCase);

    // A lock per page, created on first use. Bounded by the file's page count; only allocated for a file that
    // actually opts into coordination (the default PageChannel takes no lock manager at all).
    private readonly ConcurrentDictionary<int, ReaderWriterLockSlim> _pageLocks = new();

    /// <summary>The shared manager for a file path, so all channels open on that file coordinate.</summary>
    public static MonitorLockManager ForPath(string path)
    {
        lock (ByPath)
        {
            if (!ByPath.TryGetValue(path, out MonitorLockManager? manager))
                ByPath[path] = manager = new MonitorLockManager();
            return manager;
        }
    }

    public void EnterShared(int page) => LockFor(page).EnterReadLock();

    public void ExitShared(int page) => LockFor(page).ExitReadLock();

    public void EnterExclusive(int page) => LockFor(page).EnterWriteLock();

    public void ExitExclusive(int page) => LockFor(page).ExitWriteLock();

    private ReaderWriterLockSlim LockFor(int page) =>
        _pageLocks.GetOrAdd(page, static _ => new ReaderWriterLockSlim());
}
