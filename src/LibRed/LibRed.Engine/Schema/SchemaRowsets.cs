using LibRed.Catalog;

namespace LibRed.Engine.Schema;

/// <summary>
/// The catalog as the ADO.NET metadata collections ACE's OLE DB provider serves from <c>GetSchema</c>, in the
/// same shapes: the same collection names, the same columns in the same order, and the same values — measured
/// against ACE 16 — so code written for that provider reads what it expects from LibRed.
///
/// <para><see cref="InformationSchema"/> is a different surface: the Jet-dialect <c>INFORMATION_SCHEMA.*</c>
/// views EF's migrations query as SQL, with EFCore.Jet's ADOX-derived column sets. Both read the same
/// <see cref="JetCatalog"/>. LibRed.Ado shapes these rows into <c>DataTable</c>s and applies each collection's
/// restrictions.</para>
/// </summary>
public static class SchemaRowsets
{
    /// <summary>The collections served here. The first five are the ones ACE advertises through
    /// <c>GetSchema</c>, spelled as it spells them; the rest are its relational rowsets, which ACE serves only
    /// through <c>GetOleDbSchemaTable</c> — LibRed has no equivalent of that call, so they are named
    /// collections here, in ACE's shapes.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "Tables", "Columns", "Indexes", "Views", "Procedures",
        "ForeignKeys", "PrimaryKeys", "TableConstraints", "KeyColumnUsage", "ConstraintColumnUsage",
        "ReferentialConstraints", "CheckConstraints", "Statistics",
        "ProcedureParameters", "ViewColumns",
    ];

    /// <summary>True if <paramref name="collection"/> is one of <see cref="Names"/> (case-insensitively, as
    /// ADO.NET matches collection names).</summary>
    public static bool IsKnown(string collection) =>
        Names.Any(n => n.Equals(collection, StringComparison.OrdinalIgnoreCase));

    /// <summary>The column names of a collection, in order.</summary>
    public static IReadOnlyList<string> ColumnsOf(string collection) => Canonical(collection) switch
    {
        "Tables" => ["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "TABLE_TYPE", "TABLE_GUID", "DESCRIPTION",
            "TABLE_PROPID", "DATE_CREATED", "DATE_MODIFIED"],
        "Columns" => ["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "COLUMN_GUID", "COLUMN_PROPID",
            "ORDINAL_POSITION", "COLUMN_HASDEFAULT", "COLUMN_DEFAULT", "COLUMN_FLAGS", "IS_NULLABLE", "DATA_TYPE",
            "TYPE_GUID", "CHARACTER_MAXIMUM_LENGTH", "CHARACTER_OCTET_LENGTH", "NUMERIC_PRECISION", "NUMERIC_SCALE",
            "DATETIME_PRECISION", "CHARACTER_SET_CATALOG", "CHARACTER_SET_SCHEMA", "CHARACTER_SET_NAME",
            "COLLATION_CATALOG", "COLLATION_SCHEMA", "COLLATION_NAME", "DOMAIN_CATALOG", "DOMAIN_SCHEMA",
            "DOMAIN_NAME", "DESCRIPTION",
            // Past ACE's own shape. A provider may add columns to a collection (the SQL Server OLE DB provider
            // adds IS_COMPUTED to this one, spelled exactly so). The rest are things no ACE rowset carries: the
            // expression behind a calculated column (ACE has it only in DAO), a readable type name — this
            // rowset otherwise names types only by code — and the AutoNumber flag with its parameters, which
            // OLE DB omits entirely and ODBC smuggles into the type name.
            "IS_COMPUTED", "COLUMN_EXPRESSION", "TYPE_NAME", "IS_AUTOINCREMENT", "SEED", "INCREMENT"],
        "Indexes" => ["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "INDEX_CATALOG", "INDEX_SCHEMA", "INDEX_NAME",
            "PRIMARY_KEY", "UNIQUE", "CLUSTERED", "TYPE", "FILL_FACTOR", "INITIAL_SIZE", "NULLS", "SORT_BOOKMARKS",
            "AUTO_UPDATE", "NULL_COLLATION", "ORDINAL_POSITION", "COLUMN_NAME", "COLUMN_GUID", "COLUMN_PROPID",
            "COLLATION", "CARDINALITY", "PAGES", "FILTER_CONDITION", "INTEGRATED"],
        "Views" => ["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "VIEW_DEFINITION", "CHECK_OPTION",
            "IS_UPDATABLE", "DESCRIPTION", "DATE_CREATED", "DATE_MODIFIED"],
        "Procedures" => ["PROCEDURE_CATALOG", "PROCEDURE_SCHEMA", "PROCEDURE_NAME", "PROCEDURE_TYPE",
            "PROCEDURE_DEFINITION", "DESCRIPTION", "DATE_CREATED", "DATE_MODIFIED"],
        "ForeignKeys" => ["PK_TABLE_CATALOG", "PK_TABLE_SCHEMA", "PK_TABLE_NAME", "PK_COLUMN_NAME",
            "PK_COLUMN_GUID", "PK_COLUMN_PROPID", "FK_TABLE_CATALOG", "FK_TABLE_SCHEMA", "FK_TABLE_NAME",
            "FK_COLUMN_NAME", "FK_COLUMN_GUID", "FK_COLUMN_PROPID", "ORDINAL", "UPDATE_RULE", "DELETE_RULE",
            "PK_NAME", "FK_NAME", "DEFERRABILITY"],
        "PrimaryKeys" => ["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "COLUMN_GUID",
            "COLUMN_PROPID", "ORDINAL", "PK_NAME"],
        "TableConstraints" => ["CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME", "TABLE_CATALOG",
            "TABLE_SCHEMA", "TABLE_NAME", "CONSTRAINT_TYPE", "IS_DEFERRABLE", "INITIALLY_DEFERRED", "DESCRIPTION"],
        "KeyColumnUsage" => ["CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME", "TABLE_CATALOG",
            "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "COLUMN_GUID", "COLUMN_PROPID", "ORDINAL_POSITION"],
        "ConstraintColumnUsage" => ["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "COLUMN_GUID",
            "COLUMN_PROPID", "CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME"],
        "ReferentialConstraints" => ["CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME",
            "UNIQUE_CONSTRAINT_CATALOG", "UNIQUE_CONSTRAINT_SCHEMA", "UNIQUE_CONSTRAINT_NAME", "MATCH_OPTION",
            "UPDATE_RULE", "DELETE_RULE", "DESCRIPTION"],
        "CheckConstraints" => ["CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME", "CHECK_CLAUSE",
            "DESCRIPTION"],
        "Statistics" => ["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "CARDINALITY"],
        // ACE serves neither of these: it refuses the procedure-parameter rowset outright, and has no
        // view-column one. Both follow the shapes the OLE DB and SQL Server providers document.
        "ProcedureParameters" => ["PROCEDURE_CATALOG", "PROCEDURE_SCHEMA", "PROCEDURE_NAME", "PARAMETER_NAME",
            "ORDINAL_POSITION", "PARAMETER_TYPE", "PARAMETER_HASDEFAULT", "PARAMETER_DEFAULT", "IS_NULLABLE",
            "DATA_TYPE", "CHARACTER_MAXIMUM_LENGTH", "CHARACTER_OCTET_LENGTH", "NUMERIC_PRECISION",
            "NUMERIC_SCALE", "DESCRIPTION", "TYPE_NAME", "LOCAL_TYPE_NAME"],
        "ViewColumns" => ["VIEW_CATALOG", "VIEW_SCHEMA", "VIEW_NAME", "TABLE_CATALOG", "TABLE_SCHEMA",
            "TABLE_NAME", "COLUMN_NAME"],
        _ => throw new InvalidOperationException($"'{collection}' is not a schema collection."),
    };

    /// <summary>Declared CLR types corresponding to <see cref="ColumnsOf"/>. A nullable value still reports its
    /// underlying type, as ADO.NET metadata does; row nulls are represented by null.</summary>
    public static IReadOnlyList<Type> ColumnTypesOf(string collection) => Canonical(collection) switch
    {
        "Tables" => [typeof(string), typeof(string), typeof(string), typeof(string), typeof(Guid), typeof(string),
            typeof(long), typeof(DateTime), typeof(DateTime)],
        "Columns" => [typeof(string), typeof(string), typeof(string), typeof(string), typeof(Guid), typeof(long),
            typeof(long), typeof(bool), typeof(string), typeof(long), typeof(bool), typeof(int),
            typeof(Guid), typeof(long), typeof(long), typeof(int), typeof(short),
            typeof(long), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string),
            typeof(bool), typeof(string), typeof(string), typeof(bool), typeof(int), typeof(int)],
        "Indexes" => [typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool),
            typeof(bool), typeof(int), typeof(long), typeof(string), typeof(Guid), typeof(long),
            typeof(short), typeof(decimal), typeof(int), typeof(string), typeof(bool)],
        "Views" => [typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool),
            typeof(bool), typeof(string), typeof(DateTime), typeof(DateTime)],
        "Procedures" => [typeof(string), typeof(string), typeof(string), typeof(short),
            typeof(string), typeof(string), typeof(DateTime), typeof(DateTime)],
        "ForeignKeys" => [typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(Guid), typeof(long), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(Guid), typeof(long), typeof(long), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(short)],
        "PrimaryKeys" => [typeof(string), typeof(string), typeof(string), typeof(string), typeof(Guid),
            typeof(long), typeof(long), typeof(string)],
        "TableConstraints" => [typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(string)],
        "KeyColumnUsage" => [typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(string), typeof(Guid), typeof(long), typeof(long)],
        "ConstraintColumnUsage" => [typeof(string), typeof(string), typeof(string), typeof(string), typeof(Guid),
            typeof(long), typeof(string), typeof(string), typeof(string)],
        "ReferentialConstraints" => [typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(string)],
        "CheckConstraints" => [typeof(string), typeof(string), typeof(string), typeof(string), typeof(string)],
        "Statistics" => [typeof(string), typeof(string), typeof(string), typeof(decimal)],
        "ProcedureParameters" => [typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(int), typeof(int), typeof(bool), typeof(string), typeof(bool),
            typeof(int), typeof(long), typeof(long), typeof(int),
            typeof(short), typeof(string), typeof(string), typeof(string)],
        "ViewColumns" => [typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string)],
        _ => throw new InvalidOperationException($"'{collection}' is not a schema collection."),
    };

    /// <summary>The rows of a collection, in <see cref="ColumnsOf"/> order. A Jet file has no catalog or schema,
    /// so every *_CATALOG / *_SCHEMA value is null, as ACE reports them.</summary>
    public static IReadOnlyList<object?[]> Rows(string collection, JetDatabase database)
    {
        JetCatalog catalog = database.Catalog;
        var rows = new List<object?[]>();
        switch (Canonical(collection))
        {
            case "Tables":
                // Tables and views together, ordered by name, as ACE returns them. A stored query with declared
                // parameters is a procedure rather than a view, so it appears in neither this rowset nor Views.
                foreach (TableDef t in catalog.Tables)
                    if (TableType(t) is { } type)
                        rows.Add([null, null, t.Name, type, null, null, null, null, null]);
                foreach (string name in ViewNames(catalog))
                    rows.Add([null, null, name, "VIEW", null, null, null, null, null]);
                rows.Sort(ByName(2));
                break;

            case "Columns":
                // Every object ACE lists columns for except the system tables: user tables, the Access-owned
                // hidden tables, and views.
                foreach (TableDef t in catalog.Tables)
                {
                    if (TableType(t) is not { } type || type == "SYSTEM TABLE") continue;
                    foreach (ColumnDef c in t.Columns)
                        rows.Add([null, null, t.Name, c.Name, null, null, (long)(c.Index + 1),
                            c.DefaultValue is not null, c.DefaultValue, ColumnFlags(c), Nullable(c),
                            DataTypeCode(c), null, CharacterMaxLength(c), CharacterOctetLength(c),
                            NumericPrecision(c), NumericScale(c), DateTimePrecision(c),
                            null, null, null, null, null, null, null, null, null, null,
                            c.IsCalculated, c.CalculatedExpression, ProviderTypeName(c),
                            c.IsAutoNumber, c.IsAutoNumber ? c.Seed : null, c.IsAutoNumber ? c.Increment : null]);
                }

                // A view's columns are the shape its query produces, which only planning the query can say.
                // A column the query passes through unchanged reports the stored column behind it, facets and
                // all; a computed one has only the type the expression yields. Either way the writability flag
                // is WRITE rather than the WRITEUNKNOWN a stored column carries, as ACE reports a view's.
                foreach (string view in ViewNames(catalog))
                {
                    var described = ViewColumns(database, view);
                    for (int i = 0; i < described.Count; i++)
                    {
                        var column = described[i];
                        ColumnDef? c = column.Source;
                        bool nullable = c is not null ? ViewNullable(c) : column.ClrType != typeof(bool);
                        rows.Add(c is not null
                            ? [null, null, view, column.Name, null, null, (long)(i + 1),
                                false, null, ViewColumnFlags(c, column.Origin), nullable,
                                DataTypeCode(c), null, CharacterMaxLength(c), CharacterOctetLength(c),
                                NumericPrecision(c), NumericScale(c), DateTimePrecision(c),
                                null, null, null, null, null, null, null, null, null, null,
                                // A view column is computed when the view computes it, and also when it passes
                                // through a table's calculated column — which keeps its expression. An
                                // AutoNumber keeps its flag through a view, but not its seed: nothing is
                                // assigned through a view, so the next value belongs to the table.
                                c.IsCalculated, c.CalculatedExpression, ProviderTypeName(c),
                                c.IsAutoNumber, null, null]
                            : [null, null, view, column.Name, null, null, (long)(i + 1),
                                false, null, ComputedColumnFlags(column.ClrType, nullable, column.Origin), nullable,
                                ComputedDataTypeCode(column.ClrType, column.Currency), null,
                                ComputedTextLength(column.ClrType, column.Origin),
                                ComputedTextLength(column.ClrType, column.Origin) * 2,
                                ComputedPrecision(column.ClrType, column.Currency), (short?)column.Scale,
                                column.ClrType == typeof(DateTime) ? 0L : null,
                                null, null, null, null, null, null, null, null, null, null,
                                // Computed by the view; the expression's text is not reconstructed here.
                                true, null, ComputedTypeName(column.ClrType, column.Currency), false, null, null]);
                    }
                }
                break;

            case "Indexes":
                // One row per index column, for the same objects as Columns minus the views (an index belongs
                // to a table). Every LOGICAL index is listed, so a column covered by a named index, a primary
                // key and a relationship appears once under each name, all reporting the one real index's
                // columns and statistics — as ACE lists them. CARDINALITY is the distinct-entry count.
                foreach (TableDef t in catalog.Tables)
                {
                    if (TableType(t) is not { } type || type == "SYSTEM TABLE") continue;
                    foreach ((string name, IndexDef ix, bool isPrimaryKey) in LogicalIndexes(t))
                        for (int i = 0; i < ix.Columns.Count; i++)
                            rows.Add([null, null, t.Name, null, null, name, isPrimaryKey, ix.IsUnique, false,
                                IndexTypeBtree, FillFactor, InitialSize, Nulls(ix), false, true, NullCollationLow,
                                (long)(i + 1), ix.Columns[i].Column.Name, null, null,
                                ix.Columns[i].Ascending ? CollationAscending : CollationDescending,
                                (decimal)ix.UniqueEntryCount, null, null, true]);
                }
                break;

            case "Views":
                foreach (string name in ViewNames(catalog))
                    rows.Add([null, null, name, catalog.Views[name], null, true, null, null, null]);
                rows.Sort(ByName(2));
                break;

            case "Procedures":
                // A stored query is a procedure when it declares parameters, and an action query always is.
                // ACE's PROCEDURE_DEFINITION carries the PARAMETERS clause ahead of the statement; LibRed does
                // not reconstruct the parameters' declared types yet, so the statement alone is reported.
                foreach ((string name, string sql) in catalog.Views)
                    if (catalog.QueryParameters.ContainsKey(name))
                        rows.Add([null, null, name, ProcedureTypeReturnsRows, sql, null, null, null]);
                foreach ((string name, StoredActionQuery query) in catalog.ActionQueries)
                    rows.Add([null, null, name, ProcedureTypeReturnsRows, query.Sql, null, null, null]);
                rows.Sort(ByName(2));
                break;

            case "ForeignKeys":
                // One row per column pair, naming the parent's key on one side and the foreign key on the
                // other. PK_NAME is the constraint the parent side is keyed on, which is its primary key
                // unless the relationship references some other unique index.
                foreach (ForeignKey fk in catalog.Relationships)
                {
                    string? parentKey = ParentKeyName(fk, catalog);
                    for (int i = 0; i < fk.Columns.Count; i++)
                        rows.Add([null, null, fk.ReferencedTable, fk.Columns[i].ReferencedColumn, null, null,
                            null, null, fk.Table, fk.Columns[i].Column, null, null, (long)(i + 1),
                            RefAction(fk.CascadeUpdate, fk.UpdateSetNull), RefAction(fk.CascadeDelete, fk.DeleteSetNull),
                            parentKey, fk.Name, null]);
                }
                break;

            case "PrimaryKeys":
                foreach (TableDef t in ConstraintTables(catalog))
                    if (t.Indexes.FirstOrDefault(ix => ix.IsPrimaryKey) is { } pk)
                        for (int i = 0; i < pk.Columns.Count; i++)
                            rows.Add([null, null, t.Name, pk.Columns[i].Column.Name, null, null, (long)(i + 1), pk.Name]);
                break;

            case "TableConstraints":
                foreach ((TableDef table, string name, string kind, IndexDef? _, ForeignKey? __) in Constraints(catalog))
                    rows.Add([null, null, name, null, null, table.Name, kind, false, false, null]);
                break;

            case "KeyColumnUsage":
                // Each constraint's own key columns: a foreign key's are the columns it constrains on this
                // table. ACE agrees on all but two relationships in the Northwind corpus, where it reports the
                // table's primary-key column instead of the one the relationship's index actually holds
                // (Employees.ReportsTo read back as EmployeeID); the rule behind those two is not established,
                // and the key's own columns are what the rowset is defined to carry.
                foreach ((TableDef table, string name, string _, IndexDef? index, ForeignKey? fk) in Constraints(catalog))
                {
                    if (fk is not null)
                        for (int i = 0; i < fk.Columns.Count; i++)
                            rows.Add([null, null, name, null, null, table.Name, fk.Columns[i].Column,
                                null, null, (long)(i + 1)]);
                    else if (index is not null)
                        for (int i = 0; i < index.Columns.Count; i++)
                            rows.Add([null, null, name, null, null, table.Name, index.Columns[i].Column.Name,
                                null, null, (long)(i + 1)]);
                }
                break;

            case "ConstraintColumnUsage":
                foreach ((TableDef table, string name, string _, IndexDef? index, ForeignKey? __) in Constraints(catalog))
                    if (index is not null)
                        foreach (var column in index.Columns)
                            rows.Add([null, null, table.Name, column.Column.Name, null, null, null, null, name]);
                break;

            case "ReferentialConstraints":
                // ACE repeats the relationship's own name as the unique constraint it references, rather than
                // naming the parent's key, and reports every Jet relationship as MATCH FULL — a row with some
                // key columns null and others not is rejected rather than ignored.
                foreach (ForeignKey fk in catalog.Relationships)
                    rows.Add([null, null, fk.Name, null, null, fk.Name, "FULL",
                        RefAction(fk.CascadeUpdate, fk.UpdateSetNull), RefAction(fk.CascadeDelete, fk.DeleteSetNull), null]);
                break;

            case "CheckConstraints":
                foreach (TableDef t in catalog.Tables)
                {
                    if (TableType(t) is not { } type || type == "SYSTEM TABLE") continue;
                    foreach ((string name, string expression) in t.CheckConstraints)
                        rows.Add([null, null, name, expression, null]);
                }
                break;

            case "Statistics":
                // Every table, system ones included, with the row count the TDEF carries. Views have no
                // cardinality to report and ACE lists none.
                foreach (TableDef t in catalog.Tables)
                    if (TableType(t) is not null)
                        rows.Add([null, null, t.Name, (decimal)t.RowCount]);
                rows.Sort(ByName(2));
                break;

            case "ProcedureParameters":
                // Each stored query's declared parameters, in declaration order. Access declares a parameter's
                // type but neither a default nor whether it takes null, so those report as the rowset's
                // "no default" and nullable. An untyped parameter — Access's `Value` — reports no type.
                foreach ((string name, IReadOnlyList<StoredQueryParameter> parameters) in catalog.QueryParameters)
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        StoredQueryParameter p = parameters[i];
                        // The declared facets where the parameter row records them: a text length (reported
                        // in characters and in bytes, two per character as the Columns collection reports a
                        // column's), and a decimal's precision and scale. A type that records none falls back
                        // to the precision its type implies, as a column of it would report.
                        rows.Add([null, null, name, p.Name, i + 1, ParameterTypeInput, false, null, true,
                            p.Type is { } type ? DataTypeCodeOf(type) : null,
                            p.Size is { } size ? (long)size : null,
                            p.Size is { } octets ? (long)octets * 2 : null,
                            p.Precision ?? (p.Type is { } precisionType ? NumericPrecisionOf(precisionType) : null),
                            p.Scale is { } scale ? (short)scale : null, null,
                            p.Type is { } named ? ProviderTypeName(named) : null,
                            p.Type is { } local ? ProviderTypeName(local) : null]);
                    }
                rows.Sort(ByName(2));
                break;

            case "ViewColumns":
                // Which stored column each of a view's columns comes from, where one does — the provenance the
                // planner already tracks. A computed column has no base column and so no row here.
                var owners = ColumnOwners(catalog);
                foreach (string view in ViewNames(catalog))
                    foreach (var column in ViewColumns(database, view))
                        if (column.Source is { } source && owners.TryGetValue(source, out string? owner))
                            rows.Add([null, null, view, null, null, owner, source.Name]);
                break;
        }
        return rows;
    }

    /// <summary>The tables whose constraints are reported: the same set whose columns are, so a system table's
    /// constraints stay out of the rowsets as ACE keeps them.</summary>
    private static IEnumerable<TableDef> ConstraintTables(JetCatalog catalog) =>
        catalog.Tables.Where(t => TableType(t) is { } type && type != "SYSTEM TABLE");

    /// <summary>Every constraint on those tables: a primary key, each other unique index, and each foreign key
    /// the table declares. A foreign key's columns come from the index backing it, so it reports the columns
    /// it constrains.</summary>
    private static IEnumerable<(TableDef Table, string Name, string Kind, IndexDef? Index, ForeignKey? ForeignKey)>
        Constraints(JetCatalog catalog)
    {
        foreach (TableDef t in ConstraintTables(catalog))
        {
            foreach (IndexDef ix in t.Indexes.Where(ix => ix.IsPrimaryKey))
                yield return (t, ix.Name, "PRIMARY KEY", ix, null);
            foreach (IndexDef ix in t.Indexes.Where(ix => ix.IsUnique && !ix.IsPrimaryKey))
                yield return (t, ix.Name, "UNIQUE", ix, null);

            foreach (ForeignKey fk in catalog.Relationships.Where(fk =>
                fk.Table.Equals(t.Name, StringComparison.OrdinalIgnoreCase)))
                yield return (t, fk.Name, "FOREIGN KEY", BackingIndex(fk, t), fk);
        }
    }

    /// <summary>The index on the child table that stores a relationship's key columns.</summary>
    private static IndexDef? BackingIndex(ForeignKey fk, TableDef child)
    {
        var columns = fk.Columns.Select(c => c.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return child.Indexes.FirstOrDefault(ix =>
            ix.Columns.Count == columns.Count && ix.Columns.All(c => columns.Contains(c.Column.Name)));
    }

    /// <summary>The name of the parent-side key a relationship references — its primary key unless the
    /// relationship names some other unique index.</summary>
    private static string? ParentKeyName(ForeignKey fk, JetCatalog catalog)
    {
        TableDef? parent = catalog.Tables.FirstOrDefault(t =>
            t.Name.Equals(fk.ReferencedTable, StringComparison.OrdinalIgnoreCase));
        if (parent is null) return null;

        var columns = fk.Columns.Select(c => c.ReferencedColumn).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Covers(IndexDef ix) => ix.Columns.Count == columns.Count && ix.Columns.All(c => columns.Contains(c.Column.Name));
        return (parent.Indexes.FirstOrDefault(ix => ix.IsPrimaryKey && Covers(ix))
            ?? parent.Indexes.FirstOrDefault(ix => ix.IsUnique && Covers(ix)))?.Name;
    }

    private static string RefAction(bool cascade, bool setNull) =>
        cascade ? "CASCADE" : setNull ? "SET NULL" : "NO ACTION";

    // ACE's fixed index-rowset values, named rather than repeated: a Jet index is a non-clustered B-tree that
    // sorts nulls low, is maintained by the engine, and reports the page-size initial allocation.
    private const int IndexTypeBtree = 1;          // DBPROPVAL_IT_BTREE
    private const int FillFactor = 100;
    private const int InitialSize = 4096;
    private const int NullCollationLow = 4;        // DBPROPVAL_NC_LOW
    private const short CollationAscending = 1;    // DB_COLLATION_ASC
    private const short CollationDescending = 2;   // DB_COLLATION_DESC
    private const short ProcedureTypeReturnsRows = 3;   // DB_PT_FUNCTION
    private const int ParameterTypeInput = 1;           // DBPARAMTYPE_INPUT — Access declares no other kind

    /// <summary>Describes a query's output columns for the ADO layer's <c>GetSchemaTable</c> /
    /// <c>GetColumnSchema</c>: each column's type and, where a stored column stands behind it, that column's
    /// table, declared facets and constraints. Same rules as the <c>Columns</c> collection, so a caller reading
    /// a query's schema and one reading the table's metadata are told the same thing.</summary>
    internal static IReadOnlyList<Execution.ResultColumn> Describe(
        IReadOnlyList<Execution.OutputColumn> columns, JetCatalog catalog)
    {
        var owners = ColumnOwners(catalog);
        var described = new List<Execution.ResultColumn>(columns.Count);

        foreach (Execution.OutputColumn column in columns)
        {
            Type clrType = column.ClrType ?? typeof(object);
            if (column.Source is not { } c || !owners.TryGetValue(c, out string? table))
            {
                described.Add(new Execution.ResultColumn(
                    column.Name, clrType, AllowNull: clrType != typeof(bool), IsExpression: true, IsReadOnly: true,
                    Size: (int?)ComputedTextLength(column.ClrType, column.Origin),
                    Precision: ComputedPrecision(column.ClrType, column.Currency), Scale: column.Scale,
                    ProviderType: ComputedDataTypeCode(column.ClrType, column.Currency),
                    TypeName: ComputedTypeName(column.ClrType, column.Currency)));
                continue;
            }

            TableDef? owner = catalog.Tables.FirstOrDefault(t => t.Name == table);
            described.Add(new Execution.ResultColumn(
                column.Name, clrType, table, c.Name,
                AllowNull: Nullable(c),
                IsExpression: false,
                IsAutoIncrement: c.IsAutoNumber,
                IsKey: owner is not null && owner.Indexes.Any(ix => ix.IsPrimaryKey && Covers(ix, c)),
                IsUnique: owner is not null && owner.Indexes.Any(ix => ix.IsUnique && ix.Columns.Count == 1 && Covers(ix, c)),
                IsLong: IsLong(c),
                // A calculated column cannot be written, and neither can a value a query computed from one.
                IsReadOnly: c.IsCalculated,
                Size: (int?)CharacterMaxLength(c),
                Precision: NumericPrecision(c),
                Scale: NumericScale(c),
                ProviderType: DataTypeCode(c),
                TypeName: ProviderTypeName(c)));
        }

        return described;

        static bool Covers(IndexDef index, ColumnDef column) =>
            index.Columns.Any(c => ReferenceEquals(c.Column, column));
    }

    /// <summary>Which table owns each column, by the column's own identity, so a query's output column can be
    /// traced back to the table it came from.</summary>
    private static Dictionary<ColumnDef, string> ColumnOwners(JetCatalog catalog)
    {
        var owners = new Dictionary<ColumnDef, string>(ReferenceEqualityComparer.Instance as IEqualityComparer<ColumnDef>
            ?? EqualityComparer<ColumnDef>.Default);
        foreach (TableDef t in catalog.Tables)
            foreach (ColumnDef c in t.Columns)
                owners[c] = t.Name;
        return owners;
    }

    /// <summary>The provider's name for a column's type, as the DataTypes collection spells it — which for text
    /// and binary means naming the fixed-length forms apart from the variable ones, since the two are different
    /// types there (Char/VarChar, Binary/VarBinary) even though ACE's own flags collapse binary into one.
    /// </summary>
    private static string ProviderTypeName(ColumnDef c) => EffectiveType(c) switch
    {
        JetDataType.Text => c.IsFixedLength ? "Char" : "VarChar",
        JetDataType.Binary => c.IsFixedLength ? "Binary" : "VarBinary",
        var type => ProviderTypeName(type),
    };

    /// <summary>The provider's own name for a type, as the DataTypes collection spells it.</summary>
    private static string ProviderTypeName(JetDataType type) => type switch
    {
        JetDataType.Boolean => "Bit",
        JetDataType.Byte => "Byte",
        JetDataType.Int16 => "Short",
        JetDataType.Int32 => "Long",
        JetDataType.Int64 => "BigInt",
        JetDataType.Single => "Single",
        JetDataType.Double => "Double",
        JetDataType.Currency => "Currency",
        JetDataType.DateTime => "DateTime",
        JetDataType.DateTimeExtended => "DateTime2",
        JetDataType.Guid => "GUID",
        JetDataType.FixedPoint => "Decimal",
        JetDataType.Text => "VarChar",
        JetDataType.Memo or JetDataType.Complex => "LongText",
        JetDataType.Binary => "VarBinary",
        JetDataType.Ole => "LongBinary",
        _ => "VarBinary",
    };

    // MSysObjects.Flags decides what an object is called in the schema rowsets, the way Access classifies it —
    // not its name (measured on ACE 16): the system bit makes it a SYSTEM TABLE, the hidden bit an ACCESS TABLE
    // (the navigation-pane and resource tables), and an object carrying ExcludedFlags is not listed at all —
    // the MSysComplexType_* tables, flags 0x80030000.
    private const uint SystemFlag = 0x80000000;
    private const uint HiddenFlag = 0x00000008;
    private const uint ExcludedFlags = 0x00030000;

    /// <summary>What ACE calls this object in the schema rowsets, or null when it lists the object nowhere.</summary>
    private static string? TableType(TableDef t) =>
        (t.ObjectFlags & ExcludedFlags) != 0 ? null
        : (t.ObjectFlags & SystemFlag) != 0 ? "SYSTEM TABLE"
        : (t.ObjectFlags & HiddenFlag) != 0 ? "ACCESS TABLE"
        : "TABLE";

    /// <summary>Each of the table's logical indexes paired with the real index that stores it, primary key
    /// first and the rest by name, as ACE orders them. The parent half of a relationship is left out: Access
    /// names it <c>.r…</c> and keeps it out of its own schema views. A table read from a definition that
    /// carries no logical list (one LibRed built itself, say) falls back to its real indexes, one name each.</summary>
    private static IEnumerable<(string Name, IndexDef Index, bool IsPrimaryKey)> LogicalIndexes(TableDef t)
    {
        IEnumerable<(string Name, IndexDef Index, bool IsPrimaryKey)> all = t.LogicalIndexes.Count > 0
            ? t.LogicalIndexes
                .Where(l => !l.IsIncomingRelationship)
                .Select(l => (l.Name, Index: t.Indexes.FirstOrDefault(ix => ix.RealIndexOrdinal == l.RealIndexOrdinal), l.IsPrimaryKey))
                .Where(x => x.Index is not null)
                .Select(x => (x.Name, x.Index!, x.IsPrimaryKey))
            : t.Indexes.Select(ix => (ix.Name, ix, ix.IsPrimaryKey));

        return all
            .OrderByDescending(x => x.IsPrimaryKey)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The stored SELECT queries that are views: a query declaring parameters is a procedure.</summary>
    private static IEnumerable<string> ViewNames(JetCatalog catalog) =>
        catalog.Views.Keys.Where(name => !catalog.QueryParameters.ContainsKey(name));

    private static Comparison<object?[]> ByName(int column) =>
        (left, right) => string.Compare((string?)left[column], (string?)right[column], StringComparison.OrdinalIgnoreCase);

    /// <summary>The OLE DB type code ACE reports for a column. A memo and a text column share one code, as do an
    /// OLE and a binary column; what separates them is the long-value flag in <see cref="ColumnFlags"/>.</summary>
    private static int DataTypeCode(ColumnDef c) => DataTypeCodeOf(EffectiveType(c));

    /// <inheritdoc cref="DataTypeCode"/>
    private static int DataTypeCodeOf(JetDataType type) => type switch
    {
        JetDataType.Boolean => 11,                          // DBTYPE_BOOL
        JetDataType.Byte => 17,                             // DBTYPE_UI1
        JetDataType.Int16 => 2,                             // DBTYPE_I2
        JetDataType.Int32 => 3,                             // DBTYPE_I4 — an AutoNumber reports this too
        JetDataType.Single => 4,                            // DBTYPE_R4
        JetDataType.Double => 5,                            // DBTYPE_R8
        JetDataType.Currency => 6,                          // DBTYPE_CY
        JetDataType.DateTime => 7,                          // DBTYPE_DATE
        JetDataType.Guid => 72,                             // DBTYPE_GUID
        JetDataType.FixedPoint => 131,                      // DBTYPE_NUMERIC
        // DBTYPE_WSTR. A complex (multi-value / attachment) column reports as long text too — measured on
        // MSysResources.Data, the only complex column in the corpus.
        JetDataType.Text or JetDataType.Memo or JetDataType.Complex => 130,
        // Measured against ACE 16, which reports these two through the codes their CLR types map to even
        // though its own DataTypes list never learned them: DBTYPE_I8 and DBTYPE_DBTIMESTAMP.
        JetDataType.Int64 => 20,
        JetDataType.DateTimeExtended => 135,
        _ => 128,                                           // DBTYPE_BYTES — binary, OLE, and anything unmodelled
    };

    // DBCOLUMNFLAGS as ACE sets them: every column is deferrable, of unknown writability and may hold null;
    // a fixed-length one adds ISFIXEDLENGTH, a nullable one ISNULLABLE, and a memo/OLE column ISLONG.
    private const long FlagsEveryColumn = 0x02 | 0x40;
    private const long FlagIsFixedLength = 0x10;
    private const long FlagIsNullable = 0x20;
    private const long FlagIsLong = 0x80;

    // A stored column reports its writability as unknown; a view's column reports it as writable, which is
    // what ACE puts in the flags for one.
    private const long FlagWriteUnknown = 0x08;
    private const long FlagWrite = 0x04;

    /// <summary>The columns a view produces, from planning its query: each carries the stored column behind it
    /// where it passes one through unchanged, and otherwise the type the expression yields. A view LibRed
    /// cannot plan — one written in SQL it does not accept — reports no columns rather than failing the whole
    /// collection.</summary>
    private static IReadOnlyList<Execution.OutputColumn> ViewColumns(JetDatabase database, string view)
    {
        try
        {
            var plan = new QueryEngine(database).PlanFor($"SELECT * FROM [{view}]");
            // Describing: the plan is built for its shape alone, so reporting a view's columns reads no rows —
            // it would otherwise sort and buffer the whole of any view with an ORDER BY — and a view with
            // declared parameters describes without values nobody supplied.
            return new Execution.QueryExecutor(database, describing: true).DescribeQuery(plan);
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>A view's column is writable, where a stored one's writability is unknown — except an
    /// AutoNumber, which no one writes, and except a column a grouping produces, which cannot be written back
    /// through the view either; ACE reports both as unknown.</summary>
    private static long ViewColumnFlags(ColumnDef c, Execution.ColumnOrigin origin) =>
        FlagsEveryColumn
        | (c.IsAutoNumber || origin != Execution.ColumnOrigin.Expression ? FlagWriteUnknown : FlagWrite)
        | (IsFixedLength(c) ? FlagIsFixedLength : 0)
        | (ViewNullable(c) ? FlagIsNullable : 0)
        | (IsLong(c) ? FlagIsLong : 0);

    /// <summary>Whether a view reports the column as nullable. A view carries the type's own nullability but
    /// not the table's Required property, so a required text column reads as nullable through a view while an
    /// AutoNumber or Yes/No column — which the type itself forbids nulls in — still does not.</summary>
    private static bool ViewNullable(ColumnDef c) => !c.IsAutoNumber && c.Type != JetDataType.Boolean;

    /// <summary>A computed view column's flags. What a column is computed from changes them: a value read out
    /// of one row is not writable at all and, when it is text, is reported as a long one of no declared length;
    /// a value drawn from several rows — an aggregate, or the arms of a union — is text of Access's default
    /// width whose writability is merely unknown.</summary>
    private static long ComputedColumnFlags(Type? clrType, bool nullable, Execution.ColumnOrigin origin)
    {
        bool text = clrType == typeof(string);
        bool fromManyRows = origin != Execution.ColumnOrigin.Expression;
        return FlagsEveryColumn
            | (fromManyRows ? FlagWriteUnknown : 0)
            | (nullable ? FlagIsNullable : 0)
            | (text || clrType == typeof(byte[]) ? 0 : FlagIsFixedLength)
            | (text && !fromManyRows ? FlagIsLong : 0);
    }

    /// <summary>The OLE DB type code for a computed column. Currency and Decimal share <see cref="decimal"/>,
    /// so the planner's own Currency marking picks between them.</summary>
    private static int ComputedDataTypeCode(Type? clrType, bool currency) => clrType switch
    {
        null => 130,
        _ when clrType == typeof(bool) => 11,
        _ when clrType == typeof(byte) => 17,
        _ when clrType == typeof(short) => 2,
        _ when clrType == typeof(int) => 3,
        _ when clrType == typeof(long) => 20,
        _ when clrType == typeof(float) => 4,
        _ when clrType == typeof(double) => 5,
        _ when clrType == typeof(decimal) => currency ? 6 : 131,
        _ when clrType == typeof(DateTime) => 7,
        _ when clrType == typeof(Guid) => 72,
        _ when clrType == typeof(byte[]) => 128,
        _ => 130,
    };

    /// <summary>The provider type name for a computed column, from the type its expression yields.</summary>
    private static string ComputedTypeName(Type? clrType, bool currency) => clrType switch
    {
        null => "VarChar",
        _ when clrType == typeof(bool) => "Bit",
        _ when clrType == typeof(byte) => "Byte",
        _ when clrType == typeof(short) => "Short",
        _ when clrType == typeof(int) => "Long",
        _ when clrType == typeof(long) => "BigInt",
        _ when clrType == typeof(float) => "Single",
        _ when clrType == typeof(double) => "Double",
        _ when clrType == typeof(decimal) => currency ? "Currency" : "Decimal",
        _ when clrType == typeof(DateTime) => "DateTime",
        _ when clrType == typeof(Guid) => "GUID",
        _ when clrType == typeof(byte[]) => "VarBinary",
        _ => "VarChar",
    };

    /// <summary>A computed text column's length: Access's default width where the value is drawn from several
    /// rows, and none at all where an expression computed it from one — that column is reported as long text,
    /// which has no declared length.</summary>
    private static long? ComputedTextLength(Type? clrType, Execution.ColumnOrigin origin) =>
        clrType != typeof(string) ? null
        : origin == Execution.ColumnOrigin.Expression ? 0L
        : 255L;

    /// <summary>A computed column's numeric precision — the same per-type value a stored column of that type
    /// reports.</summary>
    private static int? ComputedPrecision(Type? clrType, bool currency) => clrType switch
    {
        _ when clrType == typeof(byte) => 3,
        _ when clrType == typeof(short) => 5,
        _ when clrType == typeof(int) => 10,
        _ when clrType == typeof(long) => 19,
        _ when clrType == typeof(float) => 7,
        _ when clrType == typeof(double) => 15,
        _ when clrType == typeof(decimal) => currency ? 19 : 28,
        _ => null,
    };

    /// <summary>A stored column's flags. A calculated one carries no write bit at all — nothing writes it, as
    /// with an expression in a view — where an ordinary column's writability is merely unknown.</summary>
    private static long ColumnFlags(ColumnDef c) =>
        FlagsEveryColumn | (c.IsCalculated ? 0 : FlagWriteUnknown)
        | (IsFixedLength(c) ? FlagIsFixedLength : 0)
        | (Nullable(c) ? FlagIsNullable : 0)
        | (IsLong(c) ? FlagIsLong : 0);

    /// <summary>Whether ACE calls the column fixed-length: a scalar type always is, whatever its descriptor
    /// says (Access's own system tables carry Long columns the descriptor marks variable, and ACE still
    /// reports them fixed); text and binary follow their descriptor, so a designer-made fixed-width text
    /// column is fixed while one declared through SQL is not; and memo/OLE never are.</summary>
    private static bool IsFixedLength(ColumnDef c) => EffectiveType(c) switch
    {
        JetDataType.Memo or JetDataType.Ole or JetDataType.Complex => false,
        // A text column is fixed when its descriptor says so — CHAR(n) and NCHAR(n) are, TEXT(n)/VARCHAR(n)
        // are not. A binary column never is here, whatever its descriptor: ACE reports BINARY(n) as variable,
        // the same collapse ADOX makes in reporting every binary column as adVarBinary.
        JetDataType.Text => c.IsFixedLength,
        JetDataType.Binary => false,
        // BIGINT and DATETIME2 postdate ACE's OLE DB provider, which describes both as variable-length with no
        // precision although each is a fixed width on disk — measured, and matched here so the metadata agrees
        // with what a caller reading through ACE would have been told.
        JetDataType.Int64 or JetDataType.DateTimeExtended => false,
        _ => true,
    };

    private static bool IsLong(ColumnDef c) =>
        EffectiveType(c) is JetDataType.Memo or JetDataType.Ole or JetDataType.Complex;

    /// <summary>The type a column reports as. A calculated column reports what its expression yields, which
    /// need not be the type its descriptor declares for storage — Access keeps a Yes/No expression in a
    /// two-byte integer, and reports it as Yes/No.</summary>
    private static JetDataType EffectiveType(ColumnDef c) =>
        c.IsCalculated && c.CalculatedResultType is { } result ? result : c.Type;

    /// <summary>Whether a stored column reports as nullable. A complex column's values live outside the row, so
    /// the AutoNumber flag its descriptor carries (for the complex id) says nothing about nulls; a calculated
    /// one is as nullable as the type it yields.</summary>
    private static bool Nullable(ColumnDef c) =>
        c.IsCalculated ? EffectiveType(c) != JetDataType.Boolean
        : c.Type == JetDataType.Complex ? c.IsNullable
        : JetStoreType.IsNullable(c);

    /// <summary>CHARACTER_MAXIMUM_LENGTH: the declared length in characters for text and bytes for binary, zero
    /// for the unbounded memo/OLE types, and — as ACE reports it — two for a Yes/No column.</summary>
    private static long? CharacterMaxLength(ColumnDef c) => EffectiveType(c) switch
    {
        JetDataType.Text or JetDataType.Binary => JetStoreType.MaxLength(c),
        JetDataType.Memo or JetDataType.Ole or JetDataType.Complex => 0L,
        JetDataType.Boolean => 2L,
        _ => null,
    };

    /// <summary>CHARACTER_OCTET_LENGTH: the storage width of the same, so twice the character count for text.</summary>
    private static long? CharacterOctetLength(ColumnDef c) => EffectiveType(c) switch
    {
        JetDataType.Text => JetStoreType.MaxLength(c) * 2L,
        JetDataType.Binary => JetStoreType.MaxLength(c),
        JetDataType.Memo or JetDataType.Ole or JetDataType.Complex => 0L,
        _ => null,
    };

    private static int? NumericPrecision(ColumnDef c) =>
        EffectiveType(c) == JetDataType.FixedPoint ? c.Precision : NumericPrecisionOf(EffectiveType(c));

    /// <inheritdoc cref="NumericPrecision"/>
    private static int? NumericPrecisionOf(JetDataType type) => type switch
    {
        JetDataType.Byte => 3,
        JetDataType.Int16 => 5,
        JetDataType.Int32 => 10,
        JetDataType.Single => 7,
        JetDataType.Double => 15,
        JetDataType.Currency => 19,
        // No precision for BIGINT: ACE reports none, though the type holds 19 digits (see IsFixedLength).
        // A DECIMAL's precision is declared per column, so the caller supplies it (see NumericPrecision);
        // 28 is the type's own maximum, which is what a parameter of the type reports.
        JetDataType.FixedPoint => 28,
        _ => null,
    };

    private static short? NumericScale(ColumnDef c) => EffectiveType(c) == JetDataType.FixedPoint ? c.Scale : null;

    private static long? DateTimePrecision(ColumnDef c) => EffectiveType(c) == JetDataType.DateTime ? 0L : null;

    /// <summary>NULLS: whether the index refuses null keys, ignores them, or takes them as keys like any other.</summary>
    private static int Nulls(IndexDef ix) => ix.Required ? 1 : ix.IgnoreNulls ? 2 : 0;

    private static string Canonical(string collection) =>
        Names.FirstOrDefault(n => n.Equals(collection, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"'{collection}' is not a schema collection.");
}