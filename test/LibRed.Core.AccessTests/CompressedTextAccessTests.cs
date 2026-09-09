using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// LibRed writes WITH COMPRESSION the way ACE does, so each case builds the same table and value through
// both engines and compares what landed on disk.
//
// The rules, all measured (see the sibling probes): every storage decision - inline, single page, chained -
// is made on the UNCOMPRESSED UTF-16 length; compression is then applied to whatever form resulted, never
// to a chained value; and it applies only when the column is declared WITH COMPRESSION, every character
// fits one byte, and it actually saves space (the 2-byte FF FE marker means 1- and 2-character values stay
// UTF-16, and compression starts at 3).
public class CompressedTextAccessTests : TempDatabaseTest
{
    [Theory]
    [InlineData("a")]           // marker costs more than it saves
    [InlineData("ab")]          // break-even, so ACE leaves it
    [InlineData("abc")]         // first length that saves
    [InlineData("hello world")]
    [InlineData("café")]        // Latin1 above ASCII
    [InlineData("一")]           // not Latin1
    [InlineData("mixed 一 text")]
    [InlineData("")]
    public void LibRed_inline_text_matches_ace(string value)
    {
        const string ddl = "CREATE TABLE TextProbe (Id LONG PRIMARY KEY, C TEXT(255) WITH COMP)";
        byte[] ace = AceBytes(ddl, "INSERT INTO TextProbe (Id, C) VALUES (1, ?)", value, OleDbType.VarWChar, "C");
        byte[] libred = LibRedBytes(ddl, value, "C");

        Assert.Equal(Convert.ToHexString(ace), Convert.ToHexString(libred));
    }

    [Theory]
    [InlineData(20)]    // inline, compressed
    [InlineData(32)]    // last inline (64 uncompressed bytes)
    [InlineData(33)]    // first single page
    [InlineData(1908)]  // last single page, still compressed
    [InlineData(1909)]  // chained, so never compressed
    [InlineData(5000)]
    public void LibRed_memo_storage_form_and_compression_match_ace(int characters)
    {
        string value = new('a', characters);
        const string ddl = "CREATE TABLE MemoProbe (Id LONG PRIMARY KEY, C LONGCHAR WITH COMP)";
        byte[] ace = AceBytes(ddl, "INSERT INTO MemoProbe (Id, C) VALUES (1, ?)", value, OleDbType.LongVarWChar, "C");
        byte[] libred = LibRedBytes(ddl, value, "C");

        // The descriptor's length and storage flag must agree; the page numbers inside it need not, since the
        // two engines allocate independently.
        Assert.Equal(Describe(ace), Describe(libred));
    }

