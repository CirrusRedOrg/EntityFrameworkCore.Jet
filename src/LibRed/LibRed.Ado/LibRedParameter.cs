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

    // The base DbParameter.Precision/Scale are no-ops (get => 0; set { }) unless a concrete
    // parameter type overrides them - SqlParameter/OdbcParameter/OleDbParameter do, so this must
    // too. Without this override, JetDecimalTypeMapping.ConfigureParameter's
    // `parameter.Value = decimal.Round(dec, parameter.Scale)` silently reads back Scale=0 (the
    // no-op default) instead of the scale it just set, rounding e.g. 8.6 to 9 before the value
    // ever reaches storage.
    public override byte Precision { get; set; }
    public override byte Scale { get; set; }

    public override void ResetDbType() => DbType = DbType.Object;
}
