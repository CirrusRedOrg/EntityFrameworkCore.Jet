namespace LibRed.IO;

/// <summary>Opaque handle to a savepoint within a <see cref="Transaction"/> (the frame's stack position).</summary>
public readonly record struct Savepoint(int Index);

/// <summary>
/// A page-level, in-process transaction: a LIFO stack of savepoint frames. Each frame holds the
/// <b>before-images</b> (raw on-disk bytes) of the pages it was the first to touch, plus the file length
/// when the frame opened — so a rollback can restore those pages and drop any allocated after the frame.
///
/// This type only tracks <i>what to undo</i>; the owning <see cref="PageChannel"/> performs the actual page
/// I/O (it is the only thing that can read/write/encrypt pages). Snapshotting is <b>per frame</b>: the first
/// write to a page within a frame records that page's current bytes, so rolling back to a savepoint restores
/// the page to its state <i>at</i> the savepoint, not at transaction start.
/// </summary>
public sealed class Transaction
{
    private sealed class Frame
    {
        public readonly Dictionary<int, byte[]> Before = [];
        public long StartLength;
    }

    private readonly List<Frame> _frames;

    internal Transaction(long originalLength) => _frames = [new Frame { StartLength = originalLength }];

    /// <summary>File length when the transaction began — the truncation target for a full rollback.</summary>
    internal long OriginalLength => _frames[0].StartLength;

    /// <summary>Nesting depth: 1 with no open savepoint, higher inside savepoints.</summary>
    internal int Depth => _frames.Count;

    /// <summary>Whether the innermost frame needs a before-image for a page about to be written: the page must
    /// pre-date this frame (offset below the frame's start length — pages allocated within the frame are dropped
    /// by truncation, not restored) and not already be snapshotted in this frame.</summary>
    internal bool NeedsBeforeImage(int page, long pageOffset) =>
        pageOffset < _frames[^1].StartLength && !_frames[^1].Before.ContainsKey(page);

    /// <summary>Records a page's pre-write raw bytes in the innermost frame. Call only when
    /// <see cref="NeedsBeforeImage"/> returned true.</summary>
    internal void RecordBeforeImage(int page, byte[] rawOriginal) => _frames[^1].Before[page] = rawOriginal;

    /// <summary>Opens a savepoint over the given current file length; returns a handle for
    /// <see cref="TakeForRollbackTo"/> / <see cref="Release"/>.</summary>
    internal Savepoint Save(long currentLength)
    {
        _frames.Add(new Frame { StartLength = currentLength });
        return new Savepoint(_frames.Count - 1);
    }

    /// <summary>Collects the before-images to restore (newest first, so the oldest image wins for a page touched
    /// in several frames) and the truncation length for rolling back to <paramref name="sp"/>. Discards the
    /// frames above the savepoint and empties the savepoint's own frame, leaving it active for reuse. The caller
    /// performs the restore I/O and truncation.</summary>
    internal (List<KeyValuePair<int, byte[]>> Images, long TruncateTo) TakeForRollbackTo(Savepoint sp)
    {
        ValidateFrame(sp.Index);
        var images = CollectNewestFirst(sp.Index);
        long truncateTo = _frames[sp.Index].StartLength;
        _frames.RemoveRange(sp.Index + 1, _frames.Count - sp.Index - 1);
        _frames[sp.Index].Before.Clear();
        return (images, truncateTo);
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

    /// <summary>Collects every before-image (newest first) for a full rollback, and the transaction's original
    /// length. The transaction is spent after this.</summary>
    internal (List<KeyValuePair<int, byte[]>> Images, long TruncateTo) TakeForRollback() =>
        (CollectNewestFirst(0), OriginalLength);

    private List<KeyValuePair<int, byte[]>> CollectNewestFirst(int fromFrame)
    {
        var images = new List<KeyValuePair<int, byte[]>>();
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
