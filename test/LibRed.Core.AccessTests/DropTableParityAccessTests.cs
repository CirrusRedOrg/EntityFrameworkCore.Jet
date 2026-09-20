using System.Data.OleDb;
using System.Text;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// DROP TABLE through ACE and through LibRed, on copies of the same ACE-built file, must leave the same file: every
/// page the table owned freed by both — its indexes' B-tree pages, a wide definition's continuation pages and a
/// large map's bitmap pages included — and the map holders retired the same way
/// (docs/format/page-05-usage-maps.md §9, page-08-released-tdef.md).
/// </summary>
[Collection(AceCollection.Name)]
public class DropTableParityAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    private const int PageSize = 4096;

    [Theory]
    [InlineData("primary key", "CREATE TABLE Doomed (Id LONG CONSTRAINT pk PRIMARY KEY, M MEMO)", 40, 20_000)]
    // The second index's map record is below the long-value maps, so it slides as they are retired.
    [InlineData("primary key and a secondary index",
        "CREATE TABLE Doomed (Id LONG CONSTRAINT pk PRIMARY KEY, M MEMO);CREATE INDEX ixId ON Doomed (Id DESC)", 40, 20_000)]
    [InlineData("a multi-page index", "CREATE TABLE Doomed (Id LONG CONSTRAINT pk PRIMARY KEY, M TEXT(200));CREATE INDEX ixM ON Doomed (M)", 3000, 200)]
    public void Libred_drops_an_indexed_table_byte_for_byte_with_ace(string label, string create, int rows, int chars)
    {
        AssertDropMatchesAce(label, connection =>
        {
            foreach (string statement in create.Split(';')) Exec(connection, statement);
            for (int i = 1; i <= rows; i++)
            {
                using OleDbCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO Doomed (Id, M) VALUES (?, ?)";
                insert.Parameters.Add("id", OleDbType.Integer).Value = i;
                insert.Parameters.Add("m", OleDbType.VarWChar, chars).Value =
                    i.ToString("D6") + new string((char)('a' + i % 26), chars - 6);
                insert.ExecuteNonQuery();
            }
        });
    }

    // 255 columns: the definition runs onto continuation pages. At 32,768 rows of one full page each, the table's
    // owned map outgrows an inline record and becomes a reference map with two bitmap pages.
    [Theory]
    [InlineData("a multi-page definition", 1)]
    [InlineData("a multi-page definition and a reference-form owned map", 32_768)]
    public void Libred_drops_a_wide_table_byte_for_byte_with_ace(string label, int rows)
    {
        AssertDropMatchesAce(label, connection => FillWide(connection, "Doomed", rows));
    }

    // How the close sizes the released-pages map when an inline record cannot simply grow from its start page,
    // each drop in its own session. W ends between pages 32,000 and 32,735, a filler F follows, and X lies wholly
    // past page 32,736 — the second bitmap range.
    //  - W then X: W's pages, 310..~32,300, are too wide for an inline record, so the first close converts the map
    //    with a bitmap page for the first range only; X's close then adds one for the second.
    //  - X alone: X's pages fit an inline record once its window moves to start at X, so the map moves rather than
    //    converting.
    [Fact]
    public void Libred_sizes_the_released_map_as_ace_does_across_sessions()
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "droppar-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start))
        {
            FillWide(connection, "W", 31_900);
            FillWide(connection, "F", 500);
            FillWide(connection, "X", 1_000);
        }

        AssertDropsMatchAce("W then X", start, "W", "X");
        AssertDropsMatchAce("X alone", start, "X");
    }

    // Released pages in the first and third bitmap ranges and none in the second: A sits in range 0; C's definition,
    // map holder and first row are made next, in range 0 too; B then fills the file past page 65,472; C's other
    // rows land in range 2. Dropping A and C in one session converts the released map with bitmap pages for ranges
    // 0 and 2 only, and grows the inline record first only as far as the highest released page it reaches, not to
    // its longest length. Explicit: the file is ~272 MB, built once and copied twice, and takes minutes.
    [Fact(Explicit = true)]
    public void Libred_converts_the_released_map_without_a_bitmap_page_for_an_empty_range()
    {
        const int Range2 = 2 * 32_736;
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "droppar-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start)) FillWide(connection, "A", 700);
        using (OleDbConnection connection = AceTestDatabase.Open(start)) FillWide(connection, "C", 1);
        using (OleDbConnection connection = AceTestDatabase.Open(start)) FillWide(connection, "B", 1);
        for (int rows = 1; new FileInfo(start).Length / PageSize < Range2 + 40;)
        {
            int add = (int)Math.Clamp(Range2 + 40 - new FileInfo(start).Length / PageSize, 64, 32_000);
            using (OleDbConnection connection = AceTestDatabase.Open(start)) AppendWide(connection, "B", rows, add);
            rows += add;
        }
        using (OleDbConnection connection = AceTestDatabase.Open(start)) AppendWide(connection, "C", 1, 999);

        string ace = TemporaryDatabase.CopyPath(start, "droppar-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(ace))
        {
            Exec(connection, "DROP TABLE A");
            Exec(connection, "DROP TABLE C");
        }

        string libred = TemporaryDatabase.CopyPath(start, "droppar-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
        {
            Assert.True(db.DropTable("A"));
            Assert.True(db.DropTable("C"));
        }

        output.WriteLine($"comparing {new FileInfo(ace).Length / PageSize} pages");
        string difference = Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);
    }

    /// <summary>A 255-column table — its definition runs onto continuation pages — of <paramref name="rows"/>
    /// rows of one full page each, filled by repeatedly copying the rows already there.</summary>
    private static void FillWide(OleDbConnection connection, string table, int rows)
    {
        var columns = new StringBuilder($"Id LONG CONSTRAINT pk{table} PRIMARY KEY");
        var values = new StringBuilder("0");
        for (int i = 1; i < 255; i++)
        {
            columns.Append($", c{i} CURRENCY");
            values.Append($", {i}");
        }
        Exec(connection, $"CREATE TABLE {table} ({columns})");
        Exec(connection, $"INSERT INTO {table} ({WideNames()}) VALUES ({values})");
        AppendWide(connection, table, 1, rows - 1);
    }

    /// <summary>Adds <paramref name="add"/> rows to a <see cref="FillWide"/> table holding <paramref name="have"/>,
    /// copying the first rows with their ids moved past the last.</summary>
    private static void AppendWide(OleDbConnection connection, string table, int have, int add)
    {
        string names = WideNames();
        string rest = names["Id".Length..];
        while (add > 0)
        {
            int step = Math.Min(Math.Min(have, add), 16_000);
            Exec(connection, $"INSERT INTO {table} ({names}) SELECT TOP {step} Id + {have}{rest} FROM {table} ORDER BY Id");
            have += step;
            add -= step;
        }
    }

    private static string WideNames()
    {
        var names = new StringBuilder("Id");
        for (int i = 1; i < 255; i++) names.Append($", c{i}");
        return names.ToString();
    }

    private void AssertDropMatchesAce(string label, Action<OleDbConnection> build)
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "droppar-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start))
            build(connection);
        AssertDropsMatchAce(label, start, "Doomed");
    }

    /// <summary>Drops <paramref name="tables"/> from copies of <paramref name="start"/> through each engine, one
    /// session per drop, and compares the results.</summary>
    private void AssertDropsMatchAce(string label, string start, params string[] tables)
    {
        string ace = TemporaryDatabase.CopyPath(start, "droppar-ace-");
        foreach (string table in tables)
            using (OleDbConnection connection = AceTestDatabase.Open(ace))
                Exec(connection, $"DROP TABLE {table}");

        string libred = TemporaryDatabase.CopyPath(start, "droppar-lib-");
        foreach (string table in tables)
            using (var db = JetDatabase.Open(libred, readOnly: false))
                Assert.True(db.DropTable(table));

        output.WriteLine($"{label}: comparing {new FileInfo(ace).Length / PageSize} pages");
        string difference = Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);
    }

    /// <summary>Every differing byte of every page, except page 0 (the modification counter), MSysObjects' data
    /// page (owner 2, the DateUpdate wall clock) and index pages — removing the catalog rows leaves the two
    /// engines with identical index content on byte-different pages, accepted in page-03-04 §10.4a. The dropped
    /// table's own index pages are compared through the free map instead.</summary>
    internal static string Difference(string acePath, string libredPath)
    {
        byte[] ace = File.ReadAllBytes(acePath), libred = File.ReadAllBytes(libredPath);
        var differences = new StringBuilder();
        int pages = Math.Max(ace.Length, libred.Length) / PageSize, lines = 0;
        for (int page = 1; page < pages && lines < 200; page++)
        {
            int at = page * PageSize;
            bool inAce = at + PageSize <= ace.Length, inLibRed = at + PageSize <= libred.Length;
            if (!inAce || !inLibRed)
            {
                differences.AppendLine($"page {page}: present in {(inAce ? "ACE" : "LibRed")} only");
                lines++;
                continue;
            }
            if (BitConverter.ToInt32(ace, at + 4) == 2) continue;
            if (ace[at] is (byte)LibRed.Pages.PageType.IntermediateIndexPage or (byte)LibRed.Pages.PageType.LeafIndexPage) continue;

            for (int i = 0, shown = 0; i < PageSize && shown < 8; i++)
                if (ace[at + i] != libred[at + i])
                {
                    differences.AppendLine(
                        $"page {page} (type 0x{ace[at]:X2} owner {BitConverter.ToInt32(ace, at + 4)}) " +
                        $"+0x{i:X3}: ace={ace[at + i]:X2} libred={libred[at + i]:X2}");
                    shown++;
                    lines++;
                }
        }
        return differences.ToString();
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 0;
        command.ExecuteNonQuery();
    }
}
