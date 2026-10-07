using System.Text;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage.Types;
using Xunit;

namespace LibRed.Core.Tests;

public class JetTypeCodecBoundaryTests
{
    private static readonly JetFormatBase Format = JetFormatBase.FromVersionByte(0x02);

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
        Assert.Equal(value, JetTypeCodec.Decode(column, JetTypeCodec.Encode(column, value, Format)));
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
        byte[] encoded = JetTypeCodec.Encode(column, value, Format);
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

    // Plain UTF-16 is copied rather than decoded when that cannot differ from the decoder, and compressed text
    // with no switch byte is read as one Latin-1 run. Both must give exactly what the general paths give.
    [Theory]
    [InlineData("")]
    [InlineData("plain ascii")]
    [InlineData("café ñ ü")]
    [InlineData("中文 text")]
    [InlineData("pair \U0001F600 kept")]
    public void Utf16_text_decodes_as_the_encoder_does(string text)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(text);
        Assert.Equal(Encoding.Unicode.GetString(bytes), JetTypeCodec.DecodeText(bytes));
    }

    [Theory]
    [InlineData(new byte[] { 0x41, 0x00, 0x00, 0xD8 })]              // a lone high surrogate
    [InlineData(new byte[] { 0x41, 0x00, 0x00, 0xDC, 0x42, 0x00 })]  // a lone low surrogate
    [InlineData(new byte[] { 0x41, 0x00, 0x42 })]                    // an odd trailing byte
    public void Malformed_utf16_is_still_handled_as_the_encoder_handles_it(byte[] bytes)
        => Assert.Equal(Encoding.Unicode.GetString(bytes), JetTypeCodec.DecodeText(bytes));

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x63, 0x61, 0x66, 0xE9 }, "café")]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x80, 0x9F, 0xFF }, "\u0080\u009Fÿ")]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x63, 0x61, 0x66, 0xE9, 0x00, 0x2D, 0x4E }, "café中")]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x61, 0x00, 0x2D, 0x4E, 0x00, 0x62 }, "a中b")]
    public void Compressed_text_decodes_in_both_modes(byte[] bytes, string expected)
        => Assert.Equal(expected, JetTypeCodec.DecodeText(bytes));

    // Padding short values is right — ACE stores fixed text space-padded to the full width. Over-long is NOT
    // truncation: this asserted that it was, which is where the bug lived. ACE refuses an over-long value on a
    // fixed column exactly as it does on a variable one (measured in FixedWidthOverflowAccessTests), so the
    // padding must not be allowed to swallow it.
    [Fact]
    public void Fixed_text_and_binary_are_padded_to_the_declared_width()
    {
        ColumnDef text = Column(JetDataType.Text, length: 6, fixedLength: true);
        Assert.Equal("A  ", JetTypeCodec.Decode(text, JetTypeCodec.Encode(text, "A", Format)));

        ColumnDef binary = Column(JetDataType.Binary, length: 3, fixedLength: true);
        Assert.Equal(new byte[] { 1, 0, 0 }, JetTypeCodec.Encode(binary, new byte[] { 1 }, Format));
    }

    [Fact]
    public void An_over_long_value_is_refused_on_a_fixed_column_too()
    {
        ColumnDef text = Column(JetDataType.Text, length: 6, fixedLength: true);
        Assert.Contains("too small to accept",
            Assert.Throws<InvalidOperationException>(() => JetTypeCodec.Encode(text, "ABCD", Format)).Message);

        ColumnDef binary = Column(JetDataType.Binary, length: 3, fixedLength: true);
        Assert.Contains("too small to accept",
            Assert.Throws<InvalidOperationException>(() => JetTypeCodec.Encode(binary, new byte[] { 1, 2, 3, 4 }, Format)).Message);
    }

    // DATETIME2 is 42 ASCII bytes, "<day>:<time>:<precision>". The width was checked and the CONTENT was not,
    // so a damaged value escaped as ArgumentOutOfRangeException (s[..-1] on a missing colon), FormatException
    // (non-digits) or an overflow — never the InvalidDataException every other type reports.
    [Theory]
    [InlineData("no colons here at all, but exactly 42 bytes!")]
    [InlineData("12345:only one colon and padding to 42.....")]
    [InlineData("abcdefghijklmnopqrs:tuvwxyzabcdefghijklmn:07")]
    [InlineData("9999999999999999999:0000000000000000000:07")]
    public void A_damaged_extended_datetime_reports_corruption(string text)
    {
        byte[] value = Encoding.ASCII.GetBytes(text.PadRight(42)[..42]);
        Assert.ThrowsAny<InvalidDataException>(() =>
            JetTypeCodec.Decode(Column(JetDataType.DateTimeExtended, length: 42, fixedLength: true), value));
    }

    // Complex used to stand in for "unsupported" here. It no longer is — its four bytes are an Int32 complex
    // id and LibRed both reads and writes them — so the example is now one of the genuinely unmodelled codes.
    [Fact]
    public void Unsupported_encoding_reports_the_column_type()
    {
        var error = Assert.Throws<NotSupportedException>(() =>
            JetTypeCodec.Encode(Column(JetDataType.Unknown0D), new object(), Format));
        Assert.Contains(nameof(JetDataType.Unknown0D), error.Message);
    }

    [Fact]
    public void A_complex_id_round_trips_as_an_int32()
    {
        ColumnDef column = Column(JetDataType.Complex, length: 4, fixedLength: true);
        Assert.Equal(new byte[] { 0x2A, 0, 0, 0 }, JetTypeCodec.Encode(column, 42, Format));
        Assert.Equal(42, JetTypeCodec.Decode(column, JetDataType.Complex, new byte[] { 0x2A, 0, 0, 0 }));
    }
}