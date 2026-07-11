namespace LibRed.IO;

/// <summary>
/// A bounded, write-through buffer pool of raw page bytes for one physical database file, <b>shared by every
/// <see cref="PageChannel"/> open on that file</b> (see <see cref="Acquire"/>). A single cache per file is what
/// keeps coexisting handles coherent: because they all read and write through the same pool, one connection's
/// committed — or, as today, uncommitted — writes are immediately visible to the others, exactly as the old
/// straight-to-disk reads were, only now served from memory instead of a <c>Seek()+Read()</c> per page.
///
/// <para>The pool owns its own copies: reads copy <i>out</i> to the caller and writes copy <i>in</i> from the
/// caller, so a caller mutating its buffer can never corrupt a cached page. Eviction is LRU, capped at
/// <see cref="CapacityPages"/>. All operations are guarded by a per-cache lock — same-file access from multiple
/// threads is serialised at the page level (concurrent unsynchronised writes to the same page were never
/// defined behaviour anyway).</para>
/// </summary>
internal sealed class PageCache
{
    /// <summary>Max resident pages per file. At a 4 KB page this is ~32 MB, comfortably covering the whole of a
    /// typical Access database (and the benchmark tables); larger databases simply evict LRU.</summary>
    private const int CapacityPages = 8192;

    private sealed class Entry(int page, byte[] bytes)
    {
        public readonly int Page = page;
        public byte[] Bytes = bytes;

        // An optional higher-layer parse of this page (e.g. an index page's decoded entries), cached alongside
        // the raw bytes so it is invalidated by the same write-through/eviction path — whenever the bytes change
        // the parse is stale, so Store() clears it. Opaque to the cache (boxed as object).
        public object? Parsed;
    }

    private readonly int _pageSize;
    private readonly object _gate = new();
    private readonly Dictionary<int, LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _lru = new(); // first = most-recently-used

    private PageCache(int pageSize) => _pageSize = pageSize;

    // --- shared registry: one cache per canonical file path, refcounted by the channels using it ---

    private static readonly Dictionary<string, (PageCache Cache, int RefCount)> Registry = new(StringComparer.Ordinal);
    private static readonly object RegistryGate = new();

    /// <summary>Returns the shared cache for <paramref name="path"/>, creating it on first use; each call must be
    /// paired with a <see cref="Release"/>. The key is the case-folded full path so relative and absolute opens
    /// of the same file share one pool (two pools for one file would reintroduce cross-handle staleness).</summary>
    public static PageCache Acquire(string path, int pageSize)
    {
        string key = Path.GetFullPath(path).ToLowerInvariant();
        lock (RegistryGate)
        {
            if (Registry.TryGetValue(key, out var slot))
            {
                Registry[key] = (slot.Cache, slot.RefCount + 1);
                return slot.Cache;
            }
            var cache = new PageCache(pageSize);
            Registry[key] = (cache, 1);
            return cache;
        }
    }

    /// <summary>Drops one reference to the cache for <paramref name="path"/>; when the last channel closes, the
    /// pool is discarded so a later re-open re-reads from disk (picking up any external change).</summary>
    public static void Release(string path)
    {
        string key = Path.GetFullPath(path).ToLowerInvariant();
        lock (RegistryGate)
        {
            if (!Registry.TryGetValue(key, out var slot)) return;
            if (slot.RefCount <= 1) Registry.Remove(key);
            else Registry[key] = (slot.Cache, slot.RefCount - 1);
        }
    }

    /// <summary>Copies the cached bytes of <paramref name="page"/> into <paramref name="destination"/> and
    /// returns true on a hit; false (leaving the buffer untouched) on a miss.</summary>
    public bool TryRead(int page, Span<byte> destination)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(page, out var node)) return false;
            _lru.Remove(node);
            _lru.AddFirst(node);
            node.Value.Bytes.CopyTo(destination);
            return true;
        }
    }

    /// <summary>Returns the cache's own byte array for a resident <paramref name="page"/> without copying it out
    /// — for a read that consumes the bytes immediately (e.g. decoding one row) and never mutates or retains
    /// them. False on a miss. The array is live cache state: treat it read-only and use it before any further
    /// cache operation on the same thread could evict or overwrite it.</summary>
    public bool TryGetArray(int page, out byte[] array)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(page, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                array = node.Value.Bytes;
                return true;
            }
            array = [];
            return false;
        }
    }

    /// <summary>Stores <paramref name="source"/> as the cached image of <paramref name="page"/> (copying it),
    /// used both to fill a read miss and to write through a page write.</summary>
    public void Store(int page, ReadOnlySpan<byte> source)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(page, out var node))
            {
                source.CopyTo(node.Value.Bytes);
                node.Value.Parsed = null; // the bytes changed, so any cached parse of them is stale
                _lru.Remove(node);
                _lru.AddFirst(node);
                return;
            }

            var entry = new Entry(page, source.ToArray());
            var added = _lru.AddFirst(entry);
            _map[page] = added;

            if (_map.Count > CapacityPages)
            {
                LinkedListNode<Entry> lruNode = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(lruNode.Value.Page);
            }
        }
    }

    /// <summary>Returns a previously cached higher-layer parse of <paramref name="page"/> (see
    /// <see cref="SetParsed"/>), or false if the page is not resident or has no parse cached.</summary>
    public bool TryGetParsed(int page, out object? parsed)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(page, out var node) && node.Value.Parsed is { } p)
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                parsed = p;
                return true;
            }
            parsed = null;
            return false;
        }
    }

    /// <summary>Attaches a higher-layer parse to a resident page so later reads can skip re-parsing; a no-op if
    /// the page is not resident (it will simply be re-parsed next time). Cleared automatically when the page's
    /// bytes change (see <see cref="Store"/>) or it is evicted.</summary>
    public void SetParsed(int page, object parsed)
    {
        lock (_gate)
            if (_map.TryGetValue(page, out var node))
                node.Value.Parsed = parsed;
    }

    /// <summary>Drops every cached page numbered <paramref name="fromPageInclusive"/> or higher — used after a
    /// rollback truncates the file so freshly-allocated (now non-existent) pages don't linger in the pool.</summary>
    public void EvictFrom(int fromPageInclusive)
    {
        lock (_gate)
        {
            var drop = _map.Keys.Where(p => p >= fromPageInclusive).ToList();
            foreach (int p in drop)
            {
                _lru.Remove(_map[p]);
                _map.Remove(p);
            }
        }
    }
}
