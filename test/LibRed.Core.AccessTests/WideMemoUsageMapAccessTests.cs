using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using Xunit;

namespace LibRed.Core.Tests;

// CREATE INDEX on a table whose memo columns overflowed the primary usage-map page.
//
// The primary page holds row 0/1 for the table, one row per index, then two rows per long-value column - but
// only for the columns that FIT (~57 inline records to a page); Create spills the rest onto dedicated pages.
// InsertIndex went on computing the new index's row from the TOTAL long-value count, so on a wide memo table
// it named a row past the end and CREATE INDEX failed outright.
//
// ACE does not squeeze it in either - it spills, exactly as it does for the columns, leaving the full page
// alone and putting the new index's map at row 0 of a page of its own. That rule was already in LibRed for
// long-value columns in Create and AddColumn; InsertIndex was the one write path without it.
public class WideMemoUsageMapAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    private const int WideMemoColumns = 40; // more than the ~27 that fit alongside the table and index maps

    [Fact]
    public void A_new_index_spills_onto_its_own_map_page_exactly_as_ace_does()
    {
        string acePath = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "widememo-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(acePath))
        {
            Execute(connection, "CREATE TABLE WideMemo (Id LONG PRIMARY KEY, [Key] LONG, "
                + string.Join(", ", Enumerable.Range(0, WideMemoColumns).Select(i => $"M{i} LONGCHAR")) + ")");
            Execute(connection, "CREATE INDEX IxKey ON WideMemo ([Key])");
        }

        string libredPath = NewWideMemoDatabase();
        using (var database = JetDatabase.Open(libredPath, readOnly: false))
            database.CreateIndex("WideMemo", "IxKey", [("Key", false)]);

        string ace = Layout(acePath);
        string libred = Layout(libredPath);
        output.WriteLine($"ACE    {ace}");
        output.WriteLine($"LibRed {libred}");
        Assert.Equal(ace, libred);
        Assert.Equal("2 indexes; primary map 57 rows; index rows: 2@primary, 0@own", libred);
    }

    // The failure this started from: rows already present, so the new index has to be populated through the
    // map it just allocated. Left unfixed this threw "Usage-map page has 57 rows; expected 83"; the first
    // attempt at a fix (append anyway) instead wrote the record below the slot directory and surfaced much
    // later as a slot whose offset is 0.
    [Fact]
    public void An_index_added_to_a_populated_wide_memo_table_is_readable_by_ace()
    {
        string path = NewWideMemoDatabase();
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var table = database.OpenTable("WideMemo");
            for (int i = 1; i <= 20; i++)
            {
                var values = new object?[2 + WideMemoColumns];
                values[0] = i;
                values[1] = 100 - i;
                table.Insert(values);
            }

            database.CreateIndex("WideMemo", "IxKey", [("Key", false)]);
        }

        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand read = connection.CreateCommand();
        read.CommandText = "SELECT Id FROM WideMemo WHERE [Key] = 93";
        Assert.Equal(7, Convert.ToInt32(read.ExecuteScalar()));
    }

    // A table whose memo columns all fit still puts the new index on the primary page, so the spill did not
    // quietly become the default.
    [Fact]
    public void A_table_whose_memo_columns_fit_keeps_the_new_index_on_the_primary_page()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "narrowmemo-libred-");
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            database.CreateTable("NarrowMemo", Specs(memoColumns: 1), primaryKey: ["Id"]);
            database.CreateIndex("NarrowMemo", "IxKey", [("Key", false)]);
        }

        Assert.Equal("2 indexes; primary map 6 rows; index rows: 2@primary, 5@primary",
            Layout(path, "NarrowMemo"));
    }

    private static string NewWideMemoDatabase()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "widememo-libred-");
        using var database = JetDatabase.Open(path, readOnly: false);
        database.CreateTable("WideMemo", Specs(WideMemoColumns), primaryKey: ["Id"]);
        return path;
    }

    private static List<ColumnSpec> Specs(int memoColumns)
    {
        var specs = new List<ColumnSpec>
        {
            new("Id", JetDataType.Int32, 4, IsFixedLength: true),
            new("Key", JetDataType.Int32, 4, IsFixedLength: true),
        };
        for (int i = 0; i < memoColumns; i++)
            specs.Add(new ColumnSpec($"M{i}", JetDataType.Memo, 0, IsFixedLength: false));
        return specs;
    }

    private static void Execute(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>The table's usage-map layout with absolute page numbers normalised away — the two engines
    /// allocate in a slightly different order, so the numbers differ where the shape must not.</summary>
    private static string Layout(string path, string table = "WideMemo")
    {
        using var channel = PageChannel.Open(path, readOnly: true);
        JetFormatBase format = channel.Format;
        TableDef def = new JetCatalog(channel).FindTable(table)!;
        (PageBuffer buf, _) = TdefChainReader.Read(channel, def.DefinitionPage);

        int dataCount = buf.ReadInt32(format.TdefIndexCountOffset);
        int colCount = buf.ReadUInt16(format.TdefColumnCountOffset);
        int pos = format.TdefRealIndexBlockOffset + dataCount * format.RealIndexEntrySize
                  + colCount * format.ColumnDescriptorSize;
        for (int i = 0; i < colCount; i++) pos += 2 + buf.ReadUInt16(pos);

        int primary = Int24(buf, format.TdefOwnedPagesOffset + 1);
        var primaryPage = new DataPage();
        primaryPage.Read(channel.ReadPage(primary), format);

        var entries = new List<string>();
        for (int i = 0; i < dataCount; i++)
        {
            int block = pos + i * IndexBlockFormat.DataBlockSize;
            int row = buf.ReadByte(block + IndexBlockFormat.UsageMapRowOffset);
            int page = Int24(buf, block + IndexBlockFormat.UsageMapRowOffset + 1);
            entries.Add($"{row}@{(page == primary ? "primary" : "own")}");
        }

        return $"{dataCount} indexes; primary map {primaryPage.Rows.Count} rows; "
            + $"index rows: {string.Join(", ", entries)}";
    }

    private static int Int24(PageBuffer buf, int offset) =>
        buf.ReadByte(offset) | (buf.ReadByte(offset + 1) << 8) | (buf.ReadByte(offset + 2) << 16);
}
