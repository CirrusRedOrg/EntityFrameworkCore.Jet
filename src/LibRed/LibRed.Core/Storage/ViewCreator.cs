using System.Buffers.Binary;
using LibRed.Catalog;
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
    private const short ObjectTypeQuery = 5;
    private const int QueriesContainerParentId = 0x0F000001;
    private const int ViewFlags = 0x10000000;
    private static readonly byte[] DefaultOwner = [0x69, 0x0C];

    // MSysQueries attribute codes (Jackcess "query rows"), verified against ACE.
    private const byte AttrType = 0x00;    // Flag = 1 for a SELECT query
    private const byte AttrFlag = 0x03;    // Flag = 2 for DISTINCT
    private const byte AttrTable = 0x05;   // named table: Name1 = table, Name2 = alias;
                                           // derived table: Expression = subquery SQL, Name2 = alias
    private const byte AttrColumn = 0x06;  // Expression = column text
    private const byte AttrJoin = 0x07;    // Expression = condition, Flag = kind, Name1/Name2 = aliases
    private const byte AttrWhere = 0x08;   // Expression = predicate text
    private const byte AttrEnd = 0xFF;
    private const short QueryTypeSelect = 1;
    private const short FlagDistinct = 2;

    private readonly PageChannel _channel = channel;
    private readonly JetCatalog _catalog = catalog;

    public void Create(string name, ViewSpec spec)
    {
        TableDef msysObjects = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");
        int idIndex = ColumnIndex(msysObjects, "Id");
        int nameIndex = ColumnIndex(msysObjects, "Name");

        // A view's name must be unique among all objects (it also cannot equal an existing table name);
        // find the next free negative id (queries increment from 0x80000000) in one scan.
        int nextId = unchecked((int)0x80000000);
        foreach (object?[] row in new Table(_channel, msysObjects).Rows())
        {
            if (string.Equals(row[nameIndex] as string, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"An object named '{name}' already exists.");
            if (row[idIndex] is int id && id < 0 && id >= nextId) nextId = id + 1;
        }

        AddObjectRow(msysObjects, name, nextId);
        AddQueryRows(nextId, spec);
    }

    private void AddObjectRow(TableDef msysObjects, string name, int objectId)
    {
        DateTime now = DateTime.Now;
        var values = new object?[msysObjects.Columns.Count];
        SetByName(msysObjects, values, "Id", objectId);
        SetByName(msysObjects, values, "ParentId", QueriesContainerParentId);
        SetByName(msysObjects, values, "Type", ObjectTypeQuery);
        SetByName(msysObjects, values, "Name", name);
        SetByName(msysObjects, values, "Flags", ViewFlags);
        SetByName(msysObjects, values, "Owner", DefaultOwner);
        SetByName(msysObjects, values, "DateCreate", now);
        SetByName(msysObjects, values, "DateUpdate", now);
        new RowInserter(_channel, msysObjects).Insert(values, updateIndexes: true);
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
        Row(mq, objectId, AttrType, order: 1, flag: QueryTypeSelect);
        Row(mq, objectId, AttrEnd, order: 1);
        if (spec.Distinct)
            Row(mq, objectId, AttrFlag, order: 1, flag: FlagDistinct);
        for (int i = 0; i < spec.Tables.Count; i++)
        {
            ViewTableSpec t = spec.Tables[i];
            // A derived table stores its subquery SQL in Expression (Name1 empty); a named table uses Name1.
            if (t.SubquerySql is { } sub)
                Row(mq, objectId, AttrTable, order: i + 1, expression: sub, name2: t.Alias);
            else
                Row(mq, objectId, AttrTable, order: i + 1, name1: t.Table, name2: t.Alias);
        }
        for (int i = 0; i < spec.Columns.Count; i++)
            Row(mq, objectId, AttrColumn, order: i + 1, flag: 0, expression: spec.Columns[i]);
        for (int i = 0; i < spec.Joins.Count; i++)
        {
            ViewJoinSpec j = spec.Joins[i];
            Row(mq, objectId, AttrJoin, order: i + 1, flag: (short)j.Kind, expression: j.Condition, name1: j.LeftAlias, name2: j.RightAlias);
        }
        if (spec.Where is { } where)
            Row(mq, objectId, AttrWhere, order: 1, expression: where);
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
