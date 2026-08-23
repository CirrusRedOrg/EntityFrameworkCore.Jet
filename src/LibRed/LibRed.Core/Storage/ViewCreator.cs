using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;

namespace LibRed.Storage;

/// <summary>
/// Creates a view (a stored SELECT query) the way Access does — an <c>MSysObjects</c> row of type 5 with
/// a negative synthetic id, plus the query decomposed into <c>MSysQueries</c> rows (one per column / table
/// / join / where, bracketed by a type row and an end row). Verified byte-faithful against ACE for the
/// "simple SELECT" views a view is allowed to contain.
/// </summary>
public sealed class ViewCreator(PageChannel channel, JetCatalog catalog)
{
    private const int ViewFlags = 0x10000000;         // a SELECT query / view
    private const int AppendFlags = 0x10000040;       // an INSERT (append) query
    private const int DataDefinitionFlags = 0x10000060; // a CREATE/DROP TABLE (data-definition) query
    private static readonly byte[] DefaultOwner = [0x69, 0x0C];
    private static readonly byte[] AdminSid = [0x68, 0x0C];

    // MSysACEs permission rows a QUERY/VIEW object gets — distinct from a table's (owner and admin both get
    // full 0xFFEFF on a table). Verified against every Northwind view: owner (0x690C) = 0xF00FE, admin/users
    // (0x680C) = 0xFFEFF. Without these, Access opens the file but warns about permissions on the query.
    private const int QueryOwnerAcm = 0xF00FE;  // 983294
    private const int QueryAdminAcm = 0xFFEFF;  // 1048319


    private readonly PageChannel _channel = channel;
    private readonly JetCatalog _catalog = catalog;

    public void Create(string name, ViewSpec spec)
    {
        int objectId = AllocateObject(name, ViewFlags);
        AddQueryRows(objectId, spec);
    }

    /// <summary>Persists a stored action query (a non-SELECT CREATE PROCEDURE body) byte-faithfully.</summary>
    public void CreateAction(string name, ActionQuerySpec spec)
    {
        int flags = spec.Kind == ActionQueryKind.DataDefinition ? DataDefinitionFlags : AppendFlags;
        int objectId = AllocateObject(name, flags);
        AddActionRows(objectId, spec);
    }

    /// <summary>Reserves the next free query object id, checks the name is unique, and writes the MSysObjects
    /// row with the given <paramref name="flags"/> (which distinguish view / append / data-definition).</summary>
    private int AllocateObject(string name, int flags)
    {
        TableDef msysObjects = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");
        int idIndex = ColumnIndex(msysObjects, "Id");
        int nameIndex = ColumnIndex(msysObjects, "Name");

        // A query's name must be unique among all objects (it also cannot equal an existing table name);
        // find the next free negative id (queries increment from 0x80000000) in one scan.
        int nextId = unchecked((int)0x80000000);
        foreach (object?[] row in new Table(_channel, msysObjects).Rows())
        {
            if (string.Equals(row[nameIndex] as string, name, StringComparison.OrdinalIgnoreCase))
                throw new SchemaObjectExistsException($"An object named '{name}' already exists.", name);
            if (row[idIndex] is int id && id < 0 && id >= nextId) nextId = id + 1;
        }

        AddObjectRow(msysObjects, name, nextId, flags);
        AddPermissionRows(nextId);
        return nextId;
    }

    /// <summary>
    /// Adds the two MSysACEs permission rows Access writes for a new query/view object — owner (0x690C) at
    /// ACM 0xF00FE and admin/users (0x680C) at ACM 0xFFEFF — maintaining the ObjectId index so Access's
    /// security check finds them. Without these Access warns about permissions when opening the query.
    /// </summary>
    private void AddPermissionRows(int objectId)
    {
        TableDef msysAces = _catalog.FindTable("MSysACEs")
            ?? throw new InvalidOperationException("MSysACEs catalog table was not found.");

        foreach ((byte[] sid, int acm) in new[] { (DefaultOwner, QueryOwnerAcm), (AdminSid, QueryAdminAcm) })
        {
            var values = new object?[msysAces.Columns.Count];
            SetByName(msysAces, values, "ACM", acm);
            SetByName(msysAces, values, "FInheritable", false);
            SetByName(msysAces, values, "ObjectId", objectId);
            SetByName(msysAces, values, "SID", sid);
            new RowInserter(_channel, msysAces).Insert(values, updateIndexes: true);
        }
    }

