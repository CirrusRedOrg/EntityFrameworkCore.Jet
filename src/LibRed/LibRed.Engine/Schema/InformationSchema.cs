using LibRed.Catalog;

namespace LibRed.Engine.Schema;

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
        // All views cover the same set — user tables PLUS the '#Dual' internal helper (INFORMATION_SCHEMA
        // surfaces it the way EFCore.Jet's AdoxSchema does), but not MSys*/temp system tables — matching how
        // EFCore.Jet applies its MSys filter uniformly across every schema query.
        var tables = catalog.Tables.Where(t => !t.IsSystem || t.Name.StartsWith('#')).ToList();
        var rows = new List<object?[]>();
        switch (View(tableName))
        {
            case "TABLES":
                // Same table set as every other view — user tables plus the '#Dual' internal helper, no
                // MSys*/temp system tables — each classified by TABLE_TYPE. Consumers filter by type anyway
                // (e.g. the creator's "has tables" check is TABLE_TYPE IN ('BASE TABLE','VIEW')).
                foreach (TableDef t in tables)
                    rows.Add([t.Name, TableType(t), t.ValidationRule, t.ValidationText]);
                break;

            case "COLUMNS":
                foreach (TableDef t in tables)
                    foreach (ColumnDef c in t.Columns)
                        // DATA_TYPE / IS_NULLABLE / CHARACTER_MAXIMUM_LENGTH come from the shared JetStoreType so
                        // they always agree with the scaffolder. Precision/Scale are cast to int (they are byte on
                        // ColumnDef) so a consumer's GetInt32 can read them.
                        // ORDINAL_POSITION is 0-based, matching EFCore.Jet's AdoxSchema.GetColumns (ADOX reports
                        // the OpenSchema position minus one; consumers only ORDER BY it).
                        rows.Add([t.Name, c.Name, c.Index, JetStoreType.TypeName(c), JetStoreType.IsNullable(c),
                            JetStoreType.MaxLength(c), c.Type == JetDataType.FixedPoint ? (int)c.Precision : (object?)null,
                            c.Type == JetDataType.FixedPoint ? (int)c.Scale : (object?)null, c.DefaultValue,
                            c.ValidationRule, c.ValidationText, c.IsAutoNumber ? c.Seed : (object?)null,
                            c.IsAutoNumber ? c.Increment : (object?)null]);
                break;

            case "INDEXES":
                foreach (TableDef t in tables)
                    foreach (IndexDef ix in t.Indexes)
                        // IS_NULLABLE / IGNORES_NULLS mirror AdoxSchema.GetIndexes' AllowNullsEnum derivation,
                        // NOT uniqueness: an index is non-nullable only when it DISALLOW NULLs (Required, flag
                        // 0x08 = adIndexNullsDisallow); IGNORES_NULLS is the Ignore-nulls state (flag 0x02) and,
                        // as ADOX gates it, is only meaningful when the index is otherwise nullable.
                        rows.Add([t.Name, ix.Name, IndexType(ix), !ix.Required, !ix.Required && ix.IgnoreNulls]);
                break;

            case "INDEX_COLUMNS":
                foreach (TableDef t in tables)
                    foreach (IndexDef ix in t.Indexes)
                        // ORDINAL_POSITION is 0-based here too, matching AdoxSchema.GetIndexColumns (which uses the
                        // loop index k directly). RELATION_COLUMNS below stays 1-based, as ADOX does there.
                        for (int i = 0; i < ix.Columns.Count; i++)
                            rows.Add([t.Name, ix.Name, i, ix.Columns[i].Column.Name, !ix.Columns[i].Ascending]);
                break;

            case "RELATIONS":
                foreach (ForeignKey fk in catalog.Relationships)
                    // RELATION_TYPE: EFCore.Jet's live provider is PreciseSchema, which overrides ADOX's value
                    // with DAO's GetRelationTypes — "ONE" for a one-to-one relationship, else "MANY". This is
                    // NOT a grbit flag (MSysRelationships.grbit only carries enforce/cascade/set-null); Access
                    // encodes one-to-one by making the child-side FK backing index UNIQUE (a 1:many child index
                    // is non-unique). So derive it from that index's uniqueness, which LibRed already models.
                    rows.Add([fk.Name, fk.Table, fk.ReferencedTable, RelationType(fk, catalog),
                        RefAction(fk.CascadeDelete, fk.DeleteSetNull), RefAction(fk.CascadeUpdate, fk.UpdateSetNull),
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

    // Classify TABLE_TYPE as EFCore.Jet's AdoxSchema / SchemaProvider does: a '#'-prefixed helper (#Dual) is an
    // INTERNAL TABLE, an MSys* table is a SYSTEM TABLE, everything else is a user BASE TABLE.
    private static string TableType(TableDef t) =>
        t.Name.StartsWith('#') ? "INTERNAL TABLE"
        : t.Name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) ? "SYSTEM TABLE"
        : "BASE TABLE";

    private static string IndexType(IndexDef ix) => ix.IsPrimaryKey ? "PRIMARY" : ix.IsUnique ? "UNIQUE" : "INDEX";

    private static string RefAction(bool cascade, bool setNull) => cascade ? "CASCADE" : setNull ? "SET NULL" : "NO ACTION";

    /// <summary>RELATION_TYPE — "ONE" for a one-to-one relationship, else "MANY", as DAO reports it. A
    /// relationship is one-to-one when the child (referencing) table's backing index over the FK columns is
    /// UNIQUE; a one-to-many child index is non-unique (see system-catalog.md). Defaults to "MANY" when no
    /// matching child index is found (e.g. an unenforced FK that Access left unindexed).</summary>
    private static string RelationType(ForeignKey fk, JetCatalog catalog)
    {
        TableDef? child = catalog.Tables.FirstOrDefault(t => t.Name.Equals(fk.Table, StringComparison.OrdinalIgnoreCase));
        var fkColumns = fk.Columns.Select(c => c.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IndexDef? backing = child?.Indexes.FirstOrDefault(ix =>
            ix.Columns.Count == fkColumns.Count
            && ix.Columns.All(c => fkColumns.Contains(c.Column.Name)));
        return backing?.IsUnique == true ? "ONE" : "MANY";
    }
}
