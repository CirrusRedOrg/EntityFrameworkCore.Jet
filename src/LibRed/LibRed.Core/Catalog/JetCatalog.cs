using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using System.Globalization;

namespace LibRed.Catalog;

/// <summary>
/// Reads the system catalog (<c>MSysObjects</c>) to enumerate the tables in a database.
/// </summary>
/// <remarks>
/// Bootstrap: MSysObjects' own TDEF is at a fixed page (<see cref="Formats.JetFormatBase.CatalogPage"/>),
/// so we build a <see cref="TableDef"/> for it from that page and read its rows like any
/// other table. For a table object, the row's <c>Id</c> is its TDEF page number.
/// </remarks>
public sealed class JetCatalog(PageChannel channel, int catalogPage = 2)
{
    // MSysObjects TDEF page — the catalog root. Normally read from the page-0 bootstrap pointer (0x20)
    // and passed in by JetDatabase; defaults to Access's canonical page 2 for direct construction.
    private readonly int _catalogPage = catalogPage;

    /// <summary>MSysObjects.Type value for a table object.</summary>
    private const short ObjectTypeTable = 1;

    /// <summary>MSysObjects.Flags bits marking a system object (`0x80000000` system, `0x00000002`
    /// system attribute).</summary>
    private const uint SystemObjectFlags = 0x80000002;

    /// <summary>MSysObjects.Flags bit marking a <b>hidden</b> object (`0x08`, observed on Access's
    /// nav-pane tables and on EFCore.Jet's `#Dual` helper). Access excludes hidden objects from its
    /// user-table list, so we treat them as non-user too.</summary>
    private const uint HiddenObjectFlags = 0x00000008;

    private readonly PageChannel _channel = channel;
    private List<TableDef>? _tables;
    private List<ForeignKey>? _relationships;
    private Dictionary<string, string>? _views;
    private Dictionary<string, StoredActionQuery>? _actionQueries;
    private Dictionary<string, IReadOnlyList<StoredQueryParameter>>? _queryParameters;
    private long _seenSchemaGeneration = channel.SchemaGeneration;

    /// <summary>All tables in the database (user and system).</summary>
    public IReadOnlyList<TableDef> Tables { get { EnsureFresh(); return _tables ??= LoadTables(); } }

    /// <summary>Views (stored simple-SELECT queries) as name → reconstructed SELECT SQL, rebuilt from
    /// each view's MSysQueries rows. Complex/system queries that don't reconstruct are omitted.</summary>
    public IReadOnlyDictionary<string, string> Views { get { EnsureFresh(); EnsureStoredQueries(); return _views!; } }

    /// <summary>Stored action queries (a CREATE PROCEDURE body that is not a SELECT) as name → readback.
    /// A supported query (CREATE/DROP TABLE, INSERT … VALUES) carries executable SQL; an unsupported one
    /// (INSERT … SELECT, etc.) carries only a reason and throws when LibRed is asked to execute it.</summary>
    public IReadOnlyDictionary<string, StoredActionQuery> ActionQueries { get { EnsureFresh(); EnsureStoredQueries(); return _actionQueries!; } }

    /// <summary>A stored query's declared parameter names in declaration order (its <c>Attribute=2</c> rows).
    /// Used to bind an <c>EXECUTE proc a, b</c>'s positional arguments to the procedure's named parameters.
    /// Empty for a query with no parameters.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<StoredQueryParameter>> QueryParameters { get { EnsureFresh(); EnsureStoredQueries(); return _queryParameters!; } }

    /// <summary>Drops the cached catalog so a freshly created table is picked up on next read.</summary>
    public void Invalidate(bool markChanged = true)
    {
        _tables = null;
        _relationships = null;
        _views = null;
        _actionQueries = null;
        _queryParameters = null;
        _seenSchemaGeneration = _channel.SchemaGeneration;
        if (markChanged) _channel.MarkSchemaChanged();
    }

    private void EnsureFresh()
    {
        long generation = _channel.SchemaGeneration;
        if (generation == _seenSchemaGeneration) return;
        Invalidate(markChanged: false);
        _seenSchemaGeneration = generation;
    }