    private void AddObjectRow(TableDef msysObjects, string name, int objectId, int flags)
    {
        DateTime now = DateTime.Now;
        var values = new object?[msysObjects.Columns.Count];
        SetByName(msysObjects, values, "Id", objectId);
        SetByName(msysObjects, values, "ParentId", CatalogFormat.ObjectContainerParentId);
        SetByName(msysObjects, values, "Type", StoredQueryFormat.ObjectTypeQuery);
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
        if (spec.Kind == ActionQueryKind.DataDefinition)
        {
            // The whole DDL statement is stored verbatim in one row; Access records it with a leading space.
            Row(mq, objectId, StoredQueryFormat.AttrAction, order: 1, flag: StoredQueryFormat.ActionDdl, expression: " " + spec.DdlSql);
        }
        else
        {
            Row(mq, objectId, StoredQueryFormat.AttrAction, order: 1, flag: StoredQueryFormat.ActionAppend, name1: spec.TargetTable);
            // Each appended column: Name2 = target column, Expression = the (literal) value; the 0x8000 flag
            // marks a VALUES append (as opposed to an INSERT … SELECT, whose columns carry Flag 0).
            var values = spec.Values ?? [];
            for (int i = 0; i < values.Count; i++)
                Row(mq, objectId, StoredQueryFormat.AttrColumn, order: i + 1, flag: StoredQueryFormat.AppendValueFlag,
                    expression: values[i].ValueExpression, name2: values[i].Column);
        }
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
        for (int i = 0; i < (spec.Parameters?.Count ?? 0); i++)
            Row(mq, objectId, StoredQueryFormat.AttrParameter, order: i + 1,
                flag: spec.Parameters![i].TypeCode, name1: spec.Parameters[i].Name);
        // DISTINCT and TOP are both StoredQueryFormat.AttrFlag (0x03) rows, distinguished by their Flag bits; a TOP row also
        // carries the count in Name1. Give them distinct Order values so the composite PK stays unique.
        int flagOrder = 1;
        if (spec.Distinct)
            Row(mq, objectId, StoredQueryFormat.AttrFlag, order: flagOrder++, flag: StoredQueryFormat.FlagDistinct);
        if (spec.Top is { } top)
            Row(mq, objectId, StoredQueryFormat.AttrFlag, order: flagOrder++, flag: StoredQueryFormat.FlagTop, name1: top.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (int i = 0; i < spec.Tables.Count; i++)
        {
            ViewTableSpec t = spec.Tables[i];
            // A derived table stores its subquery SQL in Expression (Name1 empty); a named table uses Name1.
            if (t.SubquerySql is { } sub)
                Row(mq, objectId, StoredQueryFormat.AttrTable, order: i + 1, expression: sub, name2: t.Alias);
            else
                Row(mq, objectId, StoredQueryFormat.AttrTable, order: i + 1, name1: t.Table, name2: t.Alias);
        }
        for (int i = 0; i < spec.Columns.Count; i++)
            Row(mq, objectId, StoredQueryFormat.AttrColumn, order: i + 1, flag: 0,
                expression: spec.Columns[i].Expression, name1: spec.Columns[i].Alias);
        for (int i = 0; i < spec.Joins.Count; i++)
        {
            ViewJoinSpec j = spec.Joins[i];
            Row(mq, objectId, StoredQueryFormat.AttrJoin, order: i + 1, flag: (short)j.Kind, expression: j.Condition, name1: j.LeftAlias, name2: j.RightAlias);
        }
        if (spec.Where is { } where)
            Row(mq, objectId, StoredQueryFormat.AttrWhere, order: 1, expression: where);
        for (int i = 0; i < (spec.GroupBy?.Count ?? 0); i++)
            Row(mq, objectId, StoredQueryFormat.AttrGroupBy, order: i + 1, flag: 0, expression: spec.GroupBy![i]);
        for (int i = 0; i < (spec.OrderBy?.Count ?? 0); i++)
            Row(mq, objectId, StoredQueryFormat.AttrOrderBy, order: i + 1, expression: spec.OrderBy![i].Expression,
                name1: spec.OrderBy[i].Descending ? "d" : null);
    }

    private void Row(TableDef mq, int objectId, byte attribute, int order,
        short? flag = null, string? expression = null, string? name1 = null, string? name2 = null)
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
