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
    }

    // Complex used to sit with the unknown types as `object`, because its four in-row bytes were passed
    // through undecoded. They are an Int32 complex id — the column carries the auto-number flag and its ids
    // come from the table's 0x1C counter — so the id is what a reader sees, and the values it stands for are
    // reached through ComplexColumn.
    [Fact]
    public void A_complex_column_reads_as_its_int32_id()
    {
        Assert.Equal(typeof(int), JetClrTypeMap.ToClrType(JetDataType.Complex));
    }

    // On a schema surface a complex column is presented exactly as a Memo one, because that is what ACE
    // presents: over OLE DB an attachment column gives the same schema row as a Memo column — type 130,
    // flags 234, max length 0 — and a reader types it System.String at the Memo ceiling. Only DAO names it
    // (type 101, dbAttachment). Two things used to go wrong here, both measured against ACE on complex1:
    // with no Complex case the name fell through to a bare "varchar", a store type carrying no facet where
    // every other text one does; and the counter rule below reported it NOT nullable, where ACE says
    // nullable and DAO says Required=False.
    [Fact]
    public void A_complex_column_is_presented_as_memo_is()
    {
        ColumnDef complex = Column(JetDataType.Complex, autoNumber: true);

        Assert.Equal("longchar", JetStoreType.TypeName(complex));
        Assert.Equal("longchar", JetStoreType.StoreType(complex));
        Assert.Equal(JetStoreType.TypeName(Column(JetDataType.Memo)), JetStoreType.TypeName(complex));
        Assert.Null(JetStoreType.MaxLength(complex));

        // The auto-number flag it carries must not make it non-nullable the way a real counter is. Only the
        // flag is ignored: nullability still comes from the column's own IsNullable, which the catalog sets
        // from the LvProp Required property.
        Assert.True(JetStoreType.IsNullable(complex));
        Assert.False(JetStoreType.IsNullable(Column(JetDataType.Int32, autoNumber: true)));
    }
}
