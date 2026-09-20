using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A table definition's chain holds the definition and then its 8-byte trailing reserve, which spills onto a page of
/// its own when it does not fit — so a definition within 8 bytes of a page boundary has a continuation page holding
/// reserve bytes and no definition (docs/format/page-02a-tdef.md §3.2). LibRed reads such a chain from ACE, and lays
/// its own out the same way: the same pages, each with the same free space.
/// </summary>
[Collection(AceCollection.Name)]
public class TdefReserveSpillAccessTests : TempDatabaseTest
{
    // 115 LONG columns make a 4,086-byte definition; each character added to the first column's name adds two.
    // 232 make an 8,181-byte one, whose reserve spills onto a third page.
    [Theory]
    [InlineData(115, 1, 4088, 1)]
    [InlineData(115, 2, 4090, 2)]
    [InlineData(115, 5, 4096, 2)]
    [InlineData(115, 6, 4098, 2)]
    [InlineData(232, 0, 8181, 3)]
    public void A_definition_near_a_page_boundary_is_laid_out_as_ace_lays_it(int columns, int longerName, int length, int pages)
    {
        string ace = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "tdefspill-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(ace))
        using (OleDbCommand create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE L ({string.Join(", ", Names(columns, longerName).Select(n => n + " LONG"))})";
            create.ExecuteNonQuery();
            create.CommandText = "INSERT INTO L (Id) VALUES (7)";
            create.ExecuteNonQuery();
        }

        string libred = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "tdefspill-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
        {
            db.CreateTable("L", Names(columns, longerName).Select(n => new ColumnSpec(n, JetDataType.Int32, 4, IsFixedLength: true)).ToArray());
            db.OpenTable("L").Insert([7, .. Enumerable.Repeat<object?>(null, columns - 1)]);
        }

        (int Length, IReadOnlyList<int> Free) aceChain = Chain(ace), libredChain = Chain(libred);
        Assert.Equal(length, aceChain.Length);
        Assert.Equal(pages, aceChain.Free.Count);
        Assert.Equal(aceChain.Length, libredChain.Length);
        Assert.Equal(aceChain.Free, libredChain.Free);

        // Each engine reads the other's table.
        using (var db = JetDatabase.Open(ace))
        {
            Assert.Equal(columns, db.Catalog.FindTable("L")!.Columns.Count);
            Assert.Equal(7, db.OpenTable("L").Rows().Single()[0]);
        }
        using OleDbConnection check = AceTestDatabase.Open(libred);
        using OleDbCommand read = check.CreateCommand();
        read.CommandText = "SELECT Id FROM L";
        Assert.Equal(7, read.ExecuteScalar());
    }

    private static IEnumerable<string> Names(int columns, int longerName) =>
        new[] { "Id", "c001" + new string('x', longerName) }.Concat(Enumerable.Range(2, columns - 2).Select(i => $"c{i:D3}"));

    /// <summary>Table L's definition length and each page's free space, first page first — read from the raw file,
    /// so LibRed's own chain reader is not the judge of it.</summary>
    private static (int Length, IReadOnlyList<int> Free) Chain(string path)
    {
        int first;
        using (var db = JetDatabase.Open(path)) first = db.Catalog.FindTable("L")!.DefinitionPage;
        byte[] file = File.ReadAllBytes(path);
        var free = new List<int>();
        for (int page = first, n = 0; page != 0 && n < 10; page = BitConverter.ToInt32(file, page * 4096 + 4), n++)
            free.Add(BitConverter.ToUInt16(file, page * 4096 + 2));
        return (BitConverter.ToInt32(file, first * 4096 + 8), free);
    }
}
