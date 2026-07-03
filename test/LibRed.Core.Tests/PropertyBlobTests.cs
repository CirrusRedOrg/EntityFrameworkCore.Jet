using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

public class PropertyBlobTests
{
    // The exact LvProp blob ACE writes for CREATE TABLE T (Id INTEGER, Age INTEGER DEFAULT 42,
    // Nm TEXT(50) DEFAULT 'hi') — captured by dumping the MSysObjects row.
    private static readonly byte[] AceBlob =
    [
        0x4D, 0x52, 0x32, 0x00, 0x20, 0x00, 0x00, 0x00, 0x80, 0x00, 0x18, 0x00, 0x44, 0x00, 0x65, 0x00,
        0x66, 0x00, 0x61, 0x00, 0x75, 0x00, 0x6C, 0x00, 0x74, 0x00, 0x56, 0x00, 0x61, 0x00, 0x6C, 0x00,
        0x75, 0x00, 0x65, 0x00, 0x1E, 0x00, 0x00, 0x00, 0x01, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x06, 0x00,
        0x41, 0x00, 0x67, 0x00, 0x65, 0x00, 0x0C, 0x00, 0x01, 0x0C, 0x00, 0x00, 0x04, 0x00, 0x34, 0x00,
        0x32, 0x00, 0x20, 0x00, 0x00, 0x00, 0x01, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x04, 0x00, 0x4E, 0x00,
        0x6D, 0x00, 0x10, 0x00, 0x01, 0x0C, 0x00, 0x00, 0x08, 0x00, 0x27, 0x00, 0x68, 0x00, 0x69, 0x00,
        0x27, 0x00,
    ];

    [Fact]
    public void Write_reproduces_the_ace_blob_byte_for_byte()
    {
        byte[] blob = PropertyBlob.Write(
        [
            new("Age", PropertyBlob.DefaultValueProperty, "42"),
            new("Nm", PropertyBlob.DefaultValueProperty, "'hi'"),
        ]);
        Assert.Equal(AceBlob, blob);
    }

    [Fact]
    public void ReadColumnDefaults_parses_the_ace_blob()
    {
        var defaults = PropertyBlob.ReadColumnDefaults(AceBlob);
        Assert.Equal("42", defaults["Age"]);
        Assert.Equal("'hi'", defaults["Nm"]);
        Assert.False(defaults.ContainsKey("Id"));
    }

    [Fact]
    public void ReadColumnDefaults_round_trips_written_blob()
    {
        byte[] blob = PropertyBlob.Write(
        [
            new("A", PropertyBlob.DefaultValueProperty, "0"),
            new("B", PropertyBlob.DefaultValueProperty, "-1"),
        ]);
        var defaults = PropertyBlob.ReadColumnDefaults(blob);
        Assert.Equal("0", defaults["A"]);
        Assert.Equal("-1", defaults["B"]);
    }
}