    /// <summary>All relationships (foreign keys) defined in the database.</summary>
    public IReadOnlyList<ForeignKey> Relationships { get { EnsureFresh(); return _relationships ??= LoadRelationships(); } }

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
        TableDef catalogDef = ReadTableDefinition(_catalogPage, "MSysObjects", isSystem: true);
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
            definition.ObjectFlags = flags;
            // Attach column DefaultValue and table CHECK properties from the extended-properties (LvProp) blob.
            if (row[lvpropIndex] is byte[] { Length: > 0 } blob)
            {
                var defaults = PropertyBlob.ReadColumnDefaults(blob);
                var required = PropertyBlob.ReadRequiredColumns(blob);
                foreach (ColumnDef column in definition.Columns)
                {
                    if (defaults.TryGetValue(column.Name, out string? value))
                        column.DefaultValue = value;
                    if (required.Contains(column.Name))
                        column.IsNullable = false;
                    (column.ValidationRule, column.ValidationText) = PropertyBlob.ReadValidation(blob, column.Name);
                    // A calculated column's expression and REAL result type live here, not in the descriptor
                    // (§3.4a). Without them the stored payload can only be guessed from its width, which is
                    // wrong whenever the declared and expression types differ.
                    if (column.IsCalculated)
                        (column.CalculatedExpression, column.CalculatedResultType) =
                            PropertyBlob.ReadCalculated(blob, column.Name);
                }

                var checks = PropertyBlob.ReadCheckConstraints(blob);
                if (checks.Count > 0) definition.CheckConstraints = checks;

                (definition.ValidationRule, definition.ValidationText) = PropertyBlob.ReadValidation(blob, "");
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
                IsEnforced: (kvp.Value.Flags & RelationshipFlags.DontEnforce) == 0,
                CascadeUpdate: (kvp.Value.Flags & RelationshipFlags.UpdateCascade) != 0,
                CascadeDelete: (kvp.Value.Flags & RelationshipFlags.DeleteCascade) != 0,
                DeleteSetNull: (kvp.Value.Flags & RelationshipFlags.DeleteSetNull) != 0))
            .ToList();
    }

    private void EnsureStoredQueries()
    {
        if (_views is not null) return;
        _views = [with(StringComparer.OrdinalIgnoreCase)];
        _actionQueries = [with(StringComparer.OrdinalIgnoreCase)];
        _queryParameters = [with(StringComparer.OrdinalIgnoreCase)];

        TableDef? mqDef = FindTable("MSysQueries");
        TableDef? objDef = FindTable("MSysObjects");
        if (mqDef is null || objDef is null) return;

        // Group MSysQueries rows by ObjectId.
        var mq = mqDef.Columns;
        int oid = ColumnIndex(mq, "ObjectId"), attr = ColumnIndex(mq, "Attribute"), expr = ColumnIndex(mq, "Expression"),
            flag = ColumnIndex(mq, "Flag"), n1 = ColumnIndex(mq, "Name1"), n2 = ColumnIndex(mq, "Name2"), order = ColumnIndex(mq, "Order"),
            lvExtra = ColumnIndex(mq, "LvExtra");
        var byObject = new Dictionary<int, List<object?[]>>();
        foreach (object?[] row in new Table(_channel, mqDef).Rows())
            if (row[oid] is int id)
                (byObject.TryGetValue(id, out var list) ? list : byObject[id] = []).Add(row);

        // For each query object, reconstruct it: an action query → ActionQueries, otherwise a SELECT → Views.
        // The Attribute=1 row is the OPERATION row and its Flag is the query KIND, of which SELECT (1) is one
        // value -- so the row's presence does not make a query an action query. Access writes it on plain
        // SELECTs as well as on action queries, and it writes no such row at all for others (both shapes are
        // common in the wild), which is why the kind has to be read rather than inferred from the row.
        var oc = objDef.Columns;
        int objId = ColumnIndex(oc, "Id"), objType = ColumnIndex(oc, "Type"), objName = ColumnIndex(oc, "Name");
        foreach (object?[] row in new Table(_channel, objDef).Rows())
        {
            if (row[objType] is not short type || type != StoredQueryFormat.ObjectTypeQuery) continue;
            if (row[objId] is not int id || row[objName] is not string name) continue;
            if (!byObject.TryGetValue(id, out var rows)) continue;

            object?[]? operation = rows.FirstOrDefault(r => r[attr] is byte b && b == StoredQueryFormat.AttrOperation);
            short kind = operation?[flag] is short k ? k : StoredQueryFormat.OperationSelect;
            if (operation is not null && kind != StoredQueryFormat.OperationSelect)
                _actionQueries[name] = ReconstructAction(rows, attr, expr, flag, n1, n2, order, lvExtra);
            else if (Reconstruct(rows, attr, expr, flag, n1, n2, order, lvExtra) is { } sql)
                _views[name] = sql;

            // The declared parameters (Attribute=2 rows), in declaration order, for EXECUTE positional binding.
            // Each row's Flag is the parameter's Jet type code (0 for Access's untyped parameter).
            var parameters = rows.Where(r => r[attr] is byte b && b == StoredQueryFormat.AttrParameter)
                .OrderBy(r => r[order] is byte[] ob && ob.Length >= 4
                    ? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(ob) : 0)
                .Where(r => r[n1] is string)
                .Select(r =>
                {
                    JetDataType? type = r[flag] is short f and not 0 ? (JetDataType)(byte)f : null;
                    // The declared facets ride in LvExtra: a length for text, precision and scale packed
                    // together for a decimal.
                    var (size, precision, scale) = type is { } t
                        ? StoredQueryFormat.UnpackParameterFacets(t, r[lvExtra] as int?)
                        : (null, null, null);
                    return new StoredQueryParameter((string)r[n1]!, type, size, precision, scale);
                })
                .ToList();
            if (parameters.Count > 0) _queryParameters[name] = parameters;
        }
    }

    /// <summary>Rebuilds a stored action query's executable SQL from its MSysQueries rows. Handles every kind
    /// whose statement LibRed's engine can run — CREATE/DROP TABLE, INSERT (from VALUES or from a SELECT),
    /// UPDATE, DELETE and make-table; the rest return an unsupported reason.</summary>
    private static StoredActionQuery ReconstructAction(
        List<object?[]> rows, int attr, int expr, int flag, int n1, int n2, int order, int lvExtra)
    {
        static int Ord(object? v) => v is byte[] b && b.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b) : 0;
        List<object?[]> OfAttr(byte a) => rows.Where(r => r[attr] is byte b && b == a).OrderBy(r => Ord(r[order])).ToList();

        object?[]? action = rows.FirstOrDefault(r => r[attr] is byte b && b == StoredQueryFormat.AttrOperation);
        if (action is null) return new StoredActionQuery(null, "The stored action query has no action row.");

        short kind = action[flag] is short f ? f : (short)0;
        if (kind == StoredQueryFormat.ActionDdl)
            // The whole DDL statement is in Expression (Access stored it with a leading space).
            return new StoredActionQuery((action[expr] as string)?.TrimStart(), null);

        // Every remaining kind keeps its sources, predicate and declared parameters where a SELECT keeps them,
        // so they are read the same way. A query with no FROM source at all is an INSERT … VALUES, which has
        // none by definition.
        var source = BuildFromClause(rows, attr, expr, flag, n1, n2, order);
        string? where = source is { } s ? WhereClause(rows, attr, expr, order, s.Extra) : null;
        string Where() => where is null ? "" : $" WHERE {where}";
        var columns = OfAttr(StoredQueryFormat.AttrColumn);
        string declared = ParametersClause(rows, attr, flag, n1, order, lvExtra);

        switch (kind)
        {
            case StoredQueryFormat.ActionAppend:
                {
                    string target = Quote(action[n1] as string ?? "");
                    // A literal-value column (Flag 0x8000) is an INSERT … VALUES; a Flag-0 one reads its value
                    // from the query's own FROM source, which makes it an INSERT … SELECT. Both name the target
                    // column in Name2 and hold the value's text in Expression.
                    if (columns.Count == 0)
                        return new StoredActionQuery(null, "An append query with no columns is not executed by LibRed.");

                    string targetColumns = string.Join(", ", columns.Select(r => Quote(r[n2] as string ?? "")));
                    string values = string.Join(", ", columns.Select(r => r[expr] as string ?? "NULL"));

                    if (columns.All(r => r[flag] is short cf && cf == StoredQueryFormat.AppendValueFlag))
                        return source is null
                            ? new StoredActionQuery($"{declared}INSERT INTO {target} ({targetColumns}) VALUES ({values})", null)
                            : new StoredActionQuery(null, "An append query cannot take both literal values and a source.");

                    return source is { } appendSource
                        ? new StoredActionQuery(
                            $"{declared}INSERT INTO {target} ({targetColumns}) SELECT {values} FROM {appendSource.From}{Where()}", null)
                        : new StoredActionQuery(null, "An append query with no values and no source is not executed by LibRed.");
                }

            case StoredQueryFormat.ActionUpdate when source is { } updateSource:
                {
                    // One column row per assignment: Name2 is the target column — qualified when the update runs
                    // over a join — and Expression is the new value.
                    if (columns.Count == 0)
                        return new StoredActionQuery(null, "An update query with no assignments is not executed by LibRed.");
                    string assignments = string.Join(", ",
                        columns.Select(r => $"{Qualified(r[n2] as string ?? "")} = {r[expr] as string ?? "NULL"}"));
                    return new StoredActionQuery($"{declared}UPDATE {updateSource.From} SET {assignments}{Where()}", null);
                }

            case StoredQueryFormat.ActionDelete when source is { } deleteSource:
                {
                    // Access writes `DELETE <table>.* FROM …` when the query names the table's columns and
                    // `DELETE * FROM …` when it doesn't; the column row holds that `<table>.*` verbatim.
                    string what = columns.Count > 0 ? columns[0][expr] as string ?? "*" : "*";
                    return new StoredActionQuery($"{declared}DELETE {what} FROM {deleteSource.From}{Where()}", null);
                }

            case StoredQueryFormat.ActionMakeTable when source is { } intoSource:
                {
                    // The target is on the action row; a target in ANOTHER database file (Name2) is a shape
                    // LibRed has no statement for.
                    if (action[n2] is string external && external.Length > 0)
                        return new StoredActionQuery(null, $"A make-table query writing into '{external}' is not executed by LibRed.");

                    string selected = columns.Count == 0
                        ? "*"
                        : string.Join(", ", columns.Select(r =>
                            (r[n1] as string) is { } alias ? $"{r[expr] as string} AS {Quote(alias)}" : r[expr] as string ?? ""));
                    var groupBy = OfAttr(StoredQueryFormat.AttrGroupBy).Select(r => r[expr] as string ?? "").ToList();
                    string grouping = groupBy.Count > 0 ? $" GROUP BY {string.Join(", ", groupBy)}" : "";
                    string having = OfAttr(StoredQueryFormat.AttrHaving)
                        .Select(r => r[expr] as string).FirstOrDefault(s => !string.IsNullOrEmpty(s)) is { } h
                        ? $" HAVING {h}" : "";
                    return new StoredActionQuery(
                        $"{declared}SELECT {selected} INTO {Quote(action[n1] as string ?? "")} FROM {intoSource.From}{Where()}{grouping}{having}", null);
                }
        }

        // Everything else is stored but not executed. Name the kind: "not supported" that doesn't say what it
        // is leaves a caller no way to tell an unimplemented feature from an unreadable file.
        string reason = kind switch
        {
            StoredQueryFormat.ActionUpdate or StoredQueryFormat.ActionDelete or StoredQueryFormat.ActionMakeTable =>
                "The stored action query names no table.",
            StoredQueryFormat.ActionCrosstab => "Crosstab (TRANSFORM) stored queries are not executed by LibRed yet.",
            StoredQueryFormat.ActionPassThrough => "Pass-through stored queries are not executed by LibRed yet.",
            StoredQueryFormat.ActionUnion => "UNION stored queries are not executed by LibRed yet.",
            _ => $"Kind-{kind} stored queries are not executed by LibRed yet.",
        };
        return new StoredActionQuery(null, reason);
    }

    /// <summary>Bracket-quotes a stored name, so one with a space in it survives.</summary>
    private static string Quote(string name) => $"[{name}]";

    /// <summary>Bracket-quotes a name that may already be table-qualified, quoting each part — a stored
    /// assignment names its target as <c>Orders.ShipCountry</c> when the update runs over a join, and
    /// quoting that whole string would make it one nonexistent column.</summary>
    private static string Qualified(string name) =>
        name.Contains('.') ? string.Join(".", name.Split('.').Select(Quote)) : Quote(name);

    /// <summary>
    /// A stored query's FROM clause, and the join conditions that could not go in it. Access stores a query's
    /// sources the same way whatever its kind — one <c>0x05</c> row per table and one <c>0x07</c> per join
    /// condition, flat, with no grouping — so a SELECT, an UPDATE, a DELETE and an append-from-SELECT all
    /// rebuild their sources through here. Null when the query names no table at all.
    /// </summary>
    private static (string From, List<string> Extra)? BuildFromClause(
        List<object?[]> rows, int attr, int expr, int flag, int n1, int n2, int order)
    {
        static int Ord(object? v) => v is byte[] b && b.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b) : 0;
        IEnumerable<object?[]> OfAttr(byte a) => rows.Where(r => r[attr] is byte b && b == a).OrderBy(r => Ord(r[order]));

        // A derived-table source has its subquery SQL in Expression and no Name1; a named table uses Name1.
        var tables = OfAttr(StoredQueryFormat.AttrTable)
            .Select(r => (Table: r[n1] as string ?? "", Alias: r[n2] as string, Sub: r[n1] is null ? r[expr] as string : null)).ToList();
        if (tables.Count == 0) return null;
        var joins = OfAttr(StoredQueryFormat.AttrJoin)
            .Select(r => (Cond: r[expr] as string ?? "", Kind: r[flag] is short f ? f : (short)1,
                          Left: r[n1] as string ?? "", Right: r[n2] as string ?? "")).ToList();

        static string Ident(string s) => $"[{s}]";
        static string Render((string Table, string? Alias, string? Sub) t) =>
            t.Sub is { } sub ? $"({sub}) AS {Ident(t.Alias!)}"
            : t.Alias is not null && !string.Equals(t.Alias, t.Table, StringComparison.OrdinalIgnoreCase)
                ? $"{Ident(t.Table)} AS {Ident(t.Alias)}" : Ident(t.Table);
        string Key((string Table, string? Alias, string? Sub) t) => t.Alias ?? t.Table;

        // Build a JOIN chain by a spanning walk: only add a join once one of its two tables is already in
        // scope, bringing the other into scope (so a flat set of joins from a nested source rebuilds a valid
        // left-deep tree). If the new table is the condition's *left* side, a LEFT/RIGHT join flips.
        var from = new System.Text.StringBuilder(Render(tables[0]));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Key(tables[0]) };
        var pending = joins.ToList();
        for (bool progress = true; progress;)
        {
            progress = false;
            for (int i = 0; i < pending.Count; i++)
            {
                var j = pending[i];
                bool leftIn = used.Contains(j.Left), rightIn = used.Contains(j.Right);
                if (leftIn == rightIn) continue; // both already joined (extra condition) or neither reachable yet
                string newKey = leftIn ? j.Right : j.Left;
                int ti = tables.FindIndex(t => string.Equals(Key(t), newKey, StringComparison.OrdinalIgnoreCase));
                if (ti < 0) continue;

                short kind = !leftIn && j.Kind is 2 or 3 ? (short)(j.Kind == 2 ? 3 : 2) : j.Kind; // flip on reversed order
                string kw = kind switch { 2 => "LEFT", 3 => "RIGHT", _ => "INNER" };
                from.Append(CultureInfo.InvariantCulture, $" {kw} JOIN {Render(tables[ti])} ON {j.Cond}");
                used.Add(newKey);
                pending.RemoveAt(i);
                progress = true;
                break;
            }
        }
        // Any tables the joins didn't reach are comma (cross) joins.
        foreach (var t in tables.Where(t => !used.Contains(Key(t))))
            from.Append(CultureInfo.InvariantCulture, $", {Render(t)}");

        // Joins whose two tables were both already in scope become extra WHERE conditions (a cyclic graph).
        return (from.ToString(), pending.Select(j => j.Cond).ToList());
    }

    /// <summary>The leading <c>PARAMETERS name Type, …;</c> clause a query with declared parameters (its
    /// <c>0x02</c> rows: Name1=name, Flag=Jet type code) is rebuilt with, so the parser lowers references to
    /// those names in the body into engine parameters; empty for a query declaring none. An action query
    /// declares them exactly as a SELECT does.</summary>
    private static string ParametersClause(List<object?[]> rows, int attr, int flag, int n1, int order, int lvExtra)
    {
        static int Ord(object? v) => v is byte[] b && b.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b) : 0;
        var parameters = rows
            .Where(r => r[attr] is byte b && b == StoredQueryFormat.AttrParameter)
            .OrderBy(r => Ord(r[order]))
            .Select(r => (Name: r[n1] as string, Code: r[flag] is short f ? (byte)f : (byte)0, Extra: r[lvExtra] as int?))
            .Where(p => p.Name is not null)
            .Select(p => $"[{p.Name}] {AccessTypeName(p.Code)}{Facets(p.Code, p.Extra)}")
            .ToList();

        return parameters.Count == 0 ? "" : $"PARAMETERS {string.Join(", ", parameters)}; ";
    }

    /// <summary>A declared parameter's facets as the type name's suffix — <c>(50)</c> for a text length,
    /// <c>(18,4)</c> for a decimal's precision and scale — so the rebuilt clause declares what was declared.
    /// Empty where the row records none.</summary>
    private static string Facets(byte code, int? lvExtra)
    {
        if (code == 0) return "";   // the untyped parameter, rendered as the keyword Value
        var (size, precision, scale) = StoredQueryFormat.UnpackParameterFacets((JetDataType)code, lvExtra);
        return size is { } length ? $"({length})"
            : precision is { } p ? $"({p},{scale ?? 0})"
            : "";
    }

    /// <summary>The WHERE clause: the query's stored predicate (the <c>0x08</c> row) and any join condition
    /// <see cref="BuildFromClause"/> could not place, ANDed together. The stored predicate is only
    /// parenthesised when something is being ANDed onto it — wrapping a lone predicate adds a paren pair
    /// Access never wrote, which is a needless difference from its own SQL.</summary>
    private static string? WhereClause(List<object?[]> rows, int attr, int expr, int order, List<string> extra)
    {
        static int Ord(object? v) => v is byte[] b && b.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b) : 0;
        string? where = rows
            .Where(r => r[attr] is byte b && b == StoredQueryFormat.AttrWhere)
            .OrderBy(r => Ord(r[order]))
            .Select(r => r[expr] as string).FirstOrDefault();

        return where is null ? (extra.Count > 0 ? string.Join(" AND ", extra) : null)
            : extra.Count == 0 ? where
            : string.Join(" AND ", extra.Prepend($"({where})"));
    }

    /// <summary>Rebuilds a simple-SELECT view's SQL from its MSysQueries rows; null if it uses an
    /// attribute we don't reconstruct (a non-simple query).</summary>
    private static string? Reconstruct(
        List<object?[]> rows, int attr, int expr, int flag, int n1, int n2, int order, int lvExtra)
    {
        static int Ord(object? v) => v is byte[] b && b.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b) : 0;
        IEnumerable<object?[]> OfAttr(byte a) => rows.Where(r => r[attr] is byte b && b == a).OrderBy(r => Ord(r[order]));

        // Bail out if the query uses attributes beyond a simple SELECT: a pass-through connection string
        // (0x04) or complex-type data (0x0C) mean this is not a shape we can render.
        // AttrOperation is in the list because a SELECT may carry one — the caller has already checked that
        // its kind IS SELECT, so reaching here with any other kind is impossible.
        var known = new byte[] { StoredQueryFormat.AttrType, StoredQueryFormat.AttrOperation, StoredQueryFormat.AttrParameter, StoredQueryFormat.AttrOption, StoredQueryFormat.AttrTable, StoredQueryFormat.AttrColumn, StoredQueryFormat.AttrJoin, StoredQueryFormat.AttrWhere, StoredQueryFormat.AttrGroupBy, StoredQueryFormat.AttrHaving, StoredQueryFormat.AttrOrderBy, 0xFF };
        if (rows.Any(r => r[attr] is byte b && !known.Contains(b))) return null;

        // A column row's Name1 (when present) is its output alias.
        var columns = OfAttr(StoredQueryFormat.AttrColumn)
            .Select(r => (r[n1] as string) is { } a ? $"{r[expr] as string} AS [{a}]" : r[expr] as string ?? "").ToList();
        // A query with no table rows has no FROM clause, which Access allows and stores this way (`SELECT 1
        // AS n`). One with neither tables nor columns says nothing at all, and is not a query we can rebuild.
        var source = BuildFromClause(rows, attr, expr, flag, n1, n2, order);
        if (source is null && columns.Count == 0) return null;
        // No column rows at all is Access's "SELECT *" -- the shape every auto-generated form/report
        // record-source query takes. Treating it as unreconstructable dropped those queries silently.
        if (columns.Count == 0) columns.Add("*");

        // The option bits are cumulative and can share one row, so test each as a bit across all of them.
        short options = 0;
        foreach (object?[] r in OfAttr(StoredQueryFormat.AttrOption))
            if (r[flag] is short f) options |= f;

        // DISTINCT and DISTINCTROW are separate bits and separate keywords: DISTINCT dedupes output rows,
        // DISTINCTROW dedupes by the underlying contributing rows. Emitting one for the other changes results.
        bool distinct = (options & StoredQueryFormat.FlagDistinct) != 0;
        bool distinctRow = (options & StoredQueryFormat.FlagDistinctRow) != 0;
        // TOP n: an AttrOption row with the TOP bit; the count is in Name1. The PERCENT bit rides alongside
        // it (48 = TOP PERCENT), and dropping it turns "TOP 10 PERCENT" into "TOP 10" -- silently wrong.
        string? top = OfAttr(StoredQueryFormat.AttrOption)
            .Where(r => r[flag] is short f && (f & StoredQueryFormat.FlagTop) != 0)
            .Select(r => r[n1] as string).FirstOrDefault();
        bool percent = (options & StoredQueryFormat.FlagPercent) != 0;
        // ORDER BY: one AttrOrderBy row per key (Expression = column, Name1 = "d" for descending).
        var orderBy = OfAttr(StoredQueryFormat.AttrOrderBy)
            .Select(r => (r[expr] as string ?? "") + (string.Equals(r[n1] as string, "d", StringComparison.OrdinalIgnoreCase) ? " DESC" : ""))
            .Where(s => s.Length > 0).ToList();
        var groupBy = OfAttr(StoredQueryFormat.AttrGroupBy).Select(r => r[expr] as string ?? "").ToList();
        // HAVING: a single AttrHaving row holding the predicate verbatim, carrying no flag of its own.
        string? having = OfAttr(StoredQueryFormat.AttrHaving)
            .Select(r => r[expr] as string).FirstOrDefault(s => !string.IsNullOrEmpty(s));
        string? whereClause = WhereClause(rows, attr, expr, order, source?.Extra ?? []);

        var sql = new System.Text.StringBuilder(ParametersClause(rows, attr, flag, n1, order, lvExtra));
        sql.Append("SELECT ");
        if (distinctRow) sql.Append("DISTINCTROW ");
        else if (distinct) sql.Append("DISTINCT ");
        if (top is not null) sql.Append("TOP ").Append(top).Append(percent ? " PERCENT " : " ");
        sql.Append(string.Join(", ", columns));
        if (source is { } from) sql.Append(" FROM ").Append(from.From);
        if (whereClause is not null) sql.Append(" WHERE ").Append(whereClause);
        if (groupBy.Count > 0) sql.Append(" GROUP BY ").Append(string.Join(", ", groupBy));
        if (having is not null) sql.Append(" HAVING ").Append(having);
        if (orderBy.Count > 0) sql.Append(" ORDER BY ").Append(string.Join(", ", orderBy));
        return sql.ToString();
    }

    /// <summary>The Access SQL type name for a stored parameter's Jet type code (inverse of the CREATE TABLE
    /// type mapper), used to render a read-back PARAMETERS clause. Code 0 is not a Jet type at all: it is
    /// Access's untyped parameter, which it renders as the keyword <c>Value</c>.</summary>
    private static string AccessTypeName(byte code) => code == 0 ? "Value" : (JetDataType)code switch
    {
        JetDataType.Boolean => "YESNO",
        JetDataType.Byte => "BYTE",
        JetDataType.Int16 => "SHORT",
        JetDataType.Int32 => "LONG",
        JetDataType.Int64 => "BIGINT",
        JetDataType.Single => "SINGLE",
        JetDataType.Double => "DOUBLE",
        JetDataType.Currency => "CURRENCY",
        JetDataType.DateTime => "DATETIME",
        JetDataType.Text => "TEXT",
        JetDataType.Memo => "MEMO",
        JetDataType.Guid => "GUID",
        JetDataType.Binary => "BINARY",
        JetDataType.FixedPoint => "DECIMAL",
        JetDataType.Ole => "OLEOBJECT",
        _ => "TEXT",
    };

    /// <summary>Builds a <see cref="TableDef"/> straight from a TDEF page, bypassing the catalog — used
    /// during database creation to seed the system tables before they are self-registered.</summary>
    internal TableDef ReadTableDefinitionAt(int definitionPage, string name, bool isSystem) =>
        ReadTableDefinition(definitionPage, name, isSystem);

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
            LogicalIndexes = tdef.LogicalIndexes,
            RowCount = tdef.RowCount,
            VariableColumnCount = tdef.VariableColumnCount,
            ComplexAutoNumber = tdef.ComplexAutoNumber,
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