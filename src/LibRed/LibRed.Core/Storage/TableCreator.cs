using EntityFrameworkCore.Jet.Data;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MapRetirement = ((int Row, int Page) Map, System.Collections.Generic.IReadOnlyList<(int Row, int Page)> Clear, System.Collections.Generic.IReadOnlyList<int> Pages);

namespace LibRed.Storage;

/// <summary>
/// Creates and alters tables in an existing database. <see cref="Create"/> allocates and writes the TDEF
/// page, its indexes' B-tree roots and an owned-pages usage map, then records the table in MSysObjects so
/// the catalog finds it. The rest of the class is the incremental DDL EF issues one statement at a time —
/// ADD/DROP/ALTER/RENAME for columns, indexes, relationships and CHECK constraints — each a surgical edit
/// of the existing TDEF rather than a rebuild, so unmodelled descriptor bytes survive.
/// </summary>
/// <param name="collation">The <b>database's</b> collating order, from page 0. Every non-numeric column this
/// class writes inherits it, which is what decides how that column's index keys are encoded, so it is
/// required rather than defaulted. It used to fall back to General-Legacy for "callers that don't create
/// columns" — but ALTER COLUMN creates them, by rebuilding the table, and passed nothing: on a General (v1)
/// database that silently produced v0 columns whose keys the rest of the file does not sort by.</param>
/// <param name="channel">The database file.</param>
/// <param name="catalog">The catalog to read and keep current.</param>
public sealed class TableCreator(PageChannel channel, JetCatalog catalog, Collation collation)
{
    private readonly PageChannel _channel = channel;
    private readonly JetCatalog _catalog = catalog;
    private readonly PageAllocator _allocator = new(channel);
    private readonly Collation _collation = collation;

    /// <summary>
    /// Jet/ACE caps a table at 32 indexes and the cap applies to BOTH TDEF counts — the index-data blocks at
    /// <c>0x33</c> and the logical index-info blocks at <c>0x2F</c>. Microsoft states it against the logical
    /// one: "Number of indexes in a table: 32, including indexes created internally to maintain table
    /// relationships, single-field and composite indexes."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TdefBuilder"/> checks both when a table is created with all its indexes at once, but EF does
    /// not work that way: it creates the table, then adds indexes and relationships one statement at a time,
    /// and each of those comes through a surgical insert here instead. Nothing checked those paths, so the
    /// counts simply walked past 32 — the fields are <c>Int32</c>, so nothing overflowed.
    /// </para>
    /// <para>
    /// The logical count is the one that binds, because a data block must be named by a logical block, so
    /// <c>0x33 ≤ 0x2F</c> always holds. A table many others reference gains a logical block per incoming
    /// relationship and no data block, so it overruns on <c>0x2F</c> while <c>0x33</c> still looks healthy —
    /// which is exactly what the read-side check inspected. Building EF Core's
    /// <c>ComplexNavigationsSharedType</c> model, <c>Level1</c> reached 46 logical against 31 data, and the
    /// resulting file was unreadable by Access ("Unrecognized database format", the table missing entirely)
    /// while LibRed read it back without complaint. Measured in <c>IndexCountLimitAccessTests</c>; see
    /// <c>docs/format/page-02d-constraints.md</c>.
    /// </para>
    /// </remarks>
    private const int MaxIndexesPerTable = 32;

    /// <summary>Rejects an incremental index or relationship that would take either TDEF count past the
    /// Jet/ACE limit, naming both counts so the failure says which one bound.</summary>
    private static void EnsureIndexCapacity(string tableName, string what, int dataCount, int logicalCount)
    {
        if (dataCount > MaxIndexesPerTable || logicalCount > MaxIndexesPerTable)
        {
            throw new NotSupportedException(
                $"Cannot add {what} to '{tableName}': it would leave the table with {dataCount} index-data blocks and " +
                $"{logicalCount} logical index blocks. A table can have at most {MaxIndexesPerTable} of each, counting " +
                "those backing primary keys, unique constraints and relationships - and a table referenced by many " +
                "others accumulates a logical block per incoming relationship without gaining a data block.");
        }
    }


    public void Create(
        string name,
        IReadOnlyList<ColumnSpec> columns,
        IReadOnlyList<string>? primaryKey = null,
        IReadOnlyList<RelationshipSpec>? relationships = null,
        IReadOnlyList<UniqueIndexSpec>? uniqueConstraints = null,
        IReadOnlyList<(string Column, string DefaultSql)>? columnDefaults = null,
        IReadOnlyList<(string Name, string Expression)>? checkConstraints = null,
        string? primaryKeyName = null,
        int primaryKeyDeclaredAfterColumns = 0)
    {
        relationships ??= [];
        uniqueConstraints ??= [];
        columnDefaults ??= [];
        checkConstraints ??= [];

        // Reject names ACE can't use before writing anything: > 64 chars corrupts the whole file for ACE, and
        // the characters . ! ` [ ] make the name unreferenceable in ACE SQL (both verified vs ACE). Applies only
        // to caller-supplied names — LibRed's own hidden .rN relationship-index names are generated later.
        JetName.Validate(name, "table name");
        foreach (ColumnSpec c in columns)
            JetName.Validate(c.Name, "column name");
        // Constraint names go to disk too (PK/unique → index names, FK → MSysRelationships, CHECK → LvProp) and
        // carry the same 64-char + forbidden-char limits — verified: a 100-char FK name overruns into adjacent
        // data and a 100-char index name breaks ACE's index enumeration. Only validate caller-supplied names.
        if (primaryKeyName is not null) JetName.Validate(primaryKeyName, "primary key name");
        foreach (RelationshipSpec r in relationships) JetName.Validate(r.Name, "foreign key name");
        var relationshipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RelationshipSpec r in relationships)
            if (!relationshipNames.Add(r.Name)) throw RelationshipNameTaken(r.Name);
            else EnsureRelationshipNameFree(r.Name);
        foreach (UniqueIndexSpec u in uniqueConstraints) JetName.Validate(u.Name, "unique constraint name");
        foreach ((string checkName, _) in checkConstraints) JetName.Validate(checkName, "check constraint name");

        // A table's name is free only if no table, query or linked table has it (case-insensitively) — ACE's
        // rule, reported as ACE reports it, rather than as the MSysObjects index violation writing the row hits.
        if (ObjectNameExists(name, exceptObjectId: 0))
            throw new SchemaObjectExistsException($"Table '{name}' already exists.", name);

        // Jet/ACE caps a table at 255 columns. The count/id fields are 2 bytes wide so we could physically
        // write more, but Access would refuse to open the table — fail early with a clear message instead.
        if (columns.Count > MaxColumnsPerTable)
            throw new InvalidOperationException(
                $"Table '{name}' has {columns.Count} columns; a table can have at most {MaxColumnsPerTable}.");

