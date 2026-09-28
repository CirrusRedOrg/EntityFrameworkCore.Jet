using System.Text;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// The LvProp property blob must round-trip EVERY property faithfully — including ones LibRed does not model
// (a numeric DecimalPlaces, a designer ValidationRule/Format) — because an ALTER that edits a table's defaults
// or nullability rewrites the whole blob (TableCreator.MutateLvPropForColumn does Read -> Write). Property.RawValue
// and Flags preserve each entry's exact stored value bytes and flag byte, so an unmodelled property survives
// rather than being mangled or silently converted into a definition-protected property.
public class PropertyBlobRoundTripTests
{
    [Fact]
    public void Read_write_preserves_an_unmodelled_numeric_property_and_survives_an_edit()
    {
        var original = new List<PropertyBlob.Property>
        {
            new("Price", PropertyBlob.DefaultValueProperty, "0", JetDataType.Memo),            // modelled
            new("Price", "DecimalPlaces", "", JetDataType.Byte, [2]),                          // unmodelled, numeric
            new("Price", "Format", "Currency", JetDataType.Text, Encoding.Unicode.GetBytes("Currency")), // unmodelled, text
            new("Price", "Caption", "Retail price", JetDataType.Memo,
                Encoding.Unicode.GetBytes("Retail price")) { Flags = 0 },                        // ordinary designer property
            PropertyBlob.Bool("Price", PropertyBlob.RequiredProperty, true),                   // modelled
        };
        byte[] blob = PropertyBlob.Write(original);

        // Read -> Write is a byte-faithful identity (nothing is corrupted or dropped).
        byte[] roundTripped = PropertyBlob.Write([.. PropertyBlob.Read(blob)]);
        Assert.Equal(blob, roundTripped);

        // Editing the modelled DefaultValue (as ALTER COLUMN SET DEFAULT does) must not disturb the others.
        var props = PropertyBlob.Read(blob).ToList();
        props.RemoveAll(p => p.Owner == "Price" && p.Name == PropertyBlob.DefaultValueProperty);
        props.Add(new PropertyBlob.Property("Price", PropertyBlob.DefaultValueProperty, "42"));
        var after = PropertyBlob.Read(PropertyBlob.Write(props));

        PropertyBlob.Property decimalPlaces = Assert.Single(after, p => p.Name == "DecimalPlaces");
        Assert.Equal(JetDataType.Byte, decimalPlaces.Type);
        Assert.Equal(new byte[] { 2 }, decimalPlaces.RawValue);      // the exact numeric byte, not UTF-16 junk

        PropertyBlob.Property format = Assert.Single(after, p => p.Name == "Format");
        Assert.Equal("Currency", format.Value);

        PropertyBlob.Property caption = Assert.Single(after, p => p.Name == "Caption");
        Assert.Equal(0, caption.Flags);
        Assert.Equal(Encoding.Unicode.GetBytes("Retail price"), caption.RawValue);

        Assert.Equal("42", Assert.Single(after, p => p.Name == PropertyBlob.DefaultValueProperty).Value);
    }

    // The table's own properties are owned by the empty string, and adding a CHECK constraint replaces one of
    // them. Doing that by dropping the owner's whole block and writing back only CheckConstraints took every
    // other table-level property with it — ValidationRule above all, which LibRed reads and reports but does
    // not re-emit. No public API authors a designer ValidationRule, so this pins the invariant where the defect
    // was: the read-modify-write over the property list.
    [Fact]
    public void Replacing_the_check_property_keeps_the_tables_other_properties()
    {
        byte[] blob = PropertyBlob.Write(
        [
            new PropertyBlob.Property("", PropertyBlob.ValidationRuleProperty, "[V]>0"),
            new PropertyBlob.Property("", PropertyBlob.ValidationTextProperty, "V must be positive"),
            new PropertyBlob.Property("V", PropertyBlob.DefaultValueProperty, "0"),
        ]);

        // The same shape the CHECK paths use: replace one table-owned property, leave the rest alone.
        var properties = PropertyBlob.Read(blob).ToList();
        properties.RemoveAll(p => p.Owner.Length == 0 && p.Name == PropertyBlob.CheckConstraintsProperty);
        properties.Add(new PropertyBlob.Property(
            "", PropertyBlob.CheckConstraintsProperty, PropertyBlob.WriteCheckList([("CK_T", "V < 100")])));
        byte[] updated = PropertyBlob.Write(properties, blob.AsSpan(0, 4));

        IReadOnlyList<PropertyBlob.Property> reread = PropertyBlob.Read(updated);
        (string? rule, string? text) = PropertyBlob.ReadValidation(reread, "");
        Assert.Equal("[V]>0", rule);
        Assert.Equal("V must be positive", text);
        Assert.Contains(PropertyBlob.ReadCheckConstraints(reread), c => c.Name == "CK_T");
        Assert.Contains(PropertyBlob.Read(updated), p => p.Owner == "V" && p.Value == "0");
        Assert.Equal(blob[..4], updated[..4]);          // and the signature is carried across, not restamped
    }

