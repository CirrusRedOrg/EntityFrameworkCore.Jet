using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

public class PageChannelTests
{
    [Fact]
    public void WritePage_beyond_the_end_grows_the_file_and_zero_fills_the_gap()
    {
        // A page allocated from the global free-pages map can lie past the physical end of a small
        // file (allocation defers the write); writing it must grow the file rather than throw.
        string path = Path.Combine(Path.GetTempPath(), $"libred-grow-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var channel = PageChannel.Open(path, readOnly: false);
            int before = channel.PageCount;

            var page = new byte[channel.PageSize];
            page[0] = 0xAB;
            channel.WritePage(before + 2, page); // two pages past the end

            Assert.Equal(before + 3, channel.PageCount);
            Assert.Equal(0xAB, channel.ReadPage(before + 2).Span[0]);
            Assert.Equal(0, channel.ReadPage(before + 1).Span[0]); // the skipped page is zero-filled
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RollbackTransaction_restores_modified_pages_and_drops_allocated_ones()
    {
        // The undo log is what gives EF Core's shared-database tests their per-test isolation: a
        // rolled-back transaction must leave the file byte-for-byte as it was before it began.
        string path = Path.Combine(Path.GetTempPath(), $"libred-rollback-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            byte[] before = File.ReadAllBytes(path); // baseline captured before opening (channel takes an exclusive lock)
            int pagesBefore;
            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                pagesBefore = channel.PageCount;

                channel.BeginTransaction();

                // Modify an existing page...
                var page = channel.ReadPage(1).Span.ToArray();
                page[10] ^= 0xFF;
                channel.WritePage(1, page);

                // ...and allocate a couple of new ones.
                channel.AllocatePage();
                channel.WritePage(channel.PageCount, new byte[channel.PageSize]);
                Assert.True(channel.PageCount > pagesBefore);
                Assert.True(channel.InTransaction);

                channel.RollbackTransaction();

                Assert.False(channel.InTransaction);
                Assert.Equal(pagesBefore, channel.PageCount);
            }

            Assert.Equal(before, File.ReadAllBytes(path)); // byte-for-byte identical to pre-transaction
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CommitTransaction_keeps_the_writes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-commit-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var channel = PageChannel.Open(path, readOnly: false);
            var page = channel.ReadPage(1).Span.ToArray();
            page[10] ^= 0xFF;

            channel.BeginTransaction();
            channel.WritePage(1, page);
            channel.CommitTransaction();

            Assert.False(channel.InTransaction);
            Assert.Equal(page[10], channel.ReadPage(1).Span[10]); // change survived the commit
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RollbackToSavepoint_keeps_pre_savepoint_writes_undoes_later_ones_and_drops_allocations()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-sp-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var channel = PageChannel.Open(path, readOnly: false);
            byte original2 = channel.ReadPage(2).Span[10];

            channel.BeginTransaction();

            // Before the savepoint: change page 1.
            var p1 = channel.ReadPage(1).Span.ToArray();
            p1[10] = 0x11;
            channel.WritePage(1, p1);

            Savepoint sp = channel.CreateSavepoint();
            int pagesAtSavepoint = channel.PageCount;

            // After the savepoint: change page 2 and allocate a page.
            var p2 = channel.ReadPage(2).Span.ToArray();
            p2[10] = 0x22;
            channel.WritePage(2, p2);
            channel.WritePage(channel.PageCount, new byte[channel.PageSize]);
            Assert.True(channel.PageCount > pagesAtSavepoint);

            channel.RollbackToSavepoint(sp);

            Assert.True(channel.InTransaction);                          // savepoint rollback leaves the txn open
            Assert.Equal(0x11, channel.ReadPage(1).Span[10]);            // pre-savepoint write kept
            Assert.Equal(original2, channel.ReadPage(2).Span[10]);       // post-savepoint write undone
            Assert.Equal(pagesAtSavepoint, channel.PageCount);          // page allocated after the savepoint dropped

            channel.CommitTransaction();
            Assert.Equal(0x11, channel.ReadPage(1).Span[10]);            // and the kept write survives commit
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RollbackToSavepoint_restores_a_page_to_its_savepoint_state_not_transaction_start()
    {
        // A page written both before and after the savepoint must come back to its at-savepoint bytes, which
        // is what the per-frame (not per-transaction) before-image snapshot guarantees.
        string path = Path.Combine(Path.GetTempPath(), $"libred-sp2-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var channel = PageChannel.Open(path, readOnly: false);
            channel.BeginTransaction();

            var page = channel.ReadPage(1).Span.ToArray();
            page[10] = 0x11;
            channel.WritePage(1, page);

            Savepoint sp = channel.CreateSavepoint();

            page[10] = 0x22;
            channel.WritePage(1, page);

            channel.RollbackToSavepoint(sp);

            Assert.Equal(0x11, channel.ReadPage(1).Span[10]); // restored to the savepoint state, not the original
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReleaseSavepoint_merges_into_the_parent_so_an_outer_rollback_still_undoes_it()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-sp3-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var channel = PageChannel.Open(path, readOnly: false);
            byte original = channel.ReadPage(1).Span[10];

            channel.BeginTransaction();
            Savepoint outer = channel.CreateSavepoint();

            var page = channel.ReadPage(1).Span.ToArray();
            page[10] = 0x11;
            channel.WritePage(1, page);

            Savepoint inner = channel.CreateSavepoint();
            page[10] = 0x22;
            channel.WritePage(1, page);
            channel.ReleaseSavepoint(inner);            // inner's change folds into the outer frame

            channel.RollbackToSavepoint(outer);          // undoes both the outer and the released-inner writes

            Assert.Equal(original, channel.ReadPage(1).Span[10]);
        }
        finally { File.Delete(path); }
    }
}
