using Xunit;
using LibRed.Formats;
using LibRed.Pages;

namespace LibRed.Core.Tests;

public class JetDatabaseLifetimeTests
{
    [Fact]
    public void Failed_catalog_initialization_releases_the_file()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-invalid-");

        try
        {
            // Preserve a valid format header so PageChannel.Open succeeds, but make the decoded
            // creation date invalid so JetDatabase's page-0 initialization subsequently fails.
            JetFormatBase format = TestDatabases.FormatOf(path);
            byte[] page0 = new byte[format.PageSize];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.ReadExactly(page0);
                DatabaseDefinitionPage.WriteMasked(page0, format.CreationDateOffset,
                    BitConverter.GetBytes(double.MaxValue), format);

                stream.Position = 0;
                stream.Write(page0);
            }

            JetDatabase? database = null;
            Exception? error = Record.Exception(() => database = JetDatabase.Open(path));
            database?.Dispose();
            Assert.NotNull(error);

            // On Windows this fails if the unsuccessful Open leaked its FileStream.
            File.Delete(path); // deliberately no retry: proves the failed open released its handle immediately
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) TemporaryDatabase.Delete(path);
        }
    }
}