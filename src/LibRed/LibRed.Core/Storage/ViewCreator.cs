using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using System.Buffers.Binary;

namespace LibRed.Storage;

/// <summary>
/// Creates a view (a stored SELECT query) the way Access does — an <c>MSysObjects</c> row of type 5 with
/// a negative synthetic id, plus the query decomposed into <c>MSysQueries</c> rows (one per column / table
/// / join / where, bracketed by a type row and an end row). Verified byte-faithful against ACE for the
/// "simple SELECT" views a view is allowed to contain.
/// </summary>
public sealed class ViewCreator(PageChannel channel, JetCatalog catalog)
{
    // MSysObjects.Flags for a stored query: 0x10000000 plus the kind, and the kind byte is DAO's own
    // QueryDef.Type value (verified vs ACE for all six: crosstab 16, delete 32, update 48, append 64,
    // make-table 80, data-definition 96) — not the Flag the MSysQueries action row carries, which numbers
    // the kinds differently.
    private const int ViewFlags = 0x10000000;           // a SELECT query / view (DAO type 0)
    private const int DeleteFlags = 0x10000020;
    private const int UpdateFlags = 0x10000030;
    private const int AppendFlags = 0x10000040;         // an INSERT (append) query
    private const int MakeTableFlags = 0x10000050;
    private const int DataDefinitionFlags = 0x10000060; // a CREATE/DROP TABLE (data-definition) query
    private static readonly byte[] DefaultOwner = [0x69, 0x0C];
    private static readonly byte[] AdminSid = [0x68, 0x0C];

    // MSysACEs permission rows a QUERY/VIEW object gets — distinct from a table's (owner and admin both get
    // full 0xFFEFF on a table). Verified against every Northwind view: owner (0x690C) = 0xF00FE, admin/users
    // (0x680C) = 0xFFEFF. Without these, Access opens the file but warns about permissions on the query.
    private const int QueryOwnerAcm = 0xF00FE;  // 983294
    private const int QueryAdminAcm = 0xFFEFF;  // 1048319

    // A relationship object's MSysACEs rows (verified vs ACE): owner 0xF00FE as a query's, admin 0xFFFFF.
    private const int RelationshipOwnerAcm = 0xF00FE;  // 983294
    private const int RelationshipAdminAcm = 0xFFFFF;  // 1048575


    private readonly PageChannel _channel = channel;
    private readonly JetCatalog _catalog = catalog;

    public void Create(string name, ViewSpec spec)
    {
        int objectId = AllocateQueryObject(name, ViewFlags);
        AddQueryRows(objectId, spec);
    }

    /// <summary>Persists a stored action query (a non-SELECT CREATE PROCEDURE body) byte-faithfully.</summary>
    public void CreateAction(string name, ActionQuerySpec spec)
    {
        int flags = spec.Kind switch
        {
            ActionQueryKind.DataDefinition => DataDefinitionFlags,
            ActionQueryKind.Append => AppendFlags,
            ActionQueryKind.Update => UpdateFlags,
            ActionQueryKind.Delete => DeleteFlags,
            ActionQueryKind.MakeTable => MakeTableFlags,
            _ => throw new NotSupportedException($"Action query kind {spec.Kind} is not stored yet."),
        };
        int objectId = AllocateQueryObject(name, flags);
        AddActionRows(objectId, spec);
    }

    /// <summary>
    /// Records a relationship as ACE does (verified): a type-8 <c>MSysObjects</c> object in the Relationships
    /// container, named after it, flags 0, with the next high-bit id — the sequence queries draw from, so the two
    /// interleave, and a dropped one's id is taken again — and its two <c>MSysACEs</c> rows. Refuses a name
    /// another relationship has, as ACE does; a table or query may share it.
    /// </summary>
    public void CreateRelationshipObject(string name) =>
        AllocateObject(name, CatalogFormat.ObjectTypeRelationship, CatalogFormat.RelationshipContainerParentId, flags: 0,
            RelationshipOwnerAcm, RelationshipAdminAcm);

    private int AllocateQueryObject(string name, int flags) =>
        AllocateObject(name, StoredQueryFormat.ObjectTypeQuery, CatalogFormat.ObjectContainerParentId, flags,
            QueryOwnerAcm, QueryAdminAcm);

    /// <summary>Reserves the next free high-bit object id, checks the name is free, and writes
    /// the MSysObjects row and its two MSysACEs rows. For a query the <paramref name="flags"/> distinguish view /
    /// append / data-definition.</summary>
    private int AllocateObject(string name, short type, int parentId, int flags, int ownerAcm, int adminAcm)
    {
        TableDef msysObjects = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");
        int idIndex = ColumnIndex(msysObjects, "Id");
        int nameIndex = ColumnIndex(msysObjects, "Name");
        int parentIndex = ColumnIndex(msysObjects, "ParentId");

        // A query's name must be unique among all objects (it also cannot equal an existing table name); a
        // relationship's only among the relationships, as ACE has it. Find the next free negative id (they
        // increment from 0x80000000) in the same scan.
        bool relationship = parentId == CatalogFormat.RelationshipContainerParentId;
        int nextId = unchecked((int)0x80000000);
        foreach (object?[] row in new Table(_channel, msysObjects).Rows())
        {
            if ((!relationship || row[parentIndex] is int parent && parent == parentId)
                && string.Equals(row[nameIndex] as string, name, StringComparison.OrdinalIgnoreCase))
                throw new SchemaObjectExistsException(relationship
                    ? $"There is already a relationship named '{name}' in the current database."
                    : $"An object named '{name}' already exists.", name);
            if (row[idIndex] is int id && id < 0 && id >= nextId) nextId = id + 1;
        }

        AddObjectRow(msysObjects, name, nextId, type, parentId, flags);
        AddPermissionRows(nextId, ownerAcm, adminAcm);
        return nextId;
    }

    /// <summary>
    /// Adds the two MSysACEs permission rows Access writes for a new query/view or relationship object — owner
    /// (0x690C) and admin/users (0x680C) — maintaining the ObjectId index so Access's security check finds them.
    /// Without these Access warns about permissions when opening a query.
    /// </summary>
    private void AddPermissionRows(int objectId, int ownerAcm, int adminAcm)
    {
        TableDef msysAces = _catalog.FindTable("MSysACEs")
            ?? throw new InvalidOperationException("MSysACEs catalog table was not found.");

        foreach ((byte[] sid, int acm) in new[] { (DefaultOwner, ownerAcm), (AdminSid, adminAcm) })
        {
            var values = new object?[msysAces.Columns.Count];
            SetByName(msysAces, values, "ACM", acm);
            SetByName(msysAces, values, "FInheritable", false);
            SetByName(msysAces, values, "ObjectId", objectId);
            SetByName(msysAces, values, "SID", sid);
            new RowInserter(_channel, msysAces).Insert(values, updateIndexes: true);
        }
    }

    private void AddObjectRow(TableDef msysObjects, string name, int objectId, short type, int parentId, int flags)
    {
        DateTime now = DateTime.Now;
        var values = new object?[msysObjects.Columns.Count];
        SetByName(msysObjects, values, "Id", objectId);
        SetByName(msysObjects, values, "ParentId", parentId);
        SetByName(msysObjects, values, "Type", type);
        SetByName(msysObjects, values, "Name", name);
        SetByName(msysObjects, values, "Flags", flags);
        SetByName(msysObjects, values, "Owner", DefaultOwner);
        SetByName(msysObjects, values, "DateCreate", now);
        SetByName(msysObjects, values, "DateUpdate", now);
        new RowInserter(_channel, msysObjects).Insert(values, updateIndexes: true);
    }

