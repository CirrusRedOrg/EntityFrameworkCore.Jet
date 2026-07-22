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

    // The open transaction's before-image/savepoint bookkeeping (null when none is open). This channel is the
    // I/O executor; the Transaction only tracks what to undo. Pages allocated during a frame lie beyond its
    // start length and are not snapshotted — rollback truncates the file to drop them.
    private Transaction? _active;

    private PageChannel(FileStream stream, JetFormatBase format, bool readOnly, string path, IPageCodec? codec, ILockManager? locks)
    {
        _stream = stream;
        _readOnly = readOnly;
        Format = format;
        _path = path;
        LibRedDiagnostics.EnterPageChannel();
        _codec = codec;
        // A test may inject its own manager; otherwise share the per-path one (refcounted, released on Dispose).
        _ownsLocks = locks is null;
        _locks = locks ?? MonitorLockManager.Acquire(path);
        _cache = PageCache.Acquire(path, format.PageSize);
    }

    /// <summary>Whether a transaction is currently open on this channel.</summary>
    public bool InTransaction => _active is not null;

    public JetFormatBase Format { get; }

    public int PageSize => Format.PageSize;

    /// <summary>Number of pages currently in the file.</summary>
    public int PageCount => (int)(_stream.Length / PageSize);

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

        _locks?.EnterExclusive(pageNumber);
        try
        {
            // Before the first write within the current savepoint frame to a page that pre-dates that frame,
            // snapshot its current (raw, on-disk) bytes so rollback can restore them. Pages allocated within the
            // frame lie beyond its start length and need no snapshot — rollback truncates them away wholesale.
            if (_active is not null)
            {
                long snapshotOffset = (long)pageNumber * PageSize;
                if (_active.NeedsBeforeImage(pageNumber, snapshotOffset))
                {
                    var original = new byte[PageSize];
                    _stream.Seek(snapshotOffset, SeekOrigin.Begin);
                    _stream.ReadExactly(original);
                    _active.RecordBeforeImage(pageNumber, original);
                }
            }

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
        return _active = new Transaction(_stream.Length);
    }

    /// <summary>Commits the current transaction: the writes are already on disk (write-through), so this just
    /// discards the undo bookkeeping. No-op if no transaction is open. <paramref name="flush"/> forces the OS
    /// buffers to disk (durability) — used by an explicit user commit; an implicit per-statement autocommit
    /// passes false, matching the pre-transaction behaviour of flushing only on <see cref="Dispose"/> rather
    /// than fsyncing every statement.</summary>
    public void CommitTransaction(bool flush = true)
    {
        if (_active is null) return;
        _active = null;
        if (flush) _stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Rolls the current transaction back: restores every snapshotted page to its pre-transaction
    /// bytes and truncates the file to its pre-transaction length (dropping any pages allocated
    /// during the transaction). No-op if no transaction is open.
    /// </summary>
    public void RollbackTransaction()
    {
        if (_active is null) return;
        var (images, truncateTo) = _active.TakeForRollback();
        RestoreImages(images, truncateTo);
        _active = null;
        _stream.Flush(flushToDisk: true);
    }

    /// <summary>Opens a savepoint in the current transaction; pass the handle to
    /// <see cref="RollbackToSavepoint"/> or <see cref="ReleaseSavepoint"/>.</summary>
    public Savepoint CreateSavepoint()
    {
        if (_active is null)
            throw new InvalidOperationException("No transaction is in progress.");
        return _active.Save(_stream.Length);
    }

    /// <summary>Rolls the transaction back to <paramref name="savepoint"/>: undoes every write made since it was
    /// created and drops pages allocated after it, leaving the savepoint (and the transaction) open.</summary>
    public void RollbackToSavepoint(Savepoint savepoint)
    {
        if (_active is null)
            throw new InvalidOperationException("No transaction is in progress.");
        var (images, truncateTo) = _active.TakeForRollbackTo(savepoint);
        RestoreImages(images, truncateTo);
    }

    /// <summary>Releases <paramref name="savepoint"/>, merging its changes into the enclosing scope. Only the
    /// innermost open savepoint may be released.</summary>
    public void ReleaseSavepoint(Savepoint savepoint)
    {
        if (_active is null)
            throw new InvalidOperationException("No transaction is in progress.");
        _active.Release(savepoint);
    }

    /// <summary>Restores raw before-images to disk and the plaintext pool, then truncates to
    /// <paramref name="truncateTo"/> (dropping pages allocated past that point). Images are applied newest-first
    /// so the oldest snapshot wins for a page touched in several frames; an image beyond the truncation point is
    /// written but then dropped by the truncation, which keeps the logic simple and correct.</summary>
    private void RestoreImages(List<KeyValuePair<int, byte[]>> images, long truncateTo)
    {
        foreach (var (pageNumber, original) in images)
        {
            // `original` is the raw on-disk image (ciphertext for an encrypted file) — write it back verbatim,
            // but keep the pool in plaintext by decrypting a copy for the cache.
            _stream.Seek((long)pageNumber * PageSize, SeekOrigin.Begin);
            _stream.Write(original);
            byte[] plain = original;
            if (_codec is not null) { plain = (byte[])original.Clone(); _codec.DecryptPage(pageNumber, plain); }
            _cache.Store(pageNumber, plain);
        }

        _stream.SetLength(truncateTo);
        _cache.EvictFrom((int)(truncateTo / PageSize)); // drop pages that the truncation removed
    }

    /// <summary>Retrieves a higher-layer parse of a page previously stored via <see cref="SetParsedPage"/>
    /// (e.g. an index page's decoded entries), or false if none is cached. The parse is dropped automatically
    /// when the page is written (any channel) or evicted, so a hit is always consistent with the current bytes.</summary>
    public bool TryGetParsedPage(int pageNumber, out object? parsed) => _cache.TryGetParsed(pageNumber, out parsed);

    /// <summary>Caches a higher-layer parse of a (resident) page so repeated reads — e.g. a B-tree descent that
    /// re-visits the same root/internal pages — can skip re-decoding it. The caller must not mutate the object
    /// afterwards, as it is shared with other readers of the same file.</summary>
    public void SetParsedPage(int pageNumber, object parsed) => _cache.SetParsed(pageNumber, parsed);

    public void Flush() => _stream.Flush(flushToDisk: true);

    public void Dispose()
    {
        if (!_readOnly) _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        PageCache.Release(_path); // last channel on this file drops the shared pool
        if (_ownsLocks) MonitorLockManager.Release(_path); // and the shared lock manager
        LibRedDiagnostics.ExitPageChannel();
    }
}
