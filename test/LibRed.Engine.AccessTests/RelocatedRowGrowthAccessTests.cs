using System.Data.OleDb;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using Xunit;

namespace LibRed.Engine.Tests;

// A row that has already been relocated and then grows AGAIN, past the page it was moved to. The first move
// leaves a 4-byte forward pointer in the original slot and the row itself hidden on another page; the second
// has nothing to rewrite in place, so the row has to move a second time and the pointer follow it. ACE is the
// oracle for what that leaves on disk, and for whether it happens at all rather than the row being packed
// differently.
[Collection(AceCollection.Name)]
public class RelocatedRowGrowthAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    // Wide rows fill the first page; row 4 is then grown in steps. The first step moves it, and the later ones
    // have to keep working on a page that other rows have since filled up.
    private static string[] Statements()
    {
        var s = new List<string>
        {
            "CREATE TABLE W (A LONG, B TEXT(255), C TEXT(255), CONSTRAINT pk PRIMARY KEY (A))",
        };
        // Narrow rows fill the first page, exactly as the relocation test does.
        for (int i = 1; i <= 18; i++)
            s.Add($"INSERT INTO W (A, B) VALUES ({i}, '{Text('a', i, 100)}')");

        // Widening one by more than the page's free space moves it to a page of its own.
        s.Add($"UPDATE W SET B = '{Text('z', 4, 255)}' WHERE A = 4");

        // Fill that page up behind it, so the row has nowhere left to grow where it now lies.
        for (int i = 19; i <= 30; i++)
            s.Add($"INSERT INTO W (A, B, C) VALUES ({i}, '{Text('d', i, 255)}', '{Text('e', i, 255)}')");

        // And grow it again: the second column is 510 more bytes with nothing to rewrite in place.
        s.Add($"UPDATE W SET C = '{Text('y', 4, 255)}' WHERE A = 4");
        return [.. s];
    }

    private static string Text(char seed, int row, int length) => new((char)(seed + row % 5), length);

    [Fact]
    public void Growing_an_already_relocated_row_past_its_new_page_matches_ace()
    {
        string ace = Describe(AceRun);
        string libred = Describe(LibRedRun);
        output.WriteLine($"ACE:    {ace}");
        output.WriteLine($"LibRed: {libred}");
        Assert.Equal(ace, libred);
    }

    // And the row still reads back through ACE, whichever page it ended up on.
    [Fact]
    public void Ace_reads_the_twice_moved_row_back()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "relocgrow-read-");
        try
        {
            LibRedRun(path, Statements());

            using OleDbConnection connection = AceTestDatabase.Open(path);
            using (OleDbCommand count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM W";
                Assert.Equal(30, Convert.ToInt32(count.ExecuteScalar()));
            }
            using OleDbCommand read = connection.CreateCommand();
            read.CommandText = "SELECT B, C FROM W WHERE A = 4";
            using OleDbDataReader reader = read.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(Text('z', 4, 255), reader.GetString(0));
            Assert.Equal(Text('y', 4, 255), reader.GetString(1));
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
    private static string Describe(Action<string, string[]> run)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "relocgrow-");
        try
        {
            run(path, Statements());

            int definitionPage;
            using (var database = JetDatabase.Open(path, readOnly: true))
                definitionPage = database.Catalog.FindTable("W")!.DefinitionPage;

            using var channel = PageChannel.Open(path, readOnly: true);
            JetFormatBase format = channel.Format;
            var pages = new List<string>();
            for (int page = 1; page < channel.PageCount; page++)
            {
                byte[] bytes = channel.ReadPage(page).Span.ToArray();
                if (PageHeader.ReadType(bytes) != PageType.DataPage) continue;
                if ((int)DataPage.ReadOwner(bytes, format) != definitionPage) continue;

                int slots = DataPage.ReadRowCount(bytes, format);
                var entries = Enumerable.Range(0, slots)
                    .Select(i => DataPage.ReadSlot(bytes, format, i))
                    .Select(s => ((int)s.Flags | s.Offset).ToString("X4"));
                pages.Add($"free={DataPage.ReadFreeSpace(bytes, format)}"
                    + $" [{string.Join(" ", entries)}]");
            }
            return string.Join("  |  ", pages);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}