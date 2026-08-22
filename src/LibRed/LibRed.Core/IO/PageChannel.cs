using System.Buffers.Binary;
using LibRed.Crypto;
using LibRed.Formats;

namespace LibRed.IO;

/// <summary>
/// The single primitive every higher layer reads through: page-aligned, optionally
/// decrypted access to the database file. Owns the <see cref="FileStream"/> and the
/// resolved <see cref="JetFormatBase"/>.
/// </summary>
public sealed class PageChannel : IDisposable
{
    private readonly FileStream _stream;
    private readonly bool _readOnly;

    // Shared, write-through buffer pool for this file (one pool per physical file, shared across every channel
    // open on it — see PageCache). Turns a page read from a Seek()+Read() syscall into a memory copy, and keeps
    // coexisting handles coherent because they all read/write through the same pool. Never null after Open.
    private readonly string _path;
    private readonly PageCache _cache;

    // Page decryptor for a password-encrypted database (null when the file is unencrypted). Applied to
    // every page as it comes off disk; page 0 (the readable header) is a no-op inside the codec.
    private readonly IPageCodec? _codec;

    // Cross-handle page coordination, shared per file path. Acquired here (refcounted, like the cache) unless a
    // manager is injected for a test; _ownsLocks records which, so Dispose releases only the one we acquired.
    private readonly ILockManager? _locks;
    private readonly bool _ownsLocks;

    // The open transaction's savepoint bookkeeping (null when none is open). This channel is the I/O executor;
    // the Transaction only tracks what a savepoint rollback must restore.
    private Transaction? _active;

    // Deferred-write overlay for the open transaction: page -> uncommitted plaintext bytes. Transactional writes
    // land here instead of on disk / in the shared cache, so a concurrent channel on the same file never sees
    // this channel's uncommitted pages (read-committed isolation). Commit replays it; rollback discards it.
    // Per-channel and only touched while a transaction is open, so a channel's own single thread owns it — no
    // lock. `_txPageCount` is the logical page count during a transaction (committed pages plus any the overlay
    // allocated), since deferred allocations do not grow the file until commit.
    private readonly Dictionary<int, byte[]> _overlay = [];
    // Committed plaintext image from which each transactional page was first derived. At commit, every image
    // must still match; otherwise another channel committed the same page and publishing this stale overlay
    // would silently lose that writer's change.
    private readonly Dictionary<int, byte[]?> _commitBaselines = [];
    private bool _schemaDirty;
    private int _txPageCount;

    private PageChannel(FileStream stream, JetFormatBase format, bool readOnly, string path, IPageCodec? codec, ILockManager? locks)
    {
        _stream = stream;
        _readOnly = readOnly;
        Format = format;
        _path = path;
        _codec = codec;
        // A test may inject its own manager; otherwise share the per-path one (refcounted, released on Dispose).
        _ownsLocks = locks is null;
        _locks = locks ?? MonitorLockManager.Acquire(path);
        _cache = PageCache.Acquire(path, format.PageSize);
    }

    /// <summary>Whether a transaction is currently open on this channel.</summary>
    public bool InTransaction => _active is not null;

    public JetFormatBase Format { get; }

    internal long SchemaGeneration => _cache.SchemaGeneration;

    internal void MarkSchemaChanged()
    {
        if (_active is not null) _schemaDirty = true;
        else _cache.MarkSchemaChanged();
    }

    public int PageSize => Format.PageSize;

    /// <summary>Number of pages currently in the file — or, inside a transaction, the logical count including
    /// pages the overlay has allocated but not yet written to disk.</summary>
    public int PageCount => _active is not null ? _txPageCount : (int)(_stream.Length / PageSize);

