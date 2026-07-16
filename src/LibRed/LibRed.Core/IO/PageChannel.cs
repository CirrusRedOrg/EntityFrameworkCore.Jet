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

    // Page-level undo log for the current transaction (null when none is open). Keyed by page
    // number; the value is the page's bytes as they were *before* the transaction first touched
    // it. Pages allocated during the transaction lie beyond _txnOriginalLength and are not logged
    // — rollback simply truncates the file back to that length to drop them.
    private Dictionary<int, byte[]>? _undo;
    private long _txnOriginalLength;

    private PageChannel(FileStream stream, JetFormatBase format, bool readOnly, string path, IPageCodec? codec)
    {
        _stream = stream;
        _readOnly = readOnly;
        Format = format;
        _path = path;
        _codec = codec;
        _cache = PageCache.Acquire(path, format.PageSize);
    }

    /// <summary>Whether a transaction is currently open on this channel.</summary>
    public bool InTransaction => _undo is not null;

    public JetFormatBase Format { get; }

    public int PageSize => Format.PageSize;

    /// <summary>Number of pages currently in the file.</summary>
    public int PageCount => (int)(_stream.Length / PageSize);

    /// <summary>
    /// Opens a database file, sniffs its Jet/ACE version from page 0 and resolves the
    /// matching <see cref="JetFormatBase"/>.
    /// </summary>
    public static PageChannel Open(string path, bool readOnly = true, string? password = null)
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

            return new PageChannel(stream, format, readOnly, path, codec);
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
        if (_cache.TryGetArray(pageNumber, out byte[] cached))
            return new PageBuffer(cached, pageNumber);

        var buffer = new byte[PageSize];
        long offset = (long)pageNumber * PageSize;
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.ReadExactly(buffer);
        _codec?.DecryptPage(pageNumber, buffer);
        _cache.Store(pageNumber, buffer);
        return new PageBuffer(buffer, pageNumber);
    }

    /// <summary>Reads a single page into the supplied buffer (must be at least <see cref="PageSize"/>).</summary>
    public void ReadPage(int pageNumber, Span<byte> destination)
    {
        if (destination.Length < PageSize)
            throw new ArgumentException($"Buffer must be at least {PageSize} bytes.", nameof(destination));

        // Serve from the shared pool if resident; otherwise read the file once and fill the pool.
        if (_cache.TryRead(pageNumber, destination))
            return;

        long offset = (long)pageNumber * PageSize;
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.ReadExactly(destination[..PageSize]);
        _codec?.DecryptPage(pageNumber, destination[..PageSize]);
        _cache.Store(pageNumber, destination[..PageSize]);
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

        // Before the first write to a pre-existing page inside a transaction, snapshot its current
        // bytes so Rollback can restore them. Pages beyond the file's length at BeginTransaction are
        // fresh allocations — no snapshot needed; rollback truncates them away wholesale.
        if (_undo is not null)
        {
            long pageOffset = (long)pageNumber * PageSize;
            if (pageOffset < _txnOriginalLength && !_undo.ContainsKey(pageNumber))
            {
                var original = new byte[PageSize];
                _stream.Seek(pageOffset, SeekOrigin.Begin);
                _stream.ReadExactly(original);
                _undo[pageNumber] = original;
            }
        }

        // The cache holds plaintext and the disk holds ciphertext (for an encrypted file), so encrypt a copy on
        // the way to disk — the mirror of ReadPage's decrypt — while caching the plaintext. Page 0 is a no-op
        // inside the codec (never page-encrypted).
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

        // Write through: the pool now holds the just-written (plaintext) image, so a subsequent read (this channel
        // or any other on the file) sees it without touching disk.
        _cache.Store(pageNumber, source[..PageSize]);
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
    public void BeginTransaction()
    {
        if (_readOnly)
            throw new InvalidOperationException("This channel was opened read-only.");
        if (_undo is not null)
            throw new InvalidOperationException("A transaction is already in progress.");
        _undo = [];
        _txnOriginalLength = _stream.Length;
    }

    /// <summary>Commits the current transaction: the writes are already on disk, so this just
    /// flushes and discards the undo log. No-op if no transaction is open.</summary>
    public void CommitTransaction()
    {
        if (_undo is null) return;
        _undo = null;
        _stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Rolls the current transaction back: restores every snapshotted page to its pre-transaction
    /// bytes and truncates the file to its pre-transaction length (dropping any pages allocated
    /// during the transaction). No-op if no transaction is open.
    /// </summary>
    public void RollbackTransaction()
    {
        if (_undo is null) return;

        foreach (var (pageNumber, original) in _undo)
        {
            // `original` is the raw on-disk image (ciphertext for an encrypted file) — write it back verbatim,
            // but keep the pool in plaintext by decrypting a copy for the cache.
            _stream.Seek((long)pageNumber * PageSize, SeekOrigin.Begin);
            _stream.Write(original);
            byte[] plain = original;
            if (_codec is not null) { plain = (byte[])original.Clone(); _codec.DecryptPage(pageNumber, plain); }
            _cache.Store(pageNumber, plain);
        }

        _stream.SetLength(_txnOriginalLength);
        _cache.EvictFrom((int)(_txnOriginalLength / PageSize)); // drop pages that the truncation removed
        _undo = null;
        _stream.Flush(flushToDisk: true);
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
    }
}
