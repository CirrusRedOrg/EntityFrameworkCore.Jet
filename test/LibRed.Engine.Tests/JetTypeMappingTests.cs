using LibRed.Catalog;
using LibRed.Engine.Schema;
using Xunit;

namespace LibRed.Engine.Tests;

public class JetTypeMappingTests
{
    public static TheoryData<JetDataType, string, Type> ScalarTypes => new()
    {
        { JetDataType.Boolean, "bit", typeof(bool) },
        { JetDataType.Byte, "byte", typeof(byte) },
        { JetDataType.Int16, "smallint", typeof(short) },
        { JetDataType.Int32, "integer", typeof(int) },
        { JetDataType.Int64, "bigint", typeof(long) },
        { JetDataType.Single, "single", typeof(float) },
        { JetDataType.Double, "double", typeof(double) },
        { JetDataType.Currency, "currency", typeof(decimal) },
        { JetDataType.DateTime, "datetime", typeof(DateTime) },
        { JetDataType.DateTimeExtended, "datetime2", typeof(DateTime) },
        { JetDataType.Guid, "guid", typeof(Guid) },
        { JetDataType.Memo, "longchar", typeof(string) },
        { JetDataType.Ole, "longbinary", typeof(byte[]) },
    };

    private static ColumnDef Column(
        JetDataType type, int length = 0, bool fixedLength = false, bool autoNumber = false,
        byte precision = 0, byte scale = 0)
        => new()
        {
            Name = "Value",
            Type = type,
            Length = length,
            IsFixedLength = fixedLength,
            IsAutoNumber = autoNumber,
            Precision = precision,
            Scale = scale,
        };

    [Theory]
    [MemberData(nameof(ScalarTypes))]
    public void Scalar_type_names_store_types_and_clr_types_agree(
        JetDataType type, string storeType, Type clrType)
    {
        ColumnDef column = Column(type);
        Assert.Equal(storeType, JetStoreType.TypeName(column));
        Assert.Equal(storeType, JetStoreType.StoreType(column));
        Assert.Null(JetStoreType.MaxLength(column));
        Assert.Equal(clrType, JetClrTypeMap.ToClrType(type));
    }

    [Theory]
    [InlineData(false, "varchar", "varchar(20)")]
    [InlineData(true, "char", "char(20)")]
    public void Text_length_is_reported_in_characters(bool fixedLength, string name, string storeType)
    {
        ColumnDef column = Column(JetDataType.Text, length: 40, fixedLength: fixedLength);
        Assert.Equal(name, JetStoreType.TypeName(column));
        Assert.Equal(20, JetStoreType.MaxLength(column));
        Assert.Equal(storeType, JetStoreType.StoreType(column));
        Assert.Equal(typeof(string), JetClrTypeMap.ToClrType(column.Type));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(255, 255)]
    public void Binary_length_is_in_bytes_and_fixed_binary_still_presents_as_varbinary(int length, int expected)
    {
        ColumnDef column = Column(JetDataType.Binary, length, fixedLength: true);
        Assert.Equal("varbinary", JetStoreType.TypeName(column));
        Assert.Equal(expected, JetStoreType.MaxLength(column));
        Assert.Equal($"varbinary({expected})", JetStoreType.StoreType(column));
        Assert.Equal(typeof(byte[]), JetClrTypeMap.ToClrType(column.Type));
    }

    [Fact]
    public void Decimal_and_counter_facets_are_formatted_canonically()
    {
        Assert.Equal("decimal(18,4)",
            JetStoreType.StoreType(Column(JetDataType.FixedPoint, precision: 18, scale: 4)));

        ColumnDef counter = Column(JetDataType.Int32, autoNumber: true);
        Assert.Equal("counter", JetStoreType.TypeName(counter));
        Assert.Equal("counter", JetStoreType.StoreType(counter));
        Assert.False(JetStoreType.IsNullable(counter));
    }

    [Fact]
    public void Nullability_and_unknown_types_have_defined_fallbacks()
    {
        Assert.True(JetStoreType.IsNullable(Column(JetDataType.Text)));

        var unknown = (JetDataType)byte.MaxValue;
        Assert.Equal("varchar", JetStoreType.TypeName(Column(unknown)));
        Assert.Equal("varchar", JetStoreType.StoreType(Column(unknown)));
        Assert.Equal(typeof(object), JetClrTypeMap.ToClrType(unknown));
        Assert.Equal(typeof(object), JetClrTypeMap.ToClrType(JetDataType.Complex));
    }
}
