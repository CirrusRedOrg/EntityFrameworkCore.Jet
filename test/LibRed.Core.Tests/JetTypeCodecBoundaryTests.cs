using System.Text;
using LibRed.Catalog;
using LibRed.Storage.Types;
using Xunit;

namespace LibRed.Core.Tests;

public class JetTypeCodecBoundaryTests
{
    private static ColumnDef Column(
        JetDataType type, int length = 0, bool fixedLength = false, byte scale = 0)
        => new()
        {
            Name = "Value",
            Type = type,
            Index = 0,
            Length = length,
            IsFixedLength = fixedLength,
            Scale = scale,
        };

    public static TheoryData<JetDataType, object> FixedValues => new()
    {
        { JetDataType.Byte, byte.MinValue },
        { JetDataType.Byte, byte.MaxValue },
        { JetDataType.Int16, short.MinValue },
        { JetDataType.Int16, short.MaxValue },
        { JetDataType.Int32, int.MinValue },
        { JetDataType.Int32, int.MaxValue },
        { JetDataType.Int64, long.MinValue },
        { JetDataType.Int64, long.MaxValue },
        { JetDataType.Single, float.MinValue },
        { JetDataType.Single, float.MaxValue },
        { JetDataType.Double, double.MinValue },
        { JetDataType.Double, double.MaxValue },
        { JetDataType.Currency, -922337203685477.5808m },
        { JetDataType.Currency, 922337203685477.5807m },
        { JetDataType.Guid, Guid.Empty },
        { JetDataType.Guid, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") },
    };

    [Theory]
    [MemberData(nameof(FixedValues))]
    public void Fixed_width_minimum_and_maximum_values_round_trip(JetDataType type, object value)
    {
        ColumnDef column = Column(type);
        Assert.Equal(value, JetTypeCodec.Decode(column, JetTypeCodec.Encode(column, value)));
    }

    [Theory]
    [InlineData(JetDataType.Byte, 1)]
    [InlineData(JetDataType.Int16, 2)]
    [InlineData(JetDataType.Int32, 4)]
    [InlineData(JetDataType.Int64, 8)]
    [InlineData(JetDataType.Single, 4)]
    [InlineData(JetDataType.Double, 8)]
    [InlineData(JetDataType.DateTime, 8)]
    [InlineData(JetDataType.Currency, 8)]
    [InlineData(JetDataType.Guid, 16)]
    [InlineData(JetDataType.FixedPoint, 17)]
    [InlineData(JetDataType.DateTimeExtended, 42)]
    public void Fixed_width_decode_rejects_short_and_long_payloads(JetDataType type, int length)
    {
        ColumnDef column = Column(type);
        Assert.Throws<InvalidDataException>(() => JetTypeCodec.Decode(column, new byte[length - 1]));
        Assert.Throws<InvalidDataException>(() => JetTypeCodec.Decode(column, new byte[length + 1]));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1.2345", 12345)]
    [InlineData("-1.2345", -12345)]
    [InlineData("7922816251426433759354395.0335", null)]
    public void Fixed_point_scale_round_trips_without_binary_floating_point(
        string text, int? unscaledControl)
    {
        decimal value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        ColumnDef column = Column(JetDataType.FixedPoint, scale: 4);
        byte[] encoded = JetTypeCodec.Encode(column, value);
        Assert.Equal(value, JetTypeCodec.Decode(column, encoded));
        if (unscaledControl is not null)
            Assert.Equal(unscaledControl.Value, decimal.ToInt32(value * 10_000m));
    }

    [Fact]
    public void Fixed_point_rejects_a_nonzero_128_bit_top_word()
    {
        var bytes = new byte[17];
        bytes[1] = 1;
        Assert.Throws<OverflowException>(() => JetTypeCodec.Decode(Column(JetDataType.FixedPoint), bytes));
    }

    [Fact]
    public void DateTimeExtended_decodes_day_and_tick_components_at_100ns_precision()
    {
        var expected = new DateTime(2021, 3, 4, 9, 8, 7).AddTicks(1_234_567);
        long day = expected.Ticks / TimeSpan.TicksPerDay;
        long time = expected.Ticks % TimeSpan.TicksPerDay;
        byte[] encoded = Encoding.ASCII.GetBytes($"{day:D19}:{time:D19}:07");

        Assert.Equal(42, encoded.Length);
        Assert.Equal(expected, JetTypeCodec.Decode(Column(JetDataType.DateTimeExtended), encoded));
    }

    [Fact]
    public void Compressed_and_uncompressed_empty_text_are_distinct_encodings_of_the_same_value()
    {
        Assert.Equal("", JetTypeCodec.DecodeText([]));
        Assert.Equal("", JetTypeCodec.DecodeText([0xFF, 0xFE]));
        Assert.Equal("ABC", JetTypeCodec.DecodeText([0xFF, 0xFE, 0x41, 0x42, 0x43]));
        Assert.Equal("Å", JetTypeCodec.DecodeText(Encoding.Unicode.GetBytes("Å")));
    }

    [Fact]
    public void Fixed_text_and_binary_are_padded_or_truncated_to_the_declared_width()
    {
        ColumnDef text = Column(JetDataType.Text, length: 6, fixedLength: true);
        Assert.Equal("A  ", JetTypeCodec.Decode(text, JetTypeCodec.Encode(text, "A")));
        Assert.Equal("ABC", JetTypeCodec.Decode(text, JetTypeCodec.Encode(text, "ABCD")));

        ColumnDef binary = Column(JetDataType.Binary, length: 3, fixedLength: true);
        Assert.Equal(new byte[] { 1, 0, 0 }, JetTypeCodec.Encode(binary, new byte[] { 1 }));
        Assert.Equal(new byte[] { 1, 2, 3 }, JetTypeCodec.Encode(binary, new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void Unsupported_encoding_reports_the_column_type()
    {
        var error = Assert.Throws<NotSupportedException>(() =>
            JetTypeCodec.Encode(Column(JetDataType.Complex), new object()));
        Assert.Contains(nameof(JetDataType.Complex), error.Message);
    }
}
