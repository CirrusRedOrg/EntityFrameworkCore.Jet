using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Every DOCUMENTED column-descriptor flag is modelled and composed from ColumnDef when a column is created.
public class ColumnDescriptorFlagTests
{
    [Fact]
    public void BuildColumnDescriptor_composes_the_documented_flags_from_the_model()
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(0x02); // ACE 12

        var col = new ColumnDef
        {
            Name = "H", Type = JetDataType.Memo, Index = 0, ColumnId = 3, Length = 4,
            IsUpdatable = true, IsGuidAutoNumber = true, IsHyperlink = true,
            SupportsCompressedUnicode = true, IsCalculated = true,
        };

        byte[] d = TableDefinition.BuildColumnDescriptor(col, format);

        Assert.Equal(ColumnFlags.Updatable | ColumnFlags.GuidAutoNumber | ColumnFlags.Hyperlink,
            (ColumnFlags)d[format.ColumnFlagsOffset]);
        Assert.Equal(ColumnExtendedFlags.CompressedUnicode | ColumnExtendedFlags.Calculated | ColumnExtendedFlags.EvaluatesExpression,
            (ColumnExtendedFlags)d[format.ColumnExtendedFlagsOffset]);
    }

    // ACE writes 0xC0 on a calculated column but reads only 0x40: with 0x80 alone a column is an ordinary one, its
    // value read as stored, and with 0x40 alone it is calculated (measured by patching the bit into a text column) —
    // here one with no Expression property, so ACE will not load the table: SELECT, COUNT(*) and the column schema
    // all fail "Property not found." (measured on this very file).
    [Theory]
    [InlineData(0x80, false)]
    [InlineData(0x40, true)]
    public void Only_bit_0x40_makes_a_column_calculated(byte bit, bool calculated)
    {
        string path = TemporaryDatabase.CreatePath("calc-bit-");
        try
        {
            JetDatabase.Create(path);
            int page;
            byte[] raw;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T", [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new ColumnSpec("A", JetDataType.Text, 40, IsFixedLength: false)]);
                db.OpenTable("T").Insert([1, "plain"]);
                var t = db.Catalog.FindTable("T")!;
                page = t.DefinitionPage;
                raw = t.FindColumn("A")!.RawDescriptor!;
            }

            byte[] file = File.ReadAllBytes(path);
            JetFormatBase format = JetFormatBase.FromVersionByte(file[0x14]);
            int at = file.AsSpan(page * format.PageSize, format.PageSize).IndexOf(raw);
            Assert.True(at >= 0);
            file[page * format.PageSize + at + format.ColumnExtendedFlagsOffset] |= bit;
            File.WriteAllBytes(path, file);

            using var read = JetDatabase.Open(path);
            if (calculated)
            {
                Assert.Throws<InvalidDataException>(() => read.OpenTable("T"));
                return;
            }
            Table table = read.OpenTable("T");
            ColumnDef column = table.Definition.FindColumn("A")!;
            Assert.False(column.IsCalculated);
            Assert.Equal("plain", table.Rows().Single()[column.Index]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The descriptor's reserved field at 0x11: ACE keeps the first byte's 0x01 bit through any design change and
    // zeroes the rest — the same after one rewrite as after several — while a row write leaves it alone (measured by
    // seeding each value and running ADD COLUMN, CREATE/DROP INDEX, DROP COLUMN and ALTER COLUMN through ACE).
    [Theory]
    [InlineData("A55AA55A", "01000000")]
    [InlineData("FF000000", "01000000")]
    [InlineData("01000000", "01000000")]
    [InlineData("02000000", "00000000")]
    [InlineData("00000001", "00000000")]
    public void A_design_change_keeps_only_bit_one_of_the_reserved_field(string seed, string kept)
    {
        string path = TemporaryDatabase.CreatePath("col-reserved-");
        try
        {
            JetDatabase.Create(path);
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T", [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new ColumnSpec("A", JetDataType.Text, 40, IsFixedLength: false)]);

            byte[] seedBytes = Convert.FromHexString(seed);
            long at = ReservedOffset(path, out JetFormatBase format);
            byte[] file = File.ReadAllBytes(path);
            seedBytes.CopyTo(file, at);
            File.WriteAllBytes(path, file);

            using (var db = JetDatabase.Open(path, readOnly: false))
                db.OpenTable("T").Insert([1, "row"]);
            Assert.Equal(seed, Convert.ToHexString(File.ReadAllBytes(path), (int)ReservedOffset(path, out _), 4));

            for (int n = 1; n <= 2; n++)
            {
                using (var db = JetDatabase.Open(path, readOnly: false))
                    db.AddColumn("T", new ColumnSpec($"L{n}", JetDataType.Int32, 4, IsFixedLength: true));
                Assert.Equal(kept, Convert.ToHexString(File.ReadAllBytes(path), (int)ReservedOffset(path, out _), 4));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A design change writes the record markers afresh — 0x659 at the header, each column descriptor and each
    // index-info block, 0x783 at each index-data block — whatever they held, a zero included (measured: ACE's
    // ADD COLUMN after seeding 0 or 0x100 into all four, on an .accdb and a Jet 4 .mdb alike).
    [Fact]
    public void A_design_change_writes_the_record_markers_afresh()
    {
        string path = TemporaryDatabase.CreatePath("tdef-markers-");
        try
        {
            JetDatabase.Create(path);
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T", [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new ColumnSpec("A", JetDataType.Text, 40, IsFixedLength: false)]);
                db.CreateIndex("T", "ix", [("A", false)]);
            }

            long[] Markers()
            {
                long column = ReservedOffset(path, out JetFormatBase format) - format.ColumnWriteOnceOffset + format.ColumnRecordMarkerOffset;
                int page;
                using (var db = JetDatabase.Open(path)) page = db.Catalog.FindTable("T")!.DefinitionPage;
                byte[] file = File.ReadAllBytes(path);
                long tdef = (long)page * format.PageSize;
                var regions = TableDefinition.Regions.Of(file.AsSpan((int)tdef, format.PageSize), format);
                return [tdef + format.TdefRecordMarkerOffset, column, tdef + regions.DataBlocks, tdef + regions.InfoBlocks + format.IndexInfoMarkerOffset];
            }

            byte[] seeded = File.ReadAllBytes(path);
            foreach (long at in Markers()) seeded.AsSpan((int)at, 4).Clear();
            File.WriteAllBytes(path, seeded);

            using (var db = JetDatabase.Open(path, readOnly: false))
                db.AddColumn("T", new ColumnSpec("L", JetDataType.Int32, 4, IsFixedLength: true));

            byte[] after = File.ReadAllBytes(path);
            Assert.Equal([0x659u, 0x659u, 0x783u, 0x659u], Markers().Select(at => BitConverter.ToUInt32(after, (int)at)));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Where column A's reserved field sits in the file.</summary>
    private static long ReservedOffset(string path, out JetFormatBase format)
    {
        int page;
        byte[] raw;
        using (var db = JetDatabase.Open(path))
        {
            var t = db.Catalog.FindTable("T")!;
            page = t.DefinitionPage;
            raw = t.FindColumn("A")!.RawDescriptor!;
        }
        byte[] file = File.ReadAllBytes(path);
        format = JetFormatBase.FromVersionByte(file[0x14]);
        int at = file.AsSpan(page * format.PageSize, format.PageSize).IndexOf(raw);
        Assert.True(at >= 0);
        return (long)page * format.PageSize + at + format.ColumnWriteOnceOffset;
    }

    // Bit 0x01 of that field makes a column write-once: ACE takes a value for it on INSERT and refuses one on UPDATE
    // (measured on Text, Long and Memo columns with the bit patched in; OLE DB then reports the column not writable).
    [Fact]
    public void A_write_once_column_takes_a_value_on_insert_and_refuses_one_on_update()
    {
        string path = TemporaryDatabase.CreatePath("col-writeonce-");
        try
        {
            JetDatabase.Create(path);
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T", [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new ColumnSpec("A", JetDataType.Text, 40, IsFixedLength: false)]);
            long at = ReservedOffset(path, out _);
            byte[] file = File.ReadAllBytes(path);
            file[at] = 0x01;
            File.WriteAllBytes(path, file);

            using var write = JetDatabase.Open(path, readOnly: false);
            Table table = write.OpenTable("T");
            Assert.True(table.Definition.FindColumn("A")!.IsWriteOnce);
            table.Insert([1, "first"]);
            (RowId id, object?[] row) = table.RowsWhere([0], v => (int)v[0]! == 1).Single();
            Assert.Equal("first", row[1]);

            var changed = (object?[])row.Clone();
            changed[1] = "second";
            var ex = Assert.Throws<InvalidOperationException>(() => table.Update(id, changed, new HashSet<int> { 1 }));
            Assert.Contains("Cannot update 'A'", ex.Message, StringComparison.Ordinal);

            var other = (object?[])row.Clone();
            other[0] = 2;
            table.Update(id, other, new HashSet<int> { 0 });
            Assert.Equal([2, "first"], table.Rows().Single());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The AutoNumber bit 0x04 on a Text column (a damaged descriptor): ACE still takes an explicit value, and sets the
    // counter at 0x14 to the value's first four bytes as UTF-16LE, zero-padded (measured: Customers.Address flagged,
    // "Street", "500" and "A" inserted).
    [Theory]
    [InlineData("Street", 0x00740053)]
    [InlineData("500", 0x00300035)]
    [InlineData("A", 65)]
    public void An_explicit_value_in_a_text_column_flagged_AutoNumber_sets_the_counter_to_its_first_four_bytes(
        string value, int counter)
    {
        string path = TemporaryDatabase.CreatePath("autonumber-text-");
        try
        {
            JetDatabase.Create(path);
            int page;
            byte[] raw;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T", [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new ColumnSpec("A", JetDataType.Text, 40, IsFixedLength: false)]);
                var t = db.Catalog.FindTable("T")!;
                page = t.DefinitionPage;
                raw = t.FindColumn("A")!.RawDescriptor!;
            }

            byte[] file = File.ReadAllBytes(path);
            JetFormatBase format = JetFormatBase.FromVersionByte(file[0x14]);
            int at = file.AsSpan(page * format.PageSize, format.PageSize).IndexOf(raw);
            Assert.True(at >= 0);
            file[page * format.PageSize + at + format.ColumnFlagsOffset] |= (byte)ColumnFlags.AutoNumber;
            File.WriteAllBytes(path, file);
            int Counter() => TableDefinition.ReadLastAutoNumber(TestDatabases.ReadPage(path, page), format);
            Assert.Equal(0, Counter());

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table table = db.OpenTable("T");
                Assert.True(table.Definition.FindColumn("A")!.IsAutoNumber);
                table.Insert([1, value]);
                Assert.Equal([1, value], table.Rows().Single());
            }
            Assert.Equal(counter, Counter());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The same bit on an OLE column: an explicit binary value sets the counter to its first four bytes (measured:
    // Categories.Picture flagged, bytes 01 02 03 04 05 06 set 0x14 to 0x04030201).
    [Fact]
    public void An_explicit_value_in_an_ole_column_flagged_AutoNumber_sets_the_counter_to_its_first_four_bytes()
    {
        string path = TemporaryDatabase.CreatePath("autonumber-ole-");
        try
        {
            JetDatabase.Create(path);
            int page;
            byte[] raw;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T", [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new ColumnSpec("P", JetDataType.Ole, 0, IsFixedLength: false)]);
                var t = db.Catalog.FindTable("T")!;
                page = t.DefinitionPage;
                raw = t.FindColumn("P")!.RawDescriptor!;
            }

            byte[] file = File.ReadAllBytes(path);
            JetFormatBase format = JetFormatBase.FromVersionByte(file[0x14]);
            int at = file.AsSpan(page * format.PageSize, format.PageSize).IndexOf(raw);
            Assert.True(at >= 0);
            file[page * format.PageSize + at + format.ColumnFlagsOffset] |= (byte)ColumnFlags.AutoNumber;
            File.WriteAllBytes(path, file);

            byte[] value = [1, 2, 3, 4, 5, 6];
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table table = db.OpenTable("T");
                Assert.True(table.Definition.FindColumn("P")!.IsAutoNumber);
                table.Insert([1, value]);
                Assert.Equal(value, table.Rows().Single()[1]);
            }
            Assert.Equal(0x04030201, TableDefinition.ReadLastAutoNumber(TestDatabases.ReadPage(path, page), format));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The same bit on fixed columns. A 4-byte Single takes its own bits as the counter; a column of any other width
    // cannot hold the 4-byte counter, and ACE refuses the row — explicit value and all — with the id burned
    // (measured: Order Details.Discount 0.25 set 0x14 to 0x3E800000; Quantity, UnitPrice, Orders.Freight and
    // OrderDate refused "The field is too small to accept the amount of data you attempted to add.", 0x14 + 1).
    [Theory]
    [InlineData(JetDataType.Single, 4, 0.25f, 0x3E800000, false)]
    [InlineData(JetDataType.Int16, 2, (short)-2, 1, true)]
    [InlineData(JetDataType.Currency, 8, 12.5, 1, true)]
    [InlineData(JetDataType.DateTime, 8, "2000-01-02", 1, true)]
    public void An_explicit_value_in_a_fixed_column_flagged_AutoNumber_needs_four_bytes(
        JetDataType type, int length, object value, int counter, bool refused)
    {
        string path = TemporaryDatabase.CreatePath("autonumber-fixed-");
        try
        {
            JetDatabase.Create(path);
            int page;
            byte[] raw;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T", [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new ColumnSpec("V", type, length, IsFixedLength: true)]);
                var t = db.Catalog.FindTable("T")!;
                page = t.DefinitionPage;
                raw = t.FindColumn("V")!.RawDescriptor!;
            }

            byte[] file = File.ReadAllBytes(path);
            JetFormatBase format = JetFormatBase.FromVersionByte(file[0x14]);
            int at = file.AsSpan(page * format.PageSize, format.PageSize).IndexOf(raw);
            Assert.True(at >= 0);
            file[page * format.PageSize + at + format.ColumnFlagsOffset] |= (byte)ColumnFlags.AutoNumber;
            File.WriteAllBytes(path, file);

            object stored = type switch
            {
                JetDataType.Currency => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
                JetDataType.DateTime => DateTime.Parse((string)value, System.Globalization.CultureInfo.InvariantCulture),
                _ => value,
            };
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table table = db.OpenTable("T");
                Assert.True(table.Definition.FindColumn("V")!.IsAutoNumber);
                if (refused)
                {
                    var ex = Assert.Throws<InvalidOperationException>(() => table.Insert([1, stored]));
                    Assert.Contains("too small to accept", ex.Message, StringComparison.Ordinal);
                    Assert.Empty(table.Rows());
                }
                else
                {
                    table.Insert([1, stored]);
                    Assert.Equal([1, stored], table.Rows().Single());
                }
            }
            Assert.Equal(counter, TableDefinition.ReadLastAutoNumber(TestDatabases.ReadPage(path, page), format));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Without 0x80 ACE will not evaluate a calculated column, whatever its expression: every read of it fails "This
    // calculated column contains an invalid expression" (measured by clearing the bit on a DAO-created [Qty]*2).
    [Fact]
    public void A_calculated_column_without_the_evaluate_bit_is_refused_on_read()
    {
        string path = TemporaryDatabase.CreatePath("calc-eval-");
        try
        {
            JetDatabase.Create(path);
            int page;
            byte[] raw;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T", [new("Qty", JetDataType.Int32, 4, IsFixedLength: true), ColumnSpec.Calculated("X", JetDataType.Int32, "[Qty]*2")]);
                db.OpenTable("T").Insert([7, null]);
                var t = db.Catalog.FindTable("T")!;
                page = t.DefinitionPage;
                raw = t.FindColumn("X")!.RawDescriptor!;
            }

            byte[] file = File.ReadAllBytes(path);
            JetFormatBase format = JetFormatBase.FromVersionByte(file[0x14]);
            int at = file.AsSpan(page * format.PageSize, format.PageSize).IndexOf(raw);
            Assert.True(at >= 0);
            file[page * format.PageSize + at + format.ColumnExtendedFlagsOffset] &= unchecked((byte)~0x80);
            File.WriteAllBytes(path, file);

            using var read = JetDatabase.Open(path);
            Table table = read.OpenTable("T");
            Assert.True(table.Definition.FindColumn("X")!.IsCalculated);
            var ex = Assert.Throws<LibRed.Storage.Calculated.CalculatedExpressionException>(() => table.Rows().ToList());
            Assert.Contains("invalid expression", ex.Message, StringComparison.Ordinal);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_fresh_column_is_updatable_with_no_other_documented_flags()
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(0x02);
        var col = new ColumnDef { Name = "C", Type = JetDataType.Int32, Index = 0, ColumnId = 0, Length = 4, IsFixedLength = true };

        byte[] d = TableDefinition.BuildColumnDescriptor(col, format);

        Assert.Equal(ColumnFlags.Updatable | ColumnFlags.FixedLength, (ColumnFlags)d[format.ColumnFlagsOffset]);
        Assert.Equal(0, d[format.ColumnExtendedFlagsOffset]);
    }
}