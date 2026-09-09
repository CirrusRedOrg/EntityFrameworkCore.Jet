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
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void The_row_bytes_match_ace(string ddl, string insert)
    {
        byte[]? ace = Row(ddl, insert, AceRun);
        Assert.SkipWhen(ace is null, "ACE wrote no row for this shape.");

        byte[] libred = Row(ddl, insert, LibRedRun)!;
        output.WriteLine(Convert.ToHexString(ace!));
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

    /// <summary>The bytes of the single row on table W's data page — the page the table owns, found by its
    /// owner stamp rather than by walking the usage map.</summary>
    private static byte[]? Row(string ddl, string insert, Action<string, string[]> run)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "rowbytes-");
        try
        {
            try { run(path, [ddl, insert]); }
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
                if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(format.DataRowCountOffset, 2)) == 0)
                    continue;

                int start = BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(format.DataRowDirectoryOffset, 2)) & 0x1FFF;
                return bytes.AsSpan(start, format.PageSize - start).ToArray();
            }
            return null;
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
