using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

public class TransactionFailureRecoveryTests
{
    [Fact]
    public void Conflicting_file_growth_can_rollback_and_retry_without_truncating_the_winner()
    {
        string path = Fresh("growth-conflict-");
        try
        {
            using var first = PageChannel.Open(path, readOnly: false);
            using var second = PageChannel.Open(path, readOnly: false);
            int originalCount = first.PageCount;

            first.BeginTransaction();
            second.BeginTransaction();
            int firstPage = first.AllocatePage();
            int secondPage = second.AllocatePage();
            Assert.Equal(originalCount, firstPage);
            Assert.Equal(firstPage, secondPage);
            WriteMarker(first, firstPage, 0x11);
            WriteMarker(second, secondPage, 0x22);

            first.CommitTransaction();
            var conflict = Assert.Throws<InvalidOperationException>(() => second.CommitTransaction());
            Assert.Contains("write conflict", conflict.Message, StringComparison.OrdinalIgnoreCase);
            second.RollbackTransaction();

            Assert.Equal(originalCount + 1, second.PageCount);
            Assert.Equal(0x11, second.ReadPage(firstPage).Span[100]);

            second.BeginTransaction();
            int retryPage = second.AllocatePage();
            Assert.Equal(originalCount + 1, retryPage);
            WriteMarker(second, retryPage, 0x22);
            second.CommitTransaction();

            Assert.Equal(originalCount + 2, first.PageCount);
            Assert.Equal(0x11, first.ReadPage(firstPage).Span[100]);
            Assert.Equal(0x22, first.ReadPage(retryPage).Span[100]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Disposing_channel_with_outstanding_growth_discards_the_overlay()
    {
        string path = Fresh("dispose-growth-");
        try
        {
            byte[] before = File.ReadAllBytes(path);
            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                channel.BeginTransaction();
                int page = channel.AllocatePage();
                WriteMarker(channel, page, 0x7E);
                Assert.Equal(before.Length / channel.PageSize + 1, channel.PageCount);
            }

            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Failed_multi_page_publication_restores_already_published_pages_and_leaves_transaction_rollbackable()
    {
        string path = Fresh("commit-failure-");
        try
        {
            byte[] before = File.ReadAllBytes(path);
            var locks = new FailOnceOnSecondExclusiveLockManager();
            using (var channel = PageChannel.Open(path, readOnly: false, locks: locks))
            {
                channel.BeginTransaction();
                WriteMarker(channel, 5, 0x51);
                WriteMarker(channel, 6, 0x61);

                Assert.Throws<IOException>(() => channel.CommitTransaction());
                Assert.True(channel.InTransaction);
                channel.RollbackTransaction();
            }

            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Failed_growth_publication_truncates_published_tail_and_same_transaction_can_retry_commit()
    {
        string path = Fresh("commit-growth-failure-");
        try
        {
            int originalLength = checked((int)new FileInfo(path).Length);
            var locks = new FailOnceOnSecondExclusiveLockManager();
            using (var channel = PageChannel.Open(path, readOnly: false, locks: locks))
            {
                channel.BeginTransaction();
                int firstPage = channel.AllocatePage();
                int secondPage = channel.AllocatePage();
                WriteMarker(channel, firstPage, 0x31);
                WriteMarker(channel, secondPage, 0x32);

                Assert.Throws<IOException>(() => channel.CommitTransaction());
                Assert.True(channel.InTransaction);
                Assert.Equal(originalLength, new FileInfo(path).Length);

                channel.CommitTransaction();
                Assert.False(channel.InTransaction);
                Assert.Equal(originalLength + 2 * channel.PageSize, new FileInfo(path).Length);
                Assert.Equal(0x31, channel.ReadPage(firstPage).Span[100]);
                Assert.Equal(0x32, channel.ReadPage(secondPage).Span[100]);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void WriteMarker(PageChannel channel, int page, byte marker)
    {
        byte[] bytes = channel.ReadPage(page).Span.ToArray();
        bytes[100] = marker;
        channel.WritePage(page, bytes);
    }

    private static string Fresh(string prefix) => TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, prefix);

    private sealed class FailOnceOnSecondExclusiveLockManager : ILockManager
    {
        private int _exclusiveEntries;
        public void EnterShared(int page) { }
        public void ExitShared(int page) { }
        public void EnterExclusive(int page)
        {
            if (Interlocked.Increment(ref _exclusiveEntries) == 2)
                throw new IOException("Injected commit publication failure.");
        }
        public void ExitExclusive(int page) { }
    }
}
