using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

public class IndexColumnLimitTests
{
    // A Jet/ACE index-data block has exactly 10 column slots (§3.5), so an index — and any key built
    // on one — cannot exceed 10 columns. Creating one must fail loudly, not silently truncate.
    [Fact]
    public void Primary_key_over_more_than_ten_columns_throws()
    {
        string path = Path.Combine(Path.GetTempPath(), $"idxlimit-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            var columns = Enumerable.Range(0, 11)
                .Select(i => new ColumnSpec($"C{i}", JetDataType.Int32, 4, IsFixedLength: true))
                .ToList();
            var keyColumns = columns.Select(c => c.Name).ToList();

            using var db = JetDatabase.Open(path, readOnly: false);
            var ex = Assert.Throws<NotSupportedException>(() => db.CreateTable("Wide", columns, primaryKey: keyColumns));
            Assert.Contains("10", ex.Message);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // Jet/ACE caps a table at 32 indexes (keys + relationships included). A primary key plus 32 unique
    // constraints is 33 indexes and must be rejected.
    [Fact]
    public void More_than_thirty_two_indexes_throws()
    {
        string path = Path.Combine(Path.GetTempPath(), $"idxcount-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            var columns = Enumerable.Range(0, 33)
                .Select(i => new ColumnSpec($"C{i}", JetDataType.Int32, 4, IsFixedLength: true))
                .ToList();
            var uniques = Enumerable.Range(1, 32) // C1..C32; C0 is the primary key
                .Select(i => new UniqueIndexSpec($"UQ_{i}", [$"C{i}"]))
                .ToList();

            using var db = JetDatabase.Open(path, readOnly: false);
            var ex = Assert.Throws<NotSupportedException>(() =>
                db.CreateTable("Many", columns, primaryKey: ["C0"], relationships: null, uniqueConstraints: uniques));
            Assert.Contains("32", ex.Message);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
