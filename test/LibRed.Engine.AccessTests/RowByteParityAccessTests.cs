using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Formats;
using LibRed.IO;
using Xunit;

namespace LibRed.Engine.Tests;

// The row bytes themselves — where the leading column count, the fixed region, the variable-offset table
// and the null bitmap all have to agree at once. RowCodecGappedIdTests exercises the encoder against
// itself; this puts the same INSERT through both engines and compares what lands on the data page.
//
// It found the memo compression rule (MemoCompressionAccessTests): ACE compresses an inline long value
// whether or not the column is declared WITH COMPRESSION, and LibRed was requiring the flag, so every
// short ASCII memo was stored at twice ACE's size.
[Collection(AceCollection.Name)]
public class RowByteParityAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    public static TheoryData<string, string> Shapes => new()
    {
        { "CREATE TABLE W (A LONG, B LONG)", "INSERT INTO W (A, B) VALUES (1, 2)" },
        { "CREATE TABLE W (A LONG, B LONG)", "INSERT INTO W (A) VALUES (1)" },
        { "CREATE TABLE W (A LONG, B LONG)", "INSERT INTO W (A) VALUES (NULL)" },
        { "CREATE TABLE W (A LONG, T TEXT(20))", "INSERT INTO W (A, T) VALUES (1, 'hello')" },
        { "CREATE TABLE W (A LONG, T TEXT(20))", "INSERT INTO W (A) VALUES (1)" },
        { "CREATE TABLE W (A LONG, T TEXT(20))", "INSERT INTO W (A, T) VALUES (1, '')" },
        { "CREATE TABLE W (A LONG, T TEXT(20), U TEXT(20))", "INSERT INTO W (A, T, U) VALUES (1, 'aa', 'bbbb')" },
        { "CREATE TABLE W (A LONG, B BIT, C DOUBLE, D DATETIME, T TEXT(10))",
            "INSERT INTO W (A, B, C, D, T) VALUES (1, -1, 2.5, #2024-03-04 05:06:07#, 'z')" },
        { "CREATE TABLE W (A LONG, B BIT)", "INSERT INTO W (A, B) VALUES (1, 0)" },
        { "CREATE TABLE W (A LONG, T TEXT(100))", "INSERT INTO W (A, T) VALUES (1, 'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx')" },
        // Long values across the storage forms and both content kinds.
        { "CREATE TABLE W (A LONG, M LONGTEXT)", "INSERT INTO W (A, M) VALUES (1, 'short')" },
        { "CREATE TABLE W (A LONG, M LONGTEXT)", "INSERT INTO W (A, M) VALUES (1, 'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx')" },
        { "CREATE TABLE W (A LONG, M LONGTEXT)",
            "INSERT INTO W (A, M) VALUES (1, 'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx')" },
        { "CREATE TABLE W (A LONG, M LONGTEXT)",
            "INSERT INTO W (A, M) VALUES (1, '中中中中中中中中中中中中中中中中中中中中中中中中中中中中中中')" },
        // A BigBinary value stays inline at any size, where VARBINARY stops at 510.
        { "CREATE TABLE W (A LONG, B BIGBINARY)", "INSERT INTO W (A, B) VALUES (1, 0x0102030405)" },
        { "CREATE TABLE W (A LONG, B BIGBINARY)", $"INSERT INTO W (A, B) VALUES (1, 0x{string.Concat(Enumerable.Repeat("0A1B2C", 1000))})" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void The_row_bytes_match_ace(string ddl, string insert)
    {
        byte[]? ace = Row([ddl, insert], AceRun);
        Assert.SkipWhen(ace is null, "ACE wrote no row for this shape.");

        byte[] libred = Row([ddl, insert], LibRedRun)!;
        output.WriteLine(Convert.ToHexString(ace!));
        Assert.Equal(Convert.ToHexString(ace!), Convert.ToHexString(libred));
    }

    // A row written after the table's variable columns have been dropped. The rows already on the page still
    // carry the variable trailer those columns needed, while the schema no longer says they are variable — so
    // a writer that measures its fixed-region length off an existing row measures that trailer as fixed data
    // and pads every later row to it. Both end states are asked: no variable column left at all, and one
    // added back afterwards.
    public static TheoryData<string, string[]> DroppedVariableColumns => new()
    {
        { "none left",
            [
                "CREATE TABLE W (A LONG, T TEXT(20), U TEXT(20))",
                "INSERT INTO W (A, T, U) VALUES (1, 'aa', 'bbbb')",
                "ALTER TABLE W DROP COLUMN T",
                "ALTER TABLE W DROP COLUMN U",
                "INSERT INTO W (A) VALUES (2)",
            ]
        },
        // The two single-drop shapes the colCount rule turns on: dropping the highest-id column when it is
        // fixed, and when it is variable.
        { "highest-id fixed column dropped",
            [
                "CREATE TABLE W (A LONG, B LONG)",
                "INSERT INTO W (A, B) VALUES (1, 2)",
                "ALTER TABLE W DROP COLUMN B",
                "INSERT INTO W (A) VALUES (2)",
            ]
        },
        { "highest-id variable column dropped",
            [
                "CREATE TABLE W (A LONG, T TEXT(20))",
                "INSERT INTO W (A, T) VALUES (1, 'aa')",
                "ALTER TABLE W DROP COLUMN T",
                "INSERT INTO W (A) VALUES (2)",
            ]
        },
        // A type-change ALTER burns the old column id the same way a drop does. The row the ALTER itself
        // re-lays carries the dead id's bit SET (§5); this asks what a row INSERTED afterwards carries.
        { "an id burned by a retype",
            [
                "CREATE TABLE W (A LONG, B LONG, C LONG)",
                "INSERT INTO W (A, B, C) VALUES (1, 2, 3)",
                "ALTER TABLE W ALTER COLUMN B DOUBLE",
                "INSERT INTO W (A, B, C) VALUES (4, 5, 6)",
            ]
        },
        { "one added back",
            [
                "CREATE TABLE W (A LONG, T TEXT(20), U TEXT(20))",
                "INSERT INTO W (A, T, U) VALUES (1, 'aa', 'bbbb')",
                "ALTER TABLE W DROP COLUMN T",
                "ALTER TABLE W DROP COLUMN U",
                "ALTER TABLE W ADD COLUMN V TEXT(20)",
                "INSERT INTO W (A, V) VALUES (2, 'cc')",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(DroppedVariableColumns))]
    public void A_row_written_after_the_variable_columns_were_dropped_matches_ace(string label, string[] statements)
    {
        byte[]? ace = Row(statements, AceRun, row: 1);
        Assert.SkipWhen(ace is null, "ACE wrote no row for this shape.");

        byte[] libred = Row(statements, LibRedRun, row: 1)!;
        output.WriteLine($"{label}: ace={Convert.ToHexString(ace!)} libred={Convert.ToHexString(libred)}");
        Assert.Equal(Convert.ToHexString(ace!), Convert.ToHexString(libred));
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

    /// <summary>The bytes of row <paramref name="row"/> on table W's data page — the page the table owns,
    /// found by its owner stamp rather than by walking the usage map.</summary>
    private static byte[]? Row(string[] statements, Action<string, string[]> run, int row = 0)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "rowbytes-");
        try
        {
            try { run(path, statements); }
            catch (OleDbException) { return null; }

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
                if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(format.DataRowCountOffset, 2)) <= row)
                    continue;

                // Slot offsets are non-increasing, so a row runs from its own offset to the previous slot's
                // (the page end for row 0).
                int Offset(int i) => BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(format.DataRowDirectoryOffset + i * 2, 2)) & 0x1FFF;
                int start = Offset(row);
                int end = row == 0 ? format.PageSize : Offset(row - 1);
                return bytes.AsSpan(start, end - start).ToArray();
            }
            return null;
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
