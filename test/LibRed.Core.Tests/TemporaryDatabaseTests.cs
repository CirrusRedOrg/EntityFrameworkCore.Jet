using LibRed.Tests.Shared;
using Xunit;

namespace LibRed.Core.Tests;

public class TemporaryDatabaseTests
{
    [Fact]
    public void Dispose_closes_the_database_and_removes_the_copy()
    {
        string path;
        using (var temp = TemporaryDatabase.CopyOf(TestDatabases.NorthwindAccdb, "tempdb-lifetime"))
        {
            path = temp.Path;
            temp.Open(readOnly: true);
            Assert.True(File.Exists(path));
            Assert.Throws<InvalidOperationException>(() => temp.Open(readOnly: true));
        }

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Preserve_leaves_the_copy_for_diagnostics()
    {
        string path;
        using (var temp = TemporaryDatabase.CopyOf(TestDatabases.NorthwindAccdb, "tempdb-preserve"))
        {
            path = temp.Preserve();
        }

        try { Assert.True(File.Exists(path)); }
        finally { TemporaryDatabase.Delete(path); }
    }
}
