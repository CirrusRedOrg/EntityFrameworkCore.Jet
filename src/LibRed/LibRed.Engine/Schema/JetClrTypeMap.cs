using LibRed.Catalog;

namespace LibRed.Engine.Schema;

/// <summary>Maps Jet/ACE column types to the CLR types the engine exposes.</summary>
public static class JetClrTypeMap
{
    public static Type ToClrType(JetDataType type) => type switch
    {
        JetDataType.Boolean => typeof(bool),
        JetDataType.Byte => typeof(byte),
        JetDataType.Int16 => typeof(short),
        JetDataType.Int32 => typeof(int),
        JetDataType.Int64 => typeof(long),
        JetDataType.Currency => typeof(decimal),
        JetDataType.Single => typeof(float),
        JetDataType.Double => typeof(double),
        JetDataType.DateTime => typeof(DateTime),
        JetDataType.DateTimeExtended => typeof(DateTime),
        JetDataType.Binary or JetDataType.Ole => typeof(byte[]),
        JetDataType.Text or JetDataType.Memo => typeof(string),
        JetDataType.Guid => typeof(Guid),
        JetDataType.FixedPoint => typeof(decimal),
        // The in-row value of a complex (multi-value / attachment) column is its Int32 complex id; the
        // values it stands for are read through ComplexColumn.
        JetDataType.Complex => typeof(int),
        _ => typeof(object),
    };
}