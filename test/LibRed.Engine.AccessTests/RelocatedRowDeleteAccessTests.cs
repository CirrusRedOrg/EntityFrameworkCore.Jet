using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Formats;
using LibRed.IO;
using Xunit;

namespace LibRed.Engine.Tests;

// Deleting a RELOCATED row has to reclaim the row it forwards to, not just the pointer.
//
// When an update grows a row past its page's free space it moves: the original slot keeps the row id and
// becomes a 4-byte forward pointer flagged overflow, and the row itself lands on another page flagged
// deleted so scans skip it there. Deleting that row used to set the deleted flag on the pointer and stop,
// stranding the moved row on its own page for ever — here about 523 bytes, against the pointer's four.
//
// ACE reclaims both: the target's page returns to a bare 4080 free, and the pointer's four bytes go back to
// the source page. The reclamation itself is the ordinary one, applied twice.
[Collection(AceCollection.Name)]
public class RelocatedRowDeleteAccessTests : TempDatabaseTest
{
    // Narrow rows fill the page, then one is widened by more than the free space left, which is what
    // forces the move — grow it by less and it simply fits, and nothing relocates.
    private static string[] Statements(bool delete)
    {
        var s = new List<string> { "CREATE TABLE W (A LONG, B TEXT(255), CONSTRAINT pk PRIMARY KEY (A))" };
        for (int i = 1; i <= 18; i++)
            s.Add($"INSERT INTO W (A, B) VALUES ({i}, '{new string((char)('a' + i % 26), 100)}')");
        s.Add($"UPDATE W SET B = '{new string('z', 255)}' WHERE A = 4");
        if (delete) s.Add("DELETE FROM W WHERE A = 4");
        return [.. s];
    }

    [Fact]
    public void The_update_relocates_the_row_exactly_as_ace_does()
    {
        string ace = Describe(Statements(delete: false), AceRun);
        Assert.Equal(ace, Describe(Statements(delete: false), LibRedRun));

        // A forward pointer on the first page (overflow flag, no deleted flag) and a hidden row on a second.
        Assert.Contains("4D7D", ace);
        Assert.Contains("|", ace);
    }

    [Fact]
    public void Deleting_it_reclaims_the_target_page_too()
    {
        string ace = Describe(Statements(delete: true), AceRun);
        Assert.Equal(ace, Describe(Statements(delete: true), LibRedRun));

        // The page the row had moved to comes back empty: 4080 is a data page with nothing on it.
        Assert.Contains("free=4080", ace);
    }

    // Whatever the pages look like, the surviving rows have to still read back.
    [Fact]
    public void The_surviving_rows_are_intact()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "relocdel-read-");
        try
        {
            LibRedRun(path, Statements(delete: true));

            using OleDbConnection connection = AceTestDatabase.Open(path);
            using (OleDbCommand count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM W";
                Assert.Equal(17, Convert.ToInt32(count.ExecuteScalar()));
            }
            foreach (int id in new[] { 1, 3, 5, 18 })
            {
                using OleDbCommand read = connection.CreateCommand();
                read.CommandText = $"SELECT B FROM W WHERE A = {id}";
                Assert.Equal(new string((char)('a' + id % 26), 100), read.ExecuteScalar());
            }
        }
        finally { TemporaryDatabase.Delete(path); }
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

    /// <summary>Every data page the table owns: free space and slot directory, in page order.</summary>
    private static string Describe(string[] statements, Action<string, string[]> run)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "relocdel-");
        try
        {
            run(path, statements);

            int definitionPage;
            using (var database = JetDatabase.Open(path, readOnly: true))
                definitionPage = database.Catalog.FindTable("W")!.DefinitionPage;

            using var channel = PageChannel.Open(path, readOnly: true);
            JetFormatBase format = channel.Format;
            var pages = new List<string>();
            for (int page = 1; page < channel.PageCount; page++)
            {
                byte[] bytes = channel.ReadPage(page).Span.ToArray();
                if (bytes[0] != 0x01) continue;
                if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)) != definitionPage) continue;

                int slots = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(format.DataRowCountOffset, 2));
                var entries = Enumerable.Range(0, slots)
                    .Select(i => BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(format.DataRowDirectoryOffset + i * 2, 2)).ToString("X4"));
                pages.Add($"free={BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(format.DataFreeSpaceOffset, 2))}"
                    + $" [{string.Join(" ", entries)}]");
            }
            return string.Join("  |  ", pages);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
