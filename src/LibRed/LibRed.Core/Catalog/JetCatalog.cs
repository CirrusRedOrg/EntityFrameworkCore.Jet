using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;

namespace LibRed.Catalog;

/// <summary>
/// Reads the system catalog (<c>MSysObjects</c>) to enumerate the tables in a database.
/// </summary>
/// <remarks>
/// Bootstrap: MSysObjects' own TDEF is at a fixed page (<see cref="Formats.JetFormatBase.CatalogPage"/>),
/// so we build a <see cref="TableDef"/> for it from that page and read its rows like any
/// other table. For a table object, the row's <c>Id</c> is its TDEF page number.
/// </remarks>
public sealed class JetCatalog(PageChannel channel)
{
    /// <summary>MSysObjects.Type value for a table object.</summary>
    private const short ObjectTypeTable = 1;

    /// <summary>MSysObjects.Flags bits marking a system object (`0x80000000` system, `0x00000002`
    /// system attribute).</summary>
    private const uint SystemObjectFlags = 0x80000002;

    /// <summary>MSysObjects.Flags bit marking a <b>hidden</b> object (`0x08`, observed on Access's
    /// nav-pane tables and on EFCore.Jet's `#Dual` helper). Access excludes hidden objects from its
    /// user-table list, so we treat them as non-user too.</summary>
    private const uint HiddenObjectFlags = 0x00000008;

    // MSysRelationships.grbit flags (DAO RelationAttributeEnum).
    private const int RelationshipDontEnforce = 0x00000002;
    private const int RelationshipUpdateCascade = 0x00000100;
    private const int RelationshipDeleteCascade = 0x00001000;

    /// <summary>MSysObjects.Type value for a view/query object.</summary>
    private const short ObjectTypeQuery = 5;

    private readonly PageChannel _channel = channel;
    private List<TableDef>? _tables;
    private List<ForeignKey>? _relationships;
    private Dictionary<string, string>? _views;

    /// <summary>All tables in the database (user and system).</summary>
    public IReadOnlyList<TableDef> Tables => _tables ??= LoadTables();

    /// <summary>Views (stored simple-SELECT queries) as name → reconstructed SELECT SQL, rebuilt from
    /// each view's MSysQueries rows. Complex/system queries that don't reconstruct are omitted.</summary>
    public IReadOnlyDictionary<string, string> Views => _views ??= LoadViews();

    /// <summary>Drops the cached catalog so a freshly created table is picked up on next read.</summary>
    public void Invalidate()
    {
        _tables = null;
        _relationships = null;
        _views = null;
    }

    /// <summary>All relationships (foreign keys) defined in the database.</summary>
    public IReadOnlyList<ForeignKey> Relationships => _relationships ??= LoadRelationships();

    /// <summary>Relationships for which <paramref name="table"/> is the referencing (child) table.</summary>
    public IEnumerable<ForeignKey> ForeignKeysOf(string table) =>
        Relationships.Where(r => string.Equals(r.Table, table, StringComparison.OrdinalIgnoreCase));

    /// <summary>User (non-system) tables only.</summary>
    public IEnumerable<TableDef> UserTables => Tables.Where(t => !t.IsSystem);

    public TableDef? FindTable(string name) =>
        Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private List<TableDef> LoadTables()
    {
        // Build a TableDef for MSysObjects from its own (fixed) TDEF page, then scan its rows.
        TableDef catalogDef = ReadTableDefinition(_channel.Format.CatalogPage, "MSysObjects", isSystem: true);
        var columns = catalogDef.Columns;

        int idIndex = ColumnIndex(columns, "Id");
        int typeIndex = ColumnIndex(columns, "Type");
        int nameIndex = ColumnIndex(columns, "Name");
        int flagsIndex = ColumnIndex(columns, "Flags");
        int lvpropIndex = ColumnIndex(columns, "LvProp");

        var catalog = new Table(_channel, catalogDef);
        var tables = new List<TableDef>();

        foreach (object?[] row in catalog.Rows())
        {
            if (row[typeIndex] is not short type || type != ObjectTypeTable) continue;

            int definitionPage = (int)row[idIndex]!;
            string name = (string)row[nameIndex]!;
            uint flags = unchecked((uint)(int)row[flagsIndex]!);
            // A table is "system" (excluded from the user-table list, as Access's own schema view
            // does) if it is flagged system or hidden, or is named as engine/temporary infrastructure:
            // MSys*, a leading '~' (temp), or a leading '#' (e.g. EFCore.Jet's hidden #Dual helper).
            bool isSystem = (flags & (SystemObjectFlags | HiddenObjectFlags)) != 0
                            || name.StartsWith("MSys", StringComparison.Ordinal)
                            || name.StartsWith('~')
                            || name.StartsWith('#');

            TableDef definition = ReadTableDefinition(definitionPage, name, isSystem);
            // Attach column DefaultValue and table CHECK properties from the extended-properties (LvProp) blob.
            if (row[lvpropIndex] is byte[] { Length: > 0 } blob)
            {
                var defaults = PropertyBlob.ReadColumnDefaults(blob);
                if (defaults.Count > 0)
                    foreach (ColumnDef column in definition.Columns)
                        if (defaults.TryGetValue(column.Name, out string? value))
                            column.DefaultValue = value;

                var checks = PropertyBlob.ReadCheckConstraints(blob);
                if (checks.Count > 0) definition.CheckConstraints = checks;
            }
            tables.Add(definition);
        }

        return tables;
    }

    private List<ForeignKey> LoadRelationships()
    {
        TableDef? def = FindTable("MSysRelationships");
        if (def is null) return [];

        var c = def.Columns;
        int nameIdx = ColumnIndex(c, "szRelationship");
        int childTableIdx = ColumnIndex(c, "szObject");
        int childColumnIdx = ColumnIndex(c, "szColumn");
        int parentTableIdx = ColumnIndex(c, "szReferencedObject");
        int parentColumnIdx = ColumnIndex(c, "szReferencedColumn");
        int orderIdx = ColumnIndex(c, "icolumn");
        int flagsIdx = ColumnIndex(c, "grbit");

        // One row per column; group by relationship name and order columns by icolumn.
        var groups = new Dictionary<string, (string Child, string Parent, int Flags,
            List<(int Order, string Column, string ReferencedColumn)> Columns)>();

        foreach (object?[] row in new Table(_channel, def).Rows())
        {
            string name = (string)row[nameIdx]!;
            if (!groups.TryGetValue(name, out var g))
            {
                g = ((string)row[childTableIdx]!, (string)row[parentTableIdx]!,
                     (int)row[flagsIdx]!, []);
                groups[name] = g;
            }
            g.Columns.Add(((int)row[orderIdx]!, (string)row[childColumnIdx]!, (string)row[parentColumnIdx]!));
        }

        return groups
            .Select(kvp => new ForeignKey(
                kvp.Key,
                kvp.Value.Child,
                kvp.Value.Parent,
                kvp.Value.Columns.OrderBy(x => x.Order).Select(x => (x.Column, x.ReferencedColumn)).ToList(),
                (kvp.Value.Flags & RelationshipDontEnforce) == 0,
                (kvp.Value.Flags & RelationshipUpdateCascade) != 0,
                (kvp.Value.Flags & RelationshipDeleteCascade) != 0))
            .ToList();
    }

    // MSysQueries attribute codes (see spec §11).
    private const byte QueryAttrType = 0x00, QueryAttrFlag = 0x03, QueryAttrTable = 0x05,
        QueryAttrColumn = 0x06, QueryAttrJoin = 0x07, QueryAttrWhere = 0x08;
    private const short QueryFlagDistinct = 2;

    private Dictionary<string, string> LoadViews()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        TableDef? mqDef = FindTable("MSysQueries");
        TableDef? objDef = FindTable("MSysObjects");
        if (mqDef is null || objDef is null) return result;

        // Group MSysQueries rows by ObjectId.
        var mq = mqDef.Columns;
        int oid = ColumnIndex(mq, "ObjectId"), attr = ColumnIndex(mq, "Attribute"), expr = ColumnIndex(mq, "Expression"),
            flag = ColumnIndex(mq, "Flag"), n1 = ColumnIndex(mq, "Name1"), n2 = ColumnIndex(mq, "Name2"), order = ColumnIndex(mq, "Order");
        var byObject = new Dictionary<int, List<object?[]>>();
        foreach (object?[] row in new Table(_channel, mqDef).Rows())
            if (row[oid] is int id)
                (byObject.TryGetValue(id, out var list) ? list : byObject[id] = []).Add(row);

        // For each view object, reconstruct its SELECT SQL.
        var oc = objDef.Columns;
        int objId = ColumnIndex(oc, "Id"), objType = ColumnIndex(oc, "Type"), objName = ColumnIndex(oc, "Name");
        foreach (object?[] row in new Table(_channel, objDef).Rows())
        {
            if (row[objType] is not short type || type != ObjectTypeQuery) continue;
            if (row[objId] is not int id || row[objName] is not string name) continue;
            if (byObject.TryGetValue(id, out var rows) && Reconstruct(rows, attr, expr, flag, n1, n2, order) is { } sql)
                result[name] = sql;
        }
        return result;
    }

    /// <summary>Rebuilds a simple-SELECT view's SQL from its MSysQueries rows; null if it uses an
    /// attribute we don't reconstruct (a non-simple query).</summary>
    private static string? Reconstruct(List<object?[]> rows, int attr, int expr, int flag, int n1, int n2, int order)
    {
        static int Ord(object? v) => v is byte[] b && b.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b) : 0;
        IEnumerable<object?[]> OfAttr(byte a) => rows.Where(r => r[attr] is byte b && b == a).OrderBy(r => Ord(r[order]));

        // Bail out if the query uses attributes beyond a simple SELECT (e.g. GROUP BY/HAVING/ORDER BY).
        var known = new byte[] { QueryAttrType, QueryAttrFlag, QueryAttrTable, QueryAttrColumn, QueryAttrJoin, QueryAttrWhere, 0xFF };
        if (rows.Any(r => r[attr] is byte b && !known.Contains(b))) return null;

        // A column row's Name1 (when present) is its output alias.
        var columns = OfAttr(QueryAttrColumn)
            .Select(r => (r[n1] as string) is { } a ? $"{r[expr] as string} AS [{a}]" : r[expr] as string ?? "").ToList();
        // A derived-table source has its subquery SQL in Expression and no Name1; a named table uses Name1.
        var tables = OfAttr(QueryAttrTable)
            .Select(r => (Table: r[n1] as string ?? "", Alias: r[n2] as string, Sub: r[n1] is null ? r[expr] as string : null)).ToList();
        if (columns.Count == 0 || tables.Count == 0) return null;

        bool distinct = OfAttr(QueryAttrFlag).Any(r => r[flag] is short f && (f & QueryFlagDistinct) != 0);
        var joins = OfAttr(QueryAttrJoin)
            .Select(r => (Cond: r[expr] as string ?? "", Kind: r[flag] is short f ? f : (short)1, Right: r[n2] as string ?? "")).ToList();
        string? where = OfAttr(QueryAttrWhere).Select(r => r[expr] as string).FirstOrDefault();

        static string Ident(string s) => $"[{s}]";
        static string Render((string Table, string? Alias, string? Sub) t) =>
            t.Sub is { } sub ? $"({sub}) AS {Ident(t.Alias!)}"
            : t.Alias is not null && !string.Equals(t.Alias, t.Table, StringComparison.OrdinalIgnoreCase)
                ? $"{Ident(t.Table)} AS {Ident(t.Alias)}" : Ident(t.Table);

        var from = new System.Text.StringBuilder(Render(tables[0]));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tables[0].Alias ?? tables[0].Table };
        foreach (var j in joins)
        {
            var right = tables.FirstOrDefault(t => string.Equals(t.Alias ?? t.Table, j.Right, StringComparison.OrdinalIgnoreCase));
            if (right.Table.Length == 0 && right.Sub is null) return null; // not found (a derived table has Sub set)
            string kw = j.Kind switch { 2 => "LEFT", 3 => "RIGHT", _ => "INNER" };
            from.Append($" {kw} JOIN {Render(right)} ON {j.Cond}");
            used.Add(right.Alias ?? right.Table);
        }
        // Any remaining tables are comma (cross) joins.
        foreach (var t in tables.Where(t => !used.Contains(t.Alias ?? t.Table)))
            from.Append($", {Render(t)}");

        var sql = new System.Text.StringBuilder("SELECT ");
        if (distinct) sql.Append("DISTINCT ");
        sql.Append(string.Join(", ", columns)).Append(" FROM ").Append(from);
        if (where is not null) sql.Append(" WHERE ").Append(where);
        return sql.ToString();
    }

    private TableDef ReadTableDefinition(int definitionPage, string name, bool isSystem)
    {
        var tdef = new TableDefinitionPage();
        tdef.Read(_channel, definitionPage);

        return new TableDef
        {
            Name = name,
            DefinitionPage = definitionPage,
            Columns = tdef.Columns,
            Indexes = tdef.Indexes,
            IsSystem = isSystem,
        };
    }

    private static int ColumnIndex(IReadOnlyList<ColumnDef> columns, string name)
    {
        for (int i = 0; i < columns.Count; i++)
            if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return columns[i].Index;
        throw new InvalidOperationException($"MSysObjects is missing the '{name}' column.");
    }
}
