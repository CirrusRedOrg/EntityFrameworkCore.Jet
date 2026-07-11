using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace LibRed.Data;

/// <summary>A command parameter for the LibRed provider.</summary>
public sealed class LibRedParameter : DbParameter
{
    // Like SqlParameter/OleDbParameter/OdbcParameter, an unset DbType is INFERRED from the value's CLR type
    // (not left at Object): a raw `new LibRedParameter { Value = "ALFKI" }` reports DbType.String, so EF Core's
    // command logging prints it faithfully (no bare `(DbType = Object)`). An explicitly-set DbType always wins.
    private DbType? _dbType;
    public override DbType DbType
    {
        get => _dbType ?? InferDbType(Value);
        set => _dbType = value;
    }

    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    // Defaults to false, matching SqlParameter/OleDbParameter/OdbcParameter (whose IsNullable is false until set).
    public override bool IsNullable { get; set; }
    private string _parameterName = string.Empty;
    private string _sourceColumn = string.Empty;

    [AllowNull]
    public override string ParameterName
    {
        get => _parameterName;
        set => _parameterName = value ?? string.Empty;
    }

    [AllowNull]
    public override string SourceColumn
    {
        get => _sourceColumn;
        set => _sourceColumn = value ?? string.Empty;
    }
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }

    // Mirror OleDbParameter/OdbcParameter: they derive a non-zero Size from the VALUE — a variable-length value
    // reports its length (a string's character count, a byte[]'s byte count), a byte reports 1, and the fixed
    // numeric/temporal/Guid/Boolean types all report 0 (the size comes from the value, not the DbType). EF Core's
    // command logging prints `(Size = N)` from this. An explicitly-set size always wins.
    private int _size;
    public override int Size
    {
        get => _size != 0 ? _size
            : Value switch { string s => s.Length, byte[] b => b.Length, byte => 1, _ => 0 };
        set => _size = value;
    }

    // The base DbParameter.Precision/Scale are no-ops (get => 0; set { }) unless a concrete parameter type
    // overrides them - SqlParameter/OdbcParameter/OleDbParameter do, so this must too. Two reasons:
    //  1. JetDecimalTypeMapping.ConfigureParameter's `parameter.Value = decimal.Round(dec, parameter.Scale)`
    //     must read back the scale it just set (else 8.6 rounds to 9 before it ever reaches storage).
    //  2. When a mapping sets NO scale (e.g. the `currency` mapping), OleDbParameter/OdbcParameter DERIVE
    //     precision/scale from the decimal value itself (81.1 -> P=3,S=1; 0.5 -> P=1,S=1; 100.00 -> P=5,S=2),
    //     and the round then keeps the fractional digits. We mirror that: derive from a decimal Value when the
    //     facet wasn't set explicitly (nullable backing, so an explicit 0 still wins and truncates).
    private byte? _precision;
    private byte? _scale;
    public override byte Precision
    {
        get => _precision ?? (Value is decimal d ? DecimalPrecision(d) : (byte)0);
        set => _precision = value;
    }
    public override byte Scale
    {
        get => _scale ?? (Value is decimal d ? DecimalScale(d) : (byte)0);
        set => _scale = value;
    }

    // A decimal carries its own scale; precision is the digit count of its unscaled mantissa (trailing zeros
    // included, leading integer zero excluded — 0.5 -> 1, 100.00 -> 5), matching OLE DB's derivation.
    private static byte DecimalScale(decimal d) => (byte)((decimal.GetBits(d)[3] >> 16) & 0xFF);
    private static byte DecimalPrecision(decimal d)
    {
        int[] bits = decimal.GetBits(d);
        string mantissa = new decimal(bits[0], bits[1], bits[2], false, 0)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        return (byte)Math.Max(1, mantissa.Length);
    }

    public override void ResetDbType() => _dbType = null;

    // The DbType a value maps to when none was set explicitly — the standard ADO.NET/OLE DB value inference,
    // so the parameter looks the same as the other providers'. An unknown/null value falls back to Object.
    private static DbType InferDbType(object? value) => value switch
    {
        null => DbType.Object,
        string => DbType.String,
        byte[] => DbType.Binary,
        bool => DbType.Boolean,
        byte => DbType.Byte,
        sbyte => DbType.SByte,
        short => DbType.Int16,
        ushort => DbType.UInt16,
        int => DbType.Int32,
        uint => DbType.UInt32,
        long => DbType.Int64,
        ulong => DbType.UInt64,
        float => DbType.Single,
        double => DbType.Double,
        decimal => DbType.Decimal,
        DateTime => DbType.DateTime,
        DateTimeOffset => DbType.DateTimeOffset,
        TimeSpan => DbType.Time,
        Guid => DbType.Guid,
        char => DbType.StringFixedLength,
        _ => DbType.Object,
    };
}
