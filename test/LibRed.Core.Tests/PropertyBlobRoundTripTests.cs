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