    private void AddActionRows(int objectId, ActionQuerySpec spec)
    {
        TableDef mq = _catalog.FindTable("MSysQueries")
            ?? throw new InvalidOperationException("MSysQueries catalog table was not found.");

        Row(mq, objectId, StoredQueryFormat.AttrType, order: 1, flag: StoredQueryFormat.QueryTypeSelect);
        Row(mq, objectId, StoredQueryFormat.AttrEnd, order: 1);
        AddParameterRows(mq, objectId, spec.Parameters);

        if (spec.Kind == ActionQueryKind.DataDefinition)
        {
            // The whole DDL statement is stored verbatim in one row; Access records it with a leading space.
            // Nothing is decomposed: a data-definition query has no sources, columns or predicate.
            Row(mq, objectId, StoredQueryFormat.AttrOperation, order: 1, flag: StoredQueryFormat.ActionDdl,
                expression: " " + spec.DdlSql);
            return;
        }

        // The action row carries the kind, and the target table for the two kinds that write into one.
        short kind = spec.Kind switch
        {
            ActionQueryKind.Append => StoredQueryFormat.ActionAppend,
            ActionQueryKind.Update => StoredQueryFormat.ActionUpdate,
            ActionQueryKind.Delete => StoredQueryFormat.ActionDelete,
            ActionQueryKind.MakeTable => StoredQueryFormat.ActionMakeTable,
            _ => throw new NotSupportedException($"Action query kind {spec.Kind} is not stored yet."),
        };
        Row(mq, objectId, StoredQueryFormat.AttrOperation, order: 1, flag: kind,
            name1: spec.Kind is ActionQueryKind.Append or ActionQueryKind.MakeTable ? spec.TargetTable : null);

        // Sources first: Access processes the rows in order, and a derived-table source defines an alias the
        // column expressions reference (the same reason the view path writes tables before columns).
        AddSourceRows(mq, objectId, spec.Body);

        var values = spec.Values ?? [];
        switch (spec.Kind)
        {
            case ActionQueryKind.Append:
                // Name2 = target column, Expression = the value; the 0x8000 flag marks an INSERT … VALUES,
                // where a column fed by the query's own source carries Flag 0.
                for (int i = 0; i < values.Count; i++)
                    Row(mq, objectId, StoredQueryFormat.AttrColumn, order: i + 1,
                        flag: spec.Body is null ? StoredQueryFormat.AppendValueFlag : (short)0,
                        expression: values[i].ValueExpression, name2: values[i].Column);
                break;

            case ActionQueryKind.Update:
                // One row per SET assignment, stored exactly as an append's columns are.
                for (int i = 0; i < values.Count; i++)
                    Row(mq, objectId, StoredQueryFormat.AttrColumn, order: i + 1, flag: 0,
                        expression: values[i].ValueExpression, name2: values[i].Column);
                break;

            case ActionQueryKind.Delete:
                // `DELETE t.* FROM …` keeps that target verbatim in a single column row; `DELETE FROM …`
                // stores no column row at all, and Access renders it back as `DELETE * FROM …`.
                if (spec.DeleteTarget is { } target)
                    Row(mq, objectId, StoredQueryFormat.AttrColumn, order: 1, flag: 0, expression: target);
                break;

            case ActionQueryKind.MakeTable:
                // An ordinary output list: Expression = the column, Name1 = its alias.
                var columns = spec.Body?.Columns ?? [];
                for (int i = 0; i < columns.Count; i++)
                    Row(mq, objectId, StoredQueryFormat.AttrColumn, order: i + 1, flag: 0,
                        expression: columns[i].Expression, name1: columns[i].Alias);
                break;
        }

        AddJoinAndWhereRows(mq, objectId, spec.Body);
    }

    /// <summary>The <c>0x02</c> parameter rows, in declaration order — written identically for a view and for
    /// an action query.</summary>
    private void AddParameterRows(TableDef mq, int objectId, IReadOnlyList<ViewParameterSpec>? parameters)
    {
        for (int i = 0; i < (parameters?.Count ?? 0); i++)
        {
            ViewParameterSpec p = parameters![i];
            Row(mq, objectId, StoredQueryFormat.AttrParameter, order: i + 1,
                flag: p.TypeCode, name1: p.Name,
                lvExtra: StoredQueryFormat.PackParameterFacets((JetDataType)p.TypeCode, p.Size, p.Scale));
        }
    }

    /// <summary>The <c>0x05</c> FROM rows: a named table in Name1 (alias in Name2), or a derived table whose
    /// subquery SQL goes in Expression with Name1 empty.</summary>
    private void AddSourceRows(TableDef mq, int objectId, ViewSpec? body)
    {
        var tables = body?.Tables ?? [];
        for (int i = 0; i < tables.Count; i++)
        {
            ViewTableSpec t = tables[i];
            if (t.SubquerySql is { } sub)
                Row(mq, objectId, StoredQueryFormat.AttrTable, order: i + 1, expression: sub, name2: t.Alias);
            else
                Row(mq, objectId, StoredQueryFormat.AttrTable, order: i + 1, name1: t.Table, name2: t.Alias);
        }
    }

    /// <summary>The <c>0x07</c> join rows (condition, kind, and the two tables the condition names) and the
    /// single <c>0x08</c> WHERE row.</summary>
    private void AddJoinAndWhereRows(TableDef mq, int objectId, ViewSpec? body)
    {
        var joins = body?.Joins ?? [];
        for (int i = 0; i < joins.Count; i++)
        {
            ViewJoinSpec j = joins[i];
            Row(mq, objectId, StoredQueryFormat.AttrJoin, order: i + 1, flag: (short)j.Kind,
                expression: j.Condition, name1: j.LeftAlias, name2: j.RightAlias);
        }
        if (body?.Where is { } where)
            Row(mq, objectId, StoredQueryFormat.AttrWhere, order: 1, expression: where);
    }

