using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Text collation is threaded from the database object into new column descriptors instead of being a
/// hardcoded constant, so a table created in a General (v1) database gets v1 columns. Both General orders
/// encode index keys (v0 via JetTextCollation, v1 via JetTextCollationV1); other locales are refused rather
/// than encoded with the English weight table. This verifies the plumbing is byte-faithful.
/// </summary>
public class CollationTests
{
    private static List<ColumnSpec> Columns() =>
    [
        new("Id", JetDataType.Int32, 4, IsFixedLength: true),
        new("Name", JetDataType.Text, 50, IsFixedLength: false),
        new("Note", JetDataType.Memo, 0, IsFixedLength: false),
        new("Price", JetDataType.FixedPoint, 17, IsFixedLength: true, Precision: 18, Scale: 2),
    ];

    [Fact]
    public void A_new_database_defaults_to_general_legacy()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "coll-def-");
        try
        {
            using var db = JetDatabase.Open(path);
            Assert.Equal(Collation.GeneralLegacy, db.Collation);
            Assert.Equal(CollatingOrder.General, db.Collation.Order);
            Assert.Equal(0, db.Collation.Version);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Non_numeric_columns_carry_the_database_collation_and_round_trip()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "coll-rt-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T", Columns(), primaryKey: ["Id"]);

            using var db2 = JetDatabase.Open(path);
            var cols = db2.OpenTable("T").Definition.Columns;

            // Text and Memo columns read back the database collation.
            Assert.Equal(Collation.GeneralLegacy, cols.First(c => c.Name == "Name").Collation);
            Assert.Equal(Collation.GeneralLegacy, cols.First(c => c.Name == "Note").Collation);

            // A numeric column reuses the locale bytes for precision/scale — those must survive intact,
            // and its (irrelevant) collation defaults to General legacy.
            var price = cols.First(c => c.Name == "Price");
            Assert.Equal(18, price.Precision);
            Assert.Equal(2, price.Scale);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void The_written_locale_bytes_are_byte_identical_to_the_old_hardcoded_constant()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "coll-bytes-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("T", Columns(), primaryKey: ["Id"]);
            var table = db.OpenTable("T");
            var format = db.Format;

            var tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
            int dataCount = tdef.ReadInt32(format.TdefIndexCountOffset);
            int columnBlock = format.TdefRealIndexBlockOffset + dataCount * format.RealIndexEntrySize;
            int nameIndex = table.Definition.Columns.First(c => c.Name == "Name").Index;
            var descriptor = tdef.Span.Slice(columnBlock + nameIndex * format.ColumnDescriptorSize, format.ColumnDescriptorSize);

            // 0x0409 (little-endian) locale, version 0 — exactly what the LocaleLow/LocaleHigh constants wrote.
            Assert.Equal(0x09, descriptor[0x0B]);
            Assert.Equal(0x04, descriptor[0x0C]);
            Assert.Equal(0x00, descriptor[0x0D]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // An order LibRed has not measured must be REFUSED, not encoded with the English table. A wrong key does
    // not throw and does not corrupt anything visibly — it just disagrees with ACE about where a row sorts,
    // so seeks miss rows that are present. Refusing is the only way that failure becomes visible.
    //
    // This used to be asserted with Cyrillic (1049), on the reasoning that a non-English locale obviously
    // could not be encoded with the English table. The collation survey measured 1049 and found its keys
    // byte-identical to General v0, so the example stopped being an example. The two below are refused for
    // reasons that will outlive a survey: CJK is deliberately out of scope, and Irish is the one order that
    // could not be measured at all.
    [Theory]
    [InlineData((int)CollatingOrder.Japanese, "a CJK order, deliberately out of scope")]
    [InlineData(1084, "Irish - accepted by Jet, but never measured against ACE")]
    public void Index_key_encoding_refuses_an_unmeasured_collation(int order, string why)
    {
        _ = why;   // names the case in the test output

        var column = new ColumnDef
        {
            Name = "C",
            Type = JetDataType.Text,
            Collation = new Collation((CollatingOrder)order, 0),
        };
        var ex = Assert.Throws<NotSupportedException>(() =>
            IndexKeyEncoder.Encode([(column, true)], ["abc"]));
        Assert.Contains("not implemented", ex.Message);
    }

    // The other half of the same guard, and the one a list of refusals cannot give: an order that IS
    // measured must be accepted. Cyrillic is here precisely because it moved.
    [Fact]
    public void Index_key_encoding_accepts_an_order_measured_to_equal_general()
    {
        Assert.True(new Collation(CollatingOrder.Cyrillic, 0).IsIndexKeyEncodable);

        ColumnDef Column(Collation collation) =>
            new() { Name = "C", Type = JetDataType.Text, Collation = collation };

        Assert.Equal(
            Convert.ToHexString(IndexKeyEncoder.Encode(
                [(Column(Collation.GeneralLegacy), true)], ["abc"])),
            Convert.ToHexString(IndexKeyEncoder.Encode(
                [(Column(new Collation(CollatingOrder.Cyrillic, 0)), true)], ["abc"])));
    }

    [Fact]
    public void Index_key_encoding_supports_both_general_orders()
    {
        // Both General orders encode, and to *different* bytes: v0 is the compacted one-byte-per-character
        // table, v1 the Windows NLS weights verbatim (see GeneralV1CollationTests).
        var v0 = new ColumnDef { Name = "C", Type = JetDataType.Text, Collation = Collation.GeneralLegacy };
        var v1 = new ColumnDef { Name = "C", Type = JetDataType.Text, Collation = Collation.General };

        byte[] legacy = IndexKeyEncoder.Encode([(v0, true)], ["abc"]);
        byte[] general = IndexKeyEncoder.Encode([(v1, true)], ["abc"]);

        Assert.NotEmpty(legacy);
        Assert.NotEmpty(general);
        Assert.NotEqual(Convert.ToHexString(legacy), Convert.ToHexString(general));
    }
}
