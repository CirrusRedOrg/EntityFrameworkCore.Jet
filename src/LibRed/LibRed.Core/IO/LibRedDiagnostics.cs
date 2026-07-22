using System.Diagnostics;

namespace LibRed.IO;

/// <summary>
/// Opt-in memory/lifetime tracing for diagnosing leaks under long or parallel runs. Enabled only when the
/// environment variable <c>LIBRED_MEMTRACE</c> names a file; then a background timer appends a CSV sample every
/// ~2s: managed heap, GC counts, working set/handles, the two per-path registries, total resident cache pages,
/// and live-instance counters for the major owned objects. Whichever counter climbs without bound points at the
/// leak. Zero cost when the variable is unset (the timer is never started and the counters are cheap interlocked
/// ints the callers still bump, which is negligible).
/// </summary>
public static class LibRedDiagnostics
{
    private static int _jetDatabases;
    private static int _pageChannels;
    private static int _connections;

    public static void EnterJetDatabase() { EnsureStarted(); Interlocked.Increment(ref _jetDatabases); }
    public static void ExitJetDatabase() => Interlocked.Decrement(ref _jetDatabases);
    public static void EnterPageChannel() { EnsureStarted(); Interlocked.Increment(ref _pageChannels); }
    public static void ExitPageChannel() => Interlocked.Decrement(ref _pageChannels);
    public static void EnterConnection() { EnsureStarted(); Interlocked.Increment(ref _connections); }
    public static void ExitConnection() => Interlocked.Decrement(ref _connections);

    private static volatile bool _started;
    private static Timer? _timer;
    private static readonly object Gate = new();

    /// <summary>Starts sampling on first use if <c>LIBRED_MEMTRACE</c> is set; a no-op otherwise. Cheap after the
    /// first call (a volatile flag), so the Enter hooks can call it unconditionally.</summary>
    private static void EnsureStarted()
    {
        if (_started) return;
        lock (Gate)
        {
            if (_started) return;
            _started = true; // never re-check, even when tracing is off
            string? path = Environment.GetEnvironmentVariable("LIBRED_MEMTRACE");
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                File.AppendAllText(path,
                    "elapsedMs,gcMemMB,gen0,gen1,gen2,workingSetMB,handles,jetDatabases,pageChannels,connections,cacheRegistry,cachedPages,lockRegistry\n");
            }
            catch { return; }
            _timer = new Timer(_ => Sample(path), null, dueTime: 0, period: 2000);
        }
    }

    private static void Sample(string path)
    {
        try
        {
            using Process p = Process.GetCurrentProcess();
            string line = string.Join(',',
                Environment.TickCount64,
                GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024),
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                p.WorkingSet64 / (1024 * 1024),
                p.HandleCount,
                Volatile.Read(ref _jetDatabases),
                Volatile.Read(ref _pageChannels),
                Volatile.Read(ref _connections),
                PageCache.RegistryCount,
                PageCache.TotalResidentPages,
                MonitorLockManager.RegistryCount);
            File.AppendAllText(path, line + "\n");
        }
        catch
        {
            // Diagnostics must never disturb a run.
        }
    }
}
