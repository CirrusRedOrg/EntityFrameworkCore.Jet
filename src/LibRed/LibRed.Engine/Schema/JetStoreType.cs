using LibRed.Catalog;

namespace LibRed.Engine.Schema;

/// <summary>
/// The single source for a column's SQL store-type presentation, shared by the two consumers that must agree:
/// LibRed's <see cref="InformationSchema"/> views (DATA_TYPE / IS_NULLABLE / CHARACTER_MAXIMUM_LENGTH) and the
/// database-first scaffolder (<c>LibRedDatabaseModelFactory</c>, which composes <c>DatabaseColumn.StoreType</c>).
/// Duplicating these derivations is exactly how the two diverged before — the store-type name map and the
/// "an AutoNumber/counter is never nullable" rule live here once. Mirrors EFCore.Jet's
/// <c>AdoxSchema.GetDataTypeString</c> (bare name + separate facet columns).
/// </summary>
public static class JetStoreType
{
    /// <summary>An AutoNumber (counter) column is never nullable — its non-null behaviour comes from the
    /// AutoNumber flag, not the LvProp <c>Required</c> property (verified; matches Access/DAO).</summary>
    public static bool IsNullable(ColumnDef column) => !column.IsAutoNumber && column.IsNullable;

    /// <summary>The bare type name (no facets) — <c>INFORMATION_SCHEMA.COLUMNS.DATA_TYPE</c>, with length /
    /// precision / scale reported in their own columns. Mirrors <c>AdoxSchema.GetDataTypeString</c>.</summary>
    public static string TypeName(ColumnDef column) => column.Type switch
    {
        JetDataType.Boolean => "bit",
        JetDataType.Byte => "byte",
        JetDataType.Int16 => "smallint",
        JetDataType.Int32 => column.IsAutoNumber ? "counter" : "integer",
        JetDataType.Int64 => "bigint",
        JetDataType.Single => "single",
        JetDataType.Double => "double",
        JetDataType.Currency => "currency",
        JetDataType.DateTime => "datetime",
        JetDataType.DateTimeExtended => "datetime2",
        JetDataType.Guid => "guid",
        JetDataType.FixedPoint => "decimal",
        // Text keeps its fixed/variable distinction (adChar/adVarChar → char/varchar). Binary does NOT: ADOX
        // reports every binary column as adVarBinary (it never surfaces fixed adBinary), so a fixed binary column
        // collapses to varbinary — match that, or INFORMATION_SCHEMA/scaffolding disagree with EFCore.Jet's output.
        JetDataType.Text => column.IsFixedLength ? "char" : "varchar",
        JetDataType.Binary => "varbinary",
        JetDataType.Memo => "longchar",
        JetDataType.Ole => "longbinary",
        _ => "varchar",
    };

    /// <summary>Length facet: characters for text (on-disk bytes are 2 per char), bytes for binary; null for
    /// everything else. This is <c>CHARACTER_MAXIMUM_LENGTH</c>.</summary>
    public static int? MaxLength(ColumnDef column) => column.Type switch
    {
        JetDataType.Text => Math.Max(1, column.Length / 2),
        JetDataType.Binary => Math.Max(1, column.Length),
        _ => null,
    };

    /// <summary>The full store type EF's type mapping consumes — bare name plus its facet suffix, e.g.
    /// <c>varchar(255)</c>, <c>decimal(18, 2)</c>, <c>counter</c>. Built from <see cref="TypeName"/> so it can
    /// never disagree with the INFORMATION_SCHEMA presentation.</summary>
    public static string StoreType(ColumnDef column)
    {
        string name = TypeName(column);
        return column.Type switch
        {
            JetDataType.FixedPoint => $"{name}({column.Precision}, {column.Scale})",
            JetDataType.Text or JetDataType.Binary => $"{name}({MaxLength(column)})",
            _ => name,
        };
    }
}
