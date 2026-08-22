using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Text collation is threaded from the database object into new column descriptors instead of being a
/// hardcoded constant. LibRed currently handles only General legacy (LCID 1033, version 0) — the order
/// every file it opens uses — so this verifies the plumbing is byte-faithful and that the index-key path
/// refuses any other collation rather than emitting wrong bytes.
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

    [Fact]
    public void Index_key_encoding_refuses_an_unsupported_collation()
    {
        // A column whose collation isn't General legacy (e.g. a 2010+ General v1, or a non-English locale)
        // must be rejected, not encoded with the wrong (General-v0) weight table.
        var v1 = new ColumnDef
        {
            Name = "C",
            Type = JetDataType.Text,
            Collation = new Collation(CollatingOrder.General, 1),
        };
        var ex = Assert.Throws<NotSupportedException>(() =>
            IndexKeyEncoder.Encode([(v1, true)], ["abc"]));
        Assert.Contains("not implemented", ex.Message);

        // The same column at General legacy encodes fine.
        var v0 = new ColumnDef { Name = "C", Type = JetDataType.Text, Collation = Collation.GeneralLegacy };
        Assert.NotEmpty(IndexKeyEncoder.Encode([(v0, true)], ["abc"]));
    }
}
