using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

public class PageChannelWriteTests
{
    private static string CopyToTemp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-write-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        return path;
    }

    // A Jet/ACE file is a shared-file database, so more than one channel must be able to hold it open at
    // once (EF's test infrastructure keeps a long-lived store connection AND opens per-context connections
    // to the same .accdb). An exclusive open used to throw IOException the moment the second one appeared.
    [Fact]
    public void Multiple_channels_can_open_the_same_file_at_once()
    {
        string path = CopyToTemp();
        try
        {
            using var writer = PageChannel.Open(path, readOnly: false);
            using var reader = PageChannel.Open(path, readOnly: true);   // used to throw here (FileShare.None)

            // A committed write from one handle is visible to the other (no stale page cache).
            byte[] page = writer.ReadPage(5).Span.ToArray();
            page[10] ^= 0xFF;
            writer.WritePage(5, page);
            Assert.Equal(page, reader.ReadPage(5).Span.ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Rewriting_a_page_unchanged_is_a_no_op_byte_for_byte()
    {
        string path = CopyToTemp();
        try
        {
            byte[] original;
            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                var page = channel.ReadPage(5);
                original = page.Span.ToArray();
                channel.WritePage(5, original); // identity write
            }

            using (var channel = PageChannel.Open(path, readOnly: true))
                Assert.Equal(original, channel.ReadPage(5).Span.ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Written_bytes_survive_a_reopen()
    {
        string path = CopyToTemp();
        try
        {
            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                var buffer = channel.ReadPage(5).Span.ToArray();
                buffer[100] = 0xAB;
                buffer[101] = 0xCD;
                channel.WritePage(5, buffer);
            }

            using (var channel = PageChannel.Open(path, readOnly: true))
            {
                var reread = channel.ReadPage(5).Span;
                Assert.Equal(0xAB, reread[100]);
                Assert.Equal(0xCD, reread[101]);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AllocatePage_grows_the_file_by_one_zeroed_page()
    {
        string path = CopyToTemp();
        try
        {
            int allocated;
            int countBefore;
            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                countBefore = channel.PageCount;
                allocated = channel.AllocatePage();
                Assert.Equal(countBefore, allocated);
                Assert.Equal(countBefore + 1, channel.PageCount);
            }

            using (var channel = PageChannel.Open(path, readOnly: true))
            {
                Assert.Equal(countBefore + 1, channel.PageCount);
                Assert.All(channel.ReadPage(allocated).Span.ToArray(), b => Assert.Equal(0, b));
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_only_channel_refuses_writes()
    {
        string path = CopyToTemp();
        try
        {
            using var channel = PageChannel.Open(path, readOnly: true);
            Assert.Throws<InvalidOperationException>(() => channel.WritePage(5, new byte[channel.PageSize]));
        }
        finally { File.Delete(path); }
    }
}
