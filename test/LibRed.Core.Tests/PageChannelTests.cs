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
}