    private void AddQueryRows(int objectId, ViewSpec spec)
    {
        TableDef mq = _catalog.FindTable("MSysQueries")
            ?? throw new InvalidOperationException("MSysQueries catalog table was not found.");

        // ACE's row order (verified against every Northwind view): type, end, distinct, TABLES, COLUMNS,
        // joins, where. Tables must precede columns — a derived-table source defines an alias that the
        // column expressions reference, and Access processes the rows in order, so columns-before-tables
        // makes it fail to run the view (it opens, but SELECT-from-view errors). Order fields are
        // per-attribute counters, independent of this insertion order.
        Row(mq, objectId, StoredQueryFormat.AttrType, order: 1, flag: StoredQueryFormat.QueryTypeSelect);
        Row(mq, objectId, StoredQueryFormat.AttrEnd, order: 1);
        // Declared parameters (CREATE PROCEDURE) come right after the End row, before the tables.
        AddParameterRows(mq, objectId, spec.Parameters);
        // DISTINCT and TOP are both StoredQueryFormat.AttrOption (0x03) rows, distinguished by their Flag bits; a TOP row also
        // carries the count in Name1. The bits are cumulative, so Access can put both on one row -- writing
        // them separately is equally valid and keeps the two spec fields independent here.
        // Give them distinct Order values so the composite PK stays unique.
        int flagOrder = 1;
        if (spec.Distinct)
            Row(mq, objectId, StoredQueryFormat.AttrOption, order: flagOrder++, flag: StoredQueryFormat.FlagDistinct);
        if (spec.Top is { } top)
            Row(mq, objectId, StoredQueryFormat.AttrOption, order: flagOrder++, flag: StoredQueryFormat.FlagTop, name1: top.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddSourceRows(mq, objectId, spec);
        for (int i = 0; i < spec.Columns.Count; i++)
            Row(mq, objectId, StoredQueryFormat.AttrColumn, order: i + 1, flag: 0,
                expression: spec.Columns[i].Expression, name1: spec.Columns[i].Alias);
        AddJoinAndWhereRows(mq, objectId, spec);
        for (int i = 0; i < (spec.GroupBy?.Count ?? 0); i++)
            Row(mq, objectId, StoredQueryFormat.AttrGroupBy, order: i + 1, flag: 0, expression: spec.GroupBy![i]);
        // The group filter carries no flag of its own, exactly as the WHERE row doesn't.
        if (spec.Having is { } having)
            Row(mq, objectId, StoredQueryFormat.AttrHaving, order: 1, expression: having);
        for (int i = 0; i < (spec.OrderBy?.Count ?? 0); i++)
            Row(mq, objectId, StoredQueryFormat.AttrOrderBy, order: i + 1, expression: spec.OrderBy![i].Expression,
                name1: spec.OrderBy[i].Descending ? "d" : null);
    }

    private void Row(TableDef mq, int objectId, byte attribute, int order,
        short? flag = null, string? expression = null, string? name1 = null, string? name2 = null,
        int? lvExtra = null)
    {
        var values = new object?[mq.Columns.Count];
        SetByName(mq, values, "ObjectId", objectId);
        SetByName(mq, values, "Attribute", attribute);
        var orderBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(orderBytes, order); // 4-byte big-endian per-attribute counter
        SetByName(mq, values, "Order", orderBytes);
        if (flag is { } f) SetByName(mq, values, "Flag", f);
        if (expression is not null) SetByName(mq, values, "Expression", expression);
        if (name1 is not null) SetByName(mq, values, "Name1", name1);
        if (name2 is not null) SetByName(mq, values, "Name2", name2);
        // A declared parameter's length; ACE writes it here and renders the PARAMETERS clause from it.
        if (lvExtra is { } extra) SetByName(mq, values, "LvExtra", extra);
        new RowInserter(_channel, mq).Insert(values, updateIndexes: true);
    }

    private static void SetByName(TableDef table, object?[] values, string column, object value)
    {
        ColumnDef def = table.FindColumn(column)
            ?? throw new InvalidOperationException($"'{table.Name}' is missing the '{column}' column.");
        values[def.Index] = value;
    }

    private static int ColumnIndex(TableDef table, string column) =>
        (table.FindColumn(column) ?? throw new InvalidOperationException($"'{table.Name}' is missing the '{column}' column.")).Index;
}