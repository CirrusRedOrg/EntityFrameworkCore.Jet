using System.Buffers.Binary;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

public class PropertyBlobCorruptionTests
{
    [Theory]
    [InlineData("short-block")]
    [InlineData("block-past-end")]
    [InlineData("odd-name-length")]
    [InlineData("short-owner-record")]
    [InlineData("owner-name-past-record")]
    [InlineData("short-entry")]
    [InlineData("value-past-entry")]
    [InlineData("bad-name-index")]
    [InlineData("trailing-byte")]
    public void Malformed_property_blob_is_rejected(string corruption)
    {
        byte[] blob = PropertyBlob.Write([new("A", "N", "V")]);
        int ownerBlock = 4 + BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4, 4));
        int ownerBody = ownerBlock + 6;
        int entry = ownerBody + BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(ownerBody, 2));

        switch (corruption)
        {
            case "short-block":
                BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4, 4), 5);
                break;
            case "block-past-end":
                BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4, 4), blob.Length);
                break;
            case "odd-name-length":
                BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(10, 2), 1);
                break;
            case "short-owner-record":
                BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(ownerBody, 2), 5);
                break;
            case "owner-name-past-record":
                BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(ownerBody + 4, 2), 100);
                break;
            case "short-entry":
                BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(entry, 2), 7);
                break;
            case "value-past-entry":
                BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(entry + 6, 2), 100);
                break;
            case "bad-name-index":
                BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(entry + 4, 2), 1);
                break;
            case "trailing-byte":
                blob = [.. blob, 0xFF];
                break;
        }

        Assert.Throws<InvalidDataException>(() => PropertyBlob.Read(blob));
    }

    [Fact]
    public void Serializer_rejects_values_that_do_not_fit_16_bit_fields()
    {
        Assert.Throws<ArgumentException>(() =>
            PropertyBlob.Write([new("A", new string('n', 32768), "V")]));
        Assert.Throws<ArgumentException>(() =>
            PropertyBlob.Write([new(new string('o', 32768), "N", "V")]));
        Assert.Throws<ArgumentException>(() =>
            PropertyBlob.Write([new("A", "N", "", JetDataType.Ole, new byte[65528])]));
    }

    [Fact]
    public void Checked_parser_preserves_valid_add_remove_and_round_trip_behavior()
    {
        byte[] blob = PropertyBlob.Write([new("A", "N", "V")]);
        blob = PropertyBlob.AddColumnProperties(blob, "B", [PropertyBlob.Bool("B", PropertyBlob.RequiredProperty, true)]);
        Assert.Equal(2, PropertyBlob.Read(blob).Count);

        blob = PropertyBlob.RemoveOwner(blob, "A");
        PropertyBlob.Property property = Assert.Single(PropertyBlob.Read(blob));
        Assert.Equal("B", property.Owner);
        Assert.Equal(PropertyBlob.RequiredProperty, property.Name);
        Assert.Equal("1", property.Value);
    }
}
