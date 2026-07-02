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
}
