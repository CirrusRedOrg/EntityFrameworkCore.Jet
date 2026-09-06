using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using Xunit;

namespace LibRed.Engine.Tests;

// The whole table definition LibRed writes must equal the one ACE writes for the same DDL — not just the
// column descriptors (ColumnDescriptorByteParityAccessTests) but the header, the index stats blocks, the
// index data (0x33) and info (0x2F) blocks, the name runs and the long-value region.
//
// Nothing covered this: the whole-file byte-diff the spec cites (AceModifyByteDiffProbe) is not in the tree,
// and it covered ALTER rather than CREATE. Across the shapes below the definitions come out byte-identical,
// with one measured exception recorded in Usage_map_rows_follow_declaration_order below.
//
// Constraints are named deliberately. An unnamed primary key makes ACE generate `Index_<hex>` from nothing
// reproducible while LibRed picks the stable "PrimaryKey" — an engine choice documented in TableCreator —
// so an unnamed key would only ever measure that known difference.
[Collection(AceCollection.Name)]
public class TdefByteParityAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    public static TheoryData<string, string> Shapes => new()
    {
        { "bare", "CREATE TABLE W (Id LONG)" },
        { "pk", "CREATE TABLE W (Id LONG, A LONG, CONSTRAINT pk PRIMARY KEY (Id))" },
        { "pk+text", "CREATE TABLE W (Id LONG, A LONG, B TEXT(20), CONSTRAINT pk PRIMARY KEY (Id))" },
        { "composite-pk", "CREATE TABLE W (A LONG, B LONG, C TEXT(10), CONSTRAINT pk PRIMARY KEY (A, B))" },
        { "notnull", "CREATE TABLE W (Id LONG, A LONG NOT NULL, B TEXT(20) NOT NULL, CONSTRAINT pk PRIMARY KEY (Id))" },
        { "counter", "CREATE TABLE W (Id COUNTER, A TEXT(30), CONSTRAINT pk PRIMARY KEY (Id))" },
        { "unique", "CREATE TABLE W (Id LONG, A LONG, CONSTRAINT pk PRIMARY KEY (Id), CONSTRAINT u UNIQUE (A))" },
        { "guid+decimal", "CREATE TABLE W (Id LONG, G GUID, D DECIMAL(18,4), CONSTRAINT pk PRIMARY KEY (Id))" },
        { "many-columns", "CREATE TABLE W (Id LONG, A BYTE, B SMALLINT, C REAL, D FLOAT, E CURRENCY, "
            + "F DATETIME, G BIT, H CHAR(10), I VARCHAR(40), J BINARY(8), CONSTRAINT pk PRIMARY KEY (Id))" },
        // Long-value columns with an INLINE key: the one spelling where the usage-map order agrees.
        { "memo", "CREATE TABLE W (Id LONG PRIMARY KEY, M LONGTEXT, N LONGTEXT)" },
        { "ole", "CREATE TABLE W (Id LONG PRIMARY KEY, O LONGBINARY)" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void The_whole_definition_matches_ace(string label, string sql)
    {
        var aceDef = Definition(sql, AceCreate);
        Assert.SkipWhen(aceDef is null, $"ACE would not create {label}.");

        (byte[] ace, JetFormatBase format) = aceDef!.Value;
        byte[] libred = Definition(sql, LibRedCreate)!.Value.Bytes;

        // An inline key leaves ACE naming the index unreproducibly; compare those shapes by every region
        // except the names, and the named-constraint shapes whole.
        bool generatedName = IndexNames(ace, format).Any(n => n.StartsWith("Index_", StringComparison.Ordinal));
        foreach ((string region, int start, int end) in Regions(ace, format))
        {
            if (generatedName && region is "index-names" or "header") continue;
            for (int i = start; i < Math.Min(end, Math.Min(ace.Length, libred.Length)); i++)
                Assert.True(ace[i] == libred[i],
                    $"{label}: {region} differs at 0x{i:X3} — ACE {ace[i]:X2}, LibRed {libred[i]:X2}");
        }

        if (!generatedName) Assert.Equal(ace.Length, libred.Length);
        output.WriteLine($"{label}: {ace.Length} bytes, index names [{string.Join(", ", IndexNames(ace, format))}]");
    }

    // The one measured divergence. ACE assigns usage-map rows in DECLARATION order: an inline PRIMARY KEY on
    // the first column is created before the long-value columns and takes row 2, while a trailing CONSTRAINT
    // clause is created after them and lands past their rows. LibRed cannot tell the two spellings apart —
    // the position is lost between the parser and CreateTable — so it always uses the inline order.
    //
    // Both files are self-consistent and ACE reads either, so this is faithfulness, not corruption. Asserted
    // rather than ignored so that closing the gap fails here and this note gets updated with it.
    [Fact]
    public void Usage_map_rows_follow_declaration_order()
    {
        const string inline = "CREATE TABLE W (Id LONG PRIMARY KEY, M LONGTEXT, N LONGTEXT)";
        const string named = "CREATE TABLE W (Id LONG, M LONGTEXT, N LONGTEXT, CONSTRAINT pk PRIMARY KEY (Id))";

        // Inline: the index is declared first and gets row 2, the columns follow. Both engines agree.
        Assert.Equal("index [2] long-value [3,4 5,6]", Rows(inline, AceCreate));
        Assert.Equal("index [2] long-value [3,4 5,6]", Rows(inline, LibRedCreate));

        // Named: ACE creates the columns first, so they take rows 2..5 and the index lands on 6.
        Assert.Equal("index [6] long-value [2,3 4,5]", Rows(named, AceCreate));
        Assert.Equal("index [2] long-value [3,4 5,6]", Rows(named, LibRedCreate));   // the divergence
    }

    private static void AceCreate(string path, string sql)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void LibRedCreate(string path, string sql)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        new QueryEngine(database).ExecuteNonQuery(sql);
    }

    /// <summary>Which usage-map row each index and long-value column was given.</summary>
    private static string Rows(string sql, Action<string, string> create)
    {
        (byte[] def, JetFormatBase format) = Definition(sql, create)!.Value;
        int dataCount = BinaryPrimitives.ReadInt32LittleEndian(def.AsSpan(format.TdefIndexCountOffset, 4));
        (string _, int dataStart, int _) = Regions(def, format).Single(r => r.Name == "index-data-blocks");
        (string _, int lvalStart, int lvalEnd) = Regions(def, format).Single(r => r.Name == "long-value-region");

        var indexes = Enumerable.Range(0, dataCount)
            .Select(i => def[dataStart + i * DataBlockSize + UsageMapRowOffset].ToString());

        var columns = new List<string>();
        for (int pos = lvalStart; pos + 10 <= lvalEnd
             && BinaryPrimitives.ReadUInt16LittleEndian(def.AsSpan(pos, 2)) != 0xFFFF; pos += 10)
            columns.Add($"{def[pos + 2]},{def[pos + 6]}");

        return $"index [{string.Join(",", indexes)}] long-value [{string.Join(" ", columns)}]";
    }

    // IndexBlockFormat is internal to Core; these are its DataBlockSize / InfoBlockSize / UsageMapRowOffset.
    private const int DataBlockSize = 52;
    private const int InfoBlockSize = 28;
    private const int UsageMapRowOffset = 0x22;

    private static List<string> IndexNames(byte[] def, JetFormatBase format)
    {
        (string _, int start, int end) = Regions(def, format).Single(r => r.Name == "index-names");
        var names = new List<string>();
        for (int pos = start; pos + 2 <= end;)
        {
            int len = BinaryPrimitives.ReadUInt16LittleEndian(def.AsSpan(pos, 2));
            if (len == 0 || pos + 2 + len > end) break;
            names.Add(System.Text.Encoding.Unicode.GetString(def.AsSpan(pos + 2, len)));
            pos += 2 + len;
        }
        return names;
    }

    /// <summary>The TDEF's regions in order, derived from its own counts.</summary>
    private static IEnumerable<(string Name, int Start, int End)> Regions(byte[] def, JetFormatBase format)
    {
        int dataCount = BinaryPrimitives.ReadInt32LittleEndian(def.AsSpan(format.TdefIndexCountOffset, 4));
        int logicalCount = BinaryPrimitives.ReadInt32LittleEndian(
            def.AsSpan(format.TdefLogicalIndexCountOffset, 4));
        int colCount = BinaryPrimitives.ReadUInt16LittleEndian(def.AsSpan(format.TdefColumnCountOffset, 2));

        int pos = format.TdefRealIndexBlockOffset;
        yield return ("header", 0, pos);

        yield return ("index-stats", pos, pos + dataCount * format.RealIndexEntrySize);
        pos += dataCount * format.RealIndexEntrySize;

        yield return ("column-descriptors", pos, pos + colCount * format.ColumnDescriptorSize);
        pos += colCount * format.ColumnDescriptorSize;

        int namesStart = pos;
        for (int i = 0; i < colCount; i++) pos += 2 + BinaryPrimitives.ReadUInt16LittleEndian(def.AsSpan(pos, 2));
        yield return ("column-names", namesStart, pos);

        yield return ("index-data-blocks", pos, pos + dataCount * DataBlockSize);
        pos += dataCount * DataBlockSize;

        yield return ("index-info-blocks", pos, pos + logicalCount * InfoBlockSize);
        pos += logicalCount * InfoBlockSize;

        int indexNames = pos;
        for (int i = 0; i < logicalCount && pos + 2 <= def.Length; i++)
            pos += 2 + BinaryPrimitives.ReadUInt16LittleEndian(def.AsSpan(pos, 2));
        yield return ("index-names", indexNames, pos);

        yield return ("long-value-region", pos, def.Length);
    }

    private static (byte[] Bytes, JetFormatBase Format)? Definition(string sql, Action<string, string> create)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "tdef-parity-");
        try
        {
            try { create(path, sql); }
            catch (OleDbException) { return null; }

            int definitionPage;
            using (var database = JetDatabase.Open(path, readOnly: true))
                definitionPage = database.Catalog.FindTable("W")!.DefinitionPage;

            using var channel = PageChannel.Open(path, readOnly: true);
            JetFormatBase format = channel.Format;
            byte[] page = channel.ReadPage(definitionPage).Span.ToArray();
            int length = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(format.TdefLengthOffset, 4));
            return (length > 0 && length <= page.Length ? page.AsSpan(0, length).ToArray() : page, format);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
