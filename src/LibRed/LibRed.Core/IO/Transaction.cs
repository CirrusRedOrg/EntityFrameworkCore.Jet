namespace LibRed.IO;

/// <summary>Opaque handle to a savepoint within a <see cref="Transaction"/> (the frame's stack position).</summary>
public readonly record struct Savepoint(int Index);

/// <summary>
/// A page-level, in-process transaction implemented with <b>deferred writes</b>: the owning
/// <see cref="PageChannel"/> buffers every transactional page write into a private overlay and publishes it to
/// disk and the shared cache only on commit, so concurrent channels on the same file never observe uncommitted
/// pages (read-committed isolation). A full rollback simply discards the overlay.
///
/// <para>This type tracks only what is needed to roll back <b>to a savepoint</b>: a LIFO stack of frames, each
/// recording — for every page it was the first to write — what the overlay held for that page <i>before</i>
/// this frame touched it. A <c>null</c> before-image means the page was <b>absent</b> from the overlay at the
/// frame's start, so restoring drops it. Each frame also captures the logical page count when it opened, the
/// high-water mark to restore (pages the frame allocated are dropped). The owning channel performs the actual
/// overlay restore; this type is pure bookkeeping.</para>
/// </summary>
public sealed class Transaction
{
    private sealed class Frame
    {
        // page -> overlay bytes before this frame's first write to it; null = the page was not in the overlay.
        public readonly Dictionary<int, byte[]?> Before = [];
        public int StartPageCount;
    }

    private readonly List<Frame> _frames;

    internal Transaction(int originalPageCount) => _frames = [new Frame { StartPageCount = originalPageCount }];

    /// <summary>Logical page count when the transaction began — the high-water to restore on a full rollback.</summary>
    internal int OriginalPageCount => _frames[0].StartPageCount;

    /// <summary>Nesting depth: 1 with no open savepoint, higher inside savepoints.</summary>
    internal int Depth => _frames.Count;

    /// <summary>Whether the innermost frame still needs to snapshot the overlay state of a page about to be
    /// written (i.e. this frame has not yet recorded a before-image for it).</summary>
    internal bool NeedsBeforeImage(int page) => !_frames[^1].Before.ContainsKey(page);

    /// <summary>Records, in the innermost frame, what the overlay held for <paramref name="page"/> before this
    /// frame's first write to it (<c>null</c> = it was absent). Call only when <see cref="NeedsBeforeImage"/>
    /// returned true.</summary>
    internal void RecordBeforeImage(int page, byte[]? priorOverlay) => _frames[^1].Before[page] = priorOverlay;

    /// <summary>Opens a savepoint over the given current logical page count; returns a handle for
    /// <see cref="TakeForRollbackTo"/> / <see cref="Release"/>.</summary>
    internal Savepoint Save(int currentPageCount)
    {
        _frames.Add(new Frame { StartPageCount = currentPageCount });
        return new Savepoint(_frames.Count - 1);
    }

    /// <summary>Collects the overlay before-images to restore (newest first, so the oldest image wins for a page
    /// touched in several frames) and the logical page count for rolling back to <paramref name="sp"/>. Discards
    /// the frames above the savepoint and empties the savepoint's own frame, leaving it active for reuse. The
    /// caller applies the restore to the overlay.</summary>
    internal (List<KeyValuePair<int, byte[]?>> Before, int PageCount) TakeForRollbackTo(Savepoint sp)
    {
        ValidateFrame(sp.Index);
        var before = CollectNewestFirst(sp.Index);
        int pageCount = _frames[sp.Index].StartPageCount;
        _frames.RemoveRange(sp.Index + 1, _frames.Count - sp.Index - 1);
        _frames[sp.Index].Before.Clear();
        return (before, pageCount);
    }

    /// <summary>Releases (merges) the innermost savepoint into its parent: the child's changes become part of the
    /// enclosing frame, so a later rollback of the parent still undoes them. Its before-images move down only for
    /// pages the parent hasn't already snapshotted (the parent's older image must win).</summary>
    internal void Release(Savepoint sp)
    {
        if (sp.Index != _frames.Count - 1)
            throw new InvalidOperationException("Only the innermost savepoint can be released.");
        if (sp.Index == 0)
            throw new InvalidOperationException("The transaction root is not a savepoint.");

        Frame child = _frames[^1];
        Frame parent = _frames[^2];
        foreach (var (page, image) in child.Before)
            parent.Before.TryAdd(page, image);
        _frames.RemoveAt(_frames.Count - 1);
    }

    /// <summary>Collects every overlay before-image (newest first) for a full rollback, and the transaction's
    /// original logical page count. The transaction is spent after this. (The channel may instead simply clear
    /// the whole overlay, which is equivalent and cheaper.)</summary>
    internal (List<KeyValuePair<int, byte[]?>> Before, int PageCount) TakeForRollback() =>
        (CollectNewestFirst(0), OriginalPageCount);

    private List<KeyValuePair<int, byte[]?>> CollectNewestFirst(int fromFrame)
    {
        var images = new List<KeyValuePair<int, byte[]?>>();
        for (int i = _frames.Count - 1; i >= fromFrame; i--)
            foreach (var kv in _frames[i].Before)
                images.Add(kv);
        return images;
    }

    private void ValidateFrame(int index)
    {
        if (index < 0 || index >= _frames.Count)
            throw new InvalidOperationException("The savepoint is not open in this transaction.");
    }
}
