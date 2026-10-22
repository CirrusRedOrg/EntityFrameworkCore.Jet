using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
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
// reproducible while LibRed picks the stable "PrimaryKey" — an engine choice documented in SchemaEditor —
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
        { "self-reference", "CREATE TABLE W (Id LONG, P LONG, CONSTRAINT pk PRIMARY KEY (Id), CONSTRAINT fk FOREIGN KEY (P) REFERENCES W (Id))" },
        // Names whose collation order differs from an ordinal one: `_` sorts before letters.
        { "index-name-order", "CREATE TABLE W (Id LONG, A LONG, B LONG, C LONG, CONSTRAINT pk PRIMARY KEY (Id), "
            + "CONSTRAINT [a_b] UNIQUE (A), CONSTRAINT ab UNIQUE (B), CONSTRAINT [_c] UNIQUE (C))" },
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

        (byte[] ace, JetFormatBase format, TableDefinition aceParsed) = aceDef!.Value;
        byte[] libred = Definition(sql, LibRedCreate)!.Value.Bytes;

        // An inline key leaves ACE naming the index unreproducibly; compare those shapes by every region
        // except the names, and the named-constraint shapes whole.
        bool generatedName = IndexNames(aceParsed).Any(n => n.StartsWith("Index_", StringComparison.Ordinal));
        foreach ((string region, int start, int end) in Regions(ace, format))
        {
            if (generatedName && region is "index-names" or "header") continue;
            for (int i = start; i < Math.Min(end, Math.Min(ace.Length, libred.Length)); i++)
                Assert.True(ace[i] == libred[i],
                    $"{label}: {region} differs at 0x{i:X3} — ACE {ace[i]:X2}, LibRed {libred[i]:X2}");
        }

        if (!generatedName) Assert.Equal(ace.Length, libred.Length);
        output.WriteLine($"{label}: {ace.Length} bytes, index names [{string.Join(", ", IndexNames(aceParsed))}]");
    }

    // Usage-map rows follow DECLARATION order: an inline PRIMARY KEY on the first column is declared before the
    // long-value columns and takes row 2, while a trailing CONSTRAINT clause is declared after them and lands
    // past their rows. The same table written the two ways lays its maps out differently, and both engines now
    // agree on each. (This used to record the divergence, LibRed always using the inline order because the
    // constraint's position was lost between the parser and CreateTable; it is carried through now, and the
    // rule across inline, table-level, interleaved and foreign-key shapes is in
    // CreateTableUsageMapOrderAccessTests.)
    [Fact]
    public void Usage_map_rows_follow_declaration_order()
    {
        const string inline = "CREATE TABLE W (Id LONG PRIMARY KEY, M LONGTEXT, N LONGTEXT)";
        const string named = "CREATE TABLE W (Id LONG, M LONGTEXT, N LONGTEXT, CONSTRAINT pk PRIMARY KEY (Id))";

        // Inline: the index is declared first and gets row 2, the columns follow.
        Assert.Equal("index [2] long-value [3,4 5,6]", Rows(inline, AceCreate));
        Assert.Equal("index [2] long-value [3,4 5,6]", Rows(inline, LibRedCreate));

        // Named: the columns are declared first, so they take rows 2..5 and the index lands on 6.
        Assert.Equal("index [6] long-value [2,3 4,5]", Rows(named, AceCreate));
        Assert.Equal("index [6] long-value [2,3 4,5]", Rows(named, LibRedCreate));
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
        TableDefinition definition = Definition(sql, create)!.Value.Parsed;
        var indexes = definition.Indexes.OrderBy(i => i.RealIndexOrdinal).Select(i => i.UsageMap.Row);
        var columns = definition.LongValueOwnedMaps.OrderBy(e => e.Key)
            .Select(e => $"{e.Value.Row},{definition.LongValueFreeMaps[e.Key].Row}");
        return $"index [{string.Join(",", indexes)}] long-value [{string.Join(" ", columns)}]";
    }

    private static List<string> IndexNames(TableDefinition definition) =>
        [.. definition.LogicalIndexes.Select(l => l.Name)];

    /// <summary>The TDEF's regions in order, through the walk the engine reads and writes it with.</summary>
    private static IEnumerable<(string Name, int Start, int End)> Regions(byte[] def, JetFormatBase format)
    {
        TableDefinition.Regions regions = TableDefinition.Regions.Of(def, format);
        int columnNames = regions.ColumnDescriptors + regions.ColumnCount * format.ColumnDescriptorSize;
        int indexNamesEnd = regions.IndexNames;
        for (int i = 0; i < regions.LogicalCount; i++)
            indexNamesEnd = TableDefinition.NameEntryEnd(def, indexNamesEnd, format, $"index {i}");

        yield return ("header", 0, regions.Stats);
        yield return ("index-stats", regions.Stats, regions.ColumnDescriptors);
        yield return ("column-descriptors", regions.ColumnDescriptors, columnNames);
        yield return ("column-names", columnNames, regions.DataBlocks);
        yield return ("index-data-blocks", regions.DataBlocks, regions.InfoBlocks);
        yield return ("index-info-blocks", regions.InfoBlocks, regions.IndexNames);
        yield return ("index-names", regions.IndexNames, indexNamesEnd);
        yield return ("long-value-region", indexNamesEnd, def.Length);
    }

    /// <summary>Table W's whole definition, continuation pages stitched in, and that definition parsed.</summary>
    private static (byte[] Bytes, JetFormatBase Format, TableDefinition Parsed)? Definition(
        string sql, Action<string, string> create)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "tdef-parity-");
        try
        {
            try { create(path, sql); }
            catch (OleDbException) { return null; }

            using var database = JetDatabase.Open(path, readOnly: true);
            (PageBuffer buffer, _) = TableDefinition.ReadChain(database.Channel, database.Catalog.FindTable("W")!.DefinitionPage);
            var parsed = new TableDefinition();
            parsed.Read(buffer, database.Format);
            return (buffer.Span.ToArray(), database.Format, parsed);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}