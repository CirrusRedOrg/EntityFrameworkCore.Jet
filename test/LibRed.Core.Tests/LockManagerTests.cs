using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

public class LockManagerTests
{
    [Fact]
    public void Multiple_readers_hold_the_same_page_concurrently()
    {
        // A second reader on another thread enters while the first still holds the page (readers don't exclude
        // readers). The locks are operation-scoped, so a single thread never nests two acquisitions on one page.
        var m = new MonitorLockManager();
        m.EnterShared(5);
        try
        {
            var secondEntered = new ManualResetEventSlim();
            var t = Task.Run(() => { m.EnterShared(5); m.ExitShared(5); secondEntered.Set(); });
            Assert.True(secondEntered.Wait(2000)); // not blocked by the first reader
            t.Wait(2000);
        }
        finally { m.ExitShared(5); }
    }

    [Fact]
    public void A_writer_blocks_a_reader_of_the_same_page_until_released()
    {
        var m = new MonitorLockManager();
        m.EnterExclusive(5);

        var readerEntered = new ManualResetEventSlim();
        var reader = Task.Run(() => { m.EnterShared(5); m.ExitShared(5); readerEntered.Set(); });

        Assert.False(readerEntered.Wait(250)); // the writer holds the page, so the reader can't enter
        m.ExitExclusive(5);
        Assert.True(readerEntered.Wait(2000)); // released — the reader proceeds
        reader.Wait(2000);
    }

    [Fact]
    public void Locks_on_different_pages_do_not_block_each_other()
    {
        var m = new MonitorLockManager();
        m.EnterExclusive(1);
        m.EnterExclusive(2); // different page → independent, does not block
        m.ExitExclusive(2);
        m.ExitExclusive(1);
    }

    [Fact]
    public void Acquire_returns_one_shared_manager_per_path_and_frees_it_on_last_release()
    {
        MonitorLockManager a = MonitorLockManager.Acquire(@"C:\dir\db.accdb");
        MonitorLockManager b = MonitorLockManager.Acquire(@"C:\dir\DB.accdb"); // same file (case-insensitive)
        Assert.Same(a, b);

        // Two acquisitions → two releases; a fresh acquire after that is a new manager (the old was disposed).
        MonitorLockManager.Release(@"C:\dir\db.accdb");
        MonitorLockManager.Release(@"C:\dir\db.accdb");
        Assert.NotSame(a, MonitorLockManager.Acquire(@"C:\dir\db.accdb"));
        MonitorLockManager.Release(@"C:\dir\db.accdb");
    }

    [Fact]
    public void PageChannel_reads_and_writes_correctly_under_a_lock_manager()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-locked-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var channel = PageChannel.Open(path, readOnly: false, locks: new MonitorLockManager());

            var page = channel.ReadPage(1).Span.ToArray();
            page[10] ^= 0xFF;

            channel.BeginTransaction();
            channel.WritePage(1, page);
            Assert.Equal(page[10], channel.ReadPage(1).Span[10]); // read-your-write under the lock seams
            channel.RollbackTransaction();

            Assert.NotEqual(page[10], channel.ReadPage(1).Span[10]); // rollback restored the original
        }
        finally { File.Delete(path); }
    }
}
