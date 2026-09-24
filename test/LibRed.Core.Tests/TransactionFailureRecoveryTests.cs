using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

public class TransactionFailureRecoveryTests
{
    // An update is several writes, and the row is the last of them — so it is the last that can fail. Before
    // it does, the old memo's pages have been freed and the new value's written. Through the SQL engine the
    // statement's own transaction undoes all of that; a caller using the Core API directly had no such cover,
    // and a refused update left the row naming a memo whose pages had been given away.
    [Fact]
    public void A_refused_core_update_leaves_the_row_and_its_memo_intact()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "update-undo-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var specs = new List<Catalog.ColumnSpec>
            {
                new("Id", Catalog.JetDataType.Int32, 4, IsFixedLength: true),
                new("M", Catalog.JetDataType.Memo, 0, IsFixedLength: false),
            };
            for (int i = 0; i < 9; i++)   // 9 x 510 bytes of text takes the record past the 4060 cap
                specs.Add(new Catalog.ColumnSpec($"T{i}", Catalog.JetDataType.Text, 510, IsFixedLength: false));
            db.CreateTable("U", specs, primaryKey: ["Id"]);

            string original = new('m', 4000);   // long enough to own its own pages
            Storage.Table table = db.OpenTable("U");
            var inserted = new object?[specs.Count];
            inserted[0] = 1;
            inserted[1] = original;
            for (int i = 0; i < 9; i++) inserted[2 + i] = "short";
            table.Insert(inserted);

            var (id, _) = table.Rows().WithIds().Single();
            // A new memo AND nine full text columns: the memo is written and the old one freed, and then the
            // row itself is refused.
            var oversized = new object?[specs.Count];
            oversized[0] = 1;
            oversized[1] = new string('n', 4000);
            for (int i = 0; i < 9; i++) oversized[2 + i] = new string('t', 255);
            Assert.ThrowsAny<Exception>(() =>
                table.Update(id, oversized, new HashSet<int>(Enumerable.Range(1, specs.Count - 1))));

            // Nothing moved: the row still reads, and its memo still resolves to the value it had.
            object?[] after = db.OpenTable("U").Rows().Single();
            Assert.Equal(original, after[1]);
            Assert.Equal("short", after[2]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

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
