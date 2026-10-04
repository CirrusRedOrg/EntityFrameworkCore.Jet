using System.Data.OleDb;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// A session's freed pages go back to the global free map at close, and the close lengthens the released-pages map
// to cover the highest page released (docs/format/page-05-usage-maps.md §9.1). The same DROP TABLE through ACE and
// through LibRed, each on its own copy, must leave both global maps — wherever page 0 points — byte for byte the same.
[Collection(AceCollection.Name)]
public class ReleaseAtCloseAccessTests : TempDatabaseTest
{
    private static readonly Formats.JetFormatBase Format = TestDatabases.FormatOf(TestDatabases.NorthwindAccdb);

    [Fact]
    public void A_dropped_table_leaves_both_global_maps_as_ace_leaves_them()
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start))
        {
            Exec(connection, "CREATE TABLE Doomed (Id LONG CONSTRAINT pk PRIMARY KEY, M MEMO)");
            for (int i = 1; i <= 40; i++)
            {
                using OleDbCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO Doomed (Id, M) VALUES (?, ?)";
                insert.Parameters.Add("id", OleDbType.Integer).Value = i;
                insert.Parameters.Add("m", OleDbType.LongVarWChar, 20_000).Value = new string((char)('a' + i % 26), 20_000);
                insert.ExecuteNonQuery();
            }
        }
        // Both maps are wherever page 0's pointers put them; neither engine moves them on a drop.
        (int Row, int Page) free, released;
        int releasedLengthBefore;
        using (var db = JetDatabase.Open(start))
        {
            (free, released) = (db.DefinitionPage.FreePagesMap, db.DefinitionPage.ReleasedPagesMap);
            releasedLengthBefore = TestDatabases.GlobalMap(db.Channel, Format.ReleasedPagesMapPointerOffset).Slot.Length;
        }
        byte[] before = TestDatabases.ReadPage(start, free.Page);

        string ace = TemporaryDatabase.CopyPath(start, "release-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(ace))
            Exec(connection, "DROP TABLE Doomed");

        string libred = TemporaryDatabase.CopyPath(start, "release-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            Assert.True(db.DropTable("Doomed"));

        var releasedLengths = new List<int>();
        foreach (string path in new[] { ace, libred })
        {
            using var db = JetDatabase.Open(path);
            Assert.Equal(free, db.DefinitionPage.FreePagesMap);
            Assert.Equal(released, db.DefinitionPage.ReleasedPagesMap);
            releasedLengths.Add(TestDatabases.GlobalMap(db.Channel, Format.ReleasedPagesMapPointerOffset).Slot.Length);
        }
        Assert.True(releasedLengths[0] > releasedLengthBefore, "the drop did not lengthen ACE's released map");
        Assert.Equal(releasedLengths[0], releasedLengths[1]);

        byte[] aceReleased = TestDatabases.ReadPage(ace, released.Page), libredReleased = TestDatabases.ReadPage(libred, released.Page);

        byte[] acePage = TestDatabases.ReadPage(ace, free.Page), libredPage = TestDatabases.ReadPage(libred, free.Page);
        Assert.True(acePage.AsSpan().SequenceEqual(libredPage), FreeMapDifference(start, free, before, acePage, libredPage));
        if (released.Page != free.Page)
            Assert.True(aceReleased.AsSpan().SequenceEqual(libredReleased), "the released map's holder differs");
    }

    /// <summary>The pages whose free bit differs between the two engines, with each page's type, owner and free
    /// bit in the file before the drop.</summary>
    private static string FreeMapDifference(string start, (int Row, int Page) free, byte[] before, byte[] acePage, byte[] libredPage)
    {
        (int beforeOffset, _) = DataPage.ReadSlot(before, Format, free.Row);
        (int offset, _) = DataPage.ReadSlot(acePage, Format, free.Row);
        int first = UsageMap.StartPage(acePage.AsSpan(offset), Format);
        Span<byte> aceBits = UsageMap.InlineBits(acePage.AsSpan(offset), Format);
        Span<byte> libredBits = UsageMap.InlineBits(libredPage.AsSpan(offset), Format);
        Span<byte> beforeBits = UsageMap.InlineBits(before.AsSpan(beforeOffset), Format);
        var lines = new List<string>();
        for (int bit = 0; bit < aceBits.Length * 8; bit++)
            if (BitmapBits.Get(aceBits, bit) != BitmapBits.Get(libredBits, bit))
            {
                int page = first + bit;
                byte[] bytes = TestDatabases.ReadPage(start, page);
                bool freeBefore = BitmapBits.Get(beforeBits, page - first);
                lines.Add($"page {page} (type 0x{bytes[0]:X2} owner {(int)DataPage.ReadOwner(bytes, Format)}, " +
                          $"{(freeBefore ? "free" : "used")} before the drop): " +
                          $"free in {(BitmapBits.Get(aceBits, bit) ? "ACE" : "LibRed")} only");
            }
        return lines.Count == 0 ? "the free map's holder differs outside the free map" : string.Join("; ", lines);
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}