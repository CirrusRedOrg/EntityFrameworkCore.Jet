using LibRed.Catalog;

namespace LibRed.Engine.Planning;

/// <summary>
/// Exposes the Jet-flavoured <c>INFORMATION_SCHEMA</c> views as engine-native virtual tables backed by the
/// <see cref="JetCatalog"/>. EF's migrations query these as magic dotted-name tables — e.g.
/// <c>SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = '…'</c> — which EFCore.Jet's data layer
/// intercepts and synthesises from ADOX/DAO. LibRed owns its engine, so it answers them directly from the
/// catalog: cross-platform, and composes naturally with WHERE / EXISTS (no command-string interception).
///
/// The seven views and their column sets match EFCore.Jet's <c>JetInformationSchema</c> / <c>SchemaTables</c>
/// exactly, so any query EF builds over them reads the columns it expects.
/// </summary>
public static class InformationSchema
{
    /// <summary>The magic view names, without the <c>INFORMATION_SCHEMA.</c> prefix (case-insensitive).</summary>
    private static readonly HashSet<string> Views = new(StringComparer.OrdinalIgnoreCase)
    {
        "TABLES", "COLUMNS", "INDEXES", "INDEX_COLUMNS", "RELATIONS", "RELATION_COLUMNS", "CHECK_CONSTRAINTS",
    };

    private const string Prefix = "INFORMATION_SCHEMA.";

    /// <summary>True if <paramref name="tableName"/> is one of the magic <c>INFORMATION_SCHEMA.&lt;view&gt;</c> names.</summary>
    public static bool IsInformationSchema(string tableName) =>
        tableName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
        && Views.Contains(tableName[Prefix.Length..]);

    /// <summary>The column names of the given view, in order (for the binder's synthetic schema).</summary>
    public static IReadOnlyList<string> ColumnsOf(string tableName) => View(tableName) switch
    {
        "TABLES" => ["TABLE_NAME", "TABLE_TYPE", "VALIDATION_RULE", "VALIDATION_TEXT"],
        "COLUMNS" => ["TABLE_NAME", "COLUMN_NAME", "ORDINAL_POSITION", "DATA_TYPE", "IS_NULLABLE",
            "CHARACTER_MAXIMUM_LENGTH", "NUMERIC_PRECISION", "NUMERIC_SCALE", "COLUMN_DEFAULT",
            "VALIDATION_RULE", "VALIDATION_TEXT", "IDENTITY_SEED", "IDENTITY_INCREMENT"],
        "INDEXES" => ["TABLE_NAME", "INDEX_NAME", "INDEX_TYPE", "IS_NULLABLE", "IGNORES_NULLS"],
        "INDEX_COLUMNS" => ["TABLE_NAME", "INDEX_NAME", "ORDINAL_POSITION", "COLUMN_NAME", "IS_DESCENDING"],
        "RELATIONS" => ["RELATION_NAME", "REFERENCING_TABLE_NAME", "PRINCIPAL_TABLE_NAME", "RELATION_TYPE",
            "ON_DELETE", "ON_UPDATE", "IS_ENFORCED", "IS_INHERITED"],
        "RELATION_COLUMNS" => ["RELATION_NAME", "REFERENCING_COLUMN_NAME", "PRINCIPAL_COLUMN_NAME", "ORDINAL_POSITION"],
        "CHECK_CONSTRAINTS" => ["TABLE_NAME", "CONSTRAINT_NAME", "CHECK_CLAUSE"],
        _ => throw new InvalidOperationException($"'{tableName}' is not an INFORMATION_SCHEMA view."),
    };

    /// <summary>The rows of the given view, materialised from the catalog (one <c>object?[]</c> per row, columns
    /// in <see cref="ColumnsOf"/> order).</summary>
    public static IReadOnlyList<object?[]> Rows(string tableName, JetCatalog catalog)
    {
        var tables = catalog.UserTables.ToList();
        var rows = new List<object?[]>();
        switch (View(tableName))
        {
            case "TABLES":
                foreach (TableDef t in tables)
                    rows.Add([t.Name, "BASE TABLE", null, null]);
                break;

            case "COLUMNS":
                foreach (TableDef t in tables)
                    foreach (ColumnDef c in t.Columns)
                        rows.Add([t.Name, c.Name, c.Index + 1, c.Type.ToString(), c.IsNullable,
                            IsText(c.Type) ? c.Length : (object?)null, null, null, c.DefaultValue,
                            null, null, c.IsAutoNumber ? c.Seed : (object?)null,
                            c.IsAutoNumber ? c.Increment : (object?)null]);
                break;

            case "INDEXES":
                foreach (TableDef t in tables)
                    foreach (IndexDef ix in t.Indexes)
                        rows.Add([t.Name, ix.Name, IndexType(ix), !ix.IsUnique, ix.IgnoreNulls]);
                break;

            case "INDEX_COLUMNS":
                foreach (TableDef t in tables)
                    foreach (IndexDef ix in t.Indexes)
                        for (int i = 0; i < ix.Columns.Count; i++)
                            rows.Add([t.Name, ix.Name, i + 1, ix.Columns[i].Column.Name, !ix.Columns[i].Ascending]);
                break;

            case "RELATIONS":
                foreach (ForeignKey fk in catalog.Relationships)
                    rows.Add([fk.Name, fk.Table, fk.ReferencedTable, "FOREIGN KEY",
                        RefAction(fk.CascadeDelete, fk.DeleteSetNull), RefAction(fk.CascadeUpdate, false),
                        fk.IsEnforced, fk.IsInherited]);
                break;

            case "RELATION_COLUMNS":
                foreach (ForeignKey fk in catalog.Relationships)
                    for (int i = 0; i < fk.Columns.Count; i++)
                        rows.Add([fk.Name, fk.Columns[i].Column, fk.Columns[i].ReferencedColumn, i + 1]);
                break;

            case "CHECK_CONSTRAINTS":
                foreach (TableDef t in tables)
                    foreach ((string name, string expr) in t.CheckConstraints)
                        rows.Add([t.Name, name, expr]);
                break;
        }
        return rows;
    }

    private static string View(string tableName) => tableName[Prefix.Length..].ToUpperInvariant();

    private static bool IsText(JetDataType t) => t is JetDataType.Text or JetDataType.Memo;

    private static string IndexType(IndexDef ix) => ix.IsPrimaryKey ? "PRIMARY" : ix.IsUnique ? "UNIQUE" : "INDEX";

    private static string RefAction(bool cascade, bool setNull) => cascade ? "CASCADE" : setNull ? "SET NULL" : "NO ACTION";
}
