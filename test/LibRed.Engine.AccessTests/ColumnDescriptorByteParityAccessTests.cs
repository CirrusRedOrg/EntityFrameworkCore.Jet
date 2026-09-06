using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using Xunit;

namespace LibRed.Engine.Tests;

// The whole 25-byte column descriptor LibRed writes must equal the one ACE writes for the same DDL.
//
// ColumnStorageParityAccessTests compares three fields; this compares all 25, which is where the rest of
// the divergences were hiding. Nothing checked them before: the byte-diff probe the spec cites for this
// (AceModifyByteDiffProbe) is gone from the tree, and it covered ALTER rather than CREATE.
//
// It found two, both of them beliefs rather than oversights:
//
//   0x09, the repeated column id, on EVERY column of every type. TdefBuilder wrote zero, with a comment
//   saying real files store zero there - read off the system tables, where they do. Every genuine user
//   table in every fixture carries the id, and so does everything that creates one: ACE's SQL DDL, DAO's
//   object model, and DAO-executed SQL. Compaction preserves whatever is there, so it is set at creation.
//
//   0x0C for DATETIME2 only, where ACE writes LANGID 0x0009 rather than the database's own collation.
//
// A descriptor is small and rigid, so compare it whole rather than field by field: anything that shifts
// shows up here rather than in a file Access silently dislikes.
[Collection(AceCollection.Name)]
public class ColumnDescriptorByteParityAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    public static TheoryData<string> Types =>
    [
        "BIT", "BYTE", "SMALLINT", "INTEGER", "COUNTER", "REAL", "FLOAT", "CURRENCY", "DATETIME",
        "GUID", "DECIMAL(18,4)", "CHAR(50)", "VARCHAR(50)", "TEXT(50)",
        "BINARY(50)", "VARBINARY(50)", "LONGTEXT", "LONGBINARY",
        "BIGINT",       // ACE 16+
        "DATETIME2",    // ACE 17+
    ];

    [Theory]
    [MemberData(nameof(Types))]
    public void Descriptor_bytes_match_ace(string declaration)
    {
        string sql = $"CREATE TABLE W (Id LONG, V {declaration})";

        byte[]? ace = Descriptor(Copy("desc-ace-"), path =>
        {
            using OleDbConnection connection = AceTestDatabase.Open(path);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        });

        // BIGINT needs ACE 16 and DATETIME2 needs ACE 17; older matrix legs say nothing about the mapping.
        Assert.SkipWhen(ace is null, $"This ACE build does not accept {declaration}");

        byte[]? libred = Descriptor(Copy("desc-libred-"), path =>
        {
            using var database = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(database).ExecuteNonQuery(sql);
        });
        Assert.NotNull(libred);

        output.WriteLine($"{declaration}: {Convert.ToHexString(ace!)}");
        Assert.Equal(Convert.ToHexString(ace!), Convert.ToHexString(libred!));
    }

    private static string Copy(string prefix) => TemporaryDatabase.CopyPath(
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), prefix);

    /// <summary>The descriptor bytes for column "V" — the second column of table W. A two-column table's
    /// definition fits one page, so the page is sliced directly rather than stitching a TDEF chain.</summary>
    private static byte[]? Descriptor(string path, Action<string> create)
    {
        try { create(path); }
        catch { return null; }

        int definitionPage;
        using (var database = JetDatabase.Open(path, readOnly: true))
        {
            TableDef? table = database.Catalog.FindTable("W");
            if (table is null) return null;
            definitionPage = table.DefinitionPage;
        }

        using var channel = PageChannel.Open(path, readOnly: true);
        JetFormatBase format = channel.Format;
        byte[] page = channel.ReadPage(definitionPage).Span.ToArray();
        int dataCount = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(format.TdefIndexCountOffset, 4));
        int start = format.TdefRealIndexBlockOffset + dataCount * format.RealIndexEntrySize;
        return page.AsSpan(start + format.ColumnDescriptorSize, format.ColumnDescriptorSize).ToArray();
    }
}
