using System.Data.Common;
using LibRed.Engine.Execution;

namespace LibRed.Data;

/// <summary>
/// One column of a result as <see cref="DbColumn"/> — the modern half of <c>GetSchemaTable</c>, from the same
/// description. What the engine could not know is left null rather than guessed: a Jet file has no server,
/// catalog or schema, and nothing in it is a row version or a hidden column.
/// </summary>
internal sealed class LibRedDbColumn : DbColumn
{
    internal LibRedDbColumn(ResultColumn column, int ordinal)
    {
        ColumnName = column.Name;
        ColumnOrdinal = ordinal;
        ColumnSize = column.Size;
        NumericPrecision = column.Precision;
        NumericScale = column.Scale;
        DataType = column.ClrType;
        DataTypeName = column.TypeName;
        AllowDBNull = column.AllowNull;
        BaseColumnName = column.BaseColumnName;
        BaseTableName = column.BaseTableName;
        IsAliased = column.BaseColumnName is not null
            && !string.Equals(column.BaseColumnName, column.Name, StringComparison.OrdinalIgnoreCase);
        IsExpression = column.IsExpression;
        IsAutoIncrement = column.IsAutoIncrement;
        IsKey = column.IsKey;
        IsUnique = column.IsUnique;
        IsLong = column.IsLong;
        IsReadOnly = column.IsReadOnly;
        IsIdentity = column.IsAutoIncrement;
        // Not applicable to a Jet file rather than unknown: no server, catalog or schema, no row versions,
        // and every column a query returns is one the caller asked for.
        BaseServerName = null;
        BaseCatalogName = null;
        BaseSchemaName = null;
        IsHidden = false;
        ProviderType = column.ProviderType;
    }

    /// <summary>The OLE DB type code, the same one the <c>Columns</c> schema collection reports. Named as
    /// <c>GetSchemaTable</c> names it, and reachable through the indexer as well as directly.</summary>
    public int ProviderType { get; }

    public override object? this[string property] =>
        property == nameof(ProviderType) ? ProviderType : base[property];
}
