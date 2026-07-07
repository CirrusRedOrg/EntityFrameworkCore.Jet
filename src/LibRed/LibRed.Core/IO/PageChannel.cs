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

    // Page-level undo log for the current transaction (null when none is open). Keyed by page
    // number; the value is the page's bytes as they were *before* the transaction first touched
    // it. Pages allocated during the transaction lie beyond _txnOriginalLength and are not logged
    // — rollback simply truncates the file back to that length to drop them.
    private Dictionary<int, byte[]>? _undo;
    private long _txnOriginalLength;

    private PageChannel(FileStream stream, JetFormatBase format, bool readOnly)
    {
        _stream = stream;
        _readOnly = readOnly;
        Format = format;
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
    public static PageChannel Open(string path, bool readOnly = true)
    {
        // A Jet/ACE file is a shared-file database — Access, ODBC and OLE DB all open it with multiple
        // concurrent handles (a store's long-lived connection plus per-context connections to the same
        // file, as EF's test infrastructure does). So we share read+write rather than taking the file
        // exclusively; an exclusive open (FileShare.None) would throw IOException the moment a second
        // connection touched the same .accdb. Reads go straight to the stream (no long-lived page cache),
        // so coexisting handles observe each other's committed writes.
        var stream = new FileStream(
            path,
            FileMode.Open,
            readOnly ? FileAccess.Read : FileAccess.ReadWrite,
            FileShare.ReadWrite);

        try
        {
            var format = JetFormatBase.Detect(stream);
            return new PageChannel(stream, format, readOnly);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Reads a single page into a freshly allocated buffer.</summary>
    public PageBuffer ReadPage(int pageNumber)
    {
        var buffer = new byte[PageSize];
        ReadPage(pageNumber, buffer);
        return new PageBuffer(buffer, pageNumber);
    }

    /// <summary>Reads a single page into the supplied buffer (must be at least <see cref="PageSize"/>).</summary>
    public void ReadPage(int pageNumber, Span<byte> destination)
    {
        if (destination.Length < PageSize)
            throw new ArgumentException($"Buffer must be at least {PageSize} bytes.", nameof(destination));

        long offset = (long)pageNumber * PageSize;
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.ReadExactly(destination[..PageSize]);

        // TODO: page-level decryption (RC4 for Jet3/4, AES for ACE) happens here,
        // keyed off Format + the database definition page. See LibRed.Crypto.JetCrypto.
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

        // TODO: page-level encryption mirrors the decryption in ReadPage once that lands.
        long offset = (long)pageNumber * PageSize;
        if (offset > _stream.Length)
            _stream.SetLength(offset); // zero-fills the gap up to this page
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.Write(source[..PageSize]);
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
            _stream.Seek((long)pageNumber * PageSize, SeekOrigin.Begin);
            _stream.Write(original);
        }

        _stream.SetLength(_txnOriginalLength);
        _undo = null;
        _stream.Flush(flushToDisk: true);
    }

    public void Flush() => _stream.Flush(flushToDisk: true);

    public void Dispose()
    {
        if (!_readOnly) _stream.Flush(flushToDisk: true);
        _stream.Dispose();
    }
}
