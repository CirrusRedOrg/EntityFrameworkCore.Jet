using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// A session's freed pages go back to the global free map at close, and the close lengthens the released-pages map
// to cover the highest page released (docs/format/page-05-usage-maps.md §9.1). The same DROP TABLE through ACE and
// through LibRed, each on its own copy, must leave page 1 — both global maps — byte for byte the same.
[Collection(AceCollection.Name)]
public class ReleaseAtCloseAccessTests : TempDatabaseTest
{
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
        byte[] before = ReadPage(start, 1);

        string ace = TemporaryDatabase.CopyPath(start, "release-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(ace))
            Exec(connection, "DROP TABLE Doomed");

        string libred = TemporaryDatabase.CopyPath(start, "release-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            Assert.True(db.DropTable("Doomed"));

        byte[] acePage = ReadPage(ace, 1), libredPage = ReadPage(libred, 1);
        Assert.True(ReleasedLength(acePage) > ReleasedLength(before), "the drop did not lengthen ACE's released map");
        Assert.Equal(ReleasedLength(acePage), ReleasedLength(libredPage));
        Assert.True(acePage.AsSpan().SequenceEqual(libredPage), FreeMapDifference(start, before, acePage, libredPage));
    }

    /// <summary>The pages whose free bit differs between the two engines, with each page's type, owner and free
    /// bit in the file before the drop.</summary>
    private static string FreeMapDifference(string start, byte[] before, byte[] acePage, byte[] libredPage)
    {
        int beforeOffset = BinaryPrimitives.ReadUInt16LittleEndian(before.AsSpan(14)) & 0x1FFF;
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(acePage.AsSpan(14)) & 0x1FFF;
        int first = BinaryPrimitives.ReadInt32LittleEndian(acePage.AsSpan(offset + 1));
        var lines = new List<string>();
        for (int i = offset + 5; i < acePage.Length; i++)
            for (int bit = 0; bit < 8; bit++)
                if (((acePage[i] ^ libredPage[i]) & (1 << bit)) != 0)
                {
                    int page = first + (i - offset - 5) * 8 + bit;
                    byte[] bytes = ReadPage(start, page);
                    int beforeByte = beforeOffset + 5 + (page - first) / 8;
                    bool freeBefore = (before[beforeByte] & (1 << ((page - first) % 8))) != 0;
                    lines.Add($"page {page} (type 0x{bytes[0]:X2} owner {BitConverter.ToInt32(bytes, 4)}, " +
                              $"{(freeBefore ? "free" : "used")} before the drop): " +
                              $"free in {((acePage[i] & (1 << bit)) != 0 ? "ACE" : "LibRed")} only");
                }
        return lines.Count == 0 ? "page 1 differs outside the free map" : string.Join("; ", lines);
    }

    private static int ReleasedLength(byte[] page1) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(page1.AsSpan(14)) & 0x1FFF)
        - (BinaryPrimitives.ReadUInt16LittleEndian(page1.AsSpan(16)) & 0x1FFF);

    private static byte[] ReadPage(string path, int page)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[4096];
        s.Position = page * 4096L;
        s.ReadExactly(bytes);
        return bytes;
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
