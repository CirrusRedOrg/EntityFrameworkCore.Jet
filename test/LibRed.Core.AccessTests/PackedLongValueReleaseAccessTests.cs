using System.Data.OleDb;
using System.Text;
using LibRed;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Several small long values (the single-page form, up to 3,816 bytes) pack onto one LVAL page. Deleting a
/// row retires its value's row there to a 0-length deleted+overflow tombstone and re-lays the page, and once
/// the last value on it is gone the page is stamped <see cref="PageType.ReleasedLongValuePage"/> and freed.
/// That is where the <c>0x09</c> pages in real Access files come from — see
/// <c>docs/format/page-05-usage-maps.md</c> §9.
/// </summary>
/// <remarks>
/// A <b>chained</b> value owns its pages outright and gives them back at <c>0x01</c>, which is why no
/// experiment using a memo large enough to chain ever produced a <c>0x09</c>, and why the type went
/// unexplained for so long. Both bands are covered below.
/// </remarks>
[Collection(AceCollection.Name)]
public class PackedLongValueReleaseAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    private const int PageSize = 4096, Packed = 400, Chained = 20_000;

    [Theory]
    [InlineData(Packed, 12, 4)]     // some rows: the shared pages survive, compacted
    [InlineData(Packed, 12, 12)]    // every row: the pages empty out and are released
    [InlineData(Packed, 40, 40)]    // several pages' worth
    [InlineData(Chained, 6, 6)]     // chained values: pages go back at 0x01, never 0x09
    public void Libred_releases_packed_long_values_byte_for_byte_with_ace(int chars, int rows, int remove)
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "lval-start-");
        string ace = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "lval-ace-");
        string libred = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "lval-lib-");
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(start))
            {
                Exec(connection, "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, M MEMO)");
                for (int i = 1; i <= rows; i++)
                {
                    using OleDbCommand insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO T (Id, M) VALUES (?, ?)";
                    insert.Parameters.Add("id", OleDbType.Integer).Value = i;
                    insert.Parameters.Add("m", OleDbType.LongVarWChar, chars).Value = new string((char)('a' + i % 26), chars);
                    insert.ExecuteNonQuery();
                }
            }

            File.Copy(start, ace, overwrite: true);
            using (OleDbConnection connection = AceTestDatabase.Open(ace))
                Exec(connection, $"DELETE FROM T WHERE Id <= {remove}");

            File.Copy(start, libred, overwrite: true);
            using (var database = JetDatabase.Open(libred, readOnly: false))
            {
                Table table = database.OpenTable("T");
                int id = table.Definition.FindColumn("Id")!.Index;
                foreach ((RowId rowId, object?[] values) in table.Rows().WithIds().ToList())
                    if (Convert.ToInt32(values[id]) <= remove)
                        table.Delete(rowId);
            }

            output.WriteLine($"ACE    {Census(ace)}");
            output.WriteLine($"LibRed {Census(libred)}");
            Assert.Equal(Census(ace), Census(libred));
            Assert.Equal("", Difference(ace, libred));
        }
        finally
        {
            TemporaryDatabase.Delete(start);
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>How many long-value pages are live and how many were released, so a shape difference is
    /// reported as such rather than as a wall of bytes.</summary>
    private static string Census(string path)
    {
        using var channel = PageChannel.Open(path);
        var buffer = new byte[channel.PageSize];
        int live = 0, released = 0;
        for (int page = 0; page < channel.PageCount; page++)
        {
            channel.ReadPage(page, buffer);
            if (BitConverter.ToUInt32(buffer, channel.Format.DataOwnerOffset) != 0x4C41564C) continue;
            if (buffer[0] == (byte)PageType.DataPage) live++;
            else if (buffer[0] == (byte)PageType.ReleasedLongValuePage) released++;
        }
        return $"lval live={live} released={released}";
    }

    /// <summary>Every differing byte of every page, skipping page 0 (the modification counter), MSysObjects'
    /// data page (the table's DateUpdate wall clock) and <b>index pages</b> — removing entries leaves the two
    /// engines with identical index content and byte-different pages, which is recorded and accepted in
    /// page-03-04 §10.4a.</summary>
    private static string Difference(string acePath, string libredPath)
    {
        byte[] ace = File.ReadAllBytes(acePath), libred = File.ReadAllBytes(libredPath);
        var differences = new StringBuilder();
        int pages = Math.Max(ace.Length, libred.Length) / PageSize;
        for (int page = 1; page < pages; page++)
        {
            int at = page * PageSize;
            bool inAce = at + PageSize <= ace.Length, inLibRed = at + PageSize <= libred.Length;
            if (!inAce || !inLibRed)
            {
                differences.AppendLine($"page {page}: present in {(inAce ? "ACE" : "LibRed")} only");
                continue;
            }
            if (BitConverter.ToInt32(ace, at + 4) == 2) continue;
            if (ace[at] is (byte)PageType.IntermediateIndexPage or (byte)PageType.LeafIndexPage) continue;

            for (int i = 0, shown = 0; i < PageSize && shown < 8; i++)
                if (ace[at + i] != libred[at + i])
                {
                    differences.AppendLine(
                        $"page {page} (type 0x{ace[at]:X2} owner {BitConverter.ToInt32(ace, at + 4)}) " +
                        $"+0x{i:X3}: ace={ace[at + i]:X2} libred={libred[at + i]:X2}");
                    shown++;
                }
        }
        return differences.ToString();
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
