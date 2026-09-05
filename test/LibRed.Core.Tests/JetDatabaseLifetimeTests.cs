using Xunit;
using LibRed.Formats;

namespace LibRed.Core.Tests;

public class JetDatabaseLifetimeTests
{
    private static readonly string Northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    [Fact]
    public void Failed_catalog_initialization_releases_the_file()
    {
        string path = TemporaryDatabase.CopyPath(Northwind, "libred-invalid-");

        try
        {
            // Preserve a valid format header so PageChannel.Open succeeds, but make the decoded
            // creation date invalid so JetDatabase's page-0 initialization subsequently fails.
            byte[] page0 = new byte[4096];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.ReadExactly(page0);
                byte[] invalidDate = BitConverter.GetBytes(double.MaxValue);
                for (int i = 0; i < invalidDate.Length; i++)
                {
                    int offset = JetFormatBase.CreationDateOffset + i;
                    page0[offset] = (byte)(invalidDate[i]
                        ^ JetFormatBase.PageZeroHeaderMask[offset - JetFormatBase.PageZeroHeaderMaskStart]);
                }

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
