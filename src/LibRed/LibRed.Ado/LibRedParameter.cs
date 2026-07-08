using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace LibRed.Data;

/// <summary>A command parameter for the LibRed provider.</summary>
public sealed class LibRedParameter : DbParameter
{
    public override DbType DbType { get; set; } = DbType.Object;
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; } = true;
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

    // Mirror OleDbParameter/OdbcParameter: they derive a non-zero Size from the VALUE for a byte (1), and only
    // for a byte — Int16/32/64, Single, Double, Decimal, Guid, DateTime, Boolean all report 0, and a NULL byte
    // reports 0 too (the size comes from the value, not the DbType). EF Core's command logging prints
    // `(Size = N)` from this, so the LibRed conformance baselines (copied from the Jet OLE DB provider) expect
    // `(Size = 1)` only on a non-null byte parameter. An explicitly-set size always wins.
    private int _size;
    public override int Size
    {
        get => _size != 0 ? _size : Value is byte ? 1 : 0;
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

    public override void ResetDbType() => DbType = DbType.Object;
}
