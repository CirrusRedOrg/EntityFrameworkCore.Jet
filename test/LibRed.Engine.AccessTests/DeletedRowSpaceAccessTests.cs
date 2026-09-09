using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Formats;
using LibRed.IO;
using Xunit;

namespace LibRed.Engine.Tests;

// Deleting a row gives its bytes back to the page, the way ACE does.
//
// LibRed used to only set the deleted flag and leave the row where it was, so the space was never reclaimed
// — about 21 bytes per delete, for ever. Ten delete/insert cycles on one page left LibRed 208 bytes worse
// off than ACE, which is a table spilling onto new pages far sooner than Access's would.
//
// ACE closes the gap: the rows stored below slide up, their slots follow, and the emptied slot becomes a
// zero-length tombstone whose offset is the row's FORMER END, flagged deleted + overflow (0xC000). Pointing
// it at the former end rather than the page end is what keeps the directory non-increasing, which DataPage
// relies on to derive each row's length from the previous slot.
[Collection(AceCollection.Name)]
public class DeletedRowSpaceAccessTests : TempDatabaseTest
{
    private static readonly string[] Seed =
    [
        "CREATE TABLE W (A LONG, B TEXT(20), CONSTRAINT pk PRIMARY KEY (A))",
        "INSERT INTO W (A, B) VALUES (1, 'one')",
        "INSERT INTO W (A, B) VALUES (2, 'two')",
        "INSERT INTO W (A, B) VALUES (3, 'six')",
    ];

    [Theory]
    [InlineData(1, "free=4038 dir=[D000 0FED 0FDA]")]   // first: tombstone at the page end
    [InlineData(2, "free=4038 dir=[0FED CFED 0FDA]")]   // middle: at the row above's start
    [InlineData(3, "free=4038 dir=[0FED 0FDA CFDA]")]   // last: nothing below to move
    public void A_deleted_row_returns_its_bytes(int id, string expected)
    {
        string[] statements = [.. Seed, $"DELETE FROM W WHERE A = {id}"];

        Assert.Equal(expected, Directory(statements, AceRun));
        Assert.Equal(expected, Directory(statements, LibRedRun));
    }

    // The point of it: the row bytes come back instead of accumulating. Ten delete/insert cycles used to
    // leave LibRed about 210 bytes behind ACE; now the two pages agree exactly.
    //
    // Free space still falls slowly across the churn, and correctly so — the SLOT directory grows by two
    // bytes per row ever inserted and neither engine reuses a tombstoned slot, so thirteen rows means
    // thirteen slots. That is why the assertion is equality with ACE rather than a floor: only the row
    // bytes are reclaimable, and ACE is the authority on how much that leaves.
    [Fact]
    public void Repeated_churn_matches_ace_byte_for_byte()
    {
        var statements = new List<string>(Seed);
        for (int i = 4; i < 14; i++)
        {
            statements.Add($"DELETE FROM W WHERE A = {i - 3}");
            statements.Add($"INSERT INTO W (A, B) VALUES ({i}, 'row{i}')");
        }

        string ace = Directory([.. statements], AceRun);
        Assert.Equal(ace, Directory([.. statements], LibRedRun));

        // Without reclamation the same churn cost about 21 bytes a cycle, which this could not reach.
        int free = int.Parse(ace.AsSpan("free=".Length, ace.IndexOf(' ') - "free=".Length));
        Assert.True(free > 3900, $"expected the row bytes to come back, got {ace}");
    }

    private static void AceRun(string path, string[] statements)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        foreach (string s in statements)
        {
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = s;
            command.ExecuteNonQuery();
        }
    }

    private static void LibRedRun(string path, string[] statements)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(database);
        foreach (string s in statements) engine.ExecuteNonQuery(s);
    }

    /// <summary>The table's data page: its free-space count and slot directory.</summary>
    private static string Directory(string[] statements, Action<string, string[]> run)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "deleted-space-");
        try
        {
            run(path, statements);

            int definitionPage;
            using (var database = JetDatabase.Open(path, readOnly: true))
                definitionPage = database.Catalog.FindTable("W")!.DefinitionPage;

            using var channel = PageChannel.Open(path, readOnly: true);
            JetFormatBase format = channel.Format;
            for (int page = 1; page < channel.PageCount; page++)
            {
                byte[] bytes = channel.ReadPage(page).Span.ToArray();
                if (bytes[0] != 0x01) continue;
                if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)) != definitionPage) continue;

                int slots = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(format.DataRowCountOffset, 2));
                var entries = Enumerable.Range(0, slots)
                    .Select(i => BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(format.DataRowDirectoryOffset + i * 2, 2)).ToString("X4"));
                return $"free={BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(format.DataFreeSpaceOffset, 2))} "
                    + $"dir=[{string.Join(" ", entries)}]";
            }
            return "no data page";
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