    // Every type Access stores, as Access stores it (values taken from Access-written files): the stored length
    // sets the width — a Boolean or Int16 can be four bytes, and a four-byte "Int16" is a signed 32-bit value — and
    // a Boolean is true for 01 and for FF alike.
    public static TheoryData<JetDataType, string, object, string> AccessValues() => new()
    {
        { JetDataType.Boolean, "01", true, "1" },
        { JetDataType.Boolean, "FF", true, "1" },
        { JetDataType.Boolean, "00", false, "0" },
        { JetDataType.Boolean, "01000000", true, "1" },
        { JetDataType.Boolean, "00000000", false, "0" },
        { JetDataType.Byte, "FF", (byte)255, "255" },
        { JetDataType.Int16, "0A00", (short)10, "10" },
        { JetDataType.Int16, "FFFFFFFF", -1, "-1" },
        { JetDataType.Int16, "18060000", 1560, "1560" },
        { JetDataType.Int32, "9DFFFFFF", -99, "-99" },
        { JetDataType.Single, "0000C842", 100f, "100" },
        { JetDataType.Text, "4400650073006300", "Desc", "Desc" },
        { JetDataType.Binary, "85276F677377BE40AC361424EE65776D",
            Convert.FromHexString("85276F677377BE40AC361424EE65776D"), "85276F677377BE40AC361424EE65776D" },
    };

    [Theory]
    [MemberData(nameof(AccessValues))]
    public void A_property_of_every_type_reads_as_its_value_and_writes_back_unchanged(
        JetDataType type, string hex, object expected, string text)
    {
        byte[] raw = Convert.FromHexString(hex);
        byte[] blob = PropertyBlob.Write([new PropertyBlob.Property("C", "P", "", type, raw)]);

        PropertyBlob.Property read = Assert.Single(PropertyBlob.Read(blob));
        Assert.Equal(expected, read.TypedValue);
        Assert.Equal(text, read.Value);
        Assert.Equal(blob, PropertyBlob.Write([.. PropertyBlob.Read(blob)]));
    }

    [Fact]
    public void A_datetime_property_reads_as_its_date()
    {
        // MSysDb's local_ReportDate, as Access stored it: an OLE Automation date.
        byte[] raw = Convert.FromHexString("214365C752E7E440");
        PropertyBlob.Property read = Assert.Single(PropertyBlob.Read(
            PropertyBlob.Write([new PropertyBlob.Property("", "local_ReportDate", "", JetDataType.DateTime, raw)])));
        Assert.Equal(DateTime.FromOADate(BitConverter.ToDouble(raw)), read.TypedValue);
    }