    /// <summary>
    /// Opens a database file, sniffs its Jet/ACE version from page 0 and resolves the
    /// matching <see cref="JetFormatBase"/>.
    /// </summary>
    public static PageChannel Open(string path, bool readOnly = true, string? password = null, ILockManager? locks = null)
    {
        // A Jet/ACE file is a shared-file database — Access, ODBC and OLE DB all open it with multiple
        // concurrent handles (a store's long-lived connection plus per-context connections to the same
        // file, as EF's test infrastructure does). So we share read+write rather than taking the file
        // exclusively; an exclusive open (FileShare.None) would throw IOException the moment a second
        // connection touched the same .accdb. Coexisting handles stay coherent by sharing a single per-file
        // write-through buffer pool (PageCache) rather than each caching independently — one pool means one
        // connection's writes are seen by the others, as the old straight-to-disk reads guaranteed.
        var stream = new FileStream(
            path,
            FileMode.Open,
            readOnly ? FileAccess.Read : FileAccess.ReadWrite,
            FileShare.ReadWrite);

        try
        {
            var format = JetFormatBase.Detect(stream);

            // Read page 0 (always unencrypted) to detect encryption: a nonzero database key at 0x3E means the
            // data pages are ACE-encrypted, and the EncryptionInfo descriptor lives in the clear on this page.
            var page0 = new byte[format.PageSize];
            stream.Seek(0, SeekOrigin.Begin);
            stream.ReadExactly(page0);
            int databaseKey = DecodeDatabaseKey(page0);
            // A nonzero database key means the pages are encrypted. ACE (.accdb) uses Office Agile encryption
            // (with a password); the pre-ACE Jet 3/4 formats (.mdb and the .mdw workgroup file) use the legacy
            // RC4 scheme keyed by the database key alone (no password).
            // ACE (.accdb) may use Agile (XML descriptor) or the older Office "Standard"/CryptoAPI scheme (binary
            // descriptor) — detect by descriptor, trying Agile first then Standard. The pre-ACE Jet 3/4 formats
            // (.mdb and the .mdw workgroup file) use the legacy RC4 scheme keyed by the database key alone.
            IPageCodec? codec = format.IsAccdb
                ? (IPageCodec?)AgileEncryption.TryCreate(page0, databaseKey, password)
                    ?? OfficeStandardEncryption.TryCreate(page0, databaseKey, password)
                : JetLegacyEncryption.TryCreate(databaseKey);

            // A nonzero database key means the file is encrypted; if no codec recognised the descriptor the scheme
            // is unsupported — fail clearly rather than decoding ciphertext as plaintext (which corrupts downstream).
            if (databaseKey != 0 && codec is null)
                throw new NotSupportedException("The database is encrypted with an unsupported scheme.");

            return new PageChannel(stream, format, readOnly, path, codec, locks);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Decodes the 4-byte database (encryption) key at page-0 <c>0x3E</c> through the fixed header mask.</summary>
    private static int DecodeDatabaseKey(ReadOnlySpan<byte> page0)
    {
        ReadOnlySpan<byte> mask = JetFormatBase.PageZeroHeaderMask;
        int start = JetFormatBase.PageZeroHeaderMaskStart;
        Span<byte> key = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
            key[i] = (byte)(page0[JetFormatBase.DatabaseKeyOffset + i] ^ mask[JetFormatBase.DatabaseKeyOffset - start + i]);
        return BinaryPrimitives.ReadInt32LittleEndian(key);
    }

    /// <summary>Reads a single page into a freshly allocated buffer.</summary>
    public PageBuffer ReadPage(int pageNumber)
    {
        var buffer = new byte[PageSize];
        ReadPage(pageNumber, buffer);
        return new PageBuffer(buffer, pageNumber);
    }

    /// <summary>
    /// Returns a page backed by the shared cache's own buffer, without copying it out — a zero-allocation read
    /// for callers that consume the bytes immediately and never mutate or retain them (e.g. decoding one row
    /// during an index seek, where <see cref="ReadPage(int)"/>'s per-call 4 KB copy dominated). On a cache miss
    /// the page is read from disk and cached, then returned. The returned bytes are live cache state: valid
    /// only until the next write to or eviction of this page.
    /// </summary>
    public PageBuffer ReadPageShared(int pageNumber)
    {
        // Read-your-own-writes: a page this transaction has written lives only in the overlay until commit.
        if (_active is not null && _overlay.TryGetValue(pageNumber, out byte[]? buffered))
            return new PageBuffer(buffered, pageNumber);

        // Cache hit: the cache's own lock makes the lookup atomic and copy-on-write Store keeps the returned
        // array stable, so no page lock is needed — this is the hot path and stays coordination-free.
        if (_cache.TryGetArray(pageNumber, out byte[] cached))
            return new PageBuffer(cached, pageNumber);

        // Miss: read from disk under a shared page lock so a concurrent cross-handle write of this page can't
        // tear the read. Re-check the cache after acquiring — another handle may have filled it while we waited.
        _locks?.EnterShared(pageNumber);
        try
        {
            if (_cache.TryGetArray(pageNumber, out cached))
                return new PageBuffer(cached, pageNumber);

            var buffer = new byte[PageSize];
            long offset = (long)pageNumber * PageSize;
            _stream.Seek(offset, SeekOrigin.Begin);
            _stream.ReadExactly(buffer);
            _codec?.DecryptPage(pageNumber, buffer);
            _cache.Store(pageNumber, buffer);
            return new PageBuffer(buffer, pageNumber);
        }
        finally { _locks?.ExitShared(pageNumber); }
    }

    /// <summary>Reads a single page into the supplied buffer (must be at least <see cref="PageSize"/>).</summary>
    public void ReadPage(int pageNumber, Span<byte> destination)
    {
        if (destination.Length < PageSize)
            throw new ArgumentException($"Buffer must be at least {PageSize} bytes.", nameof(destination));

        // Read-your-own-writes: a page this transaction has written lives only in the overlay until commit.
        if (_active is not null && _overlay.TryGetValue(pageNumber, out byte[]? buffered))
        {
            buffered.CopyTo(destination);
            return;
        }

        // Cache hit: served under the cache's own lock, no page lock needed (the hot path).
        if (_cache.TryRead(pageNumber, destination))
            return;

        // Miss: read from disk under a shared page lock (coordinates with a cross-handle write), re-checking
        // the cache after acquiring in case another handle filled it while we waited.
        _locks?.EnterShared(pageNumber);
        try
        {
            if (_cache.TryRead(pageNumber, destination))
                return;

            long offset = (long)pageNumber * PageSize;
            _stream.Seek(offset, SeekOrigin.Begin);
            _stream.ReadExactly(destination[..PageSize]);
            _codec?.DecryptPage(pageNumber, destination[..PageSize]);
            _cache.Store(pageNumber, destination[..PageSize]);
        }
        finally { _locks?.ExitShared(pageNumber); }
    }

    /// <summary>
    /// Writes a full page back to the file at <paramref name="pageNumber"/>. If the page lies beyond
    /// the current end of the file, the file is grown to accommodate it (any intervening pages are
    /// zero-filled). This is legitimate: a page taken from the global free-pages map can lie past the
    /// physical end (the map pre-accounts for growth, and allocation defers the physical write), and
    /// writing the page is what materialises it — the same growth Access performs on such a write.
    /// </summary>
    public void WritePage(int pageNumber, ReadOnlySpan<byte> source)
    {
        if (_readOnly)
            throw new InvalidOperationException("This channel was opened read-only.");
        if (source.Length != PageSize)
            throw new ArgumentException($"A page write must be exactly {PageSize} bytes.", nameof(source));
        if (pageNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        // Inside a transaction, defer the write into the private overlay — invisible to other channels until
        // commit. Snapshot the page's prior overlay state (once per savepoint frame) so a savepoint rollback can
        // restore it, then buffer a private copy of the new bytes and advance the logical page count.
        if (_active is not null)
        {
            if (!_commitBaselines.ContainsKey(pageNumber))
                _commitBaselines[pageNumber] = ReadCommittedPageOrNull(pageNumber);
            if (_active.NeedsBeforeImage(pageNumber))
                _active.RecordBeforeImage(pageNumber, _overlay.TryGetValue(pageNumber, out byte[]? prior) ? prior : null);
            _overlay[pageNumber] = source[..PageSize].ToArray();
            if (pageNumber >= _txPageCount) _txPageCount = pageNumber + 1;
            return;
        }

        WriteThrough(pageNumber, source);
    }

    /// <summary>Writes a page to disk and the shared cache (the committed path): encrypts a copy on the way to
    /// disk for an encrypted file while caching plaintext, growing the file if the page lies past its end. Used
    /// for non-transactional writes and to publish each overlay page on commit.</summary>
    private void WriteThrough(int pageNumber, ReadOnlySpan<byte> source)
    {
        byte[] copy = source[..PageSize].ToArray();
        _cache.PublishLocked(() => WriteThroughUnderPublishLock(pageNumber, copy));
    }

    /// <summary>Runs a logical read against one committed page-set generation. Shared: other readers on this
    /// file run concurrently; only a publication excludes them.</summary>
    internal T ReadConsistent<T>(Func<T> action) => _cache.ReadConsistent(action);

    /// <summary>Runs a logical write with every other reader and writer on this file excluded, so its pages
    /// publish as one unit.</summary>
    internal T WriteExclusive<T>(Func<T> action) => _cache.PublishLocked(action);

    private void WriteThroughUnderPublishLock(int pageNumber, ReadOnlySpan<byte> source)
    {
        _locks?.EnterExclusive(pageNumber);
        try
        {
            // The cache holds plaintext and the disk holds ciphertext (for an encrypted file), so encrypt a copy
            // on the way to disk — the mirror of ReadPage's decrypt — while caching the plaintext. Page 0 is a
            // no-op inside the codec (never page-encrypted).
            ReadOnlySpan<byte> toDisk = source[..PageSize];
            byte[]? encrypted = null;
            if (_codec is not null)
            {
                encrypted = source[..PageSize].ToArray();
                _codec.EncryptPage(pageNumber, encrypted);
                toDisk = encrypted;
            }

            long offset = (long)pageNumber * PageSize;
            if (offset > _stream.Length)
                _stream.SetLength(offset); // zero-fills the gap up to this page
            _stream.Seek(offset, SeekOrigin.Begin);
            _stream.Write(toDisk);

            // Write through: the pool now holds the just-written (plaintext) image, so a subsequent read (this
            // channel or any other on the file) sees it without touching disk.
            _cache.Store(pageNumber, source[..PageSize]);
        }
        finally { _locks?.ExitExclusive(pageNumber); }
    }

    /// <summary>
    /// Allocates a fresh page by growing the file by one page, returning its number. Jet also
    /// recycles freed pages via usage maps; appending at the end is always valid since the page
    /// count is simply the file length divided by the page size.
    /// </summary>
    public int AllocatePage()
    {
        if (_readOnly)
            throw new InvalidOperationException("This channel was opened read-only.");

        int pageNumber = PageCount;
        WritePage(pageNumber, new byte[PageSize]);
        return pageNumber;
    }

    /// <summary>
    /// Begins a page-level transaction. Subsequent <see cref="WritePage"/> calls snapshot the
    /// original bytes of each page they touch, so <see cref="RollbackTransaction"/> can undo them.
    /// Reads continue to see writes made within the transaction (read-your-writes). Nesting is not
    /// supported.
    /// </summary>
    public Transaction BeginTransaction()
    {
        if (_readOnly)
            throw new InvalidOperationException("This channel was opened read-only.");
        if (_active is not null)
            throw new InvalidOperationException("A transaction is already in progress.");
        _overlay.Clear();
        _commitBaselines.Clear();
        _schemaDirty = false;
        _txPageCount = PageCount; // committed count at start (PageCount is still file-based while _active is null)
        return _active = new Transaction(_txPageCount);
    }

    /// <summary>Commits the current transaction: publishes every buffered overlay page to disk and the shared
    /// cache — making the writes visible to other channels for the first time — in ascending page order so the
    /// file grows monotonically. No-op if no transaction is open. <paramref name="flush"/> forces the OS buffers
    /// to disk (durability) — used by an explicit user commit; an implicit per-statement autocommit passes false,
    /// matching the pre-transaction behaviour of flushing only on <see cref="Dispose"/> rather than fsyncing
    /// every statement.</summary>
    public void CommitTransaction(bool flush = true)
    {
        if (_active is null) return;

        int[] pages = _overlay.Keys.ToArray();
        Array.Sort(pages);
        _cache.PublishLocked(() =>
        {
            foreach (int page in pages)
            {
                byte[]? current = ReadCommittedPageOrNull(page);
                byte[]? baseline = _commitBaselines[page];
                if (!SamePage(baseline, current))
                    throw new InvalidOperationException(
                        $"Transaction write conflict on page {page}: another connection committed a change to this page.");
            }

            // Keep the transaction open until every page has published. If a later page fails, restore the
            // already-published prefix from its validated committed baselines so the caller can still roll back.
            var published = new List<int>(pages.Length);
            try
            {
                foreach (int page in pages)
                {
                    WriteThroughUnderPublishLock(page, _overlay[page]);
                    published.Add(page);
                }
            }
            catch (Exception publishFailure)
            {
                // The restore writes to the same file that just failed to accept a write, so they can fail
                // too — and if one does, the original cause must not be swallowed by the cleanup's exception.
                // Collect both: the caller needs the publish failure to know why the commit failed, and the
                // restore failure to know the file was left mid-publish rather than rolled back.
                List<Exception>? restoreFailures = null;
                for (int i = published.Count - 1; i >= 0; i--)
                {
                    int page = published[i];
                    try
                    {
                        if (_commitBaselines[page] is { } baseline)
                        {
                            WriteThroughUnderPublishLock(page, baseline);
                        }
                        else
                        {
                            // A null baseline is a transaction-allocated tail page. Validation proved no other
                            // writer had claimed it, and the publish gate excludes one while we truncate it again.
                            _stream.SetLength((long)page * PageSize);
                            _cache.Remove(page);
                        }
                    }
                    catch (Exception restoreFailure)
                    {
                        (restoreFailures ??= []).Add(
                            new IOException($"Could not restore page {page} after a failed commit publication.", restoreFailure));
                    }
                }

                if (restoreFailures is null) throw;
                throw new AggregateException(
                    "A commit publication failed and the already-published pages could not all be restored; " +
                    "the file is left mid-publish.", [publishFailure, .. restoreFailures]);
            }

            _active = null;
            _overlay.Clear();
            _commitBaselines.Clear();
            if (_schemaDirty) _cache.MarkSchemaChanged();
            _schemaDirty = false;
        });

        if (flush) _stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Rolls the current transaction back by discarding its overlay. Nothing the transaction wrote ever reached
    /// disk or the shared cache, so there is nothing to restore, truncate or flush. No-op if none is open.
    /// </summary>
    public void RollbackTransaction()
    {
        if (_active is null) return;
        _overlay.Clear();
        _commitBaselines.Clear();
        _schemaDirty = false;
        _active = null;
    }

    /// <summary>Opens a savepoint in the current transaction; pass the handle to
    /// <see cref="RollbackToSavepoint"/> or <see cref="ReleaseSavepoint"/>.</summary>
    public Savepoint CreateSavepoint()
    {
        if (_active is null)
            throw new InvalidOperationException("No transaction is in progress.");
        return _active.Save(_txPageCount);
    }

    /// <summary>Rolls the transaction back to <paramref name="savepoint"/>: undoes every write made since it was
    /// created and drops pages allocated after it, leaving the savepoint (and the transaction) open.</summary>
    public void RollbackToSavepoint(Savepoint savepoint)
    {
        if (_active is null)
            throw new InvalidOperationException("No transaction is in progress.");
        var (before, pageCount) = _active.TakeForRollbackTo(savepoint);
        RestoreOverlay(before, pageCount);
    }

    /// <summary>Releases <paramref name="savepoint"/>, merging its changes into the enclosing scope. Only the
    /// innermost open savepoint may be released.</summary>
    public void ReleaseSavepoint(Savepoint savepoint)
    {
        if (_active is null)
            throw new InvalidOperationException("No transaction is in progress.");
        _active.Release(savepoint);
    }

    /// <summary>Restores the overlay to a savepoint: each before-image is re-applied newest-first so the oldest
    /// snapshot wins for a page touched in several frames — a <c>null</c> image means the page was absent at the
    /// savepoint, so drop it. The logical page count is reset, dropping overlay pages the rolled-back frames
    /// allocated. Nothing touches disk or the shared cache; the overlay never left this channel.</summary>
    private void RestoreOverlay(List<KeyValuePair<int, byte[]?>> before, int pageCount)
    {
        foreach (var (page, image) in before)
        {
            if (image is null) _overlay.Remove(page);
            else _overlay[page] = image;
        }
        _txPageCount = pageCount;
        foreach (int page in _commitBaselines.Keys.Where(p => !_overlay.ContainsKey(p)).ToArray())
            _commitBaselines.Remove(page);
    }

    private byte[]? ReadCommittedPageOrNull(int pageNumber)
    {
        int committedPageCount = (int)(_stream.Length / PageSize);
        if (pageNumber < 0 || pageNumber >= committedPageCount) return null;

        var buffer = new byte[PageSize];
        if (_cache.TryRead(pageNumber, buffer)) return buffer;

        _locks?.EnterShared(pageNumber);
        try
        {
            if (_cache.TryRead(pageNumber, buffer)) return buffer;
            _stream.Seek((long)pageNumber * PageSize, SeekOrigin.Begin);
            _stream.ReadExactly(buffer);
            _codec?.DecryptPage(pageNumber, buffer);
            _cache.Store(pageNumber, buffer);
            return buffer;
        }
        finally { _locks?.ExitShared(pageNumber); }
    }

    private static bool SamePage(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    /// <summary>Retrieves a higher-layer parse of a page previously stored via <see cref="SetParsedPage"/>
    /// (e.g. an index page's decoded entries), or false if none is cached. The parse is dropped automatically
    /// when the page is written (any channel) or evicted, so a hit is always consistent with the current bytes.</summary>
    public bool TryGetParsedPage(int pageNumber, out object? parsed)
    {
        // A page buffered in this transaction's overlay has uncommitted bytes; the shared parsed cache reflects
        // the committed image, so don't serve it — force a re-parse of the overlay bytes instead.
        if (_active is not null && _overlay.ContainsKey(pageNumber)) { parsed = null; return false; }
        return _cache.TryGetParsed(pageNumber, out parsed);
    }

    /// <summary>Caches a higher-layer parse of a (resident) page so repeated reads — e.g. a B-tree descent that
    /// re-visits the same root/internal pages — can skip re-decoding it. The caller must not mutate the object
    /// afterwards, as it is shared with other readers of the same file.</summary>
    public void SetParsedPage(int pageNumber, object parsed)
    {
        // Don't attach a transaction-local parse to the shared (committed) cache entry for an overlay page.
        if (_active is not null && _overlay.ContainsKey(pageNumber)) return;
        _cache.SetParsed(pageNumber, parsed);
    }

    public void Flush() => _stream.Flush(flushToDisk: true);

    public void Dispose()
    {
        if (!_readOnly) _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        PageCache.Release(_path); // last channel on this file drops the shared pool
        if (_ownsLocks) MonitorLockManager.Release(_path); // and the shared lock manager
    }
}
