namespace LibRed.IO;

/// <summary>
/// Process-local page coordination: the handles a single process holds on one file (e.g. EF's several
/// connections) don't read a page mid-write. Shared per file path via <see cref="Acquire"/>/<see cref="Release"/>
/// (refcounted, like <see cref="PageCache"/>), so every <see cref="PageChannel"/> on the same file coordinates
/// through one manager and it is freed when the last channel closes.
///
/// <para>Locks are <b>striped</b>: a page maps to one of a fixed number of <see cref="ReaderWriterLockSlim"/> by
/// its low bits, so memory is bounded no matter how many pages a large database touches. (A per-page map grew a
/// lock per page forever and leaked millions of them.) Distinct pages almost never collide, and a lock is only
/// taken on the rare cache-miss read and on writes, so occasional false sharing is immaterial.</para>
///
/// <para>This is the reference reader/writer behaviour; the cross-process byte-range managers reproduce it
/// against the same seam. It provides no cross-process coordination and none with Access — that is the later,
/// file-based work (<c>docs/design/transactions.md</c>).</para>
/// </summary>
public sealed class MonitorLockManager : ILockManager, IDisposable
{
    private const int StripeCount = 256;
    private readonly ReaderWriterLockSlim?[] _stripes = new ReaderWriterLockSlim?[StripeCount];

    private ReaderWriterLockSlim Stripe(int page) =>
        LazyInitializer.EnsureInitialized(ref _stripes[(int)((uint)page % StripeCount)], static () => new ReaderWriterLockSlim());

    public void EnterShared(int page) => Stripe(page).EnterReadLock();
    public void ExitShared(int page) => Stripe(page).ExitReadLock();
    public void EnterExclusive(int page) => Stripe(page).EnterWriteLock();
    public void ExitExclusive(int page) => Stripe(page).ExitWriteLock();

    public void Dispose()
    {
        foreach (ReaderWriterLockSlim? stripe in _stripes) stripe?.Dispose();
    }

    // --- refcounted per-path registry: one manager per canonical file path, freed on the last Release ---

    private static readonly Dictionary<string, (MonitorLockManager Manager, int RefCount)> Registry =
        new(StringComparer.Ordinal);

    private static string Key(string path) => Path.GetFullPath(path).ToLowerInvariant();

    /// <summary>The shared manager for a file path (creating it on first use); each call must be paired with a
    /// <see cref="Release"/>.</summary>
    public static MonitorLockManager Acquire(string path)
    {
        string key = Key(path);
        lock (Registry)
        {
            if (Registry.TryGetValue(key, out var slot))
            {
                Registry[key] = (slot.Manager, slot.RefCount + 1);
                return slot.Manager;
            }
            var manager = new MonitorLockManager();
            Registry[key] = (manager, 1);
            return manager;
        }
    }

    /// <summary>Drops one reference; the last release disposes the manager (and its lock stripes).</summary>
    public static void Release(string path)
    {
        string key = Key(path);
        lock (Registry)
        {
            if (!Registry.TryGetValue(key, out var slot)) return;
            if (slot.RefCount <= 1)
            {
                Registry.Remove(key);
                slot.Manager.Dispose();
            }
            else
            {
                Registry[key] = (slot.Manager, slot.RefCount - 1);
            }
        }
    }
}