    // Of stores each CLR type as Access does; a GUID is 16 binary bytes in Guid.ToByteArray order — testf's
    // Table1 GUID property holds exactly the GUID MSysNameMap records for the table.
    [Fact]
    public void Of_stores_every_clr_type_and_reads_it_back()
    {
        var guid = Guid.Parse("676f2785-7773-40be-ac36-1424ee65776d");
        var when = new DateTime(2024, 4, 25, 18, 16, 0);
        PropertyBlob.Property[] built =
        [
            PropertyBlob.Of("C", "Bool", true),
            PropertyBlob.Of("C", "Byte", (byte)2),
            PropertyBlob.Of("C", "Int16", (short)-1),
            PropertyBlob.Of("C", "Int32", 1560),
            PropertyBlob.Of("C", "Currency", 12.5m),
            PropertyBlob.Of("C", "Single", 100f),
            PropertyBlob.Of("C", "Double", 0.1),
            PropertyBlob.Of("C", "Date", when),
            PropertyBlob.Of("C", "GUID", guid),
            PropertyBlob.Of("C", "Memo", "text"),
            PropertyBlob.Of("C", "Text", "text", JetDataType.Text),
        ];
        var read = PropertyBlob.Read(PropertyBlob.Write(built)).ToDictionary(p => p.Name);

        Assert.Equal(new byte[] { 1 }, read["Bool"].RawValue);
        Assert.Equal(true, read["Bool"].TypedValue);
        Assert.Equal((byte)2, read["Byte"].TypedValue);
        Assert.Equal((short)-1, read["Int16"].TypedValue);
        Assert.Equal(1560, read["Int32"].TypedValue);
        Assert.Equal(12.5m, read["Currency"].TypedValue);
        Assert.Equal("0000C842", Convert.ToHexString(read["Single"].RawValue!));
        Assert.Equal(0.1, read["Double"].TypedValue);
        Assert.Equal(when, read["Date"].TypedValue);
        Assert.Equal(JetDataType.Binary, read["GUID"].Type);
        Assert.Equal("85276F677377BE40AC361424EE65776D", Convert.ToHexString(read["GUID"].RawValue!));
        Assert.Equal((JetDataType.Memo, "text"), (read["Memo"].Type, read["Memo"].Value));
        Assert.Equal((JetDataType.Text, "text"), (read["Text"].Type, read["Text"].Value));
    }

    // A property constructed from its Value text is encoded as its type, not as UTF-16 text whatever the type.
    [Fact]
    public void A_property_built_from_text_is_encoded_as_its_type()
    {
        byte[] blob = PropertyBlob.Write(
        [
            new PropertyBlob.Property("C", "DecimalPlaces", "2", JetDataType.Byte),
            new PropertyBlob.Property("C", "ColumnWidth", "1560", JetDataType.Int16),
            new PropertyBlob.Property("C", "CurrencyLCID", "3081", JetDataType.Int32),
        ]);
        var read = PropertyBlob.Read(blob).ToDictionary(p => p.Name);
        Assert.Equal(new byte[] { 2 }, read["DecimalPlaces"].RawValue);
        Assert.Equal((short)1560, read["ColumnWidth"].TypedValue);
        Assert.Equal(3081, read["CurrencyLCID"].TypedValue);
    }

    // Every property blob in the Access-written fixtures decodes — each value as its type's CLR type — and a
    // rewrite of it is byte for byte the original.
    [Fact]
    public void Every_property_in_the_access_fixtures_decodes_and_writes_back_unchanged()
    {
        var typesSeen = new HashSet<JetDataType>();
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Data"), "*.accdb"))
        {
            JetDatabase db;
            try { db = JetDatabase.Open(path); }
            catch (InvalidOperationException) { continue; }   // the password-encrypted fixture
            using (db)
            {
                var objects = db.OpenTable("MSysObjects");
                int lvProp = objects.Definition.FindColumn("LvProp")!.Index;
                foreach (object?[] row in objects.Rows())
                {
                    if (row[lvProp] is not byte[] blob) continue;
                    IReadOnlyList<PropertyBlob.Property> properties = PropertyBlob.Read(blob);
                    Assert.Equal(blob, PropertyBlob.Write(properties, blob));
                    foreach (PropertyBlob.Property p in properties)
                    {
                        object? value = p.TypedValue;
                        Type expected = p.Type switch
                        {
                            JetDataType.Text or JetDataType.Memo => typeof(string),
                            JetDataType.Boolean => typeof(bool),
                            JetDataType.Single => typeof(float),
                            JetDataType.Binary or JetDataType.Ole => typeof(byte[]),
                            _ => value!.GetType(),
                        };
                        Assert.IsType(expected, value);
                        typesSeen.Add(p.Type);
                    }
                }
            }
        }
        Assert.Superset(
            new HashSet<JetDataType> { JetDataType.Boolean, JetDataType.Byte, JetDataType.Int32, JetDataType.Text, JetDataType.Memo, JetDataType.Binary },
            typesSeen);
    }