        // Before the foreign keys' type match, as ACE checks it: an OLE column referencing a LONG key gets this.
        RejectOleIndexColumns(
            (primaryKey ?? []).Concat(uniqueConstraints.SelectMany(u => u.Columns))
                .Concat(relationships.SelectMany(r => r.Columns.Select(c => c.Column))),
            n => columns.FirstOrDefault(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase))?.Type);

        relationships = relationships.Select(fk => ResolvePrimaryKeyReference(fk, creatingTable: name)).ToList();
        foreach (RelationshipSpec fk in relationships)
            EnsureSameDataTypes(fk, ColumnOf(columns), string.Equals(fk.ReferencedTable, name, StringComparison.OrdinalIgnoreCase)
                ? ColumnOf(columns)
                : ColumnOf(_catalog.FindTable(fk.ReferencedTable)));

        JetFormatBase format = _channel.Format;

        // Allocate the pages the table needs through the global free-pages map (so Access accounts
        // for them). Like Access, a fresh table has NO data page — the first is allocated lazily on
        // the first insert — so its usage maps start empty.
        int tdefPage = _allocator.Allocate();
        int usageMapPage = _allocator.Allocate();

        // Key the long-value maps by the column's *id*, not its position. The two coincide on an ordinary
        // CREATE TABLE, but a spec can carry an explicit id — the faithful-rebuild path does, and ids are
        // never reused after a DROP COLUMN — and the TDEF's long-value map is read back by id, so using the
        // position there silently points a Memo/OLE column's usage maps at the wrong column.
        // A calculated column with a Memo RESULT needs the maps too, and its declared type does not say so:
        // ACE declares such a column Text with length 0 and reaches the value through a long-value
        // descriptor, so keying off Type alone leaves it without maps and its result nowhere to go (§3.4a).
        var longValueCols = columns.Select((c, i) => (Column: c, Id: c.ColumnId ?? i, Position: i))
            .Where(x => x.Column.Type is JetDataType.Memo or JetDataType.Ole
                        || x.Column.CalculatedResultType is JetDataType.Memo)
            .ToList();

        // The table's data-block indexes: the primary key (unique), then a unique index per UNIQUE
        // constraint, then one non-unique index per foreign key over its child columns — Access enforces
        // a relationship through an index on the FK columns. Each carries the relationship (if any) it backs.
        var indexPlans = new List<(string Name, IReadOnlyList<string> Columns, bool IsPk, bool IsUnique,
            RelationshipSpec? Fk, int DeclaredAfterColumns)>();
        if (primaryKey is { Count: > 0 })
            // Name the PK index after the CONSTRAINT if one was given (ACE does the same, and the scaffolder
            // round-trips it). If unnamed, LibRed picks the stable "PrimaryKey" (the DAO/Access-UI convention)
            // — an engine choice, since ACE-via-SQL instead generates a random "Index_<hex>" with no fixed
            // value to reproduce, and nothing downstream depends on the exact name.
            indexPlans.Add((primaryKeyName ?? "PrimaryKey", primaryKey, true, true, null, primaryKeyDeclaredAfterColumns));
        foreach (UniqueIndexSpec unique in uniqueConstraints)
            indexPlans.Add((unique.Name, unique.Columns, false, true, null, unique.DeclaredAfterColumns));
        foreach (RelationshipSpec fk in relationships)
            indexPlans.Add((fk.Name, fk.Columns.Select(c => c.Column).ToList(), false, false, fk, fk.DeclaredAfterColumns));

        // Every index this CREATE would build, including the ones arriving as a PRIMARY KEY or UNIQUE
        // constraint rather than as an index — the inline `col type PRIMARY KEY` form is refused earlier, at
        // the SQL layer, but the table-level CONSTRAINT form reaches here.
        foreach (var plan in indexPlans)
            RejectCalculatedIndexColumns(plan.Name, plan.Columns,
                n => columns.FirstOrDefault(
                    c => c.CalculatedExpression is not null
                         && string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase))?.Name);

        // Usage-map layout (verified vs ACE): the primary page holds row 0 = table owned, row 1 = table
        // free, then one row per index, then two rows (owned + free) per long-value (memo/OLE) column — as
        // many *whole* columns as fit (a page holds ~57 inline records). Once the primary page is full, each
        // remaining long-value column gets its OWN usage-map page (owned = row 0, free = row 1). All maps
        // start empty. This keeps a wide table's per-column maps from overflowing a single page.
        // How many 69-byte inline map records (plus their 2-byte directory slot) fit on one page.
        int mapsPerPage = (format.PageSize - format.DataRowDirectoryOffset) / (UsageMapRecordLength + 2);
        int primaryRecords = 2 + indexPlans.Count; // data owned/free + one per index
        int colsOnPrimary = Math.Clamp((mapsPerPage - primaryRecords) / 2, 0, longValueCols.Count);
        WriteUsageMaps(format, usageMapPage, mapCount: primaryRecords + colsOnPrimary * 2);

        // Which row each of them gets. Rows 0 and 1 are the table's own; the rest go in the order the CREATE
        // TABLE statement DECLARES them (verified vs ACE): a long-value column takes two where its column is
        // written, an index takes one where its constraint is written — inline on a column, or wherever a
        // table-level CONSTRAINT appears in the element list. So `(Id LONG CONSTRAINT pk PRIMARY KEY, M MEMO)`
        // gives pk row 2 and M rows 3/4, while `(Id LONG, M MEMO, CONSTRAINT pk PRIMARY KEY (Id))` gives M
        // rows 2/3 and pk row 4 — the same table, the same maps, different rows.
        //
        // A constraint and a column that sit at the same point (an inline constraint, or a table-level one
        // written immediately after the column) put the CONSTRAINT first: measured on
        // `(Id LONG, CONSTRAINT pk PRIMARY KEY (Id), M MEMO)`, which gives pk row 2.
        //
        // This orders the map ROWS only. indexPlans keeps its own order, because that is what numbers the
        // index data blocks (RealIndexOrdinal), and ACE numbers those PK-then-unique-then-FK regardless.
        var claims = indexPlans
            .Select((p, i) => (Sort: (p.DeclaredAfterColumns, Constraint: 0, i), Index: i, LongValue: -1))
            .Concat(longValueCols.Take(colsOnPrimary)
                .Select((c, j) => (Sort: (c.Position, Constraint: 1, j), Index: -1, LongValue: j)))
            .OrderBy(c => c.Sort);

        var indexRows = new int[indexPlans.Count];
        var longValueRows = new (int Used, int Free)[longValueCols.Count];
        int nextMapRow = 2;
        foreach (var claim in claims)
        {
            if (claim.Index >= 0) indexRows[claim.Index] = nextMapRow++;
            else { longValueRows[claim.LongValue] = (nextMapRow, nextMapRow + 1); nextMapRow += 2; }
        }

        // §3.3.2 entries: a long-value column's maps are on the primary page (if it fit) or a dedicated page.
        var longValueSpecs = new List<LongValueColumnSpec>(longValueCols.Count);
        for (int j = 0; j < longValueCols.Count; j++)
        {
            int colId = longValueCols[j].Id;
            if (j < colsOnPrimary)
                longValueSpecs.Add(new LongValueColumnSpec(
                    colId, UsedRow: longValueRows[j].Used, FreeRow: longValueRows[j].Free, MapPage: usageMapPage));
            else
            {
                int columnMapPage = _allocator.Allocate();
                WriteUsageMaps(format, columnMapPage, mapCount: 2); // owned = row 0, free = row 1
                longValueSpecs.Add(new LongValueColumnSpec(colId, UsedRow: 0, FreeRow: 1, MapPage: columnMapPage));
            }
        }

        // Each index is an empty leaf root, populated as rows are inserted. Its usage map is on the primary
        // page, at the row the declaration order above gave it. A foreign key's root is allocated later, after
        // its relationship's rows (below); until then the definition names page 0.
        var indexes = new List<IndexSpec>(indexPlans.Count);
        for (int i = 0; i < indexPlans.Count; i++)
        {
            var plan = indexPlans[i];
            int rootPage = plan.Fk is null ? AllocateIndexRoot(format, tdefPage, indexRows[i], usageMapPage) : 0;
            indexes.Add(new IndexSpec(plan.Name, plan.Columns, plan.IsPk, plan.IsUnique,
                rootPage, UsageMapRow: indexRows[i], UsageMapPage: usageMapPage));
        }

        // Build the child's logical index-info blocks. A plain index (PK) maps 1:1 to its data block;
        // a foreign key's data block instead carries the *outgoing* relationship block (§3.6), linked
        // to an *incoming* block. The two ends cross-reference by index_num. For a cross-table FK the
        // incoming block is added to the parent's TDEF; for a self-reference it lives in this same TDEF.
        var childLogical = new List<TdefBuilder.LogicalIndexSpec>(indexPlans.Count);
        var incoming = new List<IncomingRelationship>();
        var parentAdds = new Dictionary<int, int>();
        // Incoming blocks that this table hosts for its own self-references are numbered after the
        // data-block logical indexes (verified vs ACE: a self-ref adds one such block at num = data count).
        int selfIncomingNum = indexPlans.Count;
        for (int i = 0; i < indexPlans.Count; i++)
        {
            var plan = indexPlans[i];
            if (plan.Fk is null)
            {
                childLogical.Add(new TdefBuilder.LogicalIndexSpec(
                    Number: i, DataOrdinal: i, FkType: 0, FkNumber: 0xFFFFFFFF, FkTablePage: 0,
                    UpdateAction: IndexBlockFormat.PlainAction, DeleteAction: IndexBlockFormat.PlainAction,
                    Type: plan.IsPk ? IndexBlockFormat.TypePrimary : IndexBlockFormat.TypeSecondary, Name: plan.Name));
                continue;
            }

            RelationshipSpec fk = plan.Fk;
            if (fk.UpdateSetNull) throw UpdateSetNullNotImplemented();
            byte upd = fk.CascadeUpdate ? IndexBlockFormat.CascadeAction : IndexBlockFormat.NoCascadeAction;
            byte del = fk.CascadeDelete ? IndexBlockFormat.CascadeAction
                : fk.DeleteSetNull ? IndexBlockFormat.SetNullAction : IndexBlockFormat.NoCascadeAction;
            byte outgoingType = fk.NoIndex ? FkTypeOutgoingNoIndex : FkTypeOutgoing;

            // A self-referencing FK: the table is not in the catalog yet (we are creating it), so resolve
            // the referenced index within the plans we are building and host both ends here.
            if (string.Equals(fk.ReferencedTable, name, StringComparison.OrdinalIgnoreCase))
            {
                int refOrdinal = SelfReferencedOrdinal(indexPlans, fk);
                int inNum = selfIncomingNum++;
                // Outgoing block (this table's child side) — NO INDEX flags it 0x03 instead of 0x02.
                childLogical.Add(new TdefBuilder.LogicalIndexSpec(
                    Number: i, DataOrdinal: i, FkType: outgoingType, FkNumber: (uint)inNum,
                    FkTablePage: tdefPage, UpdateAction: upd, DeleteAction: del,
                    Type: IndexBlockFormat.TypeForeign, Name: fk.Name));
                // Incoming block (this table's parent side), hidden ".r" name unique within the table.
                childLogical.Add(new TdefBuilder.LogicalIndexSpec(
                    Number: inNum, DataOrdinal: refOrdinal, FkType: FkTypeIncoming, FkNumber: (uint)i,
                    FkTablePage: tdefPage, UpdateAction: upd, DeleteAction: del,
                    Type: IndexBlockFormat.TypeForeign, Name: HiddenRelationshipName(inNum)));
                continue;
            }

            (int parentPage, int refOrd, int parentNextNum) = ResolveParent(fk, tdefPage);
            // Each incoming block takes the parent's next free number — past the last one this statement gave
            // it, since those blocks are not written yet.
            int parentNum = parentAdds.TryGetValue(parentPage, out int last)
                ? NextLogicalIndexNumber(parentPage, above: last)
                : parentNextNum;
            parentAdds[parentPage] = parentNum;

            childLogical.Add(new TdefBuilder.LogicalIndexSpec(
                Number: i, DataOrdinal: i, FkType: outgoingType,
                FkNumber: (uint)parentNum, FkTablePage: parentPage, UpdateAction: upd, DeleteAction: del,
                Type: IndexBlockFormat.TypeForeign, Name: fk.Name));
            incoming.Add(new IncomingRelationship(parentPage, parentNum, refOrd,
                ChildBlockNumber: (uint)i, ChildPage: tdefPage, upd, del));
        }

        // Access stores logical blocks sorted by name, ignoring case (with their names in the same order).
        childLogical.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // Build the definition and point it at the usage maps: owned-pages = row 0, free-pages =
        // row 1, both on the usage-map page.
        byte[] tdef = TdefBuilder.Build(format, TableType.User, columns, _collation, indexes, longValueSpecs, childLogical).Page;
        tdef[format.TdefOwnedPagesOffset] = 0; // owned map record row
        WriteInt24(tdef, format.TdefOwnedPagesOffset + 1, usageMapPage);
        tdef[format.TdefFreePagesOffset] = 1; // free map record row
        WriteInt24(tdef, format.TdefFreePagesOffset + 1, usageMapPage);
        // A wide table's definition can exceed one page; write it split across continuation pages if needed.
        int defEnd = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(format.TdefLengthOffset, 4));
        WriteDefinition(tdefPage, tdef[..defEnd], [], rewrite: false);

        // Per-column extended properties, in column order with DefaultValue before Required (matching ACE):
        // a DEFAULT is a memo property; a NOT NULL column carries a boolean Required property, and a nullable
        // one none. An AutoNumber follows the same rule — ACE writes Required for COUNTER NOT NULL and not for
        // a bare COUNTER (verified by reading its property blob back).
        var columnProps = new List<PropertyBlob.Property>();
        foreach (ColumnSpec col in columns)
        {
            var def = columnDefaults.FirstOrDefault(d => string.Equals(d.Column, col.Name, StringComparison.OrdinalIgnoreCase));
            if (def.DefaultSql is not null)
                columnProps.Add(new PropertyBlob.Property(col.Name, PropertyBlob.DefaultValueProperty, def.DefaultSql));
            if (!col.IsNullable)
                columnProps.Add(PropertyBlob.Bool(col.Name, PropertyBlob.RequiredProperty, true));
            columnProps.AddRange(CalculatedProperties(col));
        }

        AddCatalogRow(name, tdefPage, columnProps, checkConstraints,
            ownsComplexColumns: columns.Any(c => c.Type == JetDataType.Complex));
        AddPermissionRows(tdefPage);

        // Each foreign key's index is built after its relationship's rows, as ACE builds it (verified: a database's
        // first relationship takes MSysRelationships' first data page, and the key's index root the page after).
        for (int i = 0; i < indexPlans.Count; i++)
        {
            if (indexPlans[i].Fk is not { } fk) continue;
            AddRelationshipRows(name, fk);
            int rootPage = AllocateIndexRoot(format, tdefPage, indexRows[i], usageMapPage);
            _catalog.Invalidate();
            TableDef created = _catalog.RequireTable(name);
            new IndexWriter(_channel, created).UpdateIndexRoot(created.Indexes.First(ix => ix.RealIndexOrdinal == i), rootPage);
        }
        foreach (IncomingRelationship inc in incoming)
            AddIncomingRelationshipBlock(inc);
    }

    /// <summary>Allocates an index's root — an empty leaf — and records it in the index's own pages usage map, as
    /// Access does at CREATE, before any row exists (verified: a freshly created empty index has exactly its root
    /// bit set). As the tree grows, IndexWriter adds each page it allocates, so the map covers the whole B-tree.</summary>
    private int AllocateIndexRoot(JetFormatBase format, int tdefPage, int mapRow, int mapPage)
    {
        int rootPage = _allocator.Allocate();
        WriteEmptyLeafIndexPage(format, rootPage, owner: tdefPage);
        new UsageMapWriter(_channel).SetBit(mapRow, mapPage, rootPage, set: true);
        return rootPage;
    }

    /// <summary>The property-blob entries that make a column calculated (§3.4a), or nothing for an ordinary
    /// one. All three pieces have to agree or Access reads the payload wrongly: <c>Expression</c> is the text
    /// the engine evaluates, <c>ResultType</c> is the authority on the payload's encoding, and the three
    /// <c>FCMin*Ver</c> strings declare the Access floor a calculated column forces. The version properties
    /// are <b>not</b> DDL properties, unlike the first two — matching what ACE writes.</summary>
    private static IEnumerable<PropertyBlob.Property> CalculatedProperties(ColumnSpec column)
    {
        if (column.CalculatedExpression is not { } expression) yield break;

        JetDataType resultType = column.CalculatedResultType ?? column.Type;
        yield return new PropertyBlob.Property(
            column.Name, PropertyBlob.ExpressionProperty, expression, JetDataType.Memo);
        // A Byte property is one raw byte, so the value has to be supplied as RawValue -- a Value string
        // would be written as UTF-16 text and read back as a nonsense type code.
        yield return new PropertyBlob.Property(
            column.Name, PropertyBlob.ResultTypeProperty,
            ((byte)resultType).ToString(System.Globalization.CultureInfo.InvariantCulture),
            JetDataType.Byte, RawValue: [(byte)resultType]);
        foreach (string version in CalculatedVersionProperties)
            yield return new PropertyBlob.Property(
                column.Name, version, CalculatedMinimumVersion, JetDataType.Text)
            { Flags = 0 };
    }

    private static readonly string[] CalculatedVersionProperties =
        ["FCMinReadVer", "FCMinWriteVer", "FCMinDesignVer"];

    /// <summary>Access 2010 (ACE 14) is the floor a calculated column declares, which is also the on-disk
    /// version byte it needs — see <see cref="RaiseFormatForCalculated"/>.</summary>
    private const string CalculatedMinimumVersion = "14.0.0000.0000";

    /// <summary>ON UPDATE SET NULL pathway: the docs list it, but the ACE OLE DB provider rejects it via SQL,
    /// so its on-disk storage (the grbit flag + the index-info +0x15 action byte) is unverified. Rather than
    /// guess the bytes, fail loudly until a UI/DAO-created sample can be probed.</summary>
    private static NotImplementedException UpdateSetNullNotImplemented() => new(
        "ON UPDATE SET NULL is not supported. Only ON UPDATE NO ACTION and ON UPDATE CASCADE are supported.");
    private const byte FkTypeIncoming = 0x01;         // this table is the parent/referenced end
    private const byte FkTypeOutgoing = 0x02;         // this table is the child/referencing end (indexed)
    private const byte FkTypeOutgoingNoIndex = 0x03;  // child/referencing end declared FOREIGN KEY NO INDEX

    /// <summary>An incoming-relationship logical block to add to a parent table's TDEF.</summary>
    private readonly record struct IncomingRelationship(
        int ParentPage, int Number, int ReferencedOrdinal, uint ChildBlockNumber, int ChildPage,
        byte UpdateAction, byte DeleteAction);

    /// <summary>
    /// ACE's "same data types" rule for a relationship, measured over every pairing of the column types: each child
    /// column must have its parent column's storage type, whatever either one's length — <c>TEXT(5)</c>,
    /// <c>TEXT(20)</c> and <c>CHAR(10)</c> all pair with one another, as do <c>DECIMAL</c>s of any precision and
    /// scale and <c>BINARY</c> with <c>VARBINARY</c>. An AutoNumber is a Long on either side. Checked before
    /// anything is written. A column that is not found is left to the check that reports it.
    /// </summary>
    private static void EnsureSameDataTypes(RelationshipSpec fk,
        Func<string, JetDataType?> childColumn, Func<string, JetDataType?> parentColumn)
    {
        foreach ((string column, string referenced) in fk.Columns)
        {
            if (childColumn(column) is not { } child || parentColumn(referenced) is not { } parent) continue;
            if (child != parent)
                throw new InvalidOperationException(
                    "Relationship must be on the same number of fields with the same data types. " +
                    $"'{column}' ({child}) cannot reference '{fk.ReferencedTable}.{referenced}' ({parent}).");
        }
    }

    private static Func<string, JetDataType?> ColumnOf(IReadOnlyList<ColumnSpec> columns) =>
        name => columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Type;

    private static Func<string, JetDataType?> ColumnOf(TableDef? table) => name => table?.FindColumn(name)?.Type;

    /// <summary>The data-block ordinal of the index over a self-reference's referenced columns, found
    /// among the indexes being created for this table (the table is not in the catalog yet).</summary>
    private static int SelfReferencedOrdinal(
        List<(string Name, IReadOnlyList<string> Columns, bool IsPk, bool IsUnique, RelationshipSpec? Fk,
            int DeclaredAfterColumns)> indexPlans,
        RelationshipSpec fk)
    {
        var refColumns = fk.Columns.Select(c => c.ReferencedColumn).ToList();
        for (int j = 0; j < indexPlans.Count; j++)
            if (indexPlans[j].Columns.SequenceEqual(refColumns, StringComparer.OrdinalIgnoreCase))
                return j;
        throw new InvalidOperationException(
            $"Self-referencing foreign key '{fk.Name}' references ({string.Join(", ", refColumns)}), which is not a key or index of '{fk.ReferencedTable}'.");
    }

    /// <summary>
    /// Resolves a cross-table relationship's parent: its TDEF page, the data-block ordinal of the parent
    /// index over the referenced columns (normally the PK), and the parent's lowest free logical index_num
    /// (used to number the incoming block we will add). Self-references are handled by the caller before
    /// this is reached (the table is not yet in the catalog).
    /// </summary>
    /// <summary>The next free logical <c>index_num</c> on a TDEF: the <b>lowest</b> number no info block holds,
    /// as ACE assigns it (verified: with 1 and 2 free below a live 3, a new index takes 1 — on the child side
    /// and the parent's incoming side alike). Dropping an index or a relationship removes a block without
    /// renumbering the survivors' index_num (only their data ordinals move), so the free numbers are gaps,
    /// and neither the block COUNT nor <c>max + 1</c> names the one ACE uses — the count collides with a live
    /// block, which is what the child's cross-link at +0x0D names. With <paramref name="above"/>, the lowest free
    /// number past it — a self-reference's incoming block, numbered after its outgoing one.</summary>
    private int NextLogicalIndexNumber(int tdefPage, int above = -1)
    {
        (LibRed.IO.PageBuffer buf, _) = ReadDefinition(tdefPage);
        TdefRegions regions = TdefRegions.Of(buf.Span, _channel.Format);

        var used = new HashSet<int>();
        for (int i = 0; i < regions.LogicalCount; i++)
            used.Add(buf.ReadInt32(
                regions.InfoBlocks + i * IndexBlockFormat.InfoBlockSize + IndexBlockFormat.InfoNumberOffset));
        int next = above + 1;
        while (used.Contains(next)) next++;
        return next;
    }

    /// <summary>A relationship's parent table, or ACE's error when it does not exist.</summary>
    private TableDef ReferencedTableOf(RelationshipSpec fk) =>
        _catalog.FindTable(fk.ReferencedTable)
        ?? throw new InvalidOperationException(
            $"Cannot find table or constraint: the referenced table '{fk.ReferencedTable}' does not exist.");

    /// <summary>
    /// Resolves <c>REFERENCES table</c> with no column list (<see cref="RelationshipSpec.ReferencesPrimaryKey"/>)
    /// to the parent's primary key, pairing the child columns with the key's in order whatever either side's
    /// columns are named — as ACE does. Any other relationship comes back unchanged.
    /// </summary>
    /// <remarks>
    /// ACE refuses it when the parent has no primary key (a unique index does not stand in for one) and when the
    /// columns differ in number (verified). A table referencing itself in its own CREATE TABLE
    /// (<paramref name="creatingTable"/>) has no key in the catalog yet; the SQL parser pairs such a reference
    /// with a key the statement declares earlier, so one still unpaired here has no key to reference.
    /// </remarks>
    private RelationshipSpec ResolvePrimaryKeyReference(RelationshipSpec fk, string? creatingTable)
    {
        if (!fk.ReferencesPrimaryKey) return fk;
        bool selfInCreate = creatingTable is not null
                            && string.Equals(fk.ReferencedTable, creatingTable, StringComparison.OrdinalIgnoreCase);
        IndexDef primaryKey = (selfInCreate ? null : ReferencedTableOf(fk).Indexes.FirstOrDefault(i => i.IsPrimaryKey))
            ?? throw new InvalidOperationException(
                $"Cannot create relationship. Referenced table '{fk.ReferencedTable}' does not have a primary key.");
        return fk with
        {
            Columns = RelationshipSpec.PairColumns(fk.ReferencedTable,
                fk.Columns.Select(c => c.Column).ToList(), primaryKey.Columns.Select(c => c.Column.Name).ToList()),
            ReferencesPrimaryKey = false,
        };
    }

    private (int Page, int ReferencedOrdinal, int NextIndexNumber) ResolveParent(RelationshipSpec fk, int childPage)
    {
        TableDef parent = ReferencedTableOf(fk);
        if (parent.DefinitionPage == childPage)
            throw new InvalidOperationException($"Self-referencing foreign key '{fk.Name}' should have been handled inline.");

        var ptdef = new Pages.TableDefinitionPage();
        ptdef.Read(_channel, parent.DefinitionPage);
        var refColumns = fk.Columns.Select(c => c.ReferencedColumn).ToList();
        IndexDef refIndex = FindParentKeyIndex(ptdef.Indexes, refColumns, fk.ReferencedTable);
        return (parent.DefinitionPage, refIndex.RealIndexOrdinal, NextLogicalIndexNumber(parent.DefinitionPage));
    }

    /// <summary>ACE refuses a relationship name another relationship already has (verified); a table or query may
    /// share it. Checked before anything is written.</summary>
    private void EnsureRelationshipNameFree(string name)
    {
        if (_catalog.Relationships.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw RelationshipNameTaken(name);
    }

    private static SchemaObjectExistsException RelationshipNameTaken(string name) =>
        new($"There is already a relationship named '{name}' in the current database.", name);

    /// <summary>
    /// Writes the <c>MSysRelationships</c> rows for one relationship — one row per column pair, with
    /// <c>ccolumn</c> = the pair count, <c>icolumn</c> = the 0-based pair index, and <c>grbit</c>
    /// encoding enforce/cascade (verified against Access: an enforced no-cascade FK stores grbit 0) — and the
    /// relationship's own <c>MSysObjects</c> object, which ACE records for every relationship.
    /// </summary>
    private void AddRelationshipRows(string childTable, RelationshipSpec fk)
    {
        TableDef msys = _catalog.RequireTable("MSysRelationships");
        new ViewCreator(_channel, _catalog).CreateRelationshipObject(fk.Name);

        int grbit = 0;
        if (!fk.IsEnforced) grbit |= RelationshipFlags.DontEnforce;
        if (fk.CascadeUpdate) grbit |= RelationshipFlags.UpdateCascade;
        if (fk.CascadeDelete) grbit |= RelationshipFlags.DeleteCascade;
        if (fk.DeleteSetNull) grbit |= RelationshipFlags.DeleteSetNull;

        for (int i = 0; i < fk.Columns.Count; i++)
        {
            var (column, referencedColumn) = fk.Columns[i];
            var values = new object?[msys.Columns.Count];
            CatalogWriter.Set(msys, values, "szRelationship", fk.Name);
            CatalogWriter.Set(msys, values, "szObject", childTable);
            CatalogWriter.Set(msys, values, "szColumn", column);
            CatalogWriter.Set(msys, values, "szReferencedObject", fk.ReferencedTable);
            CatalogWriter.Set(msys, values, "szReferencedColumn", referencedColumn);
            CatalogWriter.Set(msys, values, "ccolumn", fk.Columns.Count);
            CatalogWriter.Set(msys, values, "icolumn", i);
            CatalogWriter.Set(msys, values, "grbit", grbit);
            new RowInserter(_channel, msys).Insert(values, updateIndexes: true);
        }
    }


    /// <summary>
    /// Adds an index to an existing table for CREATE INDEX. Surgically inserts a statistics block, an
    /// index-data block and a logical index-info block into the TDEF (preserving the existing columns,
    /// indexes, relationship linkage and long-value entries byte-for-byte), grows the usage-map page by one
    /// row and writes a B-tree root, back-filled from the table's existing rows.
    /// </summary>
    public void AddIndex(string tableName, string indexName, IReadOnlyList<(string Column, bool Descending)> columns,
        bool isUnique, bool isPrimary, bool disallowNull, bool ignoreNulls)
    {
        JetName.Validate(indexName, "index name");
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");
        RejectCalculatedIndexColumns(indexName, columns.Select(c => c.Column),
            n => table.Columns.FirstOrDefault(
                c => c.IsCalculated && string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase))?.Name);
        RejectOleIndexColumns(columns.Select(c => c.Column), n => table.FindColumn(n)?.Type);
        var slots = ResolveSlots(table, columns.Select(c => (c.Column, Ascending: !c.Descending)));
        // A table has one primary key, and ACE refuses a second (verified).
        if (isPrimary && table.Indexes.Any(i => i.IsPrimaryKey))
            throw new InvalidOperationException($"Primary key already exists on table '{table.Name}'.");
        InsertIndex(table, indexName, slots,
            unique: isUnique || isPrimary, required: isPrimary || disallowNull, ignoreNulls,
            (num, ord) => BuildPlainInfoBlock(num, ord, isPrimary));
    }

    /// <summary>Refuses an index over a calculated column, on whichever route asked for it.
    /// <para>This matches Access and diverges only from ACE's SQL layer, which is the one that gets it wrong.
    /// Access's <b>designer</b> does not offer a calculated column in the Indexes dialog at all and will not
    /// let it be the primary key; the <b>storage engine</b> agrees, refusing every INSERT into a table whose
    /// calculated column is indexed — <i>"Operation is not supported for this type of object."</i> Only
    /// <b>ACE via SQL</b> accepts <c>CREATE INDEX</c> (and even <c>CREATE UNIQUE INDEX</c>) on one, and what
    /// it produces is a table into which no row can ever be written. Measured for Int16 and Int32 results,
    /// with the index created both before and after rows exist (page-02e-calculated-columns).</para></summary>
    private static void RejectCalculatedIndexColumns(
        string indexName, IEnumerable<string> columnNames, Func<string, string?> findCalculated)
    {
        foreach (string name in columnNames)
            if (findCalculated(name) is { } calculated)
                throw new NotSupportedException(
                    $"Index '{indexName}' cannot include calculated column '{calculated}': a table with an index "
                    + "over a calculated column cannot hold any rows.");
    }

    /// <summary>Refuses an index over an OLE column — a key, a unique constraint, a relationship's — before anything
    /// is written, as ACE does on every route (verified: CREATE INDEX, PRIMARY KEY and UNIQUE both in CREATE TABLE and
    /// added, a foreign key in either place, and ALTER COLUMN of an indexed column to OLE). An OLE value has no index
    /// key, and without this the definition is accepted on an empty table and every later insert fails. A BigBinary
    /// column is refused the same way, with the same message (verified on the same routes).</summary>
    private static void RejectOleIndexColumns(IEnumerable<string> columnNames, Func<string, JetDataType?> typeOf)
    {
        foreach (string name in columnNames)
            if (typeOf(name) is JetDataType.Ole or JetDataType.BigBinary)
                throw new InvalidOperationException($"Invalid field definition '{name}' in definition of index or relationship.");
    }

    /// <summary>Resolves index column names to (columnId, ascending) slots against a table.</summary>
    private static List<(int Id, bool Ascending)> ResolveSlots(
        TableDef table, IEnumerable<(string Column, bool Ascending)> columns)
    {
        var byName = table.Columns.ToDictionary(c => c.Name, c => c.ColumnId, StringComparer.OrdinalIgnoreCase);
        return columns.Select(c => byName.TryGetValue(c.Column, out int id) ? (Id: id, c.Ascending)
            : throw new InvalidOperationException($"Column '{c.Column}' does not exist in '{table.Name}'.")).ToList();
    }

    /// <summary>Surgically inserts one data index and its logical info block into an existing table's TDEF,
    /// name-sorted. <paramref name="buildInfo"/> gets (block number, data-block ordinal) and
    /// returns the 28-byte info block — a plain index or an outgoing-FK block. Returns the new block number.</summary>
    private int InsertIndex(TableDef table, string indexName, List<(int Id, bool Ascending)> slots,
        bool unique, bool required, bool ignoreNulls, Func<int, int, byte[]> buildInfo)
    {
        JetFormatBase format = _channel.Format;

        // ACE rejects a duplicate index name — for a foreign key's backing index as much as a CREATE INDEX
        // (verified) — and so must LibRed: every lookup downstream (DROP INDEX, the back-fill, DROP CONSTRAINT)
        // finds an index by name with First/FirstOrDefault, so two blocks sharing one name make those operations
        // pick an arbitrary block — a DROP that removes the wrong one.
        if (table.Indexes.Any(i => string.Equals(i.Name, indexName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Table '{table.Name}' already has an index named '{indexName}'.");

        // The index-data block holds exactly IndexBlockFormat.MaxColumns slots, with no count and no
        // continuation, so a wider index cannot be represented. TdefBuilder rejects this when a table is
        // created with its indexes; this is the incremental path, where BuildIndexDataBlock would otherwise
        // write the first ten and mark the rest unused — silently storing a different index from the one
        // asked for, which ACE reads without complaint. ACE refuses instead: "Cannot have more than 10
        // fields in an index."
        if (slots.Count > IndexBlockFormat.MaxColumns)
            throw new NotSupportedException(
                $"Cannot create index '{indexName}' on '{table.Name}' over {slots.Count} columns: "
                + $"an index holds at most {IndexBlockFormat.MaxColumns} fields.");

        TdefParts parts = ParseTdef(table.DefinitionPage); // stitches continuation pages for a multi-page TDEF
        var header = new LibRed.IO.PageBuffer(parts.Header, table.DefinitionPage);
        int existingRowCount = header.ReadInt32(format.TdefRowCountOffset);

        // A unique index over rows that already exist has to be rejected if those rows aren't unique, and a
        // required one if any row leaves a key column NULL — ACE refuses the DDL for both. Done *here*, before a
        // single byte of the TDEF moves, rather than during the back-fill: this path is not transactional, so a
        // failure discovered mid-back-fill would leave the index committed to the TDEF and half-populated.
        // Scanning first means a rejected CREATE INDEX leaves the file exactly as it was.
        if ((unique || required) && existingRowCount != 0)
            EnsureExistingRowsFitIndex(table, indexName, slots, unique, required);

        int dataCount = parts.DataBlocks.Count;

        // A plain index or an outgoing FK adds one of each. Checked before a byte moves, like the
        // duplicate-key scan above, so a rejection leaves the file exactly as it was.
        EnsureIndexCapacity(table.Name, $"index '{indexName}'", dataCount + 1, parts.Logical.Count + 1);

        // The new block takes the lowest free index_num.
        int newNum = NextLogicalIndexNumber(table.DefinitionPage);

        // Allocate the new index's root (empty leaf) and its usage-map row (appended after existing rows).
        int rootPage = _allocator.Allocate();
        WriteEmptyLeafIndexPage(format, rootPage, owner: table.DefinitionPage);
        int usageMapPage = ReadInt24(header, format.TdefOwnedPagesOffset + 1);
        // Where the new index's usage map goes: appended after the last row, as ACE does. A dropped index
        // leaves its row behind and ACE never hands it out again, so the row cannot be derived from the
        // table's shape — after a DROP INDEX that names a row already taken, on a table without long-value
        // columns another live index's (IndexUsageMapTests).
        //
        // When the primary page is full ACE does not squeeze the row in but spills, exactly as it does for
        // long-value columns: measured on a 40-memo table, CREATE INDEX leaves the full 57-row primary page
        // alone and puts the new index's map at row 0 of a page of its own (WideMemoUsageMapAccessTests).
        var primaryMap = new DataPage();
        primaryMap.Read(_channel.ReadPage(usageMapPage), format);
        int primaryFree = BinaryPrimitives.ReadUInt16LittleEndian(
            _channel.ReadPage(usageMapPage).Span.Slice(format.DataFreeSpaceOffset, 2));

        int newIndexUsageRow = primaryMap.Rows.Count;
        bool ownPage = primaryFree < UsageMapRecordLength + 2;
        if (ownPage)
        {
            usageMapPage = _allocator.Allocate();
            WriteUsageMaps(format, usageMapPage, mapCount: 1);
            newIndexUsageRow = 0;
        }
        // Append the new index's (empty) usage-map row, preserving every existing record. The data maps are
        // empty on an empty table but the *existing indexes'* maps already carry their root bits (set at
        // creation), so we must not rewrite the page from scratch even when the table has no rows. A page of
        // its own already has the row, written empty above.
        if (!ownPage) AppendEmptyUsageMapRow(format, usageMapPage, newIndexUsageRow);

        // Record this index's own root, as Access does at CREATE INDEX (the empty root is the index's sole
        // page until it splits, after which IndexWriter adds each page it allocates).
        new UsageMapWriter(_channel).SetBit(newIndexUsageRow, usageMapPage, rootPage, set: true);

        // The new index adds a (zero) stats block and its data block after the existing ones, and a logical
        // block in name order. ACE's order ignores case (verified: a3 goes before IX2, and an FK named fk before
        // IX2); how it orders punctuation and accented letters is not measured.
        parts.Stats.Add(new byte[format.RealIndexEntrySize]);
        // An index over a complex column carries the complex-column flag — every one Access writes does, which a
        // rebuild restoring the index from its IndexDef would otherwise drop.
        bool complexColumn = slots.Any(s => table.Columns.Any(c => c.ColumnId == s.Id && c.Type == JetDataType.Complex));
        parts.DataBlocks.Add(BuildIndexDataBlock(
            slots, rootPage, newIndexUsageRow, usageMapPage, unique, required, ignoreNulls, complexColumn));
        int k = parts.Logical.Count(b => string.Compare(NameOf(b.Name), indexName, StringComparison.OrdinalIgnoreCase) < 0);
        parts.Logical.Insert(k, (buildInfo(newNum, dataCount), EncodeName(indexName)));

        WriteTdef(table.DefinitionPage, parts);
        _catalog.Invalidate();

        // Back-fill the new (empty) index B-tree with an entry per existing row, so the index is complete.
        if (existingRowCount != 0)
            BackfillIndex(table.Name, indexName, ignoreNulls, validateUnique: false);
        return newNum;
    }

    /// <summary>
    /// Throws if the table's existing rows cannot go into a would-be index: a duplicate key for a unique one, or
    /// a NULL in any key column for a required one — a primary key or WITH DISALLOW NULL, which ACE refuses with
    /// "Index or primary key cannot contain a Null value" (verified; an ADD COLUMN … PRIMARY KEY on a table that
    /// already holds rows is the usual way to get there). Purely a read, touching nothing on disk, so it is safe
    /// to call before the index exists. For uniqueness, rows with a null in any key column are exempt — Jet's
    /// uniqueness is over the non-null keys only, so several rows may be null (verified vs ACE; see the
    /// constraints page); a WITH IGNORE NULL index leaves them out of the B-tree altogether, so either way they
    /// cannot collide.
    /// </summary>
    /// <remarks>
    /// Comparison is on the encoded key, not the raw values, which is deliberate: the encoding is what the
    /// B-tree stores and it is collation-lossy, so "ABC" and "abc" share a key. That is precisely Access's
    /// uniqueness domain, and it keeps this agreeing with the insert- and update-time checks, which compare
    /// the same way via <see cref="IndexWriter.KeyExists"/>.
    /// </remarks>
    private void EnsureExistingRowsFitIndex(
        TableDef table, string indexName, IReadOnlyList<(int Id, bool Ascending)> slots, bool unique, bool required)
    {
        var keyColumns = slots
            .Select(s => (Column: table.Columns.First(c => c.ColumnId == s.Id), s.Ascending))
            .ToArray();

        var seen = new HashSet<string>();
        var rows = new Table(_channel, table);
        foreach (object?[] values in rows.Rows(rows.DecodeOnly(keyColumns.Select(k => k.Column.Index))))
        {
            if (keyColumns.Any(k => values[k.Column.Index] is null))
            {
                if (required)
                    throw new InvalidOperationException(
                        $"Index or primary key cannot contain a Null value: a row of '{table.Name}' has no value for index '{indexName}'.");
                continue;
            }
            if (unique && !seen.Add(Convert.ToHexString(IndexKeyEncoder.Encode(keyColumns, values))))
                throw new InvalidOperationException(
                    $"Cannot create unique index '{indexName}' on '{table.Name}': duplicate key values exist.");
        }
    }

    /// <summary>Populates a freshly added index over a table's existing rows: encodes every live row's key,
    /// then hands the lot to <see cref="IndexWriter.BulkBuild"/>, which sorts them and writes each B-tree page
    /// once. Rows with a null in an IGNORE NULL index's key are skipped, matching the per-insert path.</summary>
    /// <remarks>
    /// This used to insert the rows one at a time, which re-read, re-parsed and rebuilt a whole leaf page per
    /// row: an index over 10,000 rows cost ~440 ms and allocated over a gigabyte, nearly all of it leaves
    /// replaced by the next entry. Sorting first lets each leaf be filled and written once.
    /// <para>Uniqueness moves with it: on sorted keys a duplicate is an adjacent pair, so the per-row
    /// <see cref="IndexWriter.KeyExists"/> descent is gone. The comparison is still on the encoded key, which
    /// is Access's uniqueness domain (see <see cref="EnsureExistingRowsFitIndex"/>).</para>
    /// <para>A built index's statistics are set as ACE sets them (verified, for CREATE INDEX, a foreign key's
    /// backing index and an ALTER COLUMN's rebuild): the total entry count to the entries it now holds and the
    /// unique entry count to its distinct keys — both from the rows present, not from any earlier history.</para>
    /// </remarks>
    private void BackfillIndex(string tableName, string indexName, bool ignoreNulls, bool validateUnique)
    {
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found after adding the index.");
        IndexDef index = table.Indexes.First(ix => string.Equals(ix.Name, indexName, StringComparison.OrdinalIgnoreCase));
        List<(byte[] Key, int Pointer, bool NullKey)> entries = IndexEntries(table, index, ignoreNulls);

        new IndexWriter(_channel, table).BulkBuild(index, entries, validateUnique && index.IsUnique);
        SetBuiltStatistics(table, index, entries);
    }

    /// <summary>The entries an index over the table's current rows holds: every live row's encoded key and row
    /// pointer, less the rows an IGNORE NULL index leaves out.</summary>
    private List<(byte[] Key, int Pointer, bool NullKey)> IndexEntries(TableDef table, IndexDef index, bool ignoreNulls)
    {
        var keyColumnIds = index.Columns.Select(c => c.Column.Index).ToArray();
        var entries = new List<(byte[] Key, int Pointer, bool NullKey)>();
        // The key is all an entry is made from, so it is all that is decoded: a wide row, or one whose memo the
        // index cannot even hold, would otherwise be read whole for every entry.
        var rows = new Table(_channel, table);
        foreach ((RowId id, object?[] values) in rows.Rows(rows.DecodeOnly(keyColumnIds)).WithIds())
        {
            bool hasNullKey = keyColumnIds.Any(i => values[i] is null);
            if (ignoreNulls && hasNullKey) continue;
            entries.Add((IndexKeyEncoder.Encode(index.Columns, values), (id.Page << 8) | id.Row, hasNullKey));
        }
        return entries;
    }

    /// <summary>Sets a just-built index's statistics block as ACE sets it: total = the entries it holds, unique =
    /// its distinct keys among them.</summary>
    private void SetBuiltStatistics(TableDef table, IndexDef index, List<(byte[] Key, int Pointer, bool NullKey)> entries) =>
        WriteIndexStatistics(table, index, entries.Count, entries.Select(e => Convert.ToHexString(e.Key)).Distinct().Count());

    private void WriteIndexStatistics(TableDef table, IndexDef index, int total, int unique)
    {
        byte[] tdef = _channel.ReadPage(table.DefinitionPage).Span.ToArray();
        int at = _channel.Format.TdefRealIndexBlockOffset + index.RealIndexOrdinal * _channel.Format.RealIndexEntrySize;
        BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(at, 4), total);
        BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(at + 4, 4), unique);
        _channel.WritePage(table.DefinitionPage, tdef);
    }

    /// <summary>Appends one empty inline usage-map record (row <paramref name="newRow"/>) to an existing
    /// usage-map page, preserving every existing record. The new index tracks no pages here (IndexWriter
    /// navigates the B-tree structurally), so an empty bitmap is correct.</summary>
    private void AppendEmptyUsageMapRow(JetFormatBase format, int pageNumber, int newRow)
    {
        const int MapLength = 1 + 4 + 64; // inline type + start page + 64-byte bitmap (matches WriteUsageMaps)
        byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2));

        if (rowCount != newRow)
            throw new InvalidOperationException(
                $"Usage-map page has {rowCount} rows; expected {newRow} before appending the new index's map.");

        int minOffset = format.PageSize;
        for (int r = 0; r < rowCount; r++)
            minOffset = Math.Min(minOffset, BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + r * 2, 2)));

        int newOffset = minOffset - MapLength;
        // A record written below the directory would overwrite the slots themselves — which reads back later
        // as a slot with offset 0 "outside the row heap", a long way from the cause. The caller is expected
        // to spill onto a fresh page rather than get here; this is the backstop that keeps a mistake loud.
        if (newOffset < format.DataRowDirectoryOffset + (rowCount + 1) * 2)
            throw new InvalidOperationException(
                $"Usage-map page {pageNumber} has no room for another record: {rowCount} rows already reach "
                + $"offset {minOffset}. The new map belongs on a page of its own.");
        Array.Clear(page, newOffset, MapLength); // inline type 0x00, start page 0, zero bitmap
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + newRow * 2, 2), (ushort)newOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)(rowCount + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(newOffset - format.DataRowDirectoryOffset - (rowCount + 1) * 2));
        _channel.WritePage(pageNumber, page);
    }

    /// <summary>
    /// Adds a foreign key to an existing child table: a backing non-unique index over the child
    /// columns carrying an outgoing-relationship block, an incoming block on the parent's TDEF, and the
    /// MSysRelationships rows. The child index and parent block are written the same way inline-FK creation
    /// does (verified byte-faithful vs ACE). A self-reference hosts both ends in the one table; FOREIGN KEY
    /// NO INDEX is not yet handled.
    /// </summary>
    public void AddForeignKey(string childTable, RelationshipSpec fk)
    {
        JetName.Validate(fk.Name, "foreign key name");
        EnsureRelationshipNameFree(fk.Name);
        TableDef child = _catalog.FindTable(childTable)
            ?? throw new InvalidOperationException($"Table '{childTable}' was not found.");
        if (fk.NoIndex)
            throw new NotSupportedException("ALTER TABLE ADD FOREIGN KEY … NO INDEX is not supported yet.");
        if (fk.UpdateSetNull) throw UpdateSetNullNotImplemented();

        RejectOleIndexColumns(fk.Columns.Select(c => c.Column), n => child.FindColumn(n)?.Type);
        fk = ResolvePrimaryKeyReference(fk, creatingTable: null);
        EnsureSameDataTypes(fk, ColumnOf(child), ColumnOf(_catalog.FindTable(fk.ReferencedTable)));

        // An UNENFORCED relationship is catalog rows and nothing else — no backing index on the child, no
        // incoming block on the parent. Measured across three files: every enforced relationship has an index
        // over its columns on both sides, and none of the unenforced ones does. LIBRARY.accdb's BookTable1
        // references Book(BK_ID), a column with no index at all, which an enforced relationship could not be.
        // That is what the "Enforce Referential Integrity" checkbox means on disk: without a unique index on
        // the parent ACE cannot enforce, so it records the relationship as a declaration and stops there.
        if (!fk.IsEnforced)
        {
            AddRelationshipRows(childTable, fk);
            _catalog.Invalidate();
            return;
        }

        byte upd = fk.CascadeUpdate ? IndexBlockFormat.CascadeAction : IndexBlockFormat.NoCascadeAction;
        byte del = fk.CascadeDelete ? IndexBlockFormat.CascadeAction
            : fk.DeleteSetNull ? IndexBlockFormat.SetNullAction : IndexBlockFormat.NoCascadeAction;
        var slots = ResolveSlots(child, fk.Columns.Select(c => (c.Column, Ascending: true)));

        // A self-reference (child == parent) hosts both ends in the same TDEF: the outgoing block takes the
        // lowest free number and the incoming block the next free one above it (verified vs ACE: with 1 free
        // below a live 2, the outgoing block is 1 and the incoming 3).
        if (string.Equals(fk.ReferencedTable, childTable, StringComparison.OrdinalIgnoreCase))
        {
            int selfRefOrdinal = ReferencedOrdinalIn(child, fk);
            int inNum = NextLogicalIndexNumber(child.DefinitionPage, above: NextLogicalIndexNumber(child.DefinitionPage));
            int outNum = InsertIndex(child, fk.Name, slots,
                unique: false, required: false, ignoreNulls: false,
                (num, ord) => BuildOutgoingInfoBlock(num, ord, FkTypeOutgoing, inNum, child.DefinitionPage, upd, del));
            AddIncomingRelationshipBlock(new IncomingRelationship(
                child.DefinitionPage, inNum, selfRefOrdinal, (uint)outNum, child.DefinitionPage, upd, del));
            AddRelationshipRows(childTable, fk);
            _catalog.Invalidate();
            return;
        }

        (int parentPage, int refOrdinal, int parentNum) = ResolveParent(fk, child.DefinitionPage);
        int childBlockNum = InsertIndex(child, fk.Name, slots,
            unique: false, required: false, ignoreNulls: false,
            (num, ord) => BuildOutgoingInfoBlock(num, ord, FkTypeOutgoing, parentNum, parentPage, upd, del));

        AddIncomingRelationshipBlock(new IncomingRelationship(
            parentPage, parentNum, refOrdinal, (uint)childBlockNum, child.DefinitionPage, upd, del));
        AddRelationshipRows(childTable, fk);
        _catalog.Invalidate();
    }

    /// <summary>
    /// Drops a named FOREIGN KEY constraint, byte-faithfully with ACE: removes the child's backing index
    /// (its stats + index-data + outgoing info blocks + name) and the parent's incoming info block from the
    /// two TDEFs, frees the index's B-tree root page back to the global free map, and soft-deletes the
    /// relationship's <c>MSysRelationships</c> rows (the usage-map page is left untouched — ACE leaves the
    /// orphan map row). A self-reference hosts both ends in one TDEF. Returns false if no such relationship
    /// exists on <paramref name="childTable"/>.
    /// </summary>
    public bool DropConstraint(string childTable, string name)
    {
        ForeignKey? rel = _catalog.Relationships.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Table, childTable, StringComparison.OrdinalIgnoreCase));
        if (rel is null) return false;

        TableDef child = _catalog.FindTable(childTable)
            ?? throw new InvalidOperationException($"Table '{childTable}' was not found.");
        IndexDef? fkIndex = child.Indexes.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        bool selfRef = string.Equals(rel.ReferencedTable, childTable, StringComparison.OrdinalIgnoreCase);

        if (fkIndex is not null)
        {
            TdefParts childParts = ParseTdef(child.DefinitionPage);
            // The FK's backing index is an index like any other: its B-tree is released whole and its usage-map
            // record retired, read here while its data block is still in place (see DropIndex).
            var owned = new HashSet<int> { fkIndex.RootPage };
            var retire = new List<MapRetirement>();
            int at = IndexBlockFormat.UsageMapRowOffset;
            byte[] dataBlock = childParts.DataBlocks[fkIndex.RealIndexOrdinal];
            (int Row, int Page) map = (dataBlock[at], dataBlock[at + 1] | dataBlock[at + 2] << 8 | dataBlock[at + 3] << 16);
            var maps = new UsageMap(_channel, child);
            if (map.Page != 0)
            {
                List<int> pages = maps.PagesInMap(map.Row, map.Page).ToList();
                owned.UnionWith(pages);
                retire.Add((map, [map], pages));
            }

            int outgoing = childParts.Logical.FindIndex(b => NameOf(b.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
            int childBlockNum = BinaryPrimitives.ReadInt32LittleEndian(childParts.Logical[outgoing].Info.AsSpan(0x04, 4));

            // Remove the FK index (data ordinal) + its outgoing info block from the child, plus — for a
            // self-reference — the incoming block, which also lives here.
            RemoveTdefBlocks(childParts, removeDataOrdinal: fkIndex.RealIndexOrdinal, removeLogical: b =>
                NameOf(b.Name).Equals(name, StringComparison.OrdinalIgnoreCase) ||
                (selfRef && IsIncomingBlockFor(b.Info, childBlockNum, child.DefinitionPage)));
            WriteTdef(child.DefinitionPage, childParts);

            if (!selfRef)
            {
                TableDef parent = _catalog.FindTable(rel.ReferencedTable)
                    ?? throw new InvalidOperationException($"Table '{rel.ReferencedTable}' was not found.");
                TdefParts parentParts = ParseTdef(parent.DefinitionPage);
                RemoveTdefBlocks(parentParts, removeDataOrdinal: null, removeLogical: b =>
                    IsIncomingBlockFor(b.Info, childBlockNum, child.DefinitionPage));
                WriteTdef(parent.DefinitionPage, parentParts);
            }

            RetireMapRecords(retire, maps, owned);
            var allocator = new PageAllocator(_channel);
            foreach (int page in owned)
                allocator.Release(page);
        }

        DeleteRelationshipRows(name);
        // Its MSysObjects object and permission rows go with it, as ACE removes them (verified). A relationship
        // written before LibRed recorded the object has none.
        if (FindObjectId(name, CatalogFormat.ObjectTypeRelationship) is { } objectId)
        {
            DeleteCatalogRows("MSysObjects", "Id", objectId);
            DeleteCatalogRows("MSysACEs", "ObjectId", objectId);
        }
        _catalog.Invalidate();
        return true;
    }

    /// <summary>
    /// Drops a table — <c>DROP TABLE table</c>. Removes the object's <c>MSysObjects</c> row and its
    /// <c>MSysACEs</c> permission rows (soft-delete, as ACE does), and frees the table's pages back to the
    /// global free map when the database closes (verified vs ACE): every page of its indexes, its data and
    /// long-value pages, the TDEF page and its continuation pages, a reference-form map's bitmap pages, and any
    /// usage-map holder left with no live record. Returns false if the table doesn't exist.
    ///
    /// A table that is the <em>child</em> (referencing) side of relationships can be dropped directly: ACE
    /// lets you drop the referencing table while the parent stays, so each such relationship is removed first
    /// (via <see cref="DropConstraint"/>). But a table still <em>referenced as a parent</em> by a surviving
    /// child cannot be dropped — drop the referencing table (or the relationship) first. EF drops FKs before
    /// tables, but database-first scaffolding cleanup drops child tables directly, which must work.
    /// </summary>
    public bool DropTable(string tableName) => DropTable(tableName, keepPermissions: false);

    /// <inheritdoc cref="DropTable(string)"/>
    /// <param name="tableName">The table to drop.</param>
    /// <param name="keepPermissions">Leaves the table's <c>MSysACEs</c> rows behind, as ACE does for the flat table
    /// and template of a version history it drops with the last append-only memo (measured) — see
    /// <see cref="DropVersionHistory"/>.</param>
    private bool DropTable(string tableName, bool keepPermissions)
    {
        TableDef? table = _catalog.FindTable(tableName);
        if (table is null) return false;

        // Remove the relationships this table owns as the child (referencing) side. Materialize first —
        // DropConstraint rewrites TDEFs and invalidates the catalog on each call.
        foreach (ForeignKey rel in _catalog.Relationships
                     .Where(r => string.Equals(r.Table, tableName, StringComparison.OrdinalIgnoreCase))
                     .ToList())
            DropConstraint(tableName, rel.Name);

        // A table still referenced by a surviving child (as the parent) cannot be dropped.
        if (_catalog.Relationships.Any(r =>
                string.Equals(r.ReferencedTable, tableName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Cannot drop table '{tableName}': it is referenced by a relationship — drop the referencing table first.");

        // A complex (multi-value / attachment) column keeps its values in an f_<GUID> flat table and itself in a
        // MSysComplexColumns row, and neither is reachable from this table's own pages — so dropping the table
        // alone leaves the flat tables orphaned and catalog rows naming a table that is gone. ACE takes both
        // with it (measured: dropping complex1.accdb's Table1 through ACE leaves neither, where LibRed left
        // four of each). Each flat table goes the ordinary way, which is why this runs before the drop proper.
        var complexColumns = _catalog.ComplexColumns
            .Where(c => string.Equals(c.OwnerTable.Name, tableName, StringComparison.OrdinalIgnoreCase))
            .Select(c => (c.FlatTable.Name, c.ComplexId))
            .ToList();
        foreach ((string flatTable, int complexId) in complexColumns)
        {
            DropTable(flatTable);
            DeleteCatalogRows("MSysComplexColumns", "ComplexID", complexId);
        }

        // Re-fetch: DropConstraint above rewrote this table's TDEF (removed FK indexes) and invalidated the catalog.
        table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");

        int tdefPage = table.DefinitionPage;
        var allocator = new PageAllocator(_channel);
        var maps = new UsageMap(_channel, table);

        // Collect before freeing anything: the pointers are read out of the TDEF, which this method frees.
        var owned = new HashSet<int>();
        // Through the chain reader: a wide table's definition spans continuation pages, and parsing only the
        // first one throws on the declared length. (The map POINTERS below are at fixed offsets inside the
        // first page, so those are read from it directly, as UsageMap does.)
        var definition = new TableDefinitionPage();
        definition.Read(_channel, tdefPage);

        // The map RECORDS live as rows on owner-zero data pages, and each is retired in turn — its bits cleared
        // first where ACE clears them, then its row reclaimed, which slides every record below it up the page.
        // The order is ACE's and it shows on disk: a slide leaves a copy of the records it moved in the space
        // they vacated, so retiring the same records in another order leaves different bytes behind. Measured
        // by whole-file diff against ACE drops: each long-value column's owned then free map, bits cleared;
        // then each index's owned map in index order, bits cleared; then the table's own owned map, bits
        // cleared, and its free map, whose bits stay.
        var retire = new List<MapRetirement>();

        foreach (ColumnDef column in table.Columns)
            QueueLongValueMaps(definition, column, maps, owned, retire);

        // Each real index keeps its B-tree pages in its own owned map, whose (row, page) pointer sits in its data
        // block. Freeing only the root strands every other page of a multi-level index, and leaving the map's
        // record live keeps its holder from ever being freed.
        foreach (byte[] block in ParseTdef(tdefPage).DataBlocks)
        {
            int at = IndexBlockFormat.UsageMapRowOffset;
            (int Row, int Page) map = (block[at], block[at + 1] | block[at + 2] << 8 | block[at + 3] << 16);
            if (map.Page == 0) continue;
            List<int> pages = maps.PagesInMap(map.Row, map.Page).ToList();
            owned.UnionWith(pages);
            retire.Add((map, [map], pages));
        }
        foreach (IndexDef index in table.RealIndexes)
            owned.Add(index.RootPage);
        List<int> dataPages = maps.DataPages().ToList();
        owned.UnionWith(dataPages);
        owned.Add(tdefPage);
        // A wide table's definition continues on further pages. ACE frees them too, and leaves every byte of
        // them alone — only the first page is marked released.
        owned.UnionWith(TdefChainReader.Read(_channel, tdefPage).ContinuationPages);

        PageBuffer tdef = _channel.ReadPage(tdefPage);
        (int Row, int Page) dataOwned = (tdef.ReadByte(_channel.Format.TdefOwnedPagesOffset),
                                         tdef.ReadInt24(_channel.Format.TdefOwnedPagesOffset + 1));
        retire.Add((dataOwned, [dataOwned], dataPages));
        retire.Add(((tdef.ReadByte(_channel.Format.TdefFreePagesOffset),
                     tdef.ReadInt24(_channel.Format.TdefFreePagesOffset + 1)), [], []));

        RetireMapRecords(retire, maps, owned);

        // Access marks the released definition page itself: its type becomes 0x0108 and nothing else on
        // the page changes, so the old definition is still sitting there when Compact comes to reclaim it.
        // Measured across an ACE DROP TABLE: exactly one byte of the 4,096 differs, the type's low byte. Only
        // the TDEF is marked — the data, long-value and map-holder pages ACE frees keep their 0x0101.
        byte[] released = _channel.ReadPage(tdefPage).Span.ToArray();
        PageHeader.WriteType(released, PageType.ReleasedTableDefinition);
        _channel.WritePage(tdefPage, released);

        foreach (int page in owned)
            allocator.Release(page);   // reusable only after this handle closes, as ACE holds them

        DeleteCatalogRows("MSysObjects", "Id", tdefPage);
        if (!keepPermissions) DeleteCatalogRows("MSysACEs", "ObjectId", tdefPage);
        _catalog.Invalidate();
        return true;
    }

    /// <summary>
    /// Queues a long-value column's two usage-map records for <see cref="RetireMapRecords"/>, owned then free, and
    /// adds the pages its owned map records to <paramref name="owned"/>. A Memo/OLE (or calculated long) column owns
    /// its LVAL pages through a PER-COLUMN usage map, whose pointer sits in the TDEF keyed by column id. Those pages
    /// are not in the table's data-page map, so freeing only the data pages leaves every long value stranded — and
    /// for a memo-heavy table that is nearly the whole table. Measured against ACE: dropping a 60-row memo table
    /// returned 123 pages through ACE and 2 through LibRed, the missing 121 being LVAL pages. Nothing is queued for
    /// a column with no long-value maps.
    /// </summary>
    private static void QueueLongValueMaps(
        TableDefinitionPage definition, ColumnDef column, UsageMap maps, HashSet<int> owned, List<MapRetirement> retire)
    {
        definition.LongValueOwnedMaps.TryGetValue(column.ColumnId, out (int Row, int Page) map);
        definition.LongValueFreeMaps.TryGetValue(column.ColumnId, out (int Row, int Page) columnFree);
        if (map.Page != 0)
        {
            // Clear each page's bit on the way out, exactly as releasing a single value does: the record's
            // bitmap bytes are zeroed before its row is retired. Except a page still in the free map, whose bit
            // stays in both records — measured by whole-file diff of ACE drops: of an OLE column owning a chain,
            // two full single-value pages and its current append page, every bit went but the append page's.
            List<int> pages = maps.PagesInMap(map.Row, map.Page).ToList();
            owned.UnionWith(pages);
            HashSet<int> stillFree = columnFree.Page != 0 ? maps.PagesInMap(columnFree.Row, columnFree.Page).ToHashSet() : [];
            retire.Add((map, columnFree.Page != 0 ? [map, columnFree] : [map], pages.Where(p => !stillFree.Contains(p)).ToList()));
        }
        if (columnFree.Page != 0) retire.Add((columnFree, [], []));
    }

    /// <summary>
    /// Takes usage-map records off their pages the way ACE does, in the order given — tombstone the slot, slide the
    /// rows below it up, return the bytes to the page's free count — rather than leaving dead maps behind. On a
    /// shared holder that is the whole fix: the page survives and must not keep records for a map that no longer
    /// exists. Each record's <c>Clear</c> maps first have its <c>Pages</c> cleared. Adds to <paramref name="owned"/>
    /// the pages to release: a reference-form record's bitmap pages, and every holder left with no live row.
    /// </summary>
    private void RetireMapRecords(IEnumerable<MapRetirement> retire, UsageMap maps, HashSet<int> owned)
    {
        var usageMaps = new UsageMapWriter(_channel);
        var holders = new List<int>();
        var retired = new HashSet<(int Row, int Page)>();
        foreach (((int Row, int Page) map, IReadOnlyList<(int Row, int Page)> clear, IReadOnlyList<int> pages) in retire)
        {
            if (map.Page <= 1 || map.Page >= _channel.PageCount || !retired.Add(map)) continue;
            foreach ((int Row, int Page) cleared in clear)
                foreach (int page in pages)
                    usageMaps.SetBit(cleared.Row, cleared.Page, page, set: false);

            // A reference-form record keeps its bitmap on dedicated pages. ACE zeroes each one's bitmap — even
            // for the table's own owned map, whose bits an inline record keeps — leaves its header, and frees it.
            foreach (int bitmapPage in maps.BitmapPagesOf(map.Row, map.Page))
            {
                byte[] bitmap = _channel.ReadPage(bitmapPage).Span.ToArray();
                bitmap.AsSpan(4).Clear();
                _channel.WritePage(bitmapPage, bitmap);
                owned.Add(bitmapPage);
            }

            byte[] holderBytes = _channel.ReadPage(map.Page).Span.ToArray();
            RowInserter.ReclaimRow(_channel.Format, holderBytes, map.Row);
            _channel.WritePage(map.Page, holderBytes);
            if (!holders.Contains(map.Page)) holders.Add(map.Page);
        }

        // ACE frees a holder once the dropped records were the only thing on it — measured: for a one-memo-column
        // table ACE returned the long-value map's holder. A holder can carry records for several columns or tables
        // as separate rows, so releasing one that still serves another map would hand away a live page: corruption
        // rather than a leak. Hence the holder goes only when no live row is left on it.
        foreach (int holderPage in holders)
        {
            var holder = new DataPage();
            holder.Read(_channel.ReadPage(holderPage), _channel.Format);
            bool live = false;
            for (int row = 0; row < holder.RowCount && !live; row++)
                live = !holder.Rows[row].IsDeleted && holder.Rows[row].Length > 0;
            if (!live) owned.Add(holderPage);
        }
    }

    /// <summary>
    /// Drops a view or stored procedure — <c>DROP VIEW name</c> / <c>DROP PROCEDURE name</c>. Both are a
    /// type-5 <c>MSysObjects</c> object; ACE's two statements are interchangeable (verified: DROP VIEW works
    /// on a procedure and vice versa), so this handles either. The inverse of <c>ViewCreator</c>: deletes the
    /// object's MSysObjects row, its MSysQueries rows, and its two MSysACEs permission rows (index entries
    /// removed, not just soft-deleted). No pages to free (a query owns none — its MSysQueries rows live on the
    /// shared MSysQueries pages). Returns false if no such query object exists.
    /// </summary>
    public bool DropQueryObject(string name)
    {
        if (FindObjectId(name, StoredQueryFormat.ObjectTypeQuery) is not { } objId) return false;

        DeleteCatalogRows("MSysObjects", "Id", objId);
        DeleteCatalogRows("MSysQueries", "ObjectId", objId);
        DeleteCatalogRows("MSysACEs", "ObjectId", objId);
        _catalog.Invalidate();
        return true;
    }

    /// <summary>The <c>MSysObjects</c> id of the object of <paramref name="type"/> named <paramref name="name"/>,
    /// or null when there is none.</summary>
    private int? FindObjectId(string name, short type)
    {
        TableDef mo = _catalog.RequireTable("MSysObjects");
        int idIdx = mo.RequireColumn("Id").Index;
        int nameIdx = mo.RequireColumn("Name").Index;
        int typeIdx = mo.RequireColumn("Type").Index;

        var objects = new Table(_channel, mo);
        foreach (object?[] values in objects.Rows(objects.DecodeOnly([idIdx, nameIdx, typeIdx])))
            if (string.Equals(values[nameIdx] as string, name, StringComparison.OrdinalIgnoreCase)
                && Convert.ToInt16(values[typeIdx] ?? (short)0, CultureInfo.InvariantCulture) == type)
                return Convert.ToInt32(values[idIdx], CultureInfo.InvariantCulture);
        return null;
    }


    /// <summary>
    /// Renames a table — <c>ALTER TABLE … RENAME TO</c>. Measured against ACE (see <c>RenameFanOutProbeTest</c>):
    /// only two things move — the object's <c>MSysObjects.Name</c>, and the by-name table references in
    /// <c>MSysRelationships</c>. Everything else is deliberately left alone, matching ACE exactly:
    /// <list type="bullet">
    /// <item>indexes (including the PK) keep their own names and need no fixup — they reference the table by id;</item>
    /// <item>the relationship keeps its own name and its enforcement;</item>
    /// <item>stored queries/views are left <b>dangling</b> — ACE does not rewrite <c>MSysQueries</c> (Name
    /// AutoCorrect is an Access application feature), and "helpfully" fixing them would diverge from Jet.</item>
    /// </list>
    /// Returns false if no such table exists; throws if the new name is already taken.
    /// </summary>
    public bool RenameTable(string oldName, string newName)
    {
        TableDef? table = _catalog.FindTable(oldName);
        if (table is null) return false;
        // The same names Create refuses. A rename reaches the identical bytes by a different route, so
        // validating only on the way in left it open: renaming a COLUMN to over 64 characters makes the
        // whole database unreadable to ACE ("Unrecognized database format"), which is exactly what the
        // create-side check exists to prevent (RenameNameValidationAccessTests).
        JetName.Validate(newName, "table name");
        // The table being renamed is not a collision with itself: renaming to the same name is a no-op that ACE
        // allows (and EF's schema "move" degrades to exactly that on a schema-less engine), as is a case-only
        // change. Both verified — RenameFanOutProbeTest.
        if (ObjectNameExists(newName, exceptObjectId: table.DefinitionPage))
            throw new SchemaObjectExistsException(
                $"ALTER TABLE '{oldName}' RENAME TO '{newName}': a table or query named '{newName}' already exists.",
                newName);

        RenameCatalogObject(table.DefinitionPage, newName);
        RepointRelationshipTables(oldName, newName);
        return true;
    }

    /// <summary>Sets the <c>Name</c> of the MSysObjects row whose <c>Id</c> is this table's TDEF page.</summary>
    private void RenameCatalogObject(int tdefPage, string newName) =>
        UpdateCatalogRows("MSysObjects", "Id", tdefPage, required: true, ("Name", newName));

    /// <summary>Repoints every relationship that names <paramref name="oldName"/> on either side. Both the
    /// child (<c>szObject</c>) and parent (<c>szReferencedObject</c>) are stored by name, and a self-reference
    /// names the table twice — hence updating both columns in one pass over each row.</summary>
    private void RepointRelationshipTables(string oldName, string newName)
    {
        TableDef? def = _catalog.FindTable("MSysRelationships");
        if (def is null) return; // a database with no relationships has no catalog table to fix up

        int childIndex = def.RequireColumn("szObject").Index;
        int parentIndex = def.RequireColumn("szReferencedObject").Index;
        var table = new Table(_channel, def);

        foreach ((RowId id, object?[] values) in table.RowsWhere([childIndex, parentIndex],
            v => NameMatches(v[childIndex], oldName) || NameMatches(v[parentIndex], oldName)).ToList())
        {
            var updates = new List<(int Column, object? Value)>(2);
            if (NameMatches(values[childIndex], oldName)) updates.Add((childIndex, newName));
            if (NameMatches(values[parentIndex], oldName)) updates.Add((parentIndex, newName));
            if (updates.Count > 0)
                SetCatalogValues(table, def, id, values, updates.ToArray());
        }
    }

    /// <summary>
    /// Renames a column — <c>ALTER TABLE … RENAME COLUMN … TO</c>. Measured against ACE (see
    /// <c>RenameFanOutProbeTest</c>): the name in the TDEF's column region moves, <c>MSysRelationships</c>'
    /// by-name column references are repointed, and the column's <c>LvProp</c> property block is re-owned so it
    /// <b>keeps its DEFAULT</b>. Nothing else moves — indexes reference columns by id, so they keep their own
    /// names and need no fixup, and stored queries are left dangling exactly as ACE leaves them.
    /// Returns false if no such column exists; throws if the new name is already used on the table.
    /// </summary>
    public bool RenameColumn(string tableName, string oldName, string newName)
    {
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");
        ColumnDef? col = table.Columns.FirstOrDefault(c => string.Equals(c.Name, oldName, StringComparison.OrdinalIgnoreCase));
        if (col is null) return false;
        JetName.Validate(newName, "column name");   // see RenameTable: unchecked, this corrupts the file
        if (table.Columns.Any(c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"ALTER TABLE '{tableName}' RENAME COLUMN '{oldName}' TO '{newName}': the table already has a column named '{newName}'.");

        // A complex column's catalog row names its column by NAME, and the catalog will not resolve a row
        // whose name the owning table no longer has — so a rename that left the row behind did not merely
        // misname the column, it disconnected it from its values. Read before the TDEF is rewritten, while
        // the catalog still resolves the old name.
        int? complexId = _catalog.FindComplexColumn(tableName, oldName)?.ComplexId;

        TdefParts parts = ParseTdef(table.DefinitionPage); // stitches continuation pages for a multi-page TDEF
        RenameColumnInParts(parts, table.Columns.Count, col.Index, newName, _channel.Format);
        WriteTdef(table.DefinitionPage, parts);
        RenameColumnProperties(table.DefinitionPage, oldName, newName);
        RepointRelationshipColumns(tableName, oldName, newName);
        if (complexId is int id) SetComplexColumnName(id, newName);
        _catalog.Invalidate();
        return true;
    }

    /// <summary>Replaces the name entry of the column at <paramref name="renameIndex"/> in the column region.
    /// The descriptors are fixed-size and untouched; only the variable-length name pool is rebuilt (a
    /// different-length name shifts every following entry), and the column count is unchanged.</summary>
    private static void RenameColumnInParts(
        TdefParts parts, int colCount, int renameIndex, string newName, JetFormatBase format)
    {
        int descSize = format.ColumnDescriptorSize;
        ReadOnlySpan<byte> cols = parts.Columns;

        var descriptors = new List<byte[]>(colCount);
        for (int i = 0; i < colCount; i++)
            descriptors.Add(cols.Slice(i * descSize, descSize).ToArray());

        int np = colCount * descSize;
        var names = new List<byte[]>(colCount);
        for (int i = 0; i < colCount; i++)
        {
            int len = BinaryPrimitives.ReadUInt16LittleEndian(cols.Slice(np, 2));
            names.Add(cols.Slice(np, 2 + len).ToArray());
            np += 2 + len;
        }

        byte[] nameBytes = System.Text.Encoding.Unicode.GetBytes(newName);
        byte[] entry = new byte[2 + nameBytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(0, 2), (ushort)nameBytes.Length);
        nameBytes.CopyTo(entry, 2);
        names[renameIndex] = entry;

        var blob = new List<byte>(parts.Columns.Length);
        foreach (byte[] d in descriptors) blob.AddRange(d);
        foreach (byte[] n in names) blob.AddRange(n);
        parts.Columns = [.. blob];
    }

    /// <summary>Re-owns the renamed column's extended-property block in its table's <c>MSysObjects.LvProp</c>
    /// blob, so its DefaultValue/Required/validation survive the rename (ACE does this — verified). No-op when
    /// the column had no properties.</summary>
    private void RenameColumnProperties(int tdefPage, string oldName, string newName)
    {
        if (ReadObjectProperties(tdefPage) is not { Length: > 0 } blob) return;

        byte[] renamed = RewriteCalculatedReferences(
            PropertyBlob.RenameOwner(blob, oldName, newName), oldName, newName);
        // Nothing owned by, or referring to, this column — leave the blob exactly as it was.
        if (renamed.AsSpan().SequenceEqual(blob)) return;
        WriteObjectProperties(tdefPage, renamed);
    }

    /// <summary>The extended-property blob <c>MSysObjects.LvProp</c> holds for an object — every column's
    /// DefaultValue, Required, ValidationRule, Format, Description, AllowZeroLength and the rest, plus the
    /// table's own — or null when it has none.</summary>
    private byte[]? ReadObjectProperties(int objectId)
    {
        (_, Table table, int idIdx, ColumnDef lvProp) = ObjectProperties();
        return RowsKeyed(table, idIdx, objectId)
            .Select(r => r.Values[lvProp.Index] as byte[]).FirstOrDefault();
    }

    /// <summary>The object's <c>MSysObjects.Flags</c>, or null when it has no row.</summary>
    private int? ReadObjectFlags(int objectId)
    {
        (TableDef msys, Table table, int idIdx, _) = ObjectProperties();
        int flags = msys.FindColumn("Flags")!.Index;
        return RowsKeyed(table, idIdx, objectId).Select(r => r.Values[flags] as int?).FirstOrDefault();
    }

    /// <summary>Replaces an object's extended-property blob with <paramref name="properties"/>. Not an
    /// <see cref="UpdateCatalogRows"/> call, because <c>LvProp</c> is a long value: the blob has to be stored first
    /// — inline or on a page — and the row given the descriptor for it.</summary>
    private void WriteObjectProperties(int objectId, byte[] properties)
    {
        (TableDef msys, Table table, int idIdx, ColumnDef lvProp) = ObjectProperties();
        foreach ((RowId id, object?[] values) in RowsKeyed(table, idIdx, objectId))
        {
            byte[] descriptor = new RowInserter(_channel, msys).StorePackedLongValue(lvProp.ColumnId, properties);
            values[lvProp.Index] = new LongValueDescriptor(descriptor);
            table.Update(id, values, new HashSet<int> { lvProp.Index });
            return;
        }
    }

    /// <summary>The <c>LvProp</c> property holding an object's copy of its Name AutoCorrect map.</summary>
    private const string NameMapProperty = "NameMap";

    /// <summary>Every <c>MSysNameMap</c> row, its map decoded; empty when the file has no such table.</summary>
    public IReadOnlyList<NameMapRow> ReadNameMapRows()
    {
        if (_catalog.FindTable("MSysNameMap") is not { } def) return [];
        int guid = def.RequireColumn("GUID").Index, id = def.RequireColumn("Id").Index,
            name = def.RequireColumn("Name").Index, type = def.RequireColumn("Type").Index,
            map = def.RequireColumn("NameMap").Index;
        return [.. new Table(_channel, def).Rows().Select(r => new NameMapRow(
            (Guid)r[guid]!, (int)r[id]!, ((string)r[name]!).TrimEnd('\0'), (int)r[type]!,
            r[map] is byte[] blob ? NameMap.ReadRow(blob) : null))];
    }

    /// <summary>Replaces the map of the <c>MSysNameMap</c> row whose <c>GUID</c> is <paramref name="objectGuid"/>,
    /// and its <c>Name</c> when <paramref name="name"/> is given — stored with the trailing NUL Access gives it.
    /// Returns false when there is no such row; a row is never added, since its <c>Id</c> is not understood.</summary>
    public bool WriteNameMapRow(Guid objectGuid, NameMap map, string? name)
    {
        if (_catalog.FindTable("MSysNameMap") is not { } def) return false;
        int guidIndex = def.RequireColumn("GUID").Index, nameIndex = def.RequireColumn("Name").Index;
        ColumnDef mapColumn = def.RequireColumn("NameMap");
        var table = new Table(_channel, def);

        foreach ((RowId id, object?[] values) in table.RowsWhere([guidIndex], v => v[guidIndex] is Guid g && g == objectGuid).ToList())
        {
            // A long value, like LvProp: stored first, the row given the descriptor (see WriteObjectProperties).
            byte[] descriptor = new RowInserter(_channel, def).StorePackedLongValue(mapColumn.ColumnId, map.WriteRow());
            values[mapColumn.Index] = new LongValueDescriptor(descriptor);
            var changed = new HashSet<int> { mapColumn.Index };
            if (name is not null)
            {
                values[nameIndex] = name + "\0";
                changed.Add(nameIndex);
            }
            table.Update(id, values, changed);
            return true;
        }
        return false;
    }

    /// <summary>The <c>NameMap</c> property of the object named <paramref name="objectName"/> of
    /// <c>MSysObjects.Type</c> <paramref name="objectType"/>; null when the object has none.</summary>
    /// <exception cref="InvalidOperationException">There is no such object.</exception>
    public NameMap? ReadNameMapProperty(string objectName, short objectType)
    {
        byte[]? blob = ReadObjectProperties(RequireObjectId(objectName, objectType));
        return blob is { Length: > 0 }
            && PropertyBlob.Read(blob).FirstOrDefault(p => p.IsOwnedBy("") && p.Name == NameMapProperty) is { RawValue: { } raw }
            ? NameMap.ReadProperty(raw)
            : null;
    }

    /// <summary>Sets the object's <c>NameMap</c> property to <paramref name="map"/>, or removes it when null. An
    /// existing entry keeps its place and flag byte; a new one takes the flag <c>0x00</c> Access gives it. Every
    /// other property is left as it was.</summary>
    /// <exception cref="InvalidOperationException">There is no such object.</exception>
    public void WriteNameMapProperty(string objectName, short objectType, NameMap? map)
    {
        int objectId = RequireObjectId(objectName, objectType);
        byte[] blob = ReadObjectProperties(objectId) ?? [];
        var properties = PropertyBlob.Read(blob).ToList();
        int at = properties.FindIndex(p => p.IsOwnedBy("") && p.Name == NameMapProperty);
        if (map is null)
        {
            if (at < 0) return;
            properties.RemoveAt(at);
        }
        else
        {
            PropertyBlob.Property property = PropertyBlob.Of("", NameMapProperty, map.WriteProperty(), JetDataType.Ole);
            if (at >= 0) properties[at] = property with { Flags = properties[at].Flags, Block = properties[at].Block };
            else properties.Add(property with { Flags = 0 });
        }
        WriteObjectProperties(objectId, PropertyBlob.Write(properties, blob));
    }

    /// <summary>The <c>MSysObjects.Id</c> of the object with this name and type.</summary>
    private int RequireObjectId(string name, short type)
    {
        TableDef msys = _catalog.RequireTable("MSysObjects");
        int idIndex = msys.RequireColumn("Id").Index, nameIndex = msys.RequireColumn("Name").Index,
            typeIndex = msys.RequireColumn("Type").Index;
        var objects = new Table(_channel, msys);
        foreach (object?[] row in objects.Rows(objects.DecodeOnly([idIndex, nameIndex, typeIndex])))
            if (row[typeIndex] is short t && t == type && NameMatches(row[nameIndex], name))
                return (int)row[idIndex]!;
        throw new InvalidOperationException($"There is no object '{name}' of type {type}.");
    }

    /// <summary><c>MSysObjects</c> and the two columns every extended-property path works through: the
    /// <c>Id</c> it matches an object by, and the <c>LvProp</c> holding the blob.</summary>
    private (TableDef Definition, Table Table, int IdIndex, ColumnDef LvProp) ObjectProperties()
    {
        TableDef msys = _catalog.RequireTable("MSysObjects");
        return (msys, new Table(_channel, msys), msys.RequireColumn("Id").Index, msys.RequireColumn("LvProp"));
    }

    /// <summary>The calculated columns of <paramref name="table"/> whose expression reads
    /// <paramref name="column"/>. A malformed expression counts as reading nothing rather than throwing:
    /// refusing to drop is a safeguard, and it must not turn into a refusal to drop anything at all because
    /// some other column's expression cannot be parsed.</summary>
    private static List<string> CalculatedColumnsReading(TableDef table, ColumnDef column)
    {
        var dependents = new List<string>();
        foreach (ColumnDef candidate in table.Columns)
        {
            if (!candidate.IsCalculated || candidate.CalculatedExpression is null) continue;
            try
            {
                if (CalculatedValue.ReferencedIndexes(candidate, table.Columns).Contains(column.Index))
                    dependents.Add(candidate.Name);
            }
            catch (Calculated.CalculatedExpressionException) { /* unparseable: reads nothing we can prove */ }
        }
        return dependents;
    }

    /// <summary>Repoints every calculated <c>Expression</c> in the blob that READS the renamed column.
    /// <see cref="PropertyBlob.RenameOwner"/> moves the renamed column's own properties; this is about the
    /// OTHER columns that mention it, which nothing else would fix. Measured: without it a rename leaves
    /// <c>[Qty]*2</c> pointing at a column that no longer exists, and ACE fails every read of the calculated
    /// column — a table broken by an operation that named a different column entirely.</summary>
    private static byte[] RewriteCalculatedReferences(byte[] blob, string oldName, string newName)
    {
        var properties = PropertyBlob.Read(blob).ToList();
        bool changed = false;
        for (int i = 0; i < properties.Count; i++)
        {
            if (properties[i].Name != PropertyBlob.ExpressionProperty) continue;
            string rewritten = Calculated.CalculatedExpression.RenameColumnReference(
                properties[i].Value, oldName, newName);
            if (rewritten == properties[i].Value) continue;
            // Drop RawValue so the new text is encoded rather than the original bytes replayed.
            properties[i] = properties[i] with { Value = rewritten, RawValue = null };
            changed = true;
        }
        return changed
            ? PropertyBlob.Write(properties, blob)
            : blob;
    }

    /// <summary>Repoints every relationship that names this column, on whichever side owns it. Unlike a table
    /// name, a column name is only unique within its table, so each side is matched on its table name too.</summary>
    private void RepointRelationshipColumns(string tableName, string oldName, string newName)
    {
        TableDef? def = _catalog.FindTable("MSysRelationships");
        if (def is null) return;

        int childTable = def.RequireColumn("szObject").Index;
        int childColumn = def.RequireColumn("szColumn").Index;
        int parentTable = def.RequireColumn("szReferencedObject").Index;
        int parentColumn = def.RequireColumn("szReferencedColumn").Index;
        var table = new Table(_channel, def);

        foreach ((RowId id, object?[] values) in table.RowsWhere([childTable, childColumn, parentTable, parentColumn],
            v => (NameMatches(v[childTable], tableName) && NameMatches(v[childColumn], oldName))
                || (NameMatches(v[parentTable], tableName) && NameMatches(v[parentColumn], oldName))).ToList())
        {
            var updates = new List<(int Column, object? Value)>(2);
            if (NameMatches(values[childTable], tableName) && NameMatches(values[childColumn], oldName))
                updates.Add((childColumn, newName));
            if (NameMatches(values[parentTable], tableName) && NameMatches(values[parentColumn], oldName))
                updates.Add((parentColumn, newName));
            if (updates.Count > 0)
                SetCatalogValues(table, def, id, values, updates.ToArray());
        }
    }

    /// <summary>
    /// Whether a <b>table, saved query or linked table</b> already uses this name — the objects of the Tables
    /// container, whose names MSysObjects' unique <c>(ParentId, Name)</c> index keeps distinct. That is ACE's rule
    /// for a new or renamed table (measured, and <c>RenameFanOutProbeTest</c> for renames): a form, report, macro,
    /// module, relationship or database document of the same name does not collide. Scanned straight from
    /// MSysObjects rather than the catalog's reconstructed tables and <c>Views</c>, which omit linked tables and
    /// queries LibRed can't rebuild.
    /// </summary>
    private bool ObjectNameExists(string name, int exceptObjectId)
    {
        TableDef mo = _catalog.RequireTable("MSysObjects");
        int idIndex = mo.RequireColumn("Id").Index;
        int nameIndex = mo.RequireColumn("Name").Index;
        int parentIndex = mo.RequireColumn("ParentId").Index;

        var objects = new Table(_channel, mo);
        foreach (object?[] values in objects.Rows(objects.DecodeOnly([idIndex, nameIndex, parentIndex])))
        {
            if (!NameMatches(values[nameIndex], name)) continue;
            // Skip the object being renamed — it can't collide with itself (same-name and case-only renames).
            if (values[idIndex] is not null
                && Convert.ToInt32(values[idIndex], CultureInfo.InvariantCulture) == exceptObjectId) continue;
            if (values[parentIndex] is int parent && parent == CatalogFormat.ObjectContainerParentId) return true;
        }

        return false;
    }

    private static bool NameMatches(object? value, string name) =>
        value is string s && string.Equals(s, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rewrites some columns of one catalog row, keeping any index whose key covers a changed column in
    /// step — MSysObjects is uniquely indexed on (ParentId, Name), so a rename has to move that entry rather
    /// than just overwrite the value.</summary>
    private static void SetCatalogValues(
        Table table, TableDef def, RowId id, object?[] values, params (int Column, object? Value)[] updates)
    {
        var newValues = (object?[])values.Clone();
        var changed = new HashSet<int>();
        foreach ((int column, object? value) in updates)
        {
            newValues[column] = value;
            changed.Add(column);
        }

        foreach (IndexDef index in def.RealIndexes)
            if (index.Columns.Any(c => changed.Contains(c.Column.Index)))
                table.MoveIndexEntry(index, values, newValues, id);

        table.Update(id, newValues, changed);
    }

    /// <summary>Deletes every row of <paramref name="catalogTable"/> whose <paramref name="keyColumn"/> equals
    /// <paramref name="keyValue"/> (the object id) — used to remove a dropped table's MSysObjects and MSysACEs
    /// rows. A full delete: its <b>index entries are removed</b> (not just the slot soft-deleted) so, e.g., the
    /// MSysObjects <c>ParentIdName</c> unique index doesn't retain a stale entry that would then reject
    /// re-creating a same-named table.</summary>
    /// <summary>Sets <paramref name="updates"/> on every row of <paramref name="catalogTable"/> whose
    /// <paramref name="keyColumn"/> equals <paramref name="keyValue"/> — the update counterpart of
    /// <see cref="DeleteCatalogRows"/>, and the one way this class changes a catalog row: through
    /// <see cref="SetCatalogValues"/>, so an indexed column's entry moves with its value.</summary>
    /// <param name="catalogTable">The system table to update.</param>
    /// <param name="keyColumn">The column matched against <paramref name="keyValue"/>.</param>
    /// <param name="keyValue">The key naming the rows to update.</param>
    /// <param name="required">Whether a missing catalog table is an error. False for the ones a database need
    /// not have at all (a Jet 4 file has no <c>MSysComplexColumns</c>).</param>
    /// <param name="updates">The column/value pairs to set on each matching row.</param>
    private void UpdateCatalogRows(string catalogTable, string keyColumn, int keyValue,
        bool required, params (string Column, object? Value)[] updates)
    {
        TableDef? t = _catalog.FindTable(catalogTable);
        if (t is null)
        {
            if (!required) return;
            throw new InvalidOperationException($"{catalogTable} catalog table was not found.");
        }

        int key = t.RequireColumn(keyColumn).Index;
        var columns = updates.Select(u => (Column: t.RequireColumn(u.Column).Index, u.Value)).ToArray();
        var table = new Table(_channel, t);

        foreach ((RowId id, object?[] values) in RowsKeyed(table, key, keyValue))
            SetCatalogValues(table, t, id, values, [.. columns]);
    }

    /// <summary>The rows of a catalog table whose key column holds <paramref name="keyValue"/>, materialised
    /// before the caller writes any of them back.</summary>
    private static List<(RowId Id, object?[] Values)> RowsKeyed(Table table, int keyColumn, int keyValue) =>
        [.. table.RowsWhere([keyColumn], values => values[keyColumn] is not null
            && Convert.ToInt32(values[keyColumn], CultureInfo.InvariantCulture) == keyValue)];

    private void DeleteCatalogRows(string catalogTable, string keyColumn, int keyValue)
    {
        TableDef t = _catalog.RequireTable(catalogTable);
        int idx = t.RequireColumn(keyColumn).Index;
        var table = new Table(_channel, t);

        foreach ((RowId id, object?[] values) in RowsKeyed(table, idx, keyValue))
        {
            foreach (IndexDef index in t.RealIndexes)
                table.RemoveIndexEntry(index, values, id);
            table.Delete(id);
        }
    }

    /// <summary>Follows a renamed complex column in its <c>MSysComplexColumns</c> row. The catalog matches the
    /// row to a column by name, so a stale one stops resolving altogether — the column keeps its values on
    /// disk and nothing can reach them.</summary>
    private void SetComplexColumnName(int complexId, string columnName) =>
        UpdateCatalogRows("MSysComplexColumns", "ComplexID", complexId, required: true, ("ColumnName", columnName));

    /// <summary>
    /// Drops a secondary/unique/primary index — <c>DROP INDEX index ON table</c>. Byte-faithful with ACE
    /// (probed): remove the index's 12-byte stats block, 52-byte index-data block, and its 28-byte logical
    /// info block + name from the TDEF (decrementing counts and the data-ordinal ref of any block past it),
    /// and free its B-tree root page back to the global free map — the same index-removal path as DROP
    /// CONSTRAINT, minus the relationship linkage. A secondary index lives only in the TDEF (no MSys row).
    /// Returns false if no such index exists. Throws if the index backs a relationship (ACE rejects that —
    /// drop the relationship first). A multi-page TDEF is handled, and PK and unique indexes ARE droppable.
    /// </summary>
    public bool DropIndex(string tableName, string indexName)
    {
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");
        IndexDef? index = table.Indexes.FirstOrDefault(i => string.Equals(i.Name, indexName, StringComparison.OrdinalIgnoreCase));
        if (index is null) return false;

        if (IndexParticipatesInRelationship(table, index))
            throw new InvalidOperationException(
                $"Cannot drop index '{indexName}': it is used in a relationship — drop the relationship first.");

        TdefParts parts = ParseTdef(table.DefinitionPage); // stitches continuation pages for a multi-page TDEF

        // The whole B-tree, not just its root. Every other page of a split index is recorded only in the index's
        // own usage map, so releasing the root alone strands them — owned by nothing and never handed out again.
        // Read before the blocks go: the map pointer lives in the data block this is about to remove. ACE frees
        // the lot and takes the map's record off its holder page as well (measured by whole-file diff: the bits
        // cleared, the row tombstoned, the holder's free space back), which is what RetireMapRecords does.
        var owned = new HashSet<int> { index.RootPage };
        var retire = new List<MapRetirement>();
        int at = IndexBlockFormat.UsageMapRowOffset;
        byte[] dataBlock = parts.DataBlocks[index.RealIndexOrdinal];
        (int Row, int Page) map = (dataBlock[at], dataBlock[at + 1] | dataBlock[at + 2] << 8 | dataBlock[at + 3] << 16);
        var maps = new UsageMap(_channel, table);
        if (map.Page != 0)
        {
            List<int> pages = maps.PagesInMap(map.Row, map.Page).ToList();
            owned.UnionWith(pages);
            retire.Add((map, [map], pages));
        }

        RemoveTdefBlocks(parts, removeDataOrdinal: index.RealIndexOrdinal,
            removeLogical: b => NameOf(b.Name).Equals(indexName, StringComparison.OrdinalIgnoreCase));
        WriteTdef(table.DefinitionPage, parts);

        RetireMapRecords(retire, maps, owned);
        var allocator = new PageAllocator(_channel);
        foreach (int page in owned)
            allocator.Release(page);
        _catalog.Invalidate();
        return true;
    }

    /// <summary>True if the index IS a relationship's enforcement index — the one specific index ACE refuses
    /// to drop while the relationship exists. This is NOT "any index over the relationship's columns": a
    /// redundant same-columns secondary index is droppable, and EF relies on that (it creates an explicit
    /// index, adds the FK, then drops the now-redundant explicit index). ACE-verified (ZzProbe): with a
    /// relationship on POrd.CustomerId → PCust.Id, dropping a coincident IX_POrd_CustomerId / IX_PCust_Id
    /// succeeds, but dropping the FK's own child index (named after the relationship) or the referenced PK
    /// fails with "used in a relationship".
    /// <para>Two indexes are protected: on the child, the FK's backing index — named after the relationship,
    /// as both ACE and <see cref="AddForeignKey"/> create it; on the parent, the referenced key — the
    /// unique/primary index over the referenced columns.</para></summary>
    private bool IndexParticipatesInRelationship(TableDef table, IndexDef index)
    {
        var cols = index.Columns.Select(c => c.Column.Name).ToList();
        bool SameCols(IEnumerable<string> other) =>
            other.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                 .SequenceEqual(cols.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        // The parent side matches a unique/primary index only, which is exactly the set FindParentKeyIndex
        // will accept as a parent key — ACE refuses a relationship over anything else, so no file this engine
        // writes can have one.
        return _catalog.Relationships.Any(r =>
            (string.Equals(r.Table, table.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(index.Name, r.Name, StringComparison.OrdinalIgnoreCase)) ||
            (string.Equals(r.ReferencedTable, table.Name, StringComparison.OrdinalIgnoreCase)
                && (index.IsUnique || index.IsPrimaryKey)
                && SameCols(r.Columns.Select(c => c.ReferencedColumn))));
    }

    /// <summary>
    /// Adds a column — <c>ALTER TABLE t ADD COLUMN c type</c>. A metadata TDEF edit (probed vs ACE, the
    /// inverse of DROP COLUMN): appends the column's 25-byte descriptor + name, gives it the next column id
    /// from the <c>0x29</c> max-columns high-water (which keeps counting even past dropped ids), appends its
    /// fixed offset (end of the fixed region) or variable index (current variable count), and bumps
    /// ColumnCount (0x2D), the 0x29 high-water, and — for a variable column — VariableColumnCount (0x2B).
    /// Existing rows are not rewritten; they read the new column as NULL via the null bitmap. Fully correct
    /// on an empty table (new inserts include it); on a populated table the column is visible and old rows
    /// read NULL. A memo/OLE column additionally gets its §3.3.2 usage-map entry (two empty maps appended to
    /// the table's usage-map page — or, if that page is full, a dedicated map page, the fallback CREATE TABLE
    /// uses on a wide table). Returns false if the column already exists. Multi-page TDEFs are handled.
    /// </summary>
    public bool AddColumn(string tableName, ColumnSpec spec, string? defaultValue = null)
    {
        JetName.Validate(spec.Name, "column name");
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");
        if (table.Columns.Any(c => string.Equals(c.Name, spec.Name, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (table.Columns.Count >= MaxColumnsPerTable)
            throw new NotSupportedException($"Table '{tableName}' already has {MaxColumnsPerTable} columns, the most a table can have.");
        JetFormatBase format = _channel.Format;
        // As on the create path, a calculated column with a Memo RESULT needs the long-value maps even though
        // its declared type is Text — the value reaches a page through a descriptor either way (§3.4a).
        bool isLongValue = spec.Type is JetDataType.Memo or JetDataType.Ole
                           || spec.CalculatedResultType is JetDataType.Memo;
        TdefParts parts = ParseTdef(table.DefinitionPage); // stitches continuation pages for a multi-page TDEF

        int maxCols = BinaryPrimitives.ReadUInt16LittleEndian(parts.Header.AsSpan(format.TdefMaxColumnsOffset, 2));
        int varCount = BinaryPrimitives.ReadUInt16LittleEndian(parts.Header.AsSpan(format.TdefVariableColumnsOffset, 2));
        int colCount = BinaryPrimitives.ReadUInt16LittleEndian(parts.Header.AsSpan(format.TdefColumnCountOffset, 2));

        // The 0x29 column-id high-water never decrements on DROP COLUMN, so once 255 ids have been handed out
        // no further column can be added — even if the *live* count is lower (the guard above) — until the
        // database is compacted (which renumbers and reclaims dropped ids). ACE enforces exactly this: create
        // 255 columns, drop some, ADD COLUMN → "Too many fields defined." Mirror it rather than write a 256th
        // id ACE can't represent. Verified vs ACE.
        if (maxCols >= MaxColumnsPerTable)
            throw new NotSupportedException(
                $"Cannot add column '{spec.Name}' to '{tableName}': too many fields defined — {MaxColumnsPerTable} column ids " +
                "have been used over this table's lifetime, and dropped ids are only reclaimed by compacting the database.");

        // The width limits Create enforces through TdefBuilder apply just as much to a column added later:
        // the new column's own width, and what it does to the widest record the table can now hold.
        RecordLayout.ValidateFieldWidth(spec.Name, spec.Type, spec.Length);
        JetDataTypeVersions.EnsureStorable(spec.Type, _channel.Format.Version, spec.Name);
        RecordLayout.ValidateRecordFits(tableName,
            FixedBytes(table) + (spec.IsFixedLength && spec.Type != JetDataType.Boolean ? spec.Length : 0),
            varCount + (spec.IsFixedLength ? 0 : 1),
            maxCols + 1,
            format);

        // Same single-counter rule the CREATE path and the promote path enforce; ADD COLUMN had neither. The
        // rule is about the header's seed/increment pair, so complex columns — flagged 0x04 but allocated from
        // 0x1C — are neither the existing counter nor a conflicting one.
        if (spec.IsAutoNumber && spec.Type != JetDataType.Complex
            && table.Columns.Any(c => c.IsAutoNumber && c.Type != JetDataType.Complex))
            throw new NotSupportedException(
                $"Cannot add AutoNumber column '{spec.Name}': table '{table.Name}' already has one, and a table "
                + "can have only one.");

        var newColumn = new ColumnDef
        {
            Name = spec.Name,
            Type = spec.Type,
            Index = colCount,
            ColumnId = maxCols, // next id from the high-water (dropped ids are never reused)
            Length = spec.Length,
            // Boolean is fixed but occupies no data — the bit IS the value — so it must not advance the fixed
            // offset. Every other computation of this quantity excludes it; this one did not, putting an added
            // column one byte past where ACE puts it on a table whose only fixed columns are Booleans.
            FixedOffset = spec.IsFixedLength
                ? table.Columns.Where(c => c.IsFixedLength && c.Type != JetDataType.Boolean)
                    .Select(c => c.FixedOffset + c.Length).DefaultIfEmpty(0).Max()
                : 0,
            VariableIndex = spec.IsFixedLength ? -1 : varCount,
            // Descriptor 0x07. A VARIABLE column carries its own variable index, which is the 0x2B high-water
            // (measured vs ACE in VariableColumnHighWaterAccessTests — NOT the count of live variable columns,
            // which is lower once one has been dropped), so leave it unset and let TdefBuilder use VariableIndex.
            // A FIXED column carries the same high-water: every variable column ever given a slot has a smaller
            // id than the one being added, the dropped ones included (verified vs ACE: a LONG added after a
            // TEXT was dropped takes 2 where two TEXT columns had been, one of them gone).
            VariableTableIndex = spec.IsFixedLength ? varCount : -1,
            IsFixedLength = spec.IsFixedLength,
            IsAutoNumber = spec.IsAutoNumber,
            Precision = spec.Precision,
            Scale = spec.Scale,
            IsNullable = spec.IsNullable,
            Collation = spec.Type == JetDataType.FixedPoint ? Collation.GeneralLegacy : _collation,
            // Without this the descriptor gets no 0xC0 and the column reads back as an ordinary one: its
            // Expression and ResultType properties are written, nothing looks at them, and every row stores
            // NULL where the computed value should be.
            IsCalculated = spec.CalculatedExpression is not null,
            CalculatedExpression = spec.CalculatedExpression,
            CalculatedResultType = spec.CalculatedResultType,
        };

        // 0x09 is DAO's Field.OrdinalPosition — reported by DAO, never read by the engine, which presents columns
        // in descriptor order. DAO moves a descriptor when it sets the value, so in any file it wrote the
        // descriptors are in 0x09 order, ties allowed, and gaps open when a column is dropped. ADD COLUMN
        // compacts it, walking the descriptors in that order: each distinct value becomes
        // its rank, so tied columns stay tied, and the new column takes the next rank. DROP COLUMN, ALTER COLUMN
        // and CREATE INDEX leave it alone (all verified vs ACE: ordinals 0, 0, 3, 7 become 0, 0, 1, 2 and the
        // added column 3; with no ties, as after SQL DDL alone, that is simply each column's position).
        int rank = -1, previous = -1;
        for (int i = 0; i < colCount; i++)
        {
            Span<byte> ordinal = parts.Columns.AsSpan(i * format.ColumnDescriptorSize + format.ColumnSecondaryNumberOffset, 2);
            int value = BinaryPrimitives.ReadUInt16LittleEndian(ordinal);
            if (i == 0 || value != previous) rank++;
            previous = value;
            BinaryPrimitives.WriteUInt16LittleEndian(ordinal, (ushort)rank);
        }
        byte[] descriptor = TdefBuilder.BuildColumnDescriptor(newColumn, format);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(format.ColumnSecondaryNumberOffset, 2), (ushort)(rank + 1));
        AppendColumnToParts(parts, colCount, descriptor, spec.Name, format);

        BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefColumnCountOffset, 2), (ushort)(colCount + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefMaxColumnsOffset, 2), (ushort)(maxCols + 1));
        if (!spec.IsFixedLength)
            BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefVariableColumnsOffset, 2), (ushort)(varCount + 1));

        LongValueMapPlacement? lvMaps = isLongValue ? PlaceLongValueMaps(parts, maxCols) : null;

        WriteTdef(table.DefinitionPage, parts);

        if (lvMaps is { } placed) WriteLongValueMaps(placed);

        // NOT NULL / DEFAULT go in the table's LvProp blob (DefaultValue before Required, matching ACE), the
        // same properties CREATE TABLE writes — appended to the existing blob without disturbing other columns'.
        var props = new List<PropertyBlob.Property>();
        if (defaultValue is not null) props.Add(new PropertyBlob.Property(spec.Name, PropertyBlob.DefaultValueProperty, defaultValue));
        if (!spec.IsNullable) props.Add(PropertyBlob.Bool(spec.Name, PropertyBlob.RequiredProperty, true));
        props.AddRange(CalculatedProperties(spec));
        if (props.Count > 0) SetColumnProperties(table.DefinitionPage, spec.Name, props);

        _catalog.Invalidate();
        if (spec.IsAutoNumber) NumberExistingRows(tableName, spec);
        return true;
    }

    /// <summary>Where a column's two long-value usage-map records go: a page and its owned and free rows, on a page
    /// of their own when the table's map page is full.</summary>
    private readonly record struct LongValueMapPlacement(int Page, int UsedRow, int FreeRow, bool Dedicated);

    /// <summary>
    /// Gives a column that has become Memo/OLE its §3.3.2 usage-map entry in <paramref name="parts"/> and says where
    /// its owned and free map records go — for <see cref="WriteLongValueMaps"/> to write once the TDEF is. ACE
    /// appends the two maps to the table's existing usage-map page right after the maps already there (verified,
    /// for ADD COLUMN and for an ALTER COLUMN to Memo alike), and adds the 10-byte entry before the list's 0xFFFF
    /// terminator.
    /// </summary>
    private LongValueMapPlacement PlaceLongValueMaps(TdefParts parts, int columnId)
    {
        JetFormatBase format = _channel.Format;
        int o = format.TdefOwnedPagesOffset + 1;
        int primaryPage = parts.Header[o] | (parts.Header[o + 1] << 8) | (parts.Header[o + 2] << 16);
        byte[] primaryBytes = _channel.ReadPage(primaryPage).Span.ToArray();
        int primaryFree = BinaryPrimitives.ReadUInt16LittleEndian(primaryBytes.AsSpan(format.DataFreeSpaceOffset, 2));

        LongValueMapPlacement placement;
        if (primaryFree >= 2 * (UsageMapRecordLength + 2))
        {
            // Room on the table's usage-map page — append the two maps there (as ACE does).
            int used = BinaryPrimitives.ReadUInt16LittleEndian(primaryBytes.AsSpan(format.DataRowCountOffset, 2));
            placement = new LongValueMapPlacement(primaryPage, used, used + 1, Dedicated: false);
        }
        else
        {
            // Full — give the column its own usage-map page (owned = row 0, free = row 1), the same
            // fallback CREATE TABLE uses once its primary map page fills on a wide table.
            placement = new LongValueMapPlacement(_allocator.Allocate(), 0, 1, Dedicated: true);
        }
        AddLongValueMapEntry(parts, columnId, placement.UsedRow, placement.FreeRow, placement.Page);
        return placement;
    }

    /// <summary>Writes the two empty map records <see cref="PlaceLongValueMaps"/> placed.</summary>
    private void WriteLongValueMaps(LongValueMapPlacement placement)
    {
        JetFormatBase format = _channel.Format;
        if (placement.Dedicated)
            WriteUsageMaps(format, placement.Page, mapCount: 2); // owned = row 0, free = row 1, both empty
        else
        {
            AppendEmptyUsageMapRow(format, placement.Page, placement.UsedRow);
            AppendEmptyUsageMapRow(format, placement.Page, placement.FreeRow);
        }
    }

    /// <summary>
    /// Gives the rows already in a table values in an AutoNumber column just added to it, as ACE does rather than
    /// leaving them NULL, and sets where the counter carries on (all verified).
    /// </summary>
    /// <remarks>
    /// The existing rows are numbered 1, 2, 3 … in table order whatever the column's seed and increment. A counter
    /// with the default seed 1 and increment 1 — however it was spelled — then continues after them: two rows take
    /// 1 and 2, the next insert 3. Any other counter restarts at its own seed, even where that repeats a value the
    /// rows were given: <c>COUNTER(2, 1)</c> over two rows goes on 2, 3, 4, and <c>COUNTER(1, 5)</c> 1, 6, 11. A
    /// primary key over the new column has a value in every row either way.
    /// </remarks>
    private void NumberExistingRows(string tableName, ColumnSpec spec)
    {
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found after adding column '{spec.Name}'.");
        ColumnDef column = table.FindColumn(spec.Name)!;
        if (spec.Increment == 0) throw TdefBuilder.ZeroIncrement(spec.Name);
        int increment = spec.Increment;

        var rows = new Table(_channel, table).Rows().WithIds().ToList();
        if (rows.Count > 0)
        {
            var writer = new RowInserter(_channel, table);
            var changed = new HashSet<int> { column.Index };
            int number = 0;
            foreach ((RowId id, object?[] values) in rows)
            {
                values[column.Index] = ++number;
                writer.Update(id, values, changed);
            }
        }

        ReseedCounter(table, column, spec.Seed == 1 && increment == 1 ? rows.Count + 1 : spec.Seed, increment);
    }

    /// <summary>Inserts a long-value (memo/OLE) column's 10-byte §3.3.2 usage-map entry
    /// (<c>{col_num:2}{used row+page:4}{free row+page:4}</c>) just before the list's <c>0xFFFF</c> terminator.
    /// The new column has the highest id, so appending keeps the list in ascending column order.</summary>
    private static void AddLongValueMapEntry(TdefParts parts, int columnId, int usedRow, int freeRow, int mapPage)
    {
        byte[] lval = parts.Lval;
        int at = lval.Length - 2; // before the terminator

        var entry = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)columnId);
        entry[2] = (byte)usedRow; WriteInt24(entry, 3, mapPage);
        entry[6] = (byte)freeRow; WriteInt24(entry, 7, mapPage);

        var result = new byte[lval.Length + 10];
        Array.Copy(lval, 0, result, 0, at);
        entry.CopyTo(result, at);
        Array.Copy(lval, at, result, at + 10, 2); // the 0xFFFF terminator
        parts.Lval = result;
    }

    /// <summary>Removes a long-value column's 10-byte §3.3.2 usage-map entry from the list, keeping the other
    /// entries and the <c>0xFFFF</c> terminator. A no-op for a column without one.</summary>
    private static void RemoveLongValueMapEntry(TdefParts parts, int columnId)
    {
        byte[] lval = parts.Lval;
        for (int at = 0; at + 2 < lval.Length; at += 10)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(lval.AsSpan(at, 2)) != columnId) continue;
            var result = new byte[lval.Length - 10];
            Array.Copy(lval, 0, result, 0, at);
            Array.Copy(lval, at + 10, result, at, lval.Length - at - 10);
            parts.Lval = result;
            return;
        }
    }

    /// <summary>Sets (replaces) a column's <c>DefaultValue</c> in the table's <c>MSysObjects.LvProp</c> blob —
    /// ALTER TABLE … ALTER COLUMN … DEFAULT. Reads all properties, drops any existing DefaultValue for the
    /// column, adds the new one, and rewrites the blob (preserving every other property).</summary>
    public void SetColumnDefault(string tableName, string columnName, string defaultSql)
        => MutateLvPropForColumn(tableName, columnName, props =>
        {
            props.RemoveAll(p => p.IsOwnedBy(columnName) && p.Name == PropertyBlob.DefaultValueProperty);
            props.Add(new PropertyBlob.Property(columnName, PropertyBlob.DefaultValueProperty, defaultSql));
        });

    /// <summary>Removes a column's <c>DefaultValue</c> from the table's <c>MSysObjects.LvProp</c> blob —
    /// ALTER TABLE … ALTER COLUMN … DROP DEFAULT. Drops only that property, so the column's type and its
    /// <c>Required</c> (NOT NULL) property survive — ACE-verified. A no-op if the column had no default.</summary>
    public void DropColumnDefault(string tableName, string columnName)
        => MutateLvPropForColumn(tableName, columnName, props =>
            props.RemoveAll(p => p.IsOwnedBy(columnName) && p.Name == PropertyBlob.DefaultValueProperty));

    /// <summary>Sets or clears a column's <c>Required</c> (NOT NULL) property in the table's
    /// <c>MSysObjects.LvProp</c> blob — ALTER TABLE … ALTER COLUMN … NOT NULL / NULL. A required column carries
    /// a boolean <c>Required</c> property; a nullable one simply has none, so this drops any existing one and
    /// re-adds it only when <paramref name="required"/> (matching the CREATE-side write, and read back into
    /// <see cref="ColumnDef.IsNullable"/>). ACE-verified: ACE writes the same property for
    /// <c>ALTER COLUMN … NOT NULL</c> and enforces it.</summary>
    public void SetColumnRequired(string tableName, string columnName, bool required)
        => MutateLvPropForColumn(tableName, columnName, props =>
        {
            props.RemoveAll(p => p.IsOwnedBy(columnName) && p.Name == PropertyBlob.RequiredProperty);
            if (required) props.Add(PropertyBlob.Bool(columnName, PropertyBlob.RequiredProperty, true));
        });

    /// <summary>Reads the table's <c>MSysObjects.LvProp</c> property blob, applies <paramref name="mutate"/>,
    /// and rewrites it — the shared read-modify-write behind ALTER COLUMN … SET/DROP DEFAULT.</summary>
    private void MutateLvPropForColumn(string tableName, string columnName, Action<List<PropertyBlob.Property>> mutate)
    {
        TableDef target = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' does not exist.");
        int tdefPage = target.DefinitionPage;

        (TableDef msys, Table table, int idIdx, ColumnDef lvProp) = ObjectProperties();

        foreach ((RowId id, object?[] values) in RowsKeyed(table, idIdx, tdefPage))
        {
            byte[] blob = values[lvProp.Index] as byte[] ?? [];
            var props = PropertyBlob.Read(blob).ToList();
            mutate(props);
            byte[] updated = PropertyBlob.Write(props, blob);
            byte[] descriptor = new RowInserter(_channel, msys).StorePackedLongValue(lvProp.ColumnId, updated);
            values[lvProp.Index] = new LongValueDescriptor(descriptor);
            table.Update(id, values, new HashSet<int> { lvProp.Index });
            return;
        }
        throw new InvalidOperationException($"MSysObjects row for table '{tableName}' (page {tdefPage}) was not found.");
    }

    /// <summary>Appends a column's extended properties (DefaultValue/Required) to its table's
    /// <c>MSysObjects.LvProp</c> blob and re-stores it — the add-side counterpart of
    /// <see cref="RemoveColumnProperties"/>.</summary>
    private void SetColumnProperties(int tdefPage, string columnName, IReadOnlyList<PropertyBlob.Property> props)
    {
        (TableDef msys, Table table, int idIdx, ColumnDef lvProp) = ObjectProperties();

        foreach ((RowId id, object?[] values) in RowsKeyed(table, idIdx, tdefPage))
        {
            byte[] blob = values[lvProp.Index] as byte[] ?? [];
            byte[] updated = PropertyBlob.AddColumnProperties(blob, columnName, props);
            byte[] descriptor = new RowInserter(_channel, msys).StorePackedLongValue(lvProp.ColumnId, updated);
            values[lvProp.Index] = new LongValueDescriptor(descriptor);
            table.Update(id, values, new HashSet<int> { lvProp.Index });
            return;
        }
    }

    /// <summary>Adds a table-level CHECK to the table's <c>MSysObjects.LvProp</c> blob — ALTER TABLE ADD
    /// CONSTRAINT … CHECK. Merges with any existing checks: reads the current <c>CheckConstraints</c> property,
    /// appends the new (name, expression), and rewrites the single empty-owner table block (RemoveOwner + re-add),
    /// keeping the name pool and every column block intact. The check is enforced by the engine from the
    /// re-loaded <c>TableDef.CheckConstraints</c>.</summary>
    public void AddCheckConstraint(string tableName, string checkName, string expression)
    {
        JetName.Validate(checkName, "check constraint name");
        TableDef target = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' does not exist.");
        int tdefPage = target.DefinitionPage;

        (TableDef msys, Table table, int idIdx, ColumnDef lvProp) = ObjectProperties();

        foreach ((RowId id, object?[] values) in RowsKeyed(table, idIdx, tdefPage))
        {
            byte[] blob = values[lvProp.Index] as byte[] ?? [];

            var checks = PropertyBlob.ReadCheckConstraints(PropertyBlob.Read(blob)).ToList();
            checks.Add((checkName, expression));

            // Replace only the CheckConstraints entry. Dropping the whole table-owned block and re-adding one
            // property takes every OTHER table-level property with it — ValidationRule / ValidationText above
            // all, which LibRed reads and reports but does not re-emit, so an Access-authored table validation
            // rule vanished on the first CHECK anyone added. The column-property paths already do it this way.
            byte[] updated = ReplaceTableProperty(blob, PropertyBlob.CheckConstraintsProperty,
                checks.Count > 0 ? PropertyBlob.WriteCheckList(checks) : null);

            byte[] descriptor = new RowInserter(_channel, msys).StorePackedLongValue(lvProp.ColumnId, updated);
            values[lvProp.Index] = new LongValueDescriptor(descriptor);
            table.Update(id, values, new HashSet<int> { lvProp.Index });
            return;
        }
        throw new InvalidOperationException($"MSysObjects row for table '{tableName}' (page {tdefPage}) was not found.");
    }

    /// <summary>Drops a named table-level CHECK — ALTER TABLE … DROP CONSTRAINT. Removes the matching entry from
    /// the <c>CheckConstraints</c> list in the table's <c>MSysObjects.LvProp</c> blob (the inverse of
    /// <see cref="AddCheckConstraint"/>): if any remain, rewrites the list; if it was the last one, drops the
    /// whole table-level property block. ACE-verified: after the drop ACE stops enforcing the check. Returns
    /// false if no CHECK of that name exists (so the caller can try other constraint kinds).</summary>
    public bool DropCheckConstraint(string tableName, string checkName)
    {
        TableDef target = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' does not exist.");
        int tdefPage = target.DefinitionPage;

        (TableDef msys, Table table, int idIdx, ColumnDef lvProp) = ObjectProperties();

        foreach ((RowId id, object?[] values) in RowsKeyed(table, idIdx, tdefPage))
        {
            byte[] blob = values[lvProp.Index] as byte[] ?? [];

            var checks = PropertyBlob.ReadCheckConstraints(PropertyBlob.Read(blob)).ToList();
            if (checks.RemoveAll(c => string.Equals(c.Name, checkName, StringComparison.OrdinalIgnoreCase)) == 0)
                return false; // no CHECK of that name — let the caller try FK/PK/unique

            // Rewrite the list, or remove the entry when that was the last check — leaving every other
            // table-level property (ValidationRule, ValidationText, …) untouched. See AddCheckConstraint.
            byte[] updated = ReplaceTableProperty(blob, PropertyBlob.CheckConstraintsProperty,
                checks.Count > 0 ? PropertyBlob.WriteCheckList(checks) : null);

            byte[] descriptor = new RowInserter(_channel, msys).StorePackedLongValue(lvProp.ColumnId, updated);
            values[lvProp.Index] = new LongValueDescriptor(descriptor);
            table.Update(id, values, new HashSet<int> { lvProp.Index });
            return true;
        }
        throw new InvalidOperationException($"MSysObjects row for table '{tableName}' (page {tdefPage}) was not found.");
    }

    /// <summary>Changes a column's declared type — ALTER TABLE … ALTER COLUMN. A **variable text/binary
    /// column's max length** is a descriptor-length edit at <c>ColumnLengthOffset</c>: variable columns store
    /// each row's actual length, so widening rewrites no rows, and narrowing only scans them to check they
    /// still fit. Every other change — numeric type, a fixed column's size, fixed↔variable — is a full column
    /// rewrite, handled in place by <see cref="AlterColumnTypeInPlace"/> (byte-faithful with ACE), Memo/OLE
    /// included. Nothing here throws NotSupported for a storage-type change.</summary>
    public void AlterColumn(string tableName, string columnName, ColumnSpec newSpec)
    {
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' does not exist.");
        ColumnDef col = table.FindColumn(columnName)
            ?? throw new InvalidOperationException($"Column '{columnName}' does not exist in '{tableName}'.");
        EnsureColumnIsNotInRelationship(table, col);

        // Widening a column reaches the same bytes as declaring it wide in the first place, so the limits
        // Create enforces apply here too. This sits ahead of the identity and counter short-circuits below
        // deliberately: re-declaring an already-oversized column should report the problem, not wave it on.
        RecordLayout.ValidateFieldWidth(newSpec.Name, newSpec.Type, newSpec.Length);
        JetDataTypeVersions.EnsureStorable(newSpec.Type, _channel.Format.Version, newSpec.Name);
        // A pre-check, so an oversized re-declaration reports rather than being waved on by the short-circuits
        // below; AlterColumnTypeInPlace re-runs it against the measured fixed region, which is authoritative.
        // The variable-slot count comes from the stored 0x2B high-water — it never decrements, so deriving it
        // from the live columns under-counts on a table that has dropped or retyped one.
        RecordLayout.ValidateRecordFits(tableName,
            FixedBytes(table)
                - (col.IsFixedLength && col.Type != JetDataType.Boolean ? col.Length : 0)
                + (newSpec.IsFixedLength && newSpec.Type != JetDataType.Boolean ? newSpec.Length : 0),
            table.VariableColumnCount + (col.IsFixedLength && !newSpec.IsFixedLength ? 1 : 0),
            // A type change burns a fresh column id, so the bitmap can widen by one.
            HighWater(table) + (col.Type == newSpec.Type ? 0 : 1),
            _channel.Format);

        // A pure reseed of an existing counter — ALTER COLUMN c COUNTER(seed, increment) where c is already an
        // AutoNumber of the same storage type — changes only the next id, not the data or layout. It's an
        // in-place TDEF header edit (0x14/0x18), exactly what ACE does; a retype would needlessly re-lay every
        // row. (Changing the numeric type is a retype.)
        if (col.IsAutoNumber && newSpec.IsAutoNumber && col.Type == newSpec.Type)
        {
            ReseedCounter(table, col, newSpec.Seed, newSpec.Increment);
            return;
        }

        // Promote a plain Int32 column to an AutoNumber — a counter is stored identically (both a 4-byte Int32);
        // the only differences are the column's 0x04 flag and the header's seed/increment. So it's a metadata
        // edit, not a rebuild. (ACE/SQL Server reject this; PostgreSQL/MySQL and LibRed allow it — see spec.)
        if (!col.IsAutoNumber && newSpec.IsAutoNumber && col.Type == newSpec.Type)
        {
            PromoteColumnToCounter(table, col, newSpec.Seed, newSpec.Increment);
            return;
        }

        // Demote a counter back to a plain Int32 — the reverse, and likewise a metadata edit: clear the 0x04
        // flag and reset the header to a non-AutoNumber table's state (0x14 = 0, 0x18 = 1). ACE *allows* this
        // (unlike promotion), so LibRed matches; existing values are kept and the column stops auto-assigning.
        if (col.IsAutoNumber && !newSpec.IsAutoNumber && col.Type == newSpec.Type)
        {
            DemoteCounterToInt(table, col);
            return;
        }

        // ACE identity ALTER succeeds at exhausted ids for these measured scalar/short-value types, so a
        // re-declaration that changes nothing must not burn one. Memo/OLE still consume an id even for an
        // identical declaration, and so fall through.
        //
        // Nullability is deliberately NOT compared: no ALTER path carries it. The SQL layer always builds
        // the spec with NotNull false and applies Required separately afterwards, and the in-place retype keeps
        // the target's own. Comparing it here would make the check fail
        // for every NOT NULL column, so the same statement would burn an id — and throw at 255 — purely
        // because the column was required.
        if (col.Type is JetDataType.Boolean or JetDataType.Byte or JetDataType.Int16 or JetDataType.Int32
                or JetDataType.Single or JetDataType.Double or JetDataType.Currency or JetDataType.DateTime
                or JetDataType.Guid or JetDataType.Text or JetDataType.Binary or JetDataType.BigBinary
                or JetDataType.FixedPoint
            && col.Type == newSpec.Type
            && col.Length == newSpec.Length && col.IsFixedLength == newSpec.IsFixedLength
            && (col.Type != JetDataType.FixedPoint || (col.Precision == newSpec.Precision && col.Scale == newSpec.Scale)))
            return;

        bool variableLengthChange =
            !col.IsFixedLength && !newSpec.IsFixedLength && col.Type == newSpec.Type &&
            newSpec.Type is JetDataType.Text or JetDataType.Binary or JetDataType.BigBinary;
        // A variable text/binary length change is a cheap in-place descriptor edit (below). A storage-type change
        // (numeric type, fixed size, fixed↔variable) is a full column rewrite: the byte-faithful in-place edit
        // where it applies (all-fixed non-indexed target), else the logical rebuild (AlterColumnTypeInPlace picks).
        if (!variableLengthChange)
        {
            AlterColumnTypeInPlace(tableName, columnName, newSpec);
            return;
        }

        // Widening needs no row work — a variable column stores each row's actual length. NARROWING does: the
        // invariant "no stored value exceeds its column's declared width" is enforced on every insert and
        // update, so the statement that changes the declaration has to hold it too. Without this the ALTER
        // succeeds and leaves behind exactly the rows Access will not read back that the insert-time check
        // exists to prevent. Scanned before the TDEF is touched, so a refusal changes nothing on disk.
        if (newSpec.Length < col.Length)
            EnsureExistingValuesFit(table, col, newSpec.Length);

        JetFormatBase format = _channel.Format;
        TdefParts parts = ParseTdef(table.DefinitionPage);
        byte[] cols = parts.Columns;
        int descSize = format.ColumnDescriptorSize;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            int entry = i * descSize;
            int colId = BinaryPrimitives.ReadUInt16LittleEndian(cols.AsSpan(entry + format.ColumnNumberOffset, 2));
            if (colId != col.ColumnId) continue;
            BinaryPrimitives.WriteUInt16LittleEndian(cols.AsSpan(entry + format.ColumnLengthOffset, 2), (ushort)newSpec.Length);
            WriteTdef(table.DefinitionPage, parts);
            return;
        }
        throw new InvalidOperationException($"Descriptor for column '{columnName}' (id {col.ColumnId}) was not found.");
    }

    /// <summary>Sets or removes one property in the blob's table-owned (empty-owner) block, leaving every
    /// other property — table-level and column-level — exactly as it was. <paramref name="value"/> null
    /// removes the entry. Read-modify-write over the parsed property list, so unmodelled properties survive
    /// on their <c>RawValue</c> passthrough.</summary>
    private static byte[] ReplaceTableProperty(byte[] blob, string name, string? value)
    {
        var props = PropertyBlob.Read(blob).ToList();
        props.RemoveAll(p => p.IsOwnedBy("") && p.Name == name);
        if (value is not null)
            props.Add(new PropertyBlob.Property("", name, value));
        return PropertyBlob.Write(props, blob);
    }

    /// <summary>Refuses a narrowing ALTER when a stored value would no longer fit, reporting the same way the
    /// insert-time width check does. Text declares characters and stores UTF-16, hence the halving.</summary>
    private void EnsureExistingValuesFit(TableDef table, ColumnDef column, int newLength)
    {
        bool text = column.Type == JetDataType.Text;
        var rows = new Table(_channel, table);
        foreach (object?[] values in rows.Rows(rows.DecodeOnly([column.Index])))
        {
            int stored = values[column.Index] switch
            {
                string s => Encoding.Unicode.GetByteCount(s),
                byte[] b => b.Length,
                _ => 0,
            };
            if (stored <= newLength) continue;
            throw new InvalidOperationException(
                $"The field '{column.Name}' cannot be narrowed to {(text ? newLength / 2 : newLength)} "
                + $"{(text ? "characters" : "bytes")}: the table holds a value of "
                + $"{(text ? stored / 2 : stored)}.");
        }
    }

    /// <summary>The table's fixed-data region: the high-water of where its live columns END, not the sum of
    /// their lengths. DROP COLUMN is a metadata-only edit that leaves every survivor's fixed offset exactly
    /// where it was, so a dropped column's bytes stay in the region as dead space — a sum under-counts by
    /// precisely that hole, and would pass a declaration whose rows then overrun 4060, which is a table Access
    /// refuses to open the database for. On a table that has dropped nothing the two agree, because the
    /// offsets pack from zero. Boolean is fixed but occupies no data, so it contributes nothing, exactly as
    /// <see cref="TdefBuilder"/> counts it on create.</summary>
    private static int FixedBytes(TableDef table) =>
        table.Columns.Where(c => c.IsFixedLength && c.Type != JetDataType.Boolean)
            .Select(c => c.FixedOffset + c.Length).DefaultIfEmpty(0).Max();

    /// <summary>The TDEF's `0x29` column-id high-water — the number of ids handed out over the table's
    /// lifetime, which is what sizes a record's null bitmap (dropped ids keep their bit).</summary>
    private int HighWater(TableDef table) =>
        ReadDefinition(table.DefinitionPage).Buffer.ReadUInt16(_channel.Format.TdefMaxColumnsOffset);

    /// <summary>Whether the column is either end of a relationship — the child's FK column or the parent's
    /// referenced key. ACE refuses to alter or drop such a column; the two callers differ only in the message
    /// they raise, so the rule itself lives here.</summary>
    private bool ColumnIsInRelationship(TableDef table, ColumnDef column)
    {
        const StringComparison oic = StringComparison.OrdinalIgnoreCase;
        return _catalog.Relationships.Any(r =>
            (string.Equals(r.Table, table.Name, oic) &&
             r.Columns.Any(c => string.Equals(c.Column, column.Name, oic))) ||
            (string.Equals(r.ReferencedTable, table.Name, oic) &&
             r.Columns.Any(c => string.Equals(c.ReferencedColumn, column.Name, oic))));
    }

    /// <summary>ACE rejects every type/length alteration of a relationship column, on either the
    /// referencing or referenced side. Keep this check ahead of all specialized ALTER paths so an
    /// in-place descriptor edit cannot bypass the same rule enforced by a logical table rebuild.</summary>
    private void EnsureColumnIsNotInRelationship(TableDef table, ColumnDef column)
    {
        if (ColumnIsInRelationship(table, column))
            throw new InvalidOperationException(
                $"Cannot change field '{column.Name}'. It is part of one or more relationships.");
    }

    /// <summary>Reseeds an existing AutoNumber column in place — ALTER COLUMN c COUNTER(seed, increment). Writes
    /// the TDEF header's last-value (<c>0x14</c> = seed − increment, so the next assigned id is <c>seed</c>) and
    /// increment (<c>0x18</c>); no data or descriptor changes. ACE rejects reseeding a counter that participates
    /// in a relationship ("Cannot change field 'X'. It is part of one or more relationships." — verified); match
    /// that.</summary>
    private void ReseedCounter(TableDef table, ColumnDef col, int seed, int increment)
    {
        EnsureColumnIsNotInRelationship(table, col);

        if (increment == 0) throw TdefBuilder.ZeroIncrement(col.Name);
        JetFormatBase format = _channel.Format;
        byte[] tdef = _channel.ReadPage(table.DefinitionPage).Span.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(format.TdefLastAutoNumberOffset, 4), seed - increment);
        BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(format.TdefAutoNumberIncrementOffset, 4), increment);
        _channel.WritePage(table.DefinitionPage, tdef);
        _catalog.Invalidate();
    }

    /// <summary>Promotes a plain Int32 column to an AutoNumber in place — ALTER COLUMN c COUNTER(seed, increment)
    /// where c is a plain integer. A counter is stored identically to a Long Integer, so this only sets the
    /// column descriptor's <c>0x04</c> AutoNumber flag and the header's seed/increment (<c>0x14</c>/<c>0x18</c>);
    /// existing values are untouched. Only one column may draw on that pair, so a second is rejected — complex
    /// columns are flagged <c>0x04</c> too but allocate from <c>0x1C</c>, so they do not count as the existing
    /// one; and (like the reseed path) a column in a relationship is rejected, matching ACE.</summary>
    private void PromoteColumnToCounter(TableDef table, ColumnDef col, int seed, int increment)
    {
        if (table.Columns.Any(c => c.IsAutoNumber && c.Type != JetDataType.Complex && c.ColumnId != col.ColumnId))
            throw new InvalidOperationException(
                $"Cannot make '{col.Name}' an AutoNumber: table '{table.Name}' already has one, and a table "
                + "can have only one.");
        EnsureColumnIsNotInRelationship(table, col);

        if (increment == 0) throw TdefBuilder.ZeroIncrement(col.Name);
        JetFormatBase format = _channel.Format;
        TdefParts parts = ParseTdef(table.DefinitionPage);
        int descSize = format.ColumnDescriptorSize;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            int entry = i * descSize;
            if (BinaryPrimitives.ReadUInt16LittleEndian(parts.Columns.AsSpan(entry + format.ColumnNumberOffset, 2)) != col.ColumnId) continue;
            parts.Columns[entry + format.ColumnFlagsOffset] |= JetFormatBase.ColumnFlagAutoNumber;
            break;
        }
        BinaryPrimitives.WriteInt32LittleEndian(parts.Header.AsSpan(format.TdefLastAutoNumberOffset, 4), seed - increment);
        BinaryPrimitives.WriteInt32LittleEndian(parts.Header.AsSpan(format.TdefAutoNumberIncrementOffset, 4), increment);
        WriteTdef(table.DefinitionPage, parts);
        _catalog.Invalidate();

        // COUNTER(seed, increment) is a *sequential* counter. A surviving GenUniqueID() default would instead
        // make it a "Random" AutoNumber (IsRandomAutoNumber) — assigning random ids and ignoring the seed — so
        // clear it to honour the requested sequence. Other (literal) defaults are inert on a counter (the insert
        // path skips defaults for AutoNumber columns) and are left as-is.
        if (col.DefaultValue?.Trim().Equals("GenUniqueID()", StringComparison.OrdinalIgnoreCase) == true)
            DropColumnDefault(table.Name, col.Name);
    }

    /// <summary>Demotes an AutoNumber column back to a plain Int32 in place — ALTER COLUMN c LONG where c is a
    /// counter. Clears the descriptor's <c>0x04</c> flag and resets the header to a non-AutoNumber table's state
    /// (<c>0x14</c> = 0, <c>0x18</c> = 1); existing values are kept, the column just stops auto-assigning. ACE
    /// permits this (unlike int→counter promotion), so no divergence.</summary>
    private void DemoteCounterToInt(TableDef table, ColumnDef col)
    {
        JetFormatBase format = _channel.Format;
        TdefParts parts = ParseTdef(table.DefinitionPage);
        int descSize = format.ColumnDescriptorSize;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            int entry = i * descSize;
            if (BinaryPrimitives.ReadUInt16LittleEndian(parts.Columns.AsSpan(entry + format.ColumnNumberOffset, 2)) != col.ColumnId) continue;
            parts.Columns[entry + format.ColumnFlagsOffset] &= unchecked((byte)~JetFormatBase.ColumnFlagAutoNumber);
            break;
        }
        BinaryPrimitives.WriteInt32LittleEndian(parts.Header.AsSpan(format.TdefLastAutoNumberOffset, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(parts.Header.AsSpan(format.TdefAutoNumberIncrementOffset, 4), 1);
        WriteTdef(table.DefinitionPage, parts);
        _catalog.Invalidate();
    }

    /// <summary>Applies ACE's in-place column retype to the target descriptor within <paramref name="parts"/>
    /// (no page write — the caller writes the TDEF once): the target becomes a NEW column with a fresh id from the
    /// <c>0x29</c> high-water and its fixed data appended to the END of the current fixed region (its old slot left
    /// as dead space — ACE does not compact); <c>0x29</c> bumps, and <c>0x2B</c> too for a variable retype. Only
    /// the target descriptor changes; every other descriptor stays byte-identical. Returns the burned new id.</summary>
    private static int EditTargetDescriptor(TdefParts parts, ColumnDef target, ColumnSpec newSpec, int fixedEnd,
        Collation collation, JetFormatBase format)
    {
        int maxCols = BinaryPrimitives.ReadUInt16LittleEndian(parts.Header.AsSpan(format.TdefMaxColumnsOffset, 2));
        // ACE-only probe: with 254 columns one retype succeeds, the next fails; with 255
        // columns the first retype fails. A same-type ALTER does not reach this id-burning path.
        if (maxCols >= MaxColumnsPerTable)
            throw new NotSupportedException(
                $"Cannot change the type of '{target.Name}': too many fields defined — {MaxColumnsPerTable} column ids have been used.");
        int varCount = BinaryPrimitives.ReadUInt16LittleEndian(parts.Header.AsSpan(format.TdefVariableColumnsOffset, 2));

        Span<byte> d = parts.Columns.AsSpan(target.Index * format.ColumnDescriptorSize, format.ColumnDescriptorSize);
        d[format.ColumnTypeOffset] = (byte)newSpec.Type;
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnNumberOffset..], (ushort)maxCols); // +0x05 id burned
        // The target's var-index (+0x07) becomes the old variable-column count — the next var slot — for BOTH a
        // fixed and a variable retype (verified vs ACE); a variable retype also bumps the 0x2B var-column count.
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnVariableIndexOffset..], (ushort)varCount);
        // +0x09 is deliberately left unchanged: it is the column's ordinal position, which a retype does not
        // move (verified: ACE does not update it).
        byte flags = d[format.ColumnFlagsOffset];
        flags = newSpec.IsFixedLength ? (byte)(flags | JetFormatBase.ColumnFlagFixedLength)
                                      : (byte)(flags & ~JetFormatBase.ColumnFlagFixedLength);
        flags = newSpec.IsAutoNumber ? (byte)(flags | JetFormatBase.ColumnFlagAutoNumber)
                                     : (byte)(flags & ~JetFormatBase.ColumnFlagAutoNumber);
        d[format.ColumnFlagsOffset] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnFixedOffsetOffset..], (ushort)(newSpec.IsFixedLength ? fixedEnd : 0)); // +0x15
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnLengthOffset..], (ushort)newSpec.Length); // +0x17
        // 0x0B–0x0E is a union keyed by type, so the WHOLE union is rewritten, not just the decimal arm.
        // Writing precision/scale on the way in but nothing on the way out left a former DECIMAL(12,3) with
        // 0x0C 0x03 in its LANGID bytes, which reads back as collating order 0x030C on a text column.
        TdefBuilder.WriteLocaleUnion(d, newSpec.Type, newSpec.Precision, newSpec.Scale, collation, format);

        BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefMaxColumnsOffset, 2), (ushort)(maxCols + 1)); // 0x29++
        if (!newSpec.IsFixedLength)
            BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefVariableColumnsOffset, 2), (ushort)(varCount + 1)); // 0x2B++
        return maxCols;
    }

    /// <summary>Full in-place column type change, byte-for-byte like ACE for fixed and variable columns and
    /// targets, fixed↔variable, indexed targets, and a Memo/OLE source or target (whose long-value maps are placed
    /// and retired as ADD and DROP COLUMN do). Edits the TDEF in place (<see cref="EditTargetDescriptor"/>) and re-lays every row — the target's
    /// OLD fixed slot is kept as dead space, its converted value appended at the new offset, count + null bitmap
    /// updated. Converts values in memory first (throws on bad data before any write); runs in a transaction.</summary>
    public void AlterColumnTypeInPlace(string tableName, string columnName, ColumnSpec newSpec)
    {
        TableDef oldDef = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' does not exist.");
        ColumnDef oldTarget = oldDef.FindColumn(columnName)
            ?? throw new InvalidOperationException($"Column '{columnName}' does not exist in '{tableName}'.");
        EnsureColumnIsNotInRelationship(oldDef, oldTarget);
        if (newSpec.Type is JetDataType.Ole or JetDataType.BigBinary
            && oldDef.Indexes.Any(i => i.Columns.Any(c => c.Column.ColumnId == oldTarget.ColumnId)))
            RejectOleIndexColumns([oldTarget.Name], _ => newSpec.Type);

        // Also reached directly, not only through AlterColumn, so it carries the width limits itself.
        // The record-fits check needs the true fixed-region end, so it runs once that is measured, below.
        RecordLayout.ValidateFieldWidth(newSpec.Name, newSpec.Type, newSpec.Length);
        JetDataTypeVersions.EnsureStorable(newSpec.Type, _channel.Format.Version, newSpec.Name);

        // A column becoming Memo/OLE, or ceasing to be one, takes the same in-place edit as any other retype plus the
        // long-value side of ADD and DROP COLUMN (verified vs ACE, both directions): the new column gets its §3.3.2
        // entry and two empty map records appended to the table's map page, and each converted value is stored as an
        // insert stores it — inline up to 64 bytes, else on an LVAL page; the old column's entry leaves the TDEF, its
        // map records are retired and its pages go back at close, their bytes untouched.
        bool fromLongValue = oldTarget.Type is JetDataType.Memo or JetDataType.Ole;
        bool toLongValue = newSpec.Type is JetDataType.Memo or JetDataType.Ole;

        // Indexes that include the target column must be rebuilt (their keys change type) — captured now.
        var affectedIndexes = oldDef.Indexes
            .Where(i => i.Columns.Any(col => col.Column.Index == oldTarget.Index))
            .Select(i => i.Name).ToList();
        int oldTargetId = oldTarget.ColumnId;

        // 1. Materialize (id + raw bytes + values) before touching disk; conversion throws here on bad data.
        var reader = new RowInserter(_channel, oldDef);
        var rows = new Table(_channel, oldDef).Rows().WithIds()
            .Select(r => (r.Id, Raw: reader.ReadRow(r.Id), Values: (object?[])r.Values.Clone()))
            .ToList();
        foreach (var r in rows)
            r.Values[oldTarget.Index] = ConvertValue(r.Values[oldTarget.Index], newSpec.Type, newSpec.Name);

        // The fixed-region length is authoritative from the existing rows (their var-data-start), NOT the live
        // column descriptors — those diverge once a high-offset column has been retyped to variable and left a
        // dead fixed slot at the end. Take the MAX over every row, not row 0: ADD COLUMN of a fixed column is
        // metadata-only, so a table legitimately holds short rows written before it alongside full-width ones.
        // Sizing the whole re-lay from whichever row happened to be first either truncates the long rows' fixed
        // tails or drags the short rows' variable data up into their fixed region. The schema floor covers an
        // empty table, and rows shorter than the result are zero-filled by BuildRelaidRecord.
        int oldFixedLen = FixedBytes(oldDef);
        foreach (var r in rows)
            oldFixedLen = Math.Max(oldFixedLen, FixedRegionLength(r.Raw, RowLayout.HasVariableSection(r.Raw, oldDef.Columns)));

        // Now the widest-record check, against what this path actually produces. Both counts come from stored
        // state, not the live column list: the re-lay KEEPS the old target's fixed slot as dead space rather
        // than reclaiming it (so nothing is subtracted), and 0x2B is a high-water that never decrements (so a
        // variable→fixed retype leaves it where it is). Deriving either from the live columns under-counts on
        // any table that has dropped or retyped a column, passing a declaration that then overflows 4060 —
        // and per RecordLayout's own remarks, Access cannot open a database containing such a table at all.
        RecordLayout.ValidateRecordFits(tableName,
            oldFixedLen + (newSpec.IsFixedLength && newSpec.Type != JetDataType.Boolean ? newSpec.Length : 0),
            oldDef.VariableColumnCount + (oldTarget.IsFixedLength && !newSpec.IsFixedLength ? 1 : 0),
            HighWater(oldDef) + 1,   // the type change burns a fresh id
            _channel.Format);

        bool ownTx = !_channel.InTransaction;
        if (ownTx) _channel.BeginTransaction();
        try
        {
            JetFormatBase format = _channel.Format;

            // 2. One TDEF edit for the whole modify: patch only the target descriptor (bump 0x29 / 0x2B — its
            //    appended fixed offset is the row's true fixed-region end incl. dead slots) AND re-point every
            //    index over the target, all into the SAME parts, then write the TDEF a single time. Each index
            //    re-point needs the fresh root allocated + owned-map recycled first (page work off the TDEF).
            TdefParts parts = ParseTdef(oldDef.DefinitionPage);
            int newTargetId = EditTargetDescriptor(parts, oldTarget, newSpec, oldFixedLen, _collation, format);

            var pending = new List<(string Name, int OldRoot, int NewRoot, bool IgnoreNulls)>();
            foreach (string ixName in affectedIndexes)
            {
                IndexDef index = oldDef.Indexes.First(i => string.Equals(i.Name, ixName, StringComparison.OrdinalIgnoreCase));
                int newRoot = PrepareIndexRebuild(parts, oldDef, index, oldTargetId, newTargetId);
                pending.Add((ixName, index.RootPage, newRoot, index.IgnoreNulls));
            }

            // The long-value side: the old column's maps, read before the TDEF loses them, as DROP COLUMN reads them;
            // the new column's entry, placed as ADD COLUMN places it.
            UsageMap? oldMaps = null;
            var released = new HashSet<int>();
            var retire = new List<MapRetirement>();
            if (fromLongValue)
            {
                var oldDefinition = new TableDefinitionPage();
                oldDefinition.Read(_channel, oldDef.DefinitionPage);
                oldMaps = new UsageMap(_channel, oldDef);
                QueueLongValueMaps(oldDefinition, oldTarget, oldMaps, released, retire);
                RemoveLongValueMapEntry(parts, oldTargetId);
            }
            LongValueMapPlacement? newMaps = toLongValue ? PlaceLongValueMaps(parts, newTargetId) : null;

            WriteTdef(oldDef.DefinitionPage, parts);
            _catalog.Invalidate();

            if (newMaps is { } placed) WriteLongValueMaps(placed);
            if (oldMaps is not null)
            {
                RetireMapRecords(retire, oldMaps, released);
                foreach (int page in released) _allocator.Release(page);   // reusable only after this handle closes
            }

            TableDef newDef = _catalog.FindTable(tableName)!;
            ColumnDef newTarget = newDef.FindColumn(columnName)!;
            int newMaxId = newDef.Columns.Max(c => c.ColumnId);
            int newFixedLen = newTarget.IsFixedLength ? oldFixedLen + newTarget.Length : oldFixedLen;

            // Slots per row comes from the TDEF high-water, exactly as RowEncoder derives it — never from a
            // row's own stored numVar. A row written before a variable ADD COLUMN carries fewer slots than the
            // table has, and appending the retyped column onto such a row lands it at the wrong index while its
            // descriptor names the high-water one, which is the "A column Id is incorrect" file ACE rejects.
            var newVarCols = newDef.Columns.Where(c => !c.IsFixedLength).ToList();
            int newVarCount = newVarCols.Count == 0 ? 0
                : Math.Max(newDef.VariableColumnCount, newVarCols.Max(c => c.VariableIndex) + 1);
            var writer = new RowInserter(_channel, newDef);

            // 3. Re-lay each row: old fixed region + old var chunks verbatim (incl. the dead old slot), target
            //    appended (a new fixed slot, or a new variable chunk); count/var-table/null-bitmap rebuilt.
            //    A long value is stored first, as an insert would store it, so the chunk appended is its descriptor.
            foreach (var r in rows)
            {
                if (toLongValue)
                    r.Values[newTarget.Index] = writer.MaterializeLongValue(newTarget, r.Values[newTarget.Index]);
                writer.RewriteRowRaw(r.Id, BuildRelaidRecord(
                    r.Raw, oldFixedLen, oldDef.Columns, newTarget, r.Values, newDef.Columns, newMaxId, newFixedLen, newVarCount));
            }

            // 4. Finish each index rebuild: backfill the fresh B-tree with new-type keys, then free the old root
            //    (last, so the new root got the appended page rather than reusing this one) — as ACE does.
            foreach (var p in pending)
            {
                BackfillIndex(tableName, p.Name, p.IgnoreNulls, validateUnique: true);
                _allocator.Release(p.OldRoot);
            }

            if (ownTx) _channel.CommitTransaction();
        }
        catch when (ownTx) { _channel.RollbackTransaction(); _catalog.Invalidate(); throw; }
        _catalog.Invalidate();
    }

    /// <summary>Builds the re-laid row record, matching ACE's in-place modify byte-for-byte: the OLD fixed
    /// region and OLD variable chunks are kept verbatim (the dead old-target slot/chunk keeps its stale bytes),
    /// the converted target is appended (a new fixed slot if it is fixed, else a new variable chunk), and the
    /// leading count (= max id + 1), variable-offset table + numVar (omitted if none), and null bitmap
    /// (each dead id's bit carried over from the old row) are rebuilt.</summary>
    private static byte[] BuildRelaidRecord(byte[] oldRow, int oldFixedLen, IReadOnlyList<ColumnDef> oldCols,
        ColumnDef newTarget, object?[] values, IReadOnlyList<ColumnDef> newCols, int newMaxId, int newFixedLen,
        int newVarCount)
    {
        object? tv = values[newTarget.Index];
        byte[] targetBytes = tv is null
            ? (newTarget.IsFixedLength ? new byte[newTarget.Length] : [])
            : Types.JetTypeCodec.Encode(newTarget, tv);

        // Whether THIS row has a variable trailer, not whether the schema does — see RowLayout.HasVariableSection.
        bool hasVar = RowLayout.HasVariableSection(oldRow, oldCols);

        // Fixed region: old fixed bytes verbatim (incl. a dead fixed slot); append the target if it is fixed.
        // A row predating a fixed ADD COLUMN is shorter than the region; copy what it has and leave the rest
        // zeroed, which is what its null bitmap already says those columns are.
        var newFixed = new byte[newFixedLen];
        int rowFixedLen = Math.Min(oldFixedLen, RowLayout.Parse(oldRow, 2, hasVar).FixedRegionLength);
        Array.Copy(oldRow, 2, newFixed, 0, rowFixedLen);
        if (newTarget.IsFixedLength && tv is not null)
            Array.Copy(targetBytes, 0, newFixed, newTarget.FixedOffset, newTarget.Length);
        else if (newTarget.IsFixedLength && rowFixedLen == oldFixedLen)
        {
            // ACE writes nothing into the new slot of a NULL: it holds whatever the old record had at those
            // offsets — the start of its variable data, say — rather than zeros (verified on full-width rows).
            int from = 2 + newTarget.FixedOffset;
            int length = Math.Min(newTarget.Length, oldRow.Length - from);
            if (length > 0) Array.Copy(oldRow, from, newFixed, newTarget.FixedOffset, length);
        }

        // Variable chunks: old chunks verbatim (incl. a dead variable chunk), padded out to the table's slot
        // count so the target lands on the index its descriptor names, then the target placed at that index.
        List<byte[]> chunks = ExtractVarChunks(oldRow, hasVar);
        while (chunks.Count < newVarCount) chunks.Add([]);
        if (!newTarget.IsFixedLength) chunks[newTarget.VariableIndex] = targetBytes;

        // Assemble via the shared row layout (count + var table + null bitmap identical to a fresh encode), each
        // dead id's bit carried over from the old row's bitmap.
        int oldCount = BinaryPrimitives.ReadUInt16LittleEndian(oldRow);
        return RowEncoder.AssembleRow(newMaxId, newFixed, chunks, newCols, values,
            priorBitmap: oldRow.AsSpan(oldRow.Length - (oldCount + 7) / 8));
    }

    /// <summary>The length of a row's fixed-data region (bytes between the leading count and the variable data),
    /// read from the row itself — its variable-offset table's last entry is the variable-data start (= 2 + fixed
    /// length), or for an all-fixed row it's the whole row minus the count field and null bitmap. This is
    /// authoritative over the live column descriptors, which omit dead fixed slots left by prior retypes.</summary>
    private static int FixedRegionLength(byte[] row, bool hasVar) =>
        RowLayout.Parse(row, 2, hasVar).FixedRegionLength;

    /// <summary>Extracts a row's variable-column chunks (in variable-index order) verbatim, using the row's own
    /// stored numVar. <paramref name="hasVar"/> (from the schema) says whether a variable section exists at all —
    /// an all-fixed table omits it entirely, so its "numVar" bytes would otherwise be misread from fixed data.</summary>
    private static List<byte[]> ExtractVarChunks(byte[] row, bool hasVar)
    {
        RowLayout layout = RowLayout.Parse(row, 2, hasVar);
        var chunks = new List<byte[]>(layout.NumVar);
        for (int j = 0; j < layout.NumVar; j++)
            chunks.Add(layout.VarChunk(j).ToArray());
        return chunks;
    }

    /// <summary>Prepares one index rebuild over a just-modified column, matching ACE's reconstruction: allocate a
    /// fresh empty root leaf (appended — the old root is left orphaned) and extend/recycle the owned usage map to
    /// track it, then re-point the index-data block within <paramref name="parts"/> to the new root with the
    /// target's burned column id and the new usage-map row (bumping the stats block). The caller writes the TDEF
    /// once, then backfills the fresh B-tree and frees the old root. Returns the new root page.</summary>
    private int PrepareIndexRebuild(TdefParts parts, TableDef table, IndexDef index, int oldTargetId, int newTargetId)
    {
        JetFormatBase format = _channel.Format;

        // A fresh empty root leaf, appended; the old root is freed by the caller afterwards (ACE reuses it on the
        // next alloc). This and the owned-map recycle touch pages OFF the TDEF, so they happen before the single
        // TDEF write; only the index-data block + stats mutations below go into the shared parts.
        int newRoot = _allocator.Allocate();
        WriteEmptyLeafIndexPage(format, newRoot, owner: table.DefinitionPage);

        int usageMapPage = parts.Header[format.TdefOwnedPagesOffset + 1]
            | (parts.Header[format.TdefOwnedPagesOffset + 2] << 8) | (parts.Header[format.TdefOwnedPagesOffset + 3] << 16);

        // Recycle the index's owned-map row (ACE soft-deletes the old row and reuses its space for a new row
        // tracking the new root), reading the current row number from the (as-yet-unwritten) data block.
        Span<byte> block = parts.DataBlocks[index.RealIndexOrdinal];
        int oldUsageRow = block[IndexBlockFormat.UsageMapRowOffset];
        int newRow = RecycleOwnedMapRow(format, usageMapPage, oldUsageRow, newRoot);

        // Re-point the index-data block: the target's burned id in its column slot, the new root, the new
        // usage-map row. Its statistics are set by the backfill that follows, from the rows it then holds.
        for (int slot = 0; slot < IndexBlockFormat.MaxColumns; slot++)
        {
            int at = IndexBlockFormat.ColumnsOffset + slot * IndexBlockFormat.ColumnSlotSize;
            if (BinaryPrimitives.ReadInt16LittleEndian(block.Slice(at, 2)) == oldTargetId)
                BinaryPrimitives.WriteInt16LittleEndian(block.Slice(at, 2), (short)newTargetId);
        }
        block[IndexBlockFormat.UsageMapRowOffset] = (byte)newRow;
        BinaryPrimitives.WriteInt32LittleEndian(block.Slice(IndexBlockFormat.RootPageOffset, 4), newRoot);
        return newRoot;
    }

    /// <summary>
    /// Recycles an index's owned-pages usage-map row the way ACE does on a rebuild, in the two writes whose
    /// combined result is observable on disk: <b>(1)</b> append a fresh row at the bottom of the holder page
    /// and set the new root's bit — those bytes are then abandoned and stay as a stale copy; <b>(2)</b> lay
    /// the page out again with the old row's record <b>reclaimed</b>: its slot becomes a 0-length
    /// deleted+overflow tombstone at the preceding record's offset, every later row keeps its number while its
    /// record slides up, and the fresh map takes the position freed at the end of the live region under the
    /// appended row number. Returns that number — the only pointer the caller re-points, because no other
    /// row's number changes and each one's data travels with it.
    /// </summary>
    /// <remarks>
    /// Both halves are load-bearing and each was missed once. The stale copy decides a whole-file byte diff
    /// against ACE on a single byte (the new root's bit, at offset 49 of the abandoned record) and is
    /// invisible to free-space accounting, since it lies below the lowest live record inside the region free
    /// space already covers. The compaction is invisible to slot offsets alone and shows up only when records
    /// are identified by content — ACE's own page, before → after, with a long-value column's maps below the
    /// index's:
    /// <code>
    /// before  row2 @3889 pages=[353]   row3 @3820 pages=[]   row4 @3751 pages=[]
    /// after   row2 @3958 TOMBSTONE     row3 @3889 pages=[]   row4 @3820 pages=[]   row5 @3751 pages=[355]
    ///         stale: @3731 = 0x08      (the abandoned append, at 3682)
    /// </code>
    /// The long-value maps slid up a record width and kept rows 3 and 4; only the index's pointer moved, to
    /// the appended row 5. Writing step (1)'s record into the old row's slot instead — which produces the same
    /// bytes whenever the recycled row happens to be the LAST one, the only case an ACE-built schema gives —
    /// points a slot back up the page as soon as it is not, and no reader can walk that: a row's extent runs
    /// to where the previous slot begins. <c>AlterColumnTypeInPlace</c> scans the table through this map
    /// immediately afterwards, so the <c>ALTER</c> failed outright on any table that had gained a Memo or OLE
    /// column after its index.
    /// </remarks>
    private int RecycleOwnedMapRow(JetFormatBase format, int usageMapPage, int oldRow, int newRoot)
    {
        int dir = format.DataRowDirectoryOffset;
        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(
            _channel.ReadPage(usageMapPage).Span.Slice(format.DataRowCountOffset, 2));
        int newRow = rowCount;
        if (oldRow < 0 || oldRow >= rowCount)
            throw new InvalidDataException(
                $"Usage-map row {usageMapPage}:{oldRow} does not exist; the page has {rowCount} rows.");

        // (1) ACE's first write, kept verbatim: the appended row is where the new root's bit is set, and the
        // bytes it leaves behind are part of the file ACE produces.
        AppendEmptyUsageMapRow(format, usageMapPage, newRow);
        new UsageMapWriter(_channel).SetBit(newRow, usageMapPage, newRoot, set: true);

        // (2) Re-lay the live records. Starting from the page as it stands keeps everything this does not
        // write — the abandoned append included — exactly where ACE leaves it.
        byte[] page = _channel.ReadPage(usageMapPage).Span.ToArray();
        var holder = new DataPage();
        holder.Read(_channel.ReadPage(usageMapPage), format);

        var records = new byte[rowCount + 1][];
        var flags = new ushort[rowCount + 1];
        for (int i = 0; i <= rowCount; i++)
        {
            records[i] = i == oldRow ? [] : page.AsSpan(holder.Rows[i].Offset, holder.Rows[i].Length).ToArray();
            flags[i] = (ushort)((i == oldRow || holder.Rows[i].IsDeleted ? RowPointer.DeletedFlag : 0)
                                | (i == oldRow || holder.Rows[i].HasOverflow ? RowPointer.OverflowFlag : 0));
        }

        int directoryEnd = dir + (rowCount + 1) * 2;
        int offset = format.PageSize;
        for (int i = 0; i <= rowCount; i++)
        {
            offset -= records[i].Length;                  // a 0-length tombstone lands on the previous start
            if (offset < directoryEnd)
                throw new InvalidOperationException(
                    $"Usage-map page {usageMapPage} has no room to recycle row {oldRow}: {rowCount} rows already. "
                    + "The new map belongs on a page of its own.");
            records[i].CopyTo(page.AsSpan(offset));
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(dir + i * 2, 2),
                (ushort)(flags[i] | (offset & RowPointer.OffsetMask)));
        }
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(offset - directoryEnd));
        _channel.WritePage(usageMapPage, page);
        return newRow;
    }

    /// <summary>Converts a stored value to the CLR type for a new column type (ALTER COLUMN). NULL stays NULL;
    /// an unconvertible value throws (as ACE's rewrite would).</summary>
    /// <remarks>Throwing is the intent; the type has to be actionable. The bare <c>Convert.To*</c> calls leaked
    /// <see cref="InvalidCastException"/>/<see cref="FormatException"/>/<see cref="OverflowException"/>, none
    /// naming the column and none distinguishable from a bug in the rewrite.</remarks>
    private static object? ConvertValue(object? value, JetDataType type, string columnName)
    {
        if (value is null) return null;
        try
        {
            return ConvertCore(value, type);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException
                                      or ArgumentException)
        {
            throw new InvalidOperationException(
                $"Column '{columnName}' cannot be changed to {type}: the existing value "
                + $"'{Describe(value)}' ({value.GetType().Name}) cannot be converted to it.", ex);
        }
    }

    /// <summary>Bounded rendering for an error message, so a memo does not paste thousands of characters into
    /// one.</summary>
    private static string Describe(object value) => value switch
    {
        byte[] bytes => $"{bytes.Length} bytes",
        string { Length: > 40 } text => $"{text[..40]}…",
        _ => value.ToString() ?? "",
    };

    private static object? ConvertCore(object value, JetDataType type)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return type switch
        {
            JetDataType.Boolean => value is bool b ? b : Convert.ToBoolean(value, inv),
            JetDataType.Byte => Convert.ToByte(value, inv),
            JetDataType.Int16 => Convert.ToInt16(value, inv),
            JetDataType.Int32 => Convert.ToInt32(value, inv),
            JetDataType.Int64 => Convert.ToInt64(value, inv),
            JetDataType.Single => Convert.ToSingle(value, inv),
            JetDataType.Double => Convert.ToDouble(value, inv),
            JetDataType.Currency or JetDataType.FixedPoint => JetDecimalConverter.ToDecimal(value, inv),
            JetDataType.DateTime => value is DateTime d ? d : Convert.ToDateTime(value, inv),
            JetDataType.Text or JetDataType.Memo => Convert.ToString(value, inv),
            JetDataType.Guid => value is Guid g ? g : Guid.Parse(value.ToString()!),
            JetDataType.Binary or JetDataType.BigBinary or JetDataType.Ole => value as byte[] ?? System.Text.Encoding.Unicode.GetBytes(value.ToString()!),
            _ => value,
        };
    }


    /// <summary>Appends the new column's descriptor (after the existing descriptors) and its name (after the
    /// existing names) to the column region.</summary>
    private static void AppendColumnToParts(TdefParts parts, int colCount, byte[] descriptor, string name, JetFormatBase format)
    {
        int namesStart = colCount * format.ColumnDescriptorSize;
        ReadOnlySpan<byte> cols = parts.Columns;

        byte[] nameBytes = System.Text.Encoding.Unicode.GetBytes(name);
        var blob = new List<byte>(parts.Columns.Length + descriptor.Length + 2 + nameBytes.Length);
        blob.AddRange(cols[..namesStart].ToArray());   // existing descriptors
        blob.AddRange(descriptor);                       // new descriptor
        blob.AddRange(cols[namesStart..].ToArray());    // existing names
        blob.Add((byte)nameBytes.Length); blob.Add((byte)(nameBytes.Length >> 8));
        blob.AddRange(nameBytes);                         // new name
        parts.Columns = [.. blob];
    }

    /// <summary>
    /// Drops a column byte-faithfully with ACE (probed): a **metadata-only TDEF edit** — removes the
    /// column's 25-byte descriptor and its name, and decrements the live <c>ColumnCount</c> (0x2D). It does
    /// **not** renumber the surviving columns, recompute their fixed offsets/variable indexes, decrement the
    /// <c>VariableColumnCount</c> (0x2B stays a high-water mark), or rewrite existing rows — survivors keep
    /// their stored variable index (§3.4) so old rows still decode (the dropped column's data becomes dead
    /// bytes). Returns false if the column doesn't exist. Multi-page TDEFs are handled. Throws for a column
    /// that backs an index/key (drop that first).
    /// <para>A memo/OLE column also owns long-value pages through its own usage maps, and ACE retires those as
    /// DROP TABLE does (measured by whole-file diff): its §3.3.2 entry leaves the TDEF, its owned and free map
    /// records are retired from their holder, and its owned pages go back to the global free map at close. The
    /// pages themselves are left as they were, and the other long-value columns keep their entries and records.</para>
    /// </summary>
    public bool DropColumn(string tableName, string columnName)
    {
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");
        ColumnDef? col = table.Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
        if (col is null) return false;

        // A DELIBERATE divergence from ACE. ACE accepts dropping a column a calculated expression reads and
        // simply leaves that column unevaluatable — measured: every subsequent read of it fails, and there is
        // no way back short of recreating the column, because ACE offers no route to edit an expression at
        // all. Refusing keeps the file readable, and the caller who really wants it gone can drop the
        // calculated column first.
        if (CalculatedColumnsReading(table, col) is { Count: > 0 } dependents)
            throw new InvalidOperationException(
                $"Column '{col.Name}' cannot be dropped because "
                + $"{string.Join(", ", dependents.Select(d => $"'{d}'"))} "
                + (dependents.Count == 1
                    ? "is a calculated column that reads it. Drop it first."
                    : "are calculated columns that read it. Drop those first."));

        // ACE rejects dropping a column that participates in a relationship (as the child FK column or the
        // referenced parent key) — even a NO INDEX FK with no backing index — with "It is part of one or more
        // relationships"; you must drop the relationship first. This is correct, permanent behaviour (not a
        // gap), so mirror it. Verified vs ACE.
        if (ColumnIsInRelationship(table, col))
            throw new InvalidOperationException(
                $"Cannot drop column '{columnName}': it is part of one or more relationships — drop the relationship first.");

        // A complex (multi-value / attachment) column carries an index of its own, named `<column>_<GUID>`, so
        // the general "drop the index first" rule below would make the column undroppable. ACE drops it with
        // the column instead: measured on complex1.accdb, `ALTER TABLE Table1 DROP COLUMN att2` is accepted and
        // takes the column and its index.
        //
        // What ACE does NOT do is tidy up after it — the column's MSysComplexColumns row and its f_<GUID> flat
        // table are both still there afterwards, orphaned (measured in the same run; DROP TABLE, by contrast,
        // takes both). So neither does this: matching ACE is the rule, and a file where LibRed had removed
        // rows ACE keeps is a file that differs from the one Access would have produced.
        if (_catalog.ComplexColumns.Any(c =>
                string.Equals(c.OwnerTable.Name, tableName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.ColumnName, columnName, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string index in table.Indexes
                .Where(ix => ix.Columns.All(c => c.Column.ColumnId == col.ColumnId))
                .Select(ix => ix.Name).ToList())
                DropIndex(tableName, index);
            table = _catalog.FindTable(tableName)!;
            col = table.Columns.First(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
        }

        // ACE likewise rejects dropping an indexed/keyed column ("part of an index or is needed by the
        // system"); the index must be dropped first. Also correct, permanent behaviour. Verified vs ACE.
        if (table.Indexes.Any(ix => ix.Columns.Any(c => c.Column.ColumnId == col.ColumnId)))
            throw new InvalidOperationException(
                $"Cannot drop column '{columnName}': it is part of an index or key — drop the index/constraint first.");

        // A long-value column's maps, read from the TDEF before it loses them.
        var definition = new TableDefinitionPage();
        definition.Read(_channel, table.DefinitionPage);
        var maps = new UsageMap(_channel, table);
        var owned = new HashSet<int>();
        var retire = new List<MapRetirement>();
        QueueLongValueMaps(definition, col, maps, owned, retire);

        TdefParts parts = ParseTdef(table.DefinitionPage); // stitches continuation pages for a multi-page TDEF
        RemoveColumnFromParts(parts, table.Columns.Count, col.Index, _channel.Format);
        RemoveLongValueMapEntry(parts, col.ColumnId);
        WriteTdef(table.DefinitionPage, parts);

        RetireMapRecords(retire, maps, owned);
        var allocator = new PageAllocator(_channel);
        foreach (int page in owned)
            allocator.Release(page);   // reusable only after this handle closes, as ACE holds them

        // An append-only memo's version history goes with it (DropVersionHistory). Found before the catalog moves on,
        // and when this is the table's last append-only memo its table-level AppendOnly leaves in the same property
        // rewrite as the memo's own block — one rewrite, as ACE makes it.
        VersionHistory? history = VersionHistoryOf(tableName, columnName);
        RemoveColumnProperties(table.DefinitionPage, columnName, history is { IsLast: true }); // ACE drops its properties
        _catalog.Invalidate();
        if (history is { } h) DropVersionHistory(tableName, columnName, h);
        return true;
    }

    /// <summary>The element type of a table's version-history column: a per-table template named
    /// <c>MSysComplexTypeVH_&lt;GUID&gt;</c>, holding one value column per append-only memo, named for the memo, and
    /// a <c>Modified_&lt;GUID&gt;</c> timestamp.</summary>
    private const string VersionHistoryTemplatePrefix = "MSysComplexTypeVH_";

    private const string VersionHistoryTimestampPrefix = "Modified_";

    /// <summary>The table-level property Access sets while a table has an append-only memo.</summary>
    private const string AppendOnlyProperty = "AppendOnly";

    /// <summary>A table's version-history column as it stands for one of its append-only memos: the complex column,
    /// its template's name, and whether that memo is the last whose history it keeps.</summary>
    private sealed record VersionHistory(string ColumnName, int ComplexId, string FlatTable, string Template, bool IsLast);

    /// <summary>The version history keeping <paramref name="memoName"/>'s history, or null when the column is not an
    /// append-only memo. A table has at most one version-history column; its template carries a value column named
    /// for each append-only memo.</summary>
    private VersionHistory? VersionHistoryOf(string tableName, string memoName)
    {
        if (_catalog.ComplexColumns.FirstOrDefault(c =>
                string.Equals(c.OwnerTable.Name, tableName, StringComparison.OrdinalIgnoreCase)
                && c.ElementTypeName?.StartsWith(VersionHistoryTemplatePrefix, StringComparison.Ordinal) == true) is not { } history)
            return null;
        var values = _catalog.RequireTable(history.ElementTypeName!).Columns
            .Where(c => !c.Name.StartsWith(VersionHistoryTimestampPrefix, StringComparison.Ordinal)).ToList();
        if (!values.Any(c => string.Equals(c.Name, memoName, StringComparison.OrdinalIgnoreCase))) return null;
        return new VersionHistory(history.ColumnName, history.ComplexId, history.FlatTable.Name, history.ElementTypeName!,
            IsLast: values.Count == 1);
    }

    /// <summary>
    /// Takes a dropped append-only memo's version history with it, as ACE does (measured). A table keeps the history
    /// of all its append-only memos in one hidden complex column, whose template and flat table carry a value column
    /// per memo. While another append-only memo remains, only the dropped memo's value column goes, from both. When it
    /// was the last, the history goes entirely: the hidden column and its index, its <c>MSysComplexColumns</c> row, the
    /// flat table and the template — each released as a dropped table's is, but with its <c>MSysACEs</c> rows left
    /// behind, as ACE leaves them — and, when no other complex column is left on the table, its complex-column flag.
    /// (The table-level <c>AppendOnly</c> property has already gone, with the memo's own properties.) Unlike dropping
    /// an attachment or multi-value column directly, which leaves its registration and flat table behind.
    /// </summary>
    private void DropVersionHistory(string tableName, string memoName, VersionHistory history)
    {
        if (!history.IsLast)
        {
            DropColumn(history.FlatTable, memoName);
            DropColumn(history.Template, memoName);
            return;
        }

        int tdefPage = _catalog.RequireTable(tableName).DefinitionPage;
        DropColumn(tableName, history.ColumnName);   // the complex-column branch takes its index with it
        DropTable(history.FlatTable, keepPermissions: true);
        DeleteCatalogRows("MSysComplexColumns", "ComplexID", history.ComplexId);
        DropTable(history.Template, keepPermissions: true);

        if (!_catalog.ComplexColumns.Any(c => string.Equals(c.OwnerTable.Name, tableName, StringComparison.OrdinalIgnoreCase))
            && ReadObjectFlags(tdefPage) is int flags && (flags & CatalogFormat.ObjectFlagOwnsComplexColumns) != 0)
            UpdateCatalogRows("MSysObjects", "Id", tdefPage, required: true,
                ("Flags", flags & ~CatalogFormat.ObjectFlagOwnsComplexColumns));
        _catalog.Invalidate();
    }

    /// <summary>Removes a dropped column's extended-property block (DefaultValue, Required, …) from its
    /// table's <c>MSysObjects.LvProp</c> blob — what ACE does on DROP COLUMN (verified). Surgically removes
    /// just that column's block (keeps the name pool + other columns' blocks), re-stores the smaller blob on
    /// an LvProp page and updates the row. No-op when the column had no properties. With
    /// <paramref name="alsoTableAppendOnly"/> the table-level <c>AppendOnly</c> property leaves in the same rewrite —
    /// for the table's last append-only memo.</summary>
    private void RemoveColumnProperties(int tdefPage, string columnName, bool alsoTableAppendOnly = false)
    {
        (TableDef msys, Table table, int idIdx, ColumnDef lvProp) = ObjectProperties();

        foreach ((RowId id, object?[] values) in RowsKeyed(table, idIdx, tdefPage))
        {
            if (values[lvProp.Index] is not byte[] { Length: > 0 } blob) return;

            byte[] cleaned = PropertyBlob.RemoveOwner(blob, columnName);
            if (alsoTableAppendOnly) cleaned = ReplaceTableProperty(cleaned, AppendOnlyProperty, null);
            if (cleaned.Length == blob.Length) return; // nothing to remove

            byte[] descriptor = new RowInserter(_channel, msys).StorePackedLongValue(lvProp.ColumnId, cleaned);
            values[lvProp.Index] = new LongValueDescriptor(descriptor);
            table.Update(id, values, new HashSet<int> { lvProp.Index });
            return;
        }
    }

    /// <summary>Removes the descriptor + name of the column at <paramref name="removeIndex"/> from the
    /// column region and decrements the header's live ColumnCount (0x2D). VariableColumnCount (0x2B) is
    /// deliberately left unchanged — ACE keeps it as a high-water mark (verified).</summary>
    private static void RemoveColumnFromParts(TdefParts parts, int colCount, int removeIndex, JetFormatBase format)
    {
        int descSize = format.ColumnDescriptorSize;
        ReadOnlySpan<byte> cols = parts.Columns;

        var descriptors = new List<byte[]>(colCount);
        for (int i = 0; i < colCount; i++)
            descriptors.Add(cols.Slice(i * descSize, descSize).ToArray());

        int np = colCount * descSize;
        var names = new List<byte[]>(colCount);
        for (int i = 0; i < colCount; i++)
        {
            int len = BinaryPrimitives.ReadUInt16LittleEndian(cols.Slice(np, 2));
            names.Add(cols.Slice(np, 2 + len).ToArray());
            np += 2 + len;
        }

        descriptors.RemoveAt(removeIndex);
        names.RemoveAt(removeIndex);

        var blob = new List<byte>(parts.Columns.Length);
        foreach (byte[] d in descriptors) blob.AddRange(d);
        foreach (byte[] n in names) blob.AddRange(n);
        parts.Columns = [.. blob];

        BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefColumnCountOffset, 2), (ushort)(colCount - 1));
    }

    /// <summary>True if <paramref name="info"/> is the incoming relationship block that cross-links to the
    /// child's outgoing block number on the child's TDEF page (info block layout: +0x0C fk_type,
    /// +0x0D child block number, +0x11 child page).</summary>
    private static bool IsIncomingBlockFor(byte[] info, int childBlockNum, int childPage) =>
        info[IndexBlockFormat.InfoFkTypeOffset] == FkTypeIncoming &&
        (int)BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(IndexBlockFormat.InfoFkNumberOffset, 4)) == childBlockNum &&
        BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(IndexBlockFormat.InfoFkTablePageOffset, 4)) == childPage;

    /// <summary>Soft-deletes every MSysRelationships row for the named relationship.</summary>
    private void DeleteRelationshipRows(string name)
    {
        TableDef msys = _catalog.RequireTable("MSysRelationships");
        int nameIdx = msys.RequireColumn("szRelationship").Index;

        // A real delete, as the other catalog rows take (and as ACE's own DROP CONSTRAINT leaves the page —
        // measured by whole-file diff: the row's space back in the page's free count and the table's row count
        // down). Flagging the slot alone also left the index entries standing, pointing at a dead row.
        var table = new Table(_channel, msys);
        var rows = table.RowsWhere([nameIdx],
            values => string.Equals(values[nameIdx] as string, name, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach ((RowId id, object?[] values) in rows)
        {
            foreach (IndexDef index in msys.RealIndexes)
                table.RemoveIndexEntry(index, values, id);
            table.Delete(id);
        }
    }

    /// <summary>The name text of a TDEF name entry (2-byte UTF-16 length, then the chars).</summary>
    private static string NameOf(byte[] nameEntry) =>
        System.Text.Encoding.Unicode.GetString(nameEntry, 2, BinaryPrimitives.ReadUInt16LittleEndian(nameEntry.AsSpan(0, 2)));

    /// <summary>The parsed regions of a table definition, for surgical block removal. A multi-page definition
    /// is stitched into one buffer by <see cref="ParseTdef"/>; <see cref="Continuations"/> carries its extra
    /// pages so <see cref="WriteTdef"/> can reuse them.</summary>
    private sealed class TdefParts
    {
        public required byte[] Header;                          // [0, TdefRealIndexBlockOffset)
        public required List<byte[]> Stats;                     // one 12-byte stats block per data index
        public required byte[] Columns;                         // column descriptors + names region
        public required List<byte[]> DataBlocks;                // one 52-byte index-data block per data index
        public required List<(byte[] Info, byte[] Name)> Logical; // 28-byte info block + its name, name-sorted
        public required byte[] Lval;                            // §3.3.2 list + terminator
        public IReadOnlyList<int> Continuations = [];           // continuation-page numbers (multi-page TDEF)
    }

    private TdefParts ParseTdef(int tdefPage)
    {
        JetFormatBase format = _channel.Format;
        // Stitch any continuation pages into one contiguous buffer (offsets are absolute from page 1), so the
        // surgery below works the same for single- and multi-page definitions.
        (LibRed.IO.PageBuffer buf, IReadOnlyList<int> continuations) = ReadDefinition(tdefPage);

        TdefRegions regions = TdefRegions.Of(buf.Span, format);
        int dataCount = regions.DataCount;
        int logicalCount = regions.LogicalCount;
        int statsStart = regions.Stats;
        int afterStats = regions.ColumnDescriptors;
        int afterColumns = regions.DataBlocks;
        int infoStart = regions.InfoBlocks;
        int namePos = regions.IndexNames;
        int defEnd = buf.ReadInt32(format.TdefLengthOffset);

        var stats = new List<byte[]>(dataCount);
        for (int i = 0; i < dataCount; i++) stats.Add(buf.Slice(statsStart + i * format.RealIndexEntrySize, format.RealIndexEntrySize).ToArray());
        var dataBlocks = new List<byte[]>(dataCount);
        for (int i = 0; i < dataCount; i++) dataBlocks.Add(buf.Slice(afterColumns + i * IndexBlockFormat.DataBlockSize, IndexBlockFormat.DataBlockSize).ToArray());

        var logical = new List<(byte[], byte[])>(logicalCount);
        int np = namePos;
        for (int i = 0; i < logicalCount; i++)
        {
            byte[] info = buf.Slice(infoStart + i * IndexBlockFormat.InfoBlockSize, IndexBlockFormat.InfoBlockSize).ToArray();
            int len = buf.ReadUInt16(np);
            byte[] nm = buf.Slice(np, 2 + len).ToArray();
            np += 2 + len;
            logical.Add((info, nm));
        }

        return new TdefParts
        {
            Header = buf.Slice(0, statsStart).ToArray(),
            Stats = stats,
            Columns = buf.Slice(afterStats, afterColumns - afterStats).ToArray(),
            DataBlocks = dataBlocks,
            Logical = logical,
            Lval = buf.Slice(np, defEnd - np).ToArray(),
            Continuations = continuations,
        };
    }

    /// <summary>Removes a data index (its stats + data block at <paramref name="removeDataOrdinal"/>,
    /// decrementing the data-ordinal reference (+0x08) of every remaining info block that pointed past it)
    /// and every logical block matching <paramref name="removeLogical"/> (with its name).</summary>
    private static void RemoveTdefBlocks(TdefParts parts, int? removeDataOrdinal, Func<(byte[] Info, byte[] Name), bool> removeLogical)
    {
        // The logical blocks go first, so what is left is exactly what has to survive the renumbering below.
        parts.Logical.RemoveAll(b => removeLogical(b));

        if (removeDataOrdinal is int ord)
        {
            // One data block can be named by more than one logical block — a primary key that also backs a
            // relationship is the everyday case — and the renumbering below only moves references PAST the
            // block being removed. A surviving reference EQUAL to it would silently come to name whichever
            // index slid into the slot: same table, same file, an index quietly pointing at another index's
            // B-tree. The callers guard against this by refusing to drop an index a relationship uses; this is
            // the structural check behind that, so a route that ever gets here says so instead of writing it.
            foreach ((byte[] survivor, byte[] name) in parts.Logical)
                if (BinaryPrimitives.ReadInt32LittleEndian(survivor.AsSpan(0x08, 4)) == ord)
                    throw new InvalidOperationException(
                        $"Cannot remove index-data block {ord}: logical index '{NameOf(name)}' still refers to it.");

            parts.Stats.RemoveAt(ord);
            parts.DataBlocks.RemoveAt(ord);
            foreach ((byte[] info, _) in parts.Logical)
            {
                int num2 = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(0x08, 4));
                if (num2 > ord) BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(0x08, 4), num2 - 1);
            }
        }
    }

    /// <summary>Advances the object's <c>MSysObjects.DateUpdate</c>, leaving <c>DateCreate</c> where it is —
    /// measured: an ACE <c>ALTER TABLE … ADD COLUMN</c> moves the one and not the other. A no-op for an object
    /// with no catalog row yet, which is every table mid-CREATE.</summary>
    private void TouchObject(int objectId)
    {
        // Stamping MSysObjects from its own update path would recurse.
        if (_catalog.FindTable("MSysObjects") is { } msys && msys.DefinitionPage == objectId) return;
        UpdateCatalogRows("MSysObjects", "Id", objectId, required: false, ("DateUpdate", DateTime.Now));
    }

    private void WriteTdef(int tdefPage, TdefParts parts)
    {
        JetFormatBase format = _channel.Format;
        var body = new List<byte>(format.PageSize);
        body.AddRange(parts.Header);
        foreach (byte[] s in parts.Stats) body.AddRange(s);
        body.AddRange(parts.Columns);
        foreach (byte[] d in parts.DataBlocks) body.AddRange(d);
        foreach ((byte[] info, _) in parts.Logical) body.AddRange(info);
        foreach ((_, byte[] nm) in parts.Logical) body.AddRange(nm);
        body.AddRange(parts.Lval);
        byte[] def = [.. body];
        int defEnd = def.Length;

        BinaryPrimitives.WriteInt32LittleEndian(def.AsSpan(format.TdefIndexCountOffset, 4), parts.DataBlocks.Count);
        BinaryPrimitives.WriteInt32LittleEndian(def.AsSpan(format.TdefLogicalIndexCountOffset, 4), parts.Logical.Count);
        BinaryPrimitives.WriteInt32LittleEndian(def.AsSpan(format.TdefLengthOffset, 4), defEnd);

        // Write across the first page and continuation pages as needed (fresh ones, the old released) — handles a
        // definition that shrinks to one page, stays multi-page, or grows past a page (e.g. ADD COLUMN).
        WriteDefinition(tdefPage, def, parts.Continuations, rewrite: true);

        // Every edit to an existing table's definition comes through here, which is why the catalog stamp does
        // too: a column added, dropped, renamed or retyped, an index or relationship created or removed.
        TouchObject(tdefPage);
    }

    /// <summary>The data-block ordinal of a table's own index over the FK's referenced columns (for a
    /// self-reference — normally the primary key).</summary>
    private static int ReferencedOrdinalIn(TableDef table, RelationshipSpec fk) =>
        FindParentKeyIndex(table.Indexes, fk.Columns.Select(c => c.ReferencedColumn).ToList(), table.Name)
            .RealIndexOrdinal;

    /// <summary>The parent-side key index of a relationship: an index over exactly the referenced columns
    /// that is <b>unique or primary</b>.</summary>
    /// <remarks>
    /// The uniqueness requirement is ACE's, measured: over a plain non-unique index ACE refuses the
    /// relationship with "No unique index found for the referenced field of the primary table", while the
    /// same shape over a PRIMARY KEY succeeds. LibRed used to accept any index over the columns, which wrote
    /// a relationship ACE would not have created — and one whose backing index could then be dropped, since
    /// the DROP INDEX guard was looking for a unique one.
    /// </remarks>
    private static IndexDef FindParentKeyIndex(
        IReadOnlyList<IndexDef> candidates, IReadOnlyList<string> refColumns, string parentTable)
    {
        bool MatchesColumns(IndexDef ix) =>
            ix.Columns.Select(c => c.Column.Name).SequenceEqual(refColumns, StringComparer.OrdinalIgnoreCase);

        IndexDef? match = candidates.FirstOrDefault(ix => MatchesColumns(ix) && (ix.IsUnique || ix.IsPrimaryKey));
        if (match is not null) return match;

        // Distinguish "no index at all" from "an index, but not a unique one" — ACE's own message names the
        // second case, and it is the one a caller can fix by declaring the key unique.
        throw new InvalidOperationException(candidates.Any(MatchesColumns)
            ? $"No unique index found for the referenced field of the primary table: '{parentTable}' "
              + $"({string.Join(", ", refColumns)}) is indexed, but not uniquely."
            : $"Referenced table '{parentTable}' has no index over ({string.Join(", ", refColumns)}).");
    }

    /// <summary>The child (outgoing) end of a relationship: index_num2 = the child's own FK data block,
    /// Fk_type = outgoing, Fk_number/Fk_table = the parent's incoming block. Mirrors the inline-FK block
    /// TdefBuilder writes at creation time.</summary>
    private static byte[] BuildOutgoingInfoBlock(int number, int dataOrdinal, byte fkType, int fkNumber, int fkTablePage, byte upd, byte del)
    {
        var b = new byte[IndexBlockFormat.InfoBlockSize];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoMarkerOffset, 4), JetFormatBase.TdefRecordMarker);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoNumberOffset, 4), number);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoDataNumberOffset, 4), dataOrdinal);
        b[IndexBlockFormat.InfoFkTypeOffset] = fkType;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoFkNumberOffset, 4), (uint)fkNumber);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoFkTablePageOffset, 4), fkTablePage);
        b[IndexBlockFormat.InfoUpdateActionOffset] = upd;
        b[IndexBlockFormat.InfoDeleteActionOffset] = del;
        b[IndexBlockFormat.InfoTypeOffset] = IndexBlockFormat.TypeForeign;
        return b;
    }


    /// <summary>Reads a table definition, stitching continuation pages into one contiguous buffer (in
    /// the absolute coordinate space the descriptors use), and returns the continuation page numbers.</summary>
    private (LibRed.IO.PageBuffer Buffer, IReadOnlyList<int> ContinuationPages) ReadDefinition(int firstPage)
        => TdefChainReader.Read(_channel, firstPage);

    /// <summary>
    /// Writes a definition buffer across the first page and, if it overflows, continuation pages (each
    /// <c>[0x02][0x01][free:2][next:4]</c> then data). The first page carries the whole definition in its
    /// coordinate space; each continuation contributes <see cref="JetFormatBase.TdefContinuationHeaderSize"/>-offset data.
    /// <para>Rewriting an existing definition (<paramref name="rewrite"/>) is done as ACE does it (verified by
    /// whole-file diff, growing and shrinking): the first page is rewritten in place — alone, only the 8-byte
    /// reserve past the new end is zeroed and older bytes beyond it are left — and continuation data always goes
    /// to freshly allocated pages, while <paramref name="oldContinuations"/> are released untouched.</para>
    /// </summary>
    private void WriteDefinition(int firstPage, byte[] def, IReadOnlyList<int> oldContinuations, bool rewrite)
    {
        JetFormatBase format = _channel.Format;
        int ps = format.PageSize;
        int nextOffset = format.TdefNextPageOffset;

        foreach (int old in oldContinuations)
            _allocator.Release(old);   // reusable only after this handle closes, as ACE holds them

        if (def.Length + JetFormatBase.TdefContinuationHeaderSize <= ps)
        {
            byte[] only = rewrite ? _channel.ReadPage(firstPage).Span.ToArray() : new byte[ps];
            def.CopyTo(only, 0);
            only.AsSpan(def.Length, JetFormatBase.TdefContinuationHeaderSize).Clear(); // the reserve
            BinaryPrimitives.WriteInt32LittleEndian(only.AsSpan(nextOffset, 4), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(only.AsSpan(format.TdefFreeSpaceOffset, 2), (ushort)(ps - def.Length - JetFormatBase.TdefContinuationHeaderSize));
            _channel.WritePage(firstPage, only);
            return;
        }

        // The chain holds the definition and then its 8-byte trailing reserve, as ACE lays it out (verified): every
        // page is filled before the next begins, and the reserve follows the last definition byte, spilling onto a
        // page of its own when it does not fit — so a continuation can hold reserve bytes and no definition. A
        // 4,090-byte definition fills page 1 with 4,090 bytes and six of the reserve, and its continuation holds the
        // other two, free 4,086. Each page's free space is what it has left once both are placed.
        int maxMiddle = ps - JetFormatBase.TdefContinuationHeaderSize;
        int stored = def.Length + JetFormatBase.TdefContinuationHeaderSize;
        var chunks = new List<(int Offset, int Length, int Free)>();
        for (int consumed = ps; consumed < stored;)
        {
            int placed = Math.Min(maxMiddle, stored - consumed);
            chunks.Add((consumed, Math.Clamp(def.Length - consumed, 0, placed), maxMiddle - placed));
            consumed += placed;
        }

        // ACE allocates the last continuation first (verified: from free pages 354.. a two-page continuation became
        // first → 355 → 354, and from the end of a file first → n+1 → n).
        var pageNumbers = new int[chunks.Count];
        for (int i = chunks.Count - 1; i >= 0; i--)
            pageNumbers[i] = _allocator.Allocate();

        var page1 = new byte[ps];
        Array.Copy(def, 0, page1, 0, Math.Min(ps, def.Length)); // page 1 is completely full in a multi-page definition
        BinaryPrimitives.WriteInt32LittleEndian(page1.AsSpan(nextOffset, 4), pageNumbers[0]);
        BinaryPrimitives.WriteUInt16LittleEndian(page1.AsSpan(format.TdefFreeSpaceOffset, 2), 0);
        _channel.WritePage(firstPage, page1);

        for (int i = 0; i < chunks.Count; i++)
        {
            var (offset, length, free) = chunks[i];
            var page = new byte[ps];
            PageHeader.WriteType(page, PageType.TableDefinition);
            if (length > 0) // a page holding only the reserve starts past the definition's end
                Array.Copy(def, offset, page, JetFormatBase.TdefContinuationHeaderSize, length);
            int next = i + 1 < pageNumbers.Length ? pageNumbers[i + 1] : 0;
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(nextOffset, 4), next);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefFreeSpaceOffset, 2), (ushort)free);
            _channel.WritePage(pageNumbers[i], page);
        }
    }

    private static byte[] BuildIndexDataBlock(List<(int Id, bool Ascending)> columns, int rootPage, int usageRow, int usagePage,
        bool unique, bool required, bool ignoreNulls, bool complexColumn)
    {
        var b = new byte[IndexBlockFormat.DataBlockSize];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0, 4), IndexBlockFormat.DataMarker);
        for (int slot = 0; slot < IndexBlockFormat.MaxColumns; slot++)
        {
            int entry = IndexBlockFormat.ColumnsOffset + slot * IndexBlockFormat.ColumnSlotSize;
            if (slot < columns.Count)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(entry, 2), (short)columns[slot].Id);
                b[entry + 2] = columns[slot].Ascending ? IndexBlockFormat.ColumnAscending : (byte)0x00; // 0x00 = descending
            }
            else System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(entry, 2), IndexBlockFormat.ColumnUnused);
        }
        b[IndexBlockFormat.UsageMapRowOffset] = (byte)usageRow;
        b[IndexBlockFormat.UsageMapRowOffset + 1] = (byte)usagePage;
        b[IndexBlockFormat.UsageMapRowOffset + 2] = (byte)(usagePage >> 8);
        b[IndexBlockFormat.UsageMapRowOffset + 3] = (byte)(usagePage >> 16);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(IndexBlockFormat.RootPageOffset, 4), rootPage);
        ushort flags = IndexFlags.AlwaysSet;
        if (unique) flags |= IndexFlags.Unique;
        if (ignoreNulls) flags |= IndexFlags.IgnoreNulls;
        if (required) flags |= IndexFlags.Required;
        if (complexColumn) flags |= IndexFlags.ComplexColumn;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(IndexBlockFormat.FlagsOffset, 2), flags);
        return b;
    }

    private static byte[] BuildPlainInfoBlock(int number, int dataOrdinal, bool isPrimary)
    {
        var b = new byte[IndexBlockFormat.InfoBlockSize];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoMarkerOffset, 4), JetFormatBase.TdefRecordMarker);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoNumberOffset, 4), number);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoDataNumberOffset, 4), dataOrdinal);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoFkNumberOffset, 4), IndexBlockFormat.NoForeignKey); // no foreign key
        b[IndexBlockFormat.InfoUpdateActionOffset] = IndexBlockFormat.PlainAction;
        b[IndexBlockFormat.InfoDeleteActionOffset] = IndexBlockFormat.PlainAction;
        b[IndexBlockFormat.InfoTypeOffset] = isPrimary ? IndexBlockFormat.TypePrimary : IndexBlockFormat.TypeSecondary;
        return b;
    }

    private static int ReadInt24(LibRed.IO.PageBuffer buf, int offset) =>
        buf.ReadByte(offset) | (buf.ReadByte(offset + 1) << 8) | (buf.ReadByte(offset + 2) << 16);

    /// <summary>
    /// Adds an incoming-relationship logical index-info block (§3.6) to a parent table's already-written
    /// TDEF: it reuses the parent's referenced-key data block (no new data block), links back to the
    /// child's outgoing block, and grows the logical-index list by one (kept name-sorted). The definition is
    /// rewritten through <see cref="WriteTdef"/>, so it may span or spill onto continuation pages.
    /// </summary>
    private void AddIncomingRelationshipBlock(IncomingRelationship inc)
    {
        TdefParts parts = ParseTdef(inc.ParentPage); // stitches continuation pages for a multi-page TDEF

        // An incoming relationship adds a logical block and no data block, so this is the path a referenced
        // table overruns: 0x33 stays where it was while 0x2F climbs with every table that points here.
        EnsureIndexCapacity(
            _catalog.Tables.FirstOrDefault(t => t.DefinitionPage == inc.ParentPage)?.Name ?? $"page {inc.ParentPage}",
            "an incoming relationship", parts.DataBlocks.Count, parts.Logical.Count + 1);

        string newName = HiddenRelationshipName(inc.Number);
        int k = parts.Logical.Count(b => string.Compare(NameOf(b.Name), newName, StringComparison.OrdinalIgnoreCase) < 0); // name-sorted, ignoring case
        parts.Logical.Insert(k, (BuildIncomingInfoBlock(inc), EncodeName(newName)));

        WriteTdef(inc.ParentPage, parts);
    }

    private static byte[] BuildIncomingInfoBlock(IncomingRelationship inc)
    {
        var b = new byte[IndexBlockFormat.InfoBlockSize];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoMarkerOffset, 4), JetFormatBase.TdefRecordMarker);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoNumberOffset, 4), inc.Number);            // index_num
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoDataNumberOffset, 4), inc.ReferencedOrdinal); // index_num2 -> referenced-key data block
        b[IndexBlockFormat.InfoFkTypeOffset] = FkTypeIncoming;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoFkNumberOffset, 4), inc.ChildBlockNumber); // cross-link to child block
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(IndexBlockFormat.InfoFkTablePageOffset, 4), inc.ChildPage);
        b[IndexBlockFormat.InfoUpdateActionOffset] = inc.UpdateAction;
        b[IndexBlockFormat.InfoDeleteActionOffset] = inc.DeleteAction;
        b[IndexBlockFormat.InfoTypeOffset] = IndexBlockFormat.TypeForeign;
        return b;
    }

    private static byte[] EncodeName(string name)
    {
        byte[] chars = System.Text.Encoding.Unicode.GetBytes(name);
        var entry = new byte[2 + chars.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(0, 2), (ushort)chars.Length);
        chars.CopyTo(entry.AsSpan(2));
        return entry;
    }

    /// <summary>An incoming block's hidden name: <c>.r</c> and the letter <c>'A' + index_num</c>, as ACE names it
    /// (verified: 1 → <c>.rB</c>, 2 → <c>.rC</c>, 3 → <c>.rD</c>, in CREATE TABLE and ALTER TABLE alike). The
    /// name past <c>Z</c> is not measured.</summary>
    private static string HiddenRelationshipName(int number) =>
        number is >= 0 and < 26 ? $".r{(char)('A' + number)}" : $".r{Guid.NewGuid():N}"[..8];

    /// <summary>Writes an empty B-tree leaf (no entries) to serve as a fresh index root.</summary>
    private void WriteEmptyLeafIndexPage(JetFormatBase format, int pageNumber, int owner)
    {
        const int EntryDataOffset = 0x1E0;
        const int OwnerOffset = 0x04;

        var page = new byte[format.PageSize];
        PageHeader.WriteType(page, PageType.LeafIndexPage);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(OwnerOffset, 4), owner);
        // No entries: empty mask, no prefix compression, free space is the whole entry region.
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(format.PageSize - EntryDataOffset));
        _channel.WritePage(pageNumber, page);
    }

    /// <summary>
    /// Writes a data page of <paramref name="mapCount"/> empty inline usage-map records — like
    /// Access does for a fresh table that has no data page yet. Each record is
    /// <c>[0x00][startPage = 0][all-zero bitmap]</c>: row 0 = table owned-pages, row 1 = table
    /// free-pages, and (with an index) row 2 = the index's owned-pages. The first insert allocates a
    /// data page and sets the corresponding bit.
    /// </summary>
    private void WriteUsageMaps(JetFormatBase format, int pageNumber, int mapCount)
    {
        // An empty inline usage map: type byte + start page (0) + a bitmap of all-zero bytes. Access
        // writes a full-width bitmap; match its record length so the page layout matches byte-for-byte.
        const int MapLength = UsageMapRecordLength;

        var page = new byte[format.PageSize];
        PageHeader.WriteType(page, PageType.DataPage);
        // Owner of a usage-map page is 0 (it belongs to no table).

        int offset = format.PageSize;
        for (int row = 0; row < mapCount; row++)
        {
            offset -= MapLength;
            // page[offset] already 0x00 (inline type), start page already 0, bitmap already zero.
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + row * 2, 2), (ushort)offset);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)mapCount);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(offset - format.DataRowDirectoryOffset - mapCount * 2));
        _channel.WritePage(pageNumber, page);
    }

    // Jet/ACE hard limit on columns in a table.
    private const int MaxColumnsPerTable = 255;

    // An inline usage-map record: type byte + 4-byte start page + a 64-byte all-zero bitmap = 69 bytes.
    private const int UsageMapRecordLength = 1 + 4 + 64;

    // A user table's owner + grantee SIDs — Users owns user objects; Users + Admin get the grants — read from
    // the file being written, since an on-disk SID is masked per file (page-00 §2.3). Baking in the pair this
    // engine creates files with meant a table added to an ACE-authored file carried an owner that decodes to
    // no account in it (ACE writes 690C in Northwind where LibRed wrote its own 26CD).

    /// <summary>
    /// Adds the MSysObjects row describing the new table so Access (and the catalog) see it: Id =
    /// TDEF page, ParentId = Tables container, Type = table, Name, Flags, Owner, and create/update
    /// dates. Any column DEFAULT values are written into the extended-properties blob (LvProp, an OLE
    /// long value) as DefaultValue properties. MSysObjects' own indexes (Id, and the composite
    /// ParentId+Name used for name resolution) are maintained so Access can open the table by name.
    /// </summary>
    private void AddCatalogRow(string name, int tdefPage,
        IReadOnlyList<PropertyBlob.Property> columnProps,
        IReadOnlyList<(string Name, string Expression)> checkConstraints,
        bool ownsComplexColumns)
    {
        // Per-column properties (DefaultValue / Required) and CHECK constraints (a table property) both
        // live in the object's extended-properties (LvProp) blob.
        var props = columnProps.ToList();
        if (checkConstraints.Count > 0)
            props.Add(new PropertyBlob.Property("", PropertyBlob.CheckConstraintsProperty,
                PropertyBlob.WriteCheckList(checkConstraints)));

        // A table owning a complex column is flagged so — the rebuild recreates such tables, and Access marks
        // every one it writes.
        new CatalogWriter(_channel, _catalog).AddObjectRow(
            name, tdefPage, CatalogFormat.ObjectTypeTable, CatalogFormat.ObjectContainerParentId,
            flags: ownsComplexColumns ? CatalogFormat.ObjectFlagOwnsComplexColumns : 0, props);
    }

    // A new table's permission rows: what the Tables container grants what it creates (system-catalog §11).
    private void AddPermissionRows(int objectId) =>
        new CatalogWriter(_channel, _catalog).AddPermissionRows(objectId, CatalogFormat.ObjectContainerParentId);

    private static void WriteInt24(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
    }
}