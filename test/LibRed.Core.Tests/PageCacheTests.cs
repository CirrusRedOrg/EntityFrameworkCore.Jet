using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// The page cache is a <b>single shared, write-through pool per physical file</b>. These pin the property that
/// makes that design safe under the shared-file model (multiple channels open on one .accdb): a page one
/// channel has already cached must still reflect another channel's write. A naive per-channel cache would fail
/// the coherence test — the second channel would serve its own stale copy.
/// </summary>
public class PageCacheTests
{
    private static string CopyNorthwind(string tag)
    {
        return TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, $"pagecache-{tag}-");
    }

    [Fact]
    public void A_second_channels_cached_page_reflects_the_first_channels_write()
    {
        string path = CopyNorthwind("coherence");
        try
        {
            using var writer = PageChannel.Open(path, readOnly: false);
            using var reader = PageChannel.Open(path, readOnly: false);

            const int page = 1; // any existing non-page-0 page; we only compare raw bytes
            byte[] original = reader.ReadPage(page).Span.ToArray(); // reader caches the current image

            byte[] mutated = original.ToArray();
            mutated[0x40] ^= 0xFF; // flip a byte well past the page header so nothing rejects the write
            writer.WritePage(page, mutated);

            // The reader had this page cached from its earlier read. With a shared pool it now sees the write;
            // with independent caches it would still return the stale `original`.
            byte[] seen = reader.ReadPage(page).Span.ToArray();
            Assert.Equal(mutated, seen);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The pool is per FILE, and the key has to decide that the way the file system does. Two paths differing
    // only in the case of the name are one file where names are case-insensitive and two files where they are
    // not, so the same assertion reads both ways: the second channel sees the first's write exactly when the
    // two paths name one file. Keying on the lower-cased path made them share everywhere, which on a
    // case-sensitive file system writes one database's pages into another's.
    [Fact]
    public void Two_paths_share_a_pool_exactly_when_they_name_one_file()
    {
        string path = CopyNorthwind("case");
        string variant = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(path).ToUpperInvariant());
        Assert.NotEqual(path, variant);

        // Ground truth from the file system itself: where the upper-cased name already resolves, it is the file
        // just written; where it does not, a second file is made so both paths exist either way.
        bool sameFile = File.Exists(variant);
        if (!sameFile) File.Copy(path, variant);
        try
        {
            const int page = 1;
            byte[] mutated;
            using (var writer = PageChannel.Open(path, readOnly: false))
            using (var reader = PageChannel.Open(variant, readOnly: false))
            {
                mutated = reader.ReadPage(page).Span.ToArray(); // the reader caches the current image
                mutated[0x40] ^= 0xFF;
                writer.WritePage(page, mutated);

                Assert.Equal(sameFile, reader.ReadPage(page).Span.SequenceEqual(mutated));
            }

            // And the same on disk once both have closed: one file carries the write, two files do not.
            byte[] onDisk = File.ReadAllBytes(variant);
            int pageSize = TestDatabases.FormatOf(variant).PageSize;
            Assert.Equal(sameFile, onDisk.AsSpan(page * pageSize, pageSize).SequenceEqual(mutated));
        }
        finally
        {
            if (!sameFile) TemporaryDatabase.Delete(variant);
            TemporaryDatabase.Delete(path);
        }
    }

    [Fact]
    public void Rollback_restores_the_cached_image_not_just_the_disk()
    {
        string path = CopyNorthwind("rollback");
        try
        {
            using var ch = PageChannel.Open(path, readOnly: false);
            const int page = 1;
            byte[] original = ch.ReadPage(page).Span.ToArray();

            ch.BeginTransaction();
            byte[] mutated = original.ToArray();
            mutated[0x40] ^= 0xFF;
            ch.WritePage(page, mutated);
            Assert.Equal(mutated, ch.ReadPage(page).Span.ToArray()); // read-your-writes within the txn
            ch.RollbackTransaction();

            // After rollback the pool must serve the pre-transaction bytes, not the rolled-back write.
            Assert.Equal(original, ch.ReadPage(page).Span.ToArray());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Reopening_after_the_last_channel_closes_reads_committed_bytes_from_disk()
    {
        string path = CopyNorthwind("reopen");
        try
        {
            const int page = 1;
            byte[] mutated;
            using (var ch = PageChannel.Open(path, readOnly: false))
            {
                byte[] original = ch.ReadPage(page).Span.ToArray();
                mutated = original.ToArray();
                mutated[0x40] ^= 0xFF;
                ch.WritePage(page, mutated); // write-through to disk; flushed on dispose
            }

            // The pool was dropped when the last channel closed; a fresh open must re-read the committed image.
            using var reopened = PageChannel.Open(path, readOnly: true);
            Assert.Equal(mutated, reopened.ReadPage(page).Span.ToArray());
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}