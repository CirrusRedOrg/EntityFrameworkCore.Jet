using System.Text;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// The LvProp property blob must round-trip EVERY property faithfully — including ones LibRed does not model
// (a numeric DecimalPlaces, a designer ValidationRule/Format) — because an ALTER that edits a table's defaults
// or nullability rewrites the whole blob (TableCreator.MutateLvPropForColumn does Read -> Write). Property.RawValue
// and IsDdl preserve each entry's exact stored value bytes and DDL classification, so an unmodelled property
// survives rather than being mangled or silently converted into a definition-protected property.
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
                Encoding.Unicode.GetBytes("Retail price")) { IsDdl = false },                    // ordinary designer property
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
        Assert.False(caption.IsDdl);
        Assert.Equal(Encoding.Unicode.GetBytes("Retail price"), caption.RawValue);

        Assert.Equal("42", Assert.Single(after, p => p.Name == PropertyBlob.DefaultValueProperty).Value);
    }
}