    // "within a given table, for a given MEMO column, some data may be compressed and some data may not be
    // compressed" (Microsoft). LibRed decides per value, so one column holds all three forms at once - and
    // the reader has to cope, since a value's form is knowable only from its own marker and descriptor flag.
    [Fact]
    public void One_memo_column_holds_compressed_and_uncompressed_values_together()
    {
        string singlePage = new('a', 1908);  // compressed on one page
        string chainedValue = new('b', 1909); // one character more, so chained and left UTF-16
        string[] values = ["hello world", singlePage, chainedValue, "一二三"];

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "comp-mixed-");
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            database.CreateTable("MemoProbe",
                [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("C", JetDataType.Memo, 0, IsFixedLength: false, SupportsCompressedUnicode: true),
                ],
                primaryKey: ["Id"]);
            Table table = database.OpenTable("MemoProbe");
            for (int i = 0; i < values.Length; i++) table.Insert([i + 1, values[i]]);
        }

        // All three storage forms, in the one column.
        Assert.Equal(
            ["0x80 compressed", "0x40 compressed", "0x00 plain", "0x80 plain"],
            Descriptors(path).Select(d =>
                $"0x{(byte)(d[3] & 0xC0):X2} {(d[12] == 0xFF && d[13] == 0xFE ? "compressed" : "plain")}"));

        // LibRed reads every form back...
        using (var channel = PageChannel.Open(path, readOnly: true))
        {
            TableDef definition = new JetCatalog(channel).FindTable("MemoProbe")!;
            int column = definition.Columns.Single(c => c.Name == "C").Index;
            Assert.Equal(values, new Table(channel, definition).Rows().Select(r => (string)r[column]!));
        }

        // ...and so does ACE.
        using var connection = AceTestDatabase.Open(path);
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT C FROM MemoProbe ORDER BY Id";
        using OleDbDataReader rows = read.ExecuteReader();
        foreach (string expected in values)
        {
            Assert.True(rows.Read());
            Assert.Equal(expected, rows.GetString(0));
        }
    }

    /// <summary>Each row's long-value descriptor followed by the first bytes of its payload, so both the
    /// storage flag and the compression marker are visible.</summary>
    private static IEnumerable<byte[]> Descriptors(string path)
    {
        using var channel = PageChannel.Open(path, readOnly: true);
        TableDef definition = new JetCatalog(channel).FindTable("MemoProbe")!;
        ColumnDef column = definition.Columns.Single(c => c.Name == "C");
        var decoder = new RowDecoder(definition.Columns, channel.Format);
        var reader = new LongValueReader(channel);
        var result = new List<byte[]>();

        foreach (int number in new UsageMap(channel, definition).DataPages())
        {
            var page = new DataPage();
            page.Read(channel.ReadPage(number), channel.Format);
            for (int row = 0; row < page.RowCount; row++)
            {
                if (page.Rows[row].IsDeleted) continue;
                foreach (var raw in decoder.LongValueRaw(page.GetRow(row)))
                {
                    if (raw.Key != column.ColumnId) continue;
                    byte[] payload = reader.Resolve(raw.Value);
                    result.Add([.. raw.Value[..12], .. payload.Length >= 2 ? payload[..2] : new byte[2]]);
                }
            }
        }
        return result;
    }

    private static string Describe(byte[] descriptor)
    {
        uint word = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(descriptor);
        return $"length={word & Formats.LongValueFormat.LengthMask} flags=0x{(byte)(descriptor[3] & 0xC0):X2}";
    }

    private static byte[] AceBytes(string ddl, string insert, string value, OleDbType type, string column)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "comp-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using (OleDbCommand create = connection.CreateCommand())
            {
                create.CommandText = ddl;
                create.ExecuteNonQuery();
            }
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = insert;
            command.Parameters.Add("v", type, Math.Max(value.Length, 1)).Value = value;
            command.ExecuteNonQuery();
        }
        return StoredBytes(path, column);
    }

    private static byte[] LibRedBytes(string ddl, string value, string column)
    {
        bool memo = ddl.Contains("MemoProbe");
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "comp-libred-");
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            database.CreateTable(
                memo ? "MemoProbe" : "TextProbe",
                [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    memo
                        ? new ColumnSpec("C", JetDataType.Memo, 0, IsFixedLength: false, SupportsCompressedUnicode: true)
                        : new ColumnSpec("C", JetDataType.Text, 510, IsFixedLength: false, SupportsCompressedUnicode: true),
                ],
                primaryKey: ["Id"]);
            database.OpenTable(memo ? "MemoProbe" : "TextProbe").Insert([1, value]);
        }
        return StoredBytes(path, column);
    }

    /// <summary>The raw on-disk bytes for one column of the single row — the variable chunk for a Text
    /// column, the 12-byte long-value descriptor for a Memo one.</summary>
    private static byte[] StoredBytes(string path, string columnName)
    {
        using var channel = PageChannel.Open(path, readOnly: true);
        TableDef definition = new JetCatalog(channel).FindTable("TextProbe")
            ?? new JetCatalog(channel).FindTable("MemoProbe")!;
        ColumnDef column = definition.Columns.Single(c => c.Name == columnName);
        var decoder = new RowDecoder(definition.Columns, channel.Format);

        foreach (int number in new UsageMap(channel, definition).DataPages())
        {
            var page = new DataPage();
            page.Read(channel.ReadPage(number), channel.Format);
            for (int row = 0; row < page.RowCount; row++)
            {
                if (page.Rows[row].IsDeleted) continue;
                byte[] bytes = page.GetRow(row).ToArray();
                if (column.Type == JetDataType.Memo)
                {
                    foreach (var raw in decoder.LongValueRaw(bytes))
                        if (raw.Key == column.ColumnId)
                            return raw.Value[..12];
                    throw new InvalidOperationException("No long-value descriptor for the memo column.");
                }
                return RowLayout.Parse(bytes, 2, hasVar: true).VarChunk(column.VariableIndex).ToArray();
            }
        }
        throw new InvalidOperationException("No live row found.");
    }
}