    // Access's name pool keeps the names of properties it has since deleted, in the order they were first
    // defined. Rewriting a blob keeps that pool whole — a pool rebuilt from the surviving properties changed every
    // such blob a rewrite touched — and appends only names that are new.
    [Fact]
    public void Rewriting_a_blob_keeps_its_name_pool_whole_and_in_order()
    {
        byte[] original = PropertyBlob.Write(
        [
            new PropertyBlob.Property("", "Description", "gone soon", JetDataType.Text),
            new PropertyBlob.Property("C", PropertyBlob.DefaultValueProperty, "1"),
            PropertyBlob.Bool("C", PropertyBlob.RequiredProperty, true),
        ]);
        var props = PropertyBlob.Read(original).ToList();
        props.RemoveAll(p => p.Name == "Description");      // as Access leaves it: entry gone, name pooled
        byte[] withoutDescription = PropertyBlob.Write(props, original);

        props.Add(PropertyBlob.Of("C", "ColumnWidth", (short)1560));
        byte[] rewritten = PropertyBlob.Write(props, withoutDescription);

        Assert.Equal(
            ["Description", PropertyBlob.DefaultValueProperty, PropertyBlob.RequiredProperty, "ColumnWidth"],
            NamePool(rewritten));
        Assert.Equal(withoutDescription, PropertyBlob.Write([.. PropertyBlob.Read(withoutDescription)], withoutDescription));
    }

    private static List<string> NamePool(byte[] blob)
    {
        // The pool is the first block: [int length][short 0x80][ [short len][UTF-16] … ].
        int length = BitConverter.ToInt32(blob, 4);
        var names = new List<string>();
        for (int q = 10; q < 4 + length;)
        {
            int n = BitConverter.ToUInt16(blob, q);
            names.Add(Encoding.Unicode.GetString(blob, q + 2, n));
            q += 2 + n;
        }
        return names;
    }

    // The flag byte is a bit field, not a DDL boolean: Access writes 0x80 on its own account (every stored query
    // in one of the example databases carries it). And an index's properties sit in a block of type 0x0002 under
    // the index's name, which is usually its column's. Reading such a blob used to throw on the flag, and writing
    // it back re-typed the index's block as the column's, where the column's accessors — and a DROP or RENAME of
    // the column — took it for the column's own.
    [Fact]
    public void A_flag_byte_and_an_index_block_round_trip_and_stay_the_indexs()
    {
        const ushort IndexBlock = 0x0002;
        byte[] blob = PropertyBlob.Write(
        [
            new PropertyBlob.Property("", "Replicable", "T", JetDataType.Text) { Flags = 0x80 },
            new PropertyBlob.Property("CustomerID", PropertyBlob.DefaultValueProperty, "'X'"),
            new PropertyBlob.Property("CustomerID", "Caption", "Customer", JetDataType.Text) { Flags = 0x81 },
            PropertyBlob.Bool("CustomerID", PropertyBlob.RequiredProperty, true) with { Block = IndexBlock },
        ]);

        IReadOnlyList<PropertyBlob.Property> read = PropertyBlob.Read(blob);
        Assert.Equal(0x80, Assert.Single(read, p => p.Name == "Replicable").Flags);
        Assert.Equal(0x81, Assert.Single(read, p => p.Name == "Caption").Flags);
        Assert.Equal(IndexBlock, Assert.Single(read, p => p.Name == PropertyBlob.RequiredProperty).Block);
        Assert.Equal(blob, PropertyBlob.Write([.. read], blob.AsSpan(0, 4)));

        // The index's Required is not the column's.
        Assert.Empty(PropertyBlob.ReadRequiredColumns(read));
        Assert.Equal("'X'", PropertyBlob.ReadColumnDefaults(read)["CustomerID"]);

        // Clearing the column's Required, as ALTER COLUMN … NULL does, leaves the index's alone.
        var edited = read.ToList();
        edited.RemoveAll(p => p.IsOwnedBy("CustomerID") && p.Name == PropertyBlob.RequiredProperty);
        Assert.Equal(blob, PropertyBlob.Write(edited, blob.AsSpan(0, 4)));

        // Dropping or renaming the column moves only the column's block.
        IReadOnlyList<PropertyBlob.Property> dropped = PropertyBlob.Read(PropertyBlob.RemoveOwner(blob, "CustomerID"));
        Assert.DoesNotContain(dropped, p => p.Name == PropertyBlob.DefaultValueProperty);
        Assert.Contains(dropped, p => p.Owner == "CustomerID" && p.Block == IndexBlock);

        IReadOnlyList<PropertyBlob.Property> renamed = PropertyBlob.Read(PropertyBlob.RenameOwner(blob, "CustomerID", "CustID"));
        Assert.Equal("CustID", Assert.Single(renamed, p => p.Name == PropertyBlob.DefaultValueProperty).Owner);
        Assert.Equal("CustomerID", Assert.Single(renamed, p => p.Block == IndexBlock).Owner);
    }
}
