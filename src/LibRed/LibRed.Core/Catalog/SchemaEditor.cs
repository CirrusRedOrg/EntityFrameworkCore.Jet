using EntityFrameworkCore.Jet.Data;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MapRetirement = ((int Row, int Page) Map, System.Collections.Generic.IReadOnlyList<(int Row, int Page)> Clear, System.Collections.Generic.IReadOnlyList<int> Pages);

namespace LibRed.Catalog;

/// <summary>
/// Creates and alters tables in an existing database. <see cref="Create"/> allocates and writes the TDEF
/// page, its indexes' B-tree roots and an owned-pages usage map, then records the table in MSysObjects so
/// the catalog finds it. The rest of the class is the incremental DDL EF issues one statement at a time —
/// ADD/DROP/ALTER/RENAME for columns, indexes, relationships and CHECK constraints — each an edit of the
/// existing TDEF in place, as ACE makes it, so unmodelled descriptor bytes survive.
/// </summary>
/// <param name="collation">The <b>database's</b> collating order, from page 0. Every non-numeric column this
/// class writes inherits it, which is what decides how that column's index keys are encoded, so it is
/// required rather than defaulted: a General-Legacy fallback silently writes v0 columns into a General (v1)
/// database, whose keys the rest of the file does not sort by.</param>
/// <param name="channel">The database file.</param>
/// <param name="catalog">The catalog to read and keep current.</param>
internal sealed class SchemaEditor(PageChannel channel, JetCatalog catalog, Collation collation)
{
    private readonly PageChannel _channel = channel;
    private readonly JetCatalog _catalog = catalog;
    private readonly PageAllocator _allocator = channel.Allocator;
    private readonly Collation _collation = collation;

    /// <summary>Rejects an incremental index or relationship that would take either TDEF count past
    /// <see cref="JetFormatBase.MaxIndexesPerTable"/>, naming both counts so the failure says which one bound.</summary>
    /// <remarks><see cref="TableDefinition"/> checks both when a table is created with all its indexes at once, but EF
    /// does not work that way: it creates the table, then adds indexes and relationships one statement at a time,
    /// and each of those comes through a surgical insert here instead. Nothing checked those paths, so the counts
    /// simply walked past the limit — the fields are <c>Int32</c>, so nothing overflowed.</remarks>
    private void EnsureIndexCapacity(string tableName, string what, int dataCount, int logicalCount)
    {
        int max = _channel.Format.MaxIndexesPerTable;
        if (dataCount > max || logicalCount > max)
        {
            throw new NotSupportedException(
                $"Cannot add {what} to '{tableName}': it would leave the table with {dataCount} index-data blocks and " +
                $"{logicalCount} logical index blocks. A table can have at most {max} of each, counting " +
                "those backing primary keys, unique constraints and relationships - and a table referenced by many " +
                "others accumulates a logical block per incoming relationship without gaining a data block.");
        }
    }


    /// <summary>Raises the file to the format its new columns need — a <c>BIGINT</c> or <c>DATETIME2</c> column's
    /// type, or a calculated column's ACE 14, the floor its own <c>FCMinReadVer</c> property declares — as Access
    /// itself does rather than refusing the DDL (verified: ACE adding a Date/Time Extended column to an ACE 12 file
    /// moves the version byte to 0x06 and nothing else; docs/format/page-00-database.md). A Jet MDB cannot take an
    /// ACE version and is refused. It joins the caller's transaction, so a failed statement takes the version back
    /// down with it, and it runs after the statement's checks, so a read-only database is refused rather than
    /// written.</summary>
    private void RaiseFormatFor(IReadOnlyList<ColumnSpec> columns)
    {
        JetVersion needed = columns
            .SelectMany(c => (JetVersion[])[
                JetDataTypeVersions.Required(c.Type)?.Min ?? JetVersion.Version3,
                c.CalculatedExpression is not null ? JetVersion.Version14_2010 : JetVersion.Version3])
            .DefaultIfEmpty(JetVersion.Version3).Max();
        _channel.RaiseFormatVersion((byte)needed);
    }

    public void Create(
        string name,
        IReadOnlyList<ColumnSpec> columns,
        IReadOnlyList<string>? primaryKey = null,
        IReadOnlyList<RelationshipSpec>? relationships = null,
        IReadOnlyList<UniqueIndexSpec>? uniqueConstraints = null,
        IReadOnlyList<(string Column, string DefaultSql)>? columnDefaults = null,
        IReadOnlyList<(string Name, string Expression, IReadOnlyList<(string Table, IReadOnlyList<string> Columns)> Reads)>? checkConstraints = null,
        string? primaryKeyName = null,
        int primaryKeyDeclaredAfter = 0,
        TableType tableType = TableType.User,
        bool flatTable = false)
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
        foreach (UniqueIndexSpec u in uniqueConstraints) JetName.Validate(u.Name, "unique constraint name");
        foreach ((string checkName, _, _) in checkConstraints) JetName.Validate(checkName, "check constraint name");

        // A legal name is all a read-only database looks at: a taken name, a relationship's target and everything
        // below are refused as read-only (verified vs ACE).
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, name);

        var relationshipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RelationshipSpec r in relationships)
            if (!relationshipNames.Add(r.Name)) throw RelationshipNameTaken(r.Name);
            else EnsureRelationshipNameFree(r.Name);

        // A table's name is free only if no table, query or linked table has it (case-insensitively) — ACE's
        // rule, reported as ACE reports it, rather than as the MSysObjects index violation writing the row hits.
        if (ObjectNameExists(name, exceptObjectId: 0))
            throw new SchemaObjectExistsException($"Table '{name}' already exists.", name);

        // Fail early with a clear message rather than write a table Access would refuse to open.
        if (columns.Count > _channel.Format.MaxColumnsPerTable)
            throw new InvalidOperationException(
                $"Table '{name}' has {columns.Count} columns; a table can have at most {_channel.Format.MaxColumnsPerTable}.");

        // Each complex column takes the registry's next ComplexIDs, in column order, before its descriptor is written.
        if (columns.Any(c => c.ComplexTemplate is not null))
        {
            int complexId = NextComplexId();
            columns = [.. columns.Select(c => c.ComplexTemplate is null ? c : c with { ComplexId = complexId++ })];
        }

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

        // Built before anything is written, since a default can be refused.
        var defaults = new Dictionary<string, PropertyBlob.Property>(StringComparer.OrdinalIgnoreCase);
        foreach ((string column, string defaultSql) in columnDefaults)
            if (columns.FirstOrDefault(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)) is { } spec)
                defaults.TryAdd(spec.Name, PropertyBlob.DefaultValue(spec.Name, spec.Type, defaultSql));

        RaiseFormatFor(columns);
        JetFormatBase format = _channel.Format;

        // Allocate the pages the table needs through the global free-pages map (so Access accounts
        // for them). Like Access, a fresh table has NO data page — the first is allocated lazily on
        // the first insert — so its usage maps start empty.
        int tdefPage = _allocator.Allocate();
        int usageMapPage = _allocator.Allocate();

        // Key the long-value maps by the column's *id*, not its position. The two coincide on an ordinary
        // CREATE TABLE, but a spec can carry an explicit id, and the TDEF's long-value map is read back by id,
        // so using the position there silently points a Memo/OLE column's usage maps at the wrong column.
        // A calculated column with a Memo RESULT needs the maps too, and its declared type does not say so:
        // ACE declares such a column Text with length 0 and reaches the value through a long-value
        // descriptor, so keying off Type alone leaves it without maps and its result nowhere to go (§3.4a).
        var longValueCols = columns.Select((c, i) => (Column: c, Id: c.ColumnId ?? i, Position: i))
            .Where(x => x.Column.Type is JetDataType.Memo or JetDataType.Ole
                        || x.Column.CalculatedResultType is JetDataType.Memo)
            .ToList();

        // The table's data-block indexes: the primary key (unique), a unique index per UNIQUE constraint, and
        // one non-unique index per foreign key over its child columns — Access enforces a relationship through
        // an index on the FK columns. Each carries the relationship (if any) it backs. They are numbered in the
        // order the statement declares them, whatever their kind (verified vs ACE); with no order given (every
        // DeclaredAfter 0) the stable sort leaves them primary key, unique, foreign key.
        var indexPlans = new List<(string Name, IReadOnlyList<string> Columns, bool IsPk, bool IsUnique,
            RelationshipSpec? Fk, int DeclaredAfter)>();
        if (primaryKey is { Count: > 0 })
            // Name the PK index after the CONSTRAINT if one was given (ACE does the same, and the scaffolder
            // round-trips it). If unnamed, LibRed picks the stable "PrimaryKey" (the DAO/Access-UI convention)
            // — an engine choice, since ACE-via-SQL instead generates a random "Index_<hex>" with no fixed
            // value to reproduce, and nothing downstream depends on the exact name.
            indexPlans.Add((primaryKeyName ?? "PrimaryKey", primaryKey, true, true, null, primaryKeyDeclaredAfter));
        foreach (UniqueIndexSpec unique in uniqueConstraints)
            indexPlans.Add((unique.Name, unique.Columns, false, true, null, unique.DeclaredAfter));
        foreach (RelationshipSpec fk in relationships)
            indexPlans.Add((fk.Name, fk.Columns.Select(c => c.Column).ToList(), false, false, fk, fk.DeclaredAfter));
        indexPlans = [.. indexPlans.OrderBy(p => p.DeclaredAfter)];

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
        // A constraint over the same columns and of the same kind as one declared before it — two UNIQUEs, two
        // foreign keys — takes that one's data block, as an index added later does (InsertIndex; verified vs ACE):
        // the kind decides the block's flags. Each plan's data ordinal is its own, numbered among the plans that have
        // one, or the one it shares; only an owner has a usage map, a root and a data block.
        int[] dataOrdinal = new int[indexPlans.Count];
        var owners = new List<int>();
        for (int i = 0; i < indexPlans.Count; i++)
        {
            var plan = indexPlans[i];
            int match = owners.FindIndex(o => indexPlans[o].IsPk == plan.IsPk && indexPlans[o].IsUnique == plan.IsUnique
                && indexPlans[o].Columns.SequenceEqual(plan.Columns, StringComparer.OrdinalIgnoreCase));
            dataOrdinal[i] = match >= 0 ? match : owners.Count;
            if (match < 0) owners.Add(i);
        }
        bool Owns(int i) => owners[dataOrdinal[i]] == i;

        int mapsPerPage = UsageMap.RecordsFitting(format, format.PageSize - format.DataRowDirectoryOffset);
        int primaryRecords = 2 + owners.Count; // data owned/free + one per data block
        int colsOnPrimary = Math.Clamp((mapsPerPage - primaryRecords) / 2, 0, longValueCols.Count);
        _channel.WritePage(usageMapPage, UsageMap.NewMapPage(format, primaryRecords + colsOnPrimary * 2));

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
        // An index's DeclaredAfter counts the constraints declared before it as well as the columns; less those
        // constraints, it is the columns before it, which is what places it among the long-value columns — and
        // its root among the columns' extended properties, below.
        int[] columnsBefore = [.. indexPlans.Select(p => p.DeclaredAfter - indexPlans.Count(q => q.DeclaredAfter < p.DeclaredAfter))];
        var claims = owners
            .Select(i => (Sort: (columnsBefore[i], Constraint: 0, i), Index: i, LongValue: -1))
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
                _channel.WritePage(columnMapPage, UsageMap.NewMapPage(format, 2)); // owned = row 0, free = row 1
                longValueSpecs.Add(new LongValueColumnSpec(colId, UsedRow: 0, FreeRow: 1, MapPage: columnMapPage));
            }
        }

        // Each index is an empty leaf root, populated as rows are inserted; its root is allocated below, once the
        // definition has been built and the catalog row written. Its usage map is on the primary page, at the row the
        // declaration order above gave it.
        var indexes = owners.Select(i => new IndexSpec(indexPlans[i].Name, indexPlans[i].Columns, indexPlans[i].IsPk,
            indexPlans[i].IsUnique, RootPage: 0, UsageMapRow: indexRows[i], UsageMapPage: usageMapPage)).ToList();

        // Build the child's logical index-info blocks, one per plan, each naming its plan's data block. A plain
        // index's (PK's) is a plain block; a foreign key's instead carries the *outgoing* relationship block (§3.6), linked
        // to an *incoming* block. The two ends cross-reference by index_num. For a cross-table FK the
        // incoming block is added to the parent's TDEF; for a self-reference it lives in this same TDEF.
        var childLogical = new List<TableDefinition.LogicalIndexSpec>(indexPlans.Count);
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
                childLogical.Add(TableDefinition.LogicalIndexSpec.Plain(i, dataOrdinal[i], plan.IsPk, plan.Name));
                continue;
            }

            RelationshipSpec fk = plan.Fk;
            if (fk.UpdateSetNull) throw UpdateSetNullNotImplemented();
            (RelationshipAction upd, RelationshipAction del) = Actions(fk);
            ForeignKeyType outgoingType = fk.NoIndex ? ForeignKeyType.OutgoingNoIndex : ForeignKeyType.Outgoing;

            // A self-referencing FK: the table is not in the catalog yet (we are creating it), so resolve
            // the referenced index within the plans we are building and host both ends here.
            if (string.Equals(fk.ReferencedTable, name, StringComparison.OrdinalIgnoreCase))
            {
                // Only a key declared before the reference serves: ACE refuses one declared after it (verified).
                int refOrdinal = dataOrdinal[SelfReferencedOrdinal(indexPlans.GetRange(0, i), fk)];
                int inNum = selfIncomingNum++;
                // Outgoing block (this table's child side) — NO INDEX flags it 0x03 instead of 0x02.
                childLogical.Add(new TableDefinition.LogicalIndexSpec(
                    Number: i, DataOrdinal: dataOrdinal[i], FkType: outgoingType, FkNumber: (uint)inNum,
                    FkTablePage: tdefPage, UpdateAction: upd, DeleteAction: del,
                    Type: IndexInfoType.Foreign, Name: fk.Name));
                // Incoming block (this table's parent side), hidden ".r" name unique within the table.
                childLogical.Add(new IncomingRelationship(tdefPage, inNum, refOrdinal, (uint)i, tdefPage, upd, del)
                    .Spec(HiddenRelationshipName(inNum)));
                continue;
            }

            (int parentPage, int refOrd, int parentNextNum) = ResolveParent(fk, tdefPage);
            // Each incoming block takes the parent's next free number — past the last one this statement gave
            // it, since those blocks are not written yet.
            int parentNum = parentAdds.TryGetValue(parentPage, out int last)
                ? NextLogicalIndexNumber(parentPage, above: last)
                : parentNextNum;
            parentAdds[parentPage] = parentNum;

            childLogical.Add(new TableDefinition.LogicalIndexSpec(
                Number: i, DataOrdinal: dataOrdinal[i], FkType: outgoingType,
                FkNumber: (uint)parentNum, FkTablePage: parentPage, UpdateAction: upd, DeleteAction: del,
                Type: IndexInfoType.Foreign, Name: fk.Name));
            incoming.Add(new IncomingRelationship(parentPage, parentNum, refOrd,
                ChildBlockNumber: (uint)i, ChildPage: tdefPage, upd, del));
        }

        // Access stores logical blocks sorted by name in the database's collation (with their names in the same order).
        JetTextComparer names = JetTextComparer.For(_collation);
        childLogical.Sort((a, b) => names.Compare(a.Name, b.Name));

        // Build the definition, pointed at the usage maps on the usage-map page — before the catalog row is written,
        // since the build refuses a declaration ACE refuses (a NUMERIC precision, a record too wide).
        byte[] Build() => TableDefinition.Build(format, tableType, columns, _collation, indexes, longValueSpecs, childLogical,
            usageMapPage).Page;
        Build();

        // The table's MSysObjects row, with no extended properties yet (they are written below, one at a time), comes
        // before the index roots, which come before the definition's continuation pages (measured: a CREATE TABLE whose
        // row took a new MSysObjects page put that page before the primary key's root; a wide table's root comes before
        // its definition's continuations). A table owning a complex column is flagged so — Access marks every one it
        // writes.
        ObjectAttributes objectFlags = flatTable ? ObjectAttributes.System | ObjectAttributes.ComplexFlatTable
            : columns.Any(c => c.Type == JetDataType.Complex) ? ObjectAttributes.OwnsComplexColumns
            : ObjectAttributes.None;
        _catalog.AddObjectRow(name, tdefPage, ObjectType.Table, JetCatalog.ObjectContainerParentId, flags: unchecked((int)objectFlags));

        // A key declared after a column with extended properties gets its root after those properties are written, and
        // a foreign key's after its relationship's rows (both below); until then the definition names page 0.
        bool HasProperties(ColumnSpec c) => defaults.ContainsKey(c.Name) || !c.IsNullable || c.CalculatedExpression is not null;
        bool[] afterProperties = [.. columnsBefore.Select(n => columns.Take(n).Any(HasProperties))];
        for (int k = 0; k < owners.Count; k++)
        {
            int i = owners[k];
            if (indexPlans[i].Fk is not null || afterProperties[i]) continue;
            int rootPage = AllocateIndexRoot(format, tdefPage);
            new UsageMap(_channel).SetBit(indexRows[i], usageMapPage, rootPage, set: true);
            indexes[k] = indexes[k] with { RootPage = rootPage };
        }

        byte[] tdef = Build();
        // A wide table's definition can exceed one page; write it split across continuation pages if needed.
        int defEnd = TableDefinition.ReadLength(tdef, format);
        TableDefinition.WriteChain(_channel, _allocator, tdefPage, tdef[..defEnd], [], rewrite: false);

        // A new table's permission rows: what the Tables container grants what it creates (system-catalog §11).
        _catalog.AddPermissionRows(tdefPage, JetCatalog.ObjectContainerParentId, inheritable: flatTable);

        // A root allocated after the definition is written: recorded in its usage map, then named in the definition.
        void AllocateRoot(int i)
        {
            int rootPage = AllocateIndexRoot(format, tdefPage);
            new UsageMap(_channel).SetBit(indexRows[i], usageMapPage, rootPage, set: true);
            _catalog.Invalidate();
            TableDefinition created = _catalog.RequireTable(name);
            new IndexTree(_channel, created).UpdateIndexRoot(created.Indexes.First(ix => ix.RealIndexOrdinal == dataOrdinal[i]), rootPage);
        }

        // The extended properties are written ONE AT A TIME, in column order with DefaultValue before Required:
        // each write stores the whole blob so far — inline while it fits, on a packed LvProp page after — and frees
        // the version it replaces (verified vs ACE: four DEFAULTs leave three zero-length tombstones ahead of the
        // live blob on that page, and a column with both DEFAULT and NOT NULL is two writes). A DEFAULT is a memo
        // property; a NOT NULL column carries a boolean Required property, and a nullable one none — an
        // AutoNumber too: ACE writes Required for COUNTER NOT NULL and not for a bare COUNTER. A key declared after
        // the column takes its root here, before the next column's (verified: a UNIQUE declared inline after a
        // DEFAULT, or at the end of the statement, comes after the property page; a PRIMARY KEY on a first column
        // with no properties before it). A CHECK, a table property, is written after the columns'.
        var written = new List<PropertyBlob.Property>();
        void Write(PropertyBlob.Property property)
        {
            written.Add(property);
            WriteObjectProperties(tdefPage, PropertyBlob.Write(written));
        }
        for (int position = 0; position < columns.Count; position++)
        {
            ColumnSpec col = columns[position];
            if (defaults.TryGetValue(col.Name, out PropertyBlob.Property def)) Write(def);
            if (!col.IsNullable) Write(PropertyBlob.Bool(col.Name, PropertyBlob.RequiredProperty, true));
            foreach (PropertyBlob.Property calculated in CalculatedProperties(col)) Write(calculated);
            foreach (int i in owners)
                if (afterProperties[i] && indexPlans[i].Fk is null && columnsBefore[i] == position + 1) AllocateRoot(i);
        }
        foreach (int i in owners)
            if (afterProperties[i] && indexPlans[i].Fk is null && columnsBefore[i] > columns.Count) AllocateRoot(i);
        if (checkConstraints.Count > 0)
            Write(new PropertyBlob.Property("", PropertyBlob.CheckConstraintsProperty,
                PropertyBlob.WriteCheckList([.. checkConstraints.Select(c => (c.Name, c.Expression))])));
        // Each check's object, after the table's own row, as ACE writes them (measured, whole-file diff).
        foreach ((string checkName, string expression, var reads) in checkConstraints)
            _catalog.CreateCheckObject(checkName, name, expression, CheckReadsText(reads));

        // Each foreign key's index is built after its relationship's rows, as ACE builds it (verified: a database's
        // first relationship takes MSysRelationships' first data page, and the key's index root the page after).
        for (int i = 0; i < indexPlans.Count; i++)
        {
            if (indexPlans[i].Fk is not { } fk) continue;
            AddRelationshipRows(name, fk);
            if (Owns(i)) AllocateRoot(i);
        }
        foreach (IncomingRelationship inc in incoming)
            AddIncomingRelationshipBlock(inc);
        foreach (ColumnSpec complex in columns.Where(c => c.ComplexTemplate is not null))
            AddComplexStorage(name, complex);
    }

    /// <summary>Allocates an index's root — an empty leaf owned by the table at <paramref name="tdefPage"/>. Every
    /// caller then records it in the index's own pages usage map, as Access does at CREATE, before any row exists
    /// (verified: a freshly created empty index has exactly its root bit set); as the tree grows, IndexTree adds
    /// each page it allocates, so the map covers the whole B-tree.</summary>
    private int AllocateIndexRoot(JetFormatBase format, int tdefPage)
    {
        int rootPage = _allocator.Allocate();
        _channel.WritePage(rootPage, IndexTree.EmptyLeaf(format, owner: tdefPage));
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
    /// version byte it needs — see <see cref="RaiseFormatFor"/>.</summary>
    private const string CalculatedMinimumVersion = "14.0.0000.0000";

    /// <summary>ON UPDATE SET NULL pathway: the docs list it, but the ACE OLE DB provider rejects it via SQL,
    /// so its on-disk storage (the grbit flag + the index-info +0x15 action byte) is unverified. Rather than
    /// guess the bytes, fail loudly until a UI/DAO-created sample can be probed.</summary>
    private static NotImplementedException UpdateSetNullNotImplemented() => new(
        "ON UPDATE SET NULL is not supported. Only ON UPDATE NO ACTION and ON UPDATE CASCADE are supported.");

    /// <summary>The update and delete actions a relationship's info blocks carry, on both its ends.</summary>
    private static (RelationshipAction Update, RelationshipAction Delete) Actions(RelationshipSpec fk) =>
        (fk.CascadeUpdate ? RelationshipAction.Cascade : RelationshipAction.NoCascade,
         fk.CascadeDelete ? RelationshipAction.Cascade
            : fk.DeleteSetNull ? RelationshipAction.SetNull : RelationshipAction.NoCascade);

    /// <summary>An incoming-relationship logical block to add to a parent table's TDEF.</summary>
    private readonly record struct IncomingRelationship(
        int ParentPage, int Number, int ReferencedOrdinal, uint ChildBlockNumber, int ChildPage,
        RelationshipAction UpdateAction, RelationshipAction DeleteAction)
    {
        /// <summary>The block: <c>index_num2</c> names the parent's referenced-key data block, and the foreign
        /// key fields cross-link to the child's outgoing block.</summary>
        public TableDefinition.LogicalIndexSpec Spec(string name) => new(
            Number, ReferencedOrdinal, ForeignKeyType.Incoming, ChildBlockNumber, ChildPage,
            UpdateAction, DeleteAction, IndexInfoType.Foreign, name);
    }

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

    private static Func<string, JetDataType?> ColumnOf(TableDefinition? table) => name => table?.FindColumn(name)?.Type;

    /// <summary>The data-block ordinal of a self-reference's parent-side key, found among the indexes being
    /// created for this table (the table is not in the catalog yet) on <see cref="FindParentKeyIndex"/>'s terms:
    /// ACE refuses one over another relationship's non-unique index (verified).</summary>
    private static int SelfReferencedOrdinal(
        List<(string Name, IReadOnlyList<string> Columns, bool IsPk, bool IsUnique, RelationshipSpec? Fk,
            int DeclaredAfter)> indexPlans,
        RelationshipSpec fk) =>
        ParentKeyPosition(
            [.. indexPlans.Select(p => ((IEnumerable<string>)p.Columns, p.IsPk || p.IsUnique))],
            [.. fk.Columns.Select(c => c.ReferencedColumn)], fk.ReferencedTable);

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
        var used = TableDefinition.ReadParts(_channel, tdefPage).Logical
            .Select(b => TableDefinition.ReadInfoBlock(b.Info, _channel.Format, NameOf(b.Name)).Number).ToHashSet();
        int next = above + 1;
        while (used.Contains(next)) next++;
        return next;
    }

    /// <summary>Whether every row of <paramref name="childTable"/> has its parent, on ACE's <b>MATCH FULL</b> terms
    /// (verified vs ACE): a key that is entirely null needs no parent, and a partly null one can never match.</summary>
    internal bool ExistingRowsHaveParents(
        string childTable, string parentTable, IReadOnlyList<(string Column, string ReferencedColumn)> columns)
    {
        TableDefinition child = _catalog.RequireTable(childTable);
        TableDefinition parent = _catalog.RequireTable(parentTable);
        IndexDef key = FindParentKeyIndex(parent.Indexes, [.. columns.Select(c => c.ReferencedColumn)], parent.Name);
        int[] childColumns = [.. columns.Select(c => child.RequireColumn(c.Column).Index)];
        int[] parentColumns = [.. columns.Select(c => parent.RequireColumn(c.ReferencedColumn).Index)];

        var rows = new Table(_channel, child, _catalog);
        var parentKeys = new IndexTree(_channel, parent);
        var probe = new object?[parent.Columns.Count];
        foreach (object?[] row in rows.Rows(rows.DecodeOnly(childColumns)))
        {
            int nulls = childColumns.Count(c => row[c] is null);
            if (nulls == childColumns.Length) continue;
            if (nulls > 0) return false;
            for (int i = 0; i < childColumns.Length; i++) probe[parentColumns[i]] = row[childColumns[i]];
            if (!parentKeys.KeyExists(key, probe)) return false;
        }
        return true;
    }

    /// <summary>The table a design change names — the one it alters, indexes or drops from, or a relationship's parent
    /// — or ACE's error when there is none: "Cannot find table or constraint." for every one of them (verified: ALTER
    /// TABLE in each form, CREATE and DROP INDEX, and a FOREIGN KEY's REFERENCES).</summary>
    private TableDefinition DesignTable(string name) =>
        _catalog.FindTable(name) ?? throw new InvalidOperationException("Cannot find table or constraint.");

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
        IndexDef primaryKey = (selfInCreate ? null : DesignTable(fk.ReferencedTable).Indexes.FirstOrDefault(i => i.IsPrimaryKey))
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
        TableDefinition parent = DesignTable(fk.ReferencedTable);
        if (parent.DefinitionPage == childPage)
            throw new InvalidOperationException($"Self-referencing foreign key '{fk.Name}' should have been handled inline.");

        var ptdef = new TableDefinition();
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

    internal static SchemaObjectExistsException RelationshipNameTaken(string name) =>
        new($"There is already a relationship named '{name}' in the current database.", name);

    /// <summary>
    /// Writes the <c>MSysRelationships</c> rows for one relationship — one row per column pair, with
    /// <c>ccolumn</c> = the pair count, <c>icolumn</c> = the 0-based pair index, and <c>grbit</c>
    /// encoding enforce/cascade (verified against Access: an enforced no-cascade FK stores grbit 0) — and the
    /// relationship's own <c>MSysObjects</c> object, which ACE records for every relationship.
    /// </summary>
    private void AddRelationshipRows(string childTable, RelationshipSpec fk)
    {
        TableDefinition msys = _catalog.RequireTable("MSysRelationships");
        _catalog.CreateRelationshipObject(fk.Name);

        var grbit = RelationshipFlags.None;
        if (!fk.IsEnforced) grbit |= RelationshipFlags.DontEnforce;
        if (fk.CascadeUpdate) grbit |= RelationshipFlags.UpdateCascade;
        if (fk.CascadeDelete) grbit |= RelationshipFlags.DeleteCascade;
        if (fk.DeleteSetNull) grbit |= RelationshipFlags.DeleteSetNull;

        for (int i = 0; i < fk.Columns.Count; i++)
        {
            var (column, referencedColumn) = fk.Columns[i];
            var values = new object?[msys.Columns.Count];
            JetCatalog.Set(msys, values, "szRelationship", fk.Name);
            JetCatalog.Set(msys, values, "szObject", childTable);
            JetCatalog.Set(msys, values, "szColumn", column);
            JetCatalog.Set(msys, values, "szReferencedObject", fk.ReferencedTable);
            JetCatalog.Set(msys, values, "szReferencedColumn", referencedColumn);
            JetCatalog.Set(msys, values, "ccolumn", fk.Columns.Count);
            JetCatalog.Set(msys, values, "icolumn", i);
            JetCatalog.Set(msys, values, "grbit", (int)grbit);
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
        TableDefinition table = DesignTable(tableName);
        AddIndex(table, indexName, columns, isUnique, isPrimary, disallowNull, ignoreNulls);
    }

    /// <summary>Adds an index using an already resolved definition, including during catalog creation.</summary>
    internal void AddIndex(TableDefinition table, string indexName, IReadOnlyList<(string Column, bool Descending)> columns,
        bool isUnique, bool isPrimary, bool disallowNull, bool ignoreNulls)
    {
        JetName.Validate(indexName, "index name");
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, table.Name);
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
            (num, ord) => TableDefinition.LogicalIndexSpec.Plain(num, ord, isPrimary, indexName));
    }

    /// <summary>Refuses an index over a calculated column, on whichever route asked for it.
    /// <para>This matches Access and diverges only from ACE's SQL layer, which is the one that gets it wrong.
    /// Access's <b>designer</b> does not offer a calculated column in the Indexes dialog at all and will not
    /// let it be the primary key; the <b>storage engine</b> agrees, refusing every INSERT into a table whose
    /// calculated column is indexed — <i>"Operation is not supported for this type of object."</i> Only
    /// <b>ACE via SQL</b> accepts <c>CREATE INDEX</c> (and even <c>CREATE UNIQUE INDEX</c>) on one while the
    /// table is empty, and what it produces is a table into which no row can ever be written; once rows exist
    /// it refuses the <c>CREATE INDEX</c> itself (page-02e-calculated-columns).</para></summary>
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
        TableDefinition table, IEnumerable<(string Column, bool Ascending)> columns)
    {
        var byName = table.Columns.ToDictionary(c => c.Name, c => c.ColumnId, StringComparer.OrdinalIgnoreCase);
        return columns.Select(c => byName.TryGetValue(c.Column, out int id) ? (Id: id, c.Ascending)
            : throw new InvalidOperationException($"Column '{c.Column}' does not exist in '{table.Name}'.")).ToList();
    }

    /// <summary>Surgically inserts one index into an existing table's TDEF: its logical info block, name-sorted, and
    /// its data index — or, where the table has a data block over the same columns, directions and flags, that one.
    /// <paramref name="buildInfo"/> gets (block number, data-block ordinal) and returns the info block's fields — a
    /// plain index or an outgoing-FK block. Returns the new block number.</summary>
    private int InsertIndex(TableDefinition table, string indexName, List<(int Id, bool Ascending)> slots,
        bool unique, bool required, bool ignoreNulls, Func<int, int, TableDefinition.LogicalIndexSpec> buildInfo)
    {
        JetFormatBase format = _channel.Format;

        // ACE rejects a duplicate index name — for a foreign key's backing index as much as a CREATE INDEX
        // (verified) — and so must LibRed: every lookup downstream (DROP INDEX, the back-fill, DROP CONSTRAINT)
        // finds an index by name with First/FirstOrDefault, so two blocks sharing one name make those operations
        // pick an arbitrary block — a DROP that removes the wrong one.
        JetTextComparer names = JetTextComparer.For(_collation);
        if (table.LogicalIndexes.Any(l => names.Equals(l.Name, indexName)))
            throw new InvalidOperationException(
                $"Table '{table.Name}' already has an index named '{indexName}'.");

        // The index-data block holds exactly IndexDataMaxColumns slots, with no count and no continuation, so
        // a wider index cannot be represented. TableDefinition rejects this when a table is created with its
        // indexes; this is the incremental path, where BuildDataBlock would otherwise write the first ten and
        // mark the rest unused — silently storing a different index from the one asked for, which ACE reads
        // without complaint. ACE refuses instead: "Cannot have more than 10 fields in an index."
        if (slots.Count > format.IndexDataMaxColumns)
            throw new NotSupportedException(
                $"Cannot create index '{indexName}' on '{table.Name}' over {slots.Count} columns: "
                + $"an index holds at most {format.IndexDataMaxColumns} fields.");

        TableDefinition.Parts parts = TableDefinition.ReadParts(_channel, table.DefinitionPage); // stitches continuation pages for a multi-page TDEF
        var header = new PageBuffer(parts.Header, table.DefinitionPage);
        int existingRowCount = TableDefinition.ReadRowCount(parts.Header, format);

        // A unique index over rows that already exist has to be rejected if those rows aren't unique, and a
        // required one if any row leaves a key column NULL — ACE refuses the DDL for both. Done *here*, before a
        // single byte of the TDEF moves, rather than during the back-fill: this path is not transactional, so a
        // failure discovered mid-back-fill would leave the index committed to the TDEF and half-populated.
        // Scanning first means a rejected CREATE INDEX leaves the file exactly as it was.
        if ((unique || required) && existingRowCount != 0)
            EnsureExistingRowsFitIndex(table, indexName, slots, unique, required);

        int dataCount = parts.DataBlocks.Count;
        bool complexColumn = slots.Any(s => table.Columns.Any(c => c.ColumnId == s.Id && c.Type == JetDataType.Complex));
        byte[] dataBlock = TableDefinition.BuildDataBlock(format, slots, rootPage: 0, usageRow: 0, usagePage: 0,
            unique, ignoreNulls, required, complexColumn);

        // An index over the same columns, in the same directions and with the same flags as a data block the table
        // has takes no block of its own — whatever either is, a plain index, a key or a relationship's: its logical
        // block names that one (verified vs ACE). Everything but the block's B-tree has to match.
        int shared = parts.DataBlocks.FindIndex(existing =>
        {
            byte[] keyOnly = [.. existing];
            TableDefinition.WriteIndexTree(keyOnly, format, rootPage: 0, usageRow: 0, usagePage: 0);
            return keyOnly.AsSpan().SequenceEqual(dataBlock);
        });

        // A plain index or an outgoing FK adds a logical block, and a data block unless it shares one. Checked
        // before a byte moves, like the duplicate-key scan above, so a rejection leaves the file exactly as it was.
        EnsureIndexCapacity(table.Name, $"index '{indexName}'", shared < 0 ? dataCount + 1 : dataCount, parts.Logical.Count + 1);

        // The new block takes the lowest free index_num.
        int newNum = NextLogicalIndexNumber(table.DefinitionPage);
        if (shared >= 0)
        {
            InsertLogicalBlock(parts, indexName, buildInfo(newNum, shared));
            WriteTdef(table.DefinitionPage, parts);
            _catalog.Invalidate();
            return newNum;
        }

        // Allocate the new index's root (empty leaf) and its usage-map row (appended after existing rows).
        int rootPage = AllocateIndexRoot(format, table.DefinitionPage);
        int usageMapPage = header.ReadRecordPointer(format.TdefOwnedPagesOffset).Page;
        // Where the new index's usage map goes: appended after the last row, as ACE does. A dropped index
        // leaves its row behind and ACE never hands it out again, so the row cannot be derived from the
        // table's shape — after a DROP INDEX that names a row already taken, on a table without long-value
        // columns another live index's (IndexUsageMapTests).
        //
        // When the primary page is full ACE does not squeeze the row in but spills, exactly as it does for
        // long-value columns: measured on a 40-memo table, CREATE INDEX leaves the full 57-row primary page
        // alone and puts the new index's map at row 0 of a page of its own (WideMemoUsageMapAccessTests).
        int newIndexUsageRow;
        if (UsageMap.RecordsFitting(format, DataPage.ReadFreeSpace(_channel.ReadPage(usageMapPage).Span, format)) < 1)
        {
            // A page of its own, its one row written empty.
            usageMapPage = _allocator.Allocate();
            _channel.WritePage(usageMapPage, UsageMap.NewMapPage(format, 1));
            newIndexUsageRow = 0;
        }
        else
        {
            // Appended, preserving every existing record. The data maps are empty on an empty table but the
            // *existing indexes'* maps already carry their root bits (set at creation), so the page must not be
            // rewritten from scratch even when the table has no rows.
            newIndexUsageRow = AppendEmptyUsageMapRow(format, usageMapPage);
        }

        // Record this index's own root, as Access does at CREATE INDEX (the empty root is the index's sole
        // page until it splits, after which IndexTree adds each page it allocates).
        new UsageMap(_channel).SetBit(newIndexUsageRow, usageMapPage, rootPage, set: true);

        // The new index adds a (zero) stats block and its data block after the existing ones, and a logical block.
        parts.Stats.Add(new byte[format.RealIndexEntrySize]);
        TableDefinition.WriteIndexTree(dataBlock, format, rootPage, newIndexUsageRow, usageMapPage);
        parts.DataBlocks.Add(dataBlock);
        InsertLogicalBlock(parts, indexName, buildInfo(newNum, dataCount));

        WriteTdef(table.DefinitionPage, parts);
        _catalog.Invalidate();

        // Back-fill the new (empty) index B-tree with an entry per existing row, so the index is complete.
        if (existingRowCount != 0)
            BackfillIndex(table, indexName, ignoreNulls, validateUnique: false);
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
    /// the same way via <see cref="IndexTree.KeyExists"/>.
    /// </remarks>
    private void EnsureExistingRowsFitIndex(
        TableDefinition table, string indexName, IReadOnlyList<(int Id, bool Ascending)> slots, bool unique, bool required)
    {
        var keyColumns = slots
            .Select(s => (Column: table.Columns.First(c => c.ColumnId == s.Id), s.Ascending))
            .ToArray();

        var seen = new HashSet<string>();
        var rows = new Table(_channel, table, _catalog);
        foreach (object?[] values in rows.Rows(rows.DecodeOnly(keyColumns.Select(k => k.Column.Index))))
        {
            if (keyColumns.Any(k => values[k.Column.Index] is null))
            {
                if (required)
                    throw new InvalidOperationException(
                        $"Index or primary key cannot contain a Null value: a row of '{table.Name}' has no value for index '{indexName}'.");
                continue;
            }
            if (unique && !seen.Add(Convert.ToHexString(IndexKeyCodec.Encode(keyColumns, values))))
                throw new InvalidOperationException(
                    $"Cannot create unique index '{indexName}' on '{table.Name}': duplicate key values exist.");
        }
    }

    /// <summary>Populates a freshly added index over a table's existing rows: encodes every live row's key,
    /// then hands the lot to <see cref="IndexTree.BulkBuild"/>, which sorts them and writes each B-tree page
    /// once. Rows with a null in an IGNORE NULL index's key are skipped, matching the per-insert path.</summary>
    /// <remarks>
    /// This used to insert the rows one at a time, which re-read, re-parsed and rebuilt a whole leaf page per
    /// row: an index over 10,000 rows cost ~440 ms and allocated over a gigabyte, nearly all of it leaves
    /// replaced by the next entry. Sorting first lets each leaf be filled and written once.
    /// <para>Uniqueness moves with it: on sorted keys a duplicate is an adjacent pair, so the per-row
    /// <see cref="IndexTree.KeyExists"/> descent is gone. The comparison is still on the encoded key, which
    /// is Access's uniqueness domain (see <see cref="EnsureExistingRowsFitIndex"/>).</para>
    /// <para>A built index's statistics are set as ACE sets them (verified, for CREATE INDEX, a foreign key's
    /// backing index and an index an ALTER COLUMN rebuilds): the total entry count to the entries it now holds and the
    /// unique entry count to its distinct keys — both from the rows present, not from any earlier history.</para>
    /// </remarks>
    private void BackfillIndex(TableDefinition definition, string indexName, bool ignoreNulls, bool validateUnique)
    {
        // The catalog's first index cannot resolve its own table until it has been back-filled.
        TableDefinition table = _catalog.ReadTableDefinition(definition.DefinitionPage, definition.Name, definition.IsSystem);
        IndexDef index = table.FindIndex(indexName)
            ?? throw new InvalidOperationException($"Index '{indexName}' was not found on '{table.Name}'.");
        List<(byte[] Key, int Pointer, bool NullKey)> entries = IndexEntries(table, index, ignoreNulls);

        new IndexTree(_channel, table).BulkBuild(index, entries, validateUnique && index.IsUnique);
        SetBuiltStatistics(table, index, entries);
    }

    /// <summary>The entries an index over the table's current rows holds: every live row's encoded key and row
    /// pointer, less the rows an IGNORE NULL index leaves out.</summary>
    private List<(byte[] Key, int Pointer, bool NullKey)> IndexEntries(TableDefinition table, IndexDef index, bool ignoreNulls)
    {
        var keyColumnIds = index.Columns.Select(c => c.Column.Index).ToArray();
        var entries = new List<(byte[] Key, int Pointer, bool NullKey)>();
        // The key is all an entry is made from, so it is all that is decoded: a wide row, or one whose memo the
        // index cannot even hold, would otherwise be read whole for every entry.
        var rows = new Table(_channel, table, _catalog);
        foreach ((RowId id, object?[] values) in rows.Rows(rows.DecodeOnly(keyColumnIds)).WithIds())
        {
            bool hasNullKey = keyColumnIds.Any(i => values[i] is null);
            if (ignoreNulls && hasNullKey) continue;
            entries.Add((IndexKeyCodec.Encode(index.Columns, values), id.Packed, hasNullKey));
        }
        return entries;
    }

    /// <summary>Sets a just-built index's statistics block as ACE sets it: total = the entries it holds, unique =
    /// its distinct keys among them.</summary>
    private void SetBuiltStatistics(TableDefinition table, IndexDef index, List<(byte[] Key, int Pointer, bool NullKey)> entries)
    {
        byte[] tdef = _channel.ReadPage(table.DefinitionPage).Span.ToArray();
        TableDefinition.WriteIndexCounts(tdef, _channel.Format, index.RealIndexOrdinal,
            entries.Count, entries.Select(e => Convert.ToHexString(e.Key)).Distinct().Count());
        _channel.WritePage(table.DefinitionPage, tdef);
    }

    /// <summary>Appends one empty inline usage-map record to an existing usage-map page, preserving every existing
    /// record, and returns its row. The new map tracks no pages yet, so an empty bitmap is correct.</summary>
    private int AppendEmptyUsageMapRow(JetFormatBase format, int pageNumber)
    {
        byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();

        // A record written below the directory would overwrite the slots themselves — which reads back later
        // as a slot with offset 0 "outside the row heap", a long way from the cause. The caller is expected
        // to spill onto a fresh page rather than get here; this is the backstop that keeps a mistake loud.
        // An all-zero record is an empty inline map: inline type, start page 0, zero bitmap.
        if (!DataPage.TryAppendRow(page, format, new byte[format.UsageMapInlineRecordSize], RowSlotFlags.None, out int row))
            throw new InvalidOperationException(
                $"Usage-map page {pageNumber} has no room for another record: {DataPage.ReadRowCount(page, format)} rows "
                + $"already reach offset {DataPage.LowestRowOffset(page, format)}. The new map belongs on a page of its own.");
        _channel.WritePage(pageNumber, page);
        return row;
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
        TableDefinition child = DesignTable(childTable);
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, child.Name);
        EnsureRelationshipNameFree(fk.Name);
        if (fk.NoIndex)
            throw new NotSupportedException("ALTER TABLE ADD FOREIGN KEY … NO INDEX is not supported yet.");
        if (fk.UpdateSetNull) throw UpdateSetNullNotImplemented();

        RejectOleIndexColumns(fk.Columns.Select(c => c.Column), n => child.FindColumn(n)?.Type);
        fk = ResolvePrimaryKeyReference(fk, creatingTable: null);
        EnsureSameDataTypes(fk, ColumnOf(child), ColumnOf(_catalog.FindTable(fk.ReferencedTable)));

        // ACE refuses a relationship the existing rows already break — an orphan, or a partly null composite key
        // (verified; the ON DELETE action makes no difference).
        if (fk.IsEnforced && DesignTable(fk.ReferencedTable) is var parent
            && !ExistingRowsHaveParents(child.Name, parent.Name, fk.Columns))
            throw new InvalidOperationException(
                "Cannot create relationships to enforce referential integrity. Existing data in table "
                + $"'{child.Name}' violates referential integrity rules in table '{parent.Name}'.");

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

        (RelationshipAction upd, RelationshipAction del) = Actions(fk);
        var slots = ResolveSlots(child, fk.Columns.Select(c => (c.Column, Ascending: true)));

        // A self-reference (child == parent) hosts both ends in the same TDEF: the outgoing block takes the
        // lowest free number and the incoming block the next free one above it (verified vs ACE: with 1 free
        // below a live 2, the outgoing block is 1 and the incoming 3).
        (int parentPage, int refOrdinal, int parentNum) =
            string.Equals(fk.ReferencedTable, childTable, StringComparison.OrdinalIgnoreCase)
                ? (child.DefinitionPage, ReferencedOrdinalIn(child, fk),
                    NextLogicalIndexNumber(child.DefinitionPage, above: NextLogicalIndexNumber(child.DefinitionPage)))
                : ResolveParent(fk, child.DefinitionPage);
        int childBlockNum = InsertIndex(child, fk.Name, slots,
            unique: false, required: false, ignoreNulls: false,
            (num, ord) => new TableDefinition.LogicalIndexSpec(num, ord, ForeignKeyType.Outgoing, (uint)parentNum,
                parentPage, upd, del, IndexInfoType.Foreign, fk.Name));

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

        TableDefinition child = DesignTable(childTable);
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, child.Name);
        IndexDef? fkIndex = child.FindIndex(name);
        bool selfRef = string.Equals(rel.ReferencedTable, childTable, StringComparison.OrdinalIgnoreCase);

        int? childBlockNum = null;
        var owned = new HashSet<int>();
        var retire = new List<MapRetirement>();
        var maps = new UsageMap(_channel, child);
        if (fkIndex is not null)
        {
            TableDefinition.Parts childParts = TableDefinition.ReadParts(_channel, child.DefinitionPage);
            int outgoing = OutgoingBlockNumber(childParts, name);
            childBlockNum = outgoing;

            // Remove the FK index (data ordinal) + its outgoing info block from the child, plus — for a
            // self-reference — the incoming block, which also lives here. The FK's backing index is an index like
            // any other: its B-tree is released whole and its usage-map record retired, unless another index
            // shares its data block and keeps it.
            if (TableDefinition.RemoveIndexBlocks(_channel.Format, childParts, removeDataOrdinal: fkIndex.RealIndexOrdinal, removeLogical: b =>
                    NameOf(b.Name).Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    (selfRef && IsIncomingBlockFor(b, outgoing, child.DefinitionPage))))
                QueueIndexMap(fkIndex, maps, owned, retire);
            WriteTdef(child.DefinitionPage, childParts);
        }

        DetachRelationship(rel, child, childBlockNum);

        RetireMapRecords(retire, maps, owned);
        var allocator = _channel.Allocator;
        foreach (int page in owned)
            allocator.Release(page);
        _catalog.Invalidate();
        return true;
    }

    /// <summary>The number of the outgoing relationship block named <paramref name="name"/> in a child's
    /// definition — what the parent's incoming block cross-links to.</summary>
    private int OutgoingBlockNumber(TableDefinition.Parts childParts, string name)
    {
        (byte[] Info, byte[] Name) outgoing =
            childParts.Logical.First(b => NameOf(b.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
        return TableDefinition.ReadInfoBlock(outgoing.Info, _channel.Format, name).Number;
    }

    /// <summary>
    /// What removing relationship <paramref name="rel"/> takes from outside the child's own definition, for both a
    /// <c>DROP CONSTRAINT</c> and a <c>DROP TABLE</c> of the child: the parent's incoming block (where the
    /// relationship is enforced, <paramref name="childBlockNum"/> naming the child's outgoing block, and the
    /// parent is another table), its <c>MSysRelationships</c> rows, and its <c>MSysObjects</c> object and
    /// permission rows (verified: ACE removes them; a relationship written before LibRed recorded the object has
    /// none).
    /// </summary>
    private void DetachRelationship(ForeignKey rel, TableDefinition child, int? childBlockNum)
    {
        if (childBlockNum is { } number
            && !string.Equals(rel.ReferencedTable, child.Name, StringComparison.OrdinalIgnoreCase))
        {
            TableDefinition parent = _catalog.FindTable(rel.ReferencedTable)
                ?? throw new InvalidOperationException($"Table '{rel.ReferencedTable}' was not found.");
            TableDefinition.Parts parentParts = TableDefinition.ReadParts(_channel, parent.DefinitionPage);
            TableDefinition.RemoveIndexBlocks(_channel.Format, parentParts, removeDataOrdinal: null, removeLogical: b =>
                IsIncomingBlockFor(b, number, child.DefinitionPage));
            WriteTdef(parent.DefinitionPage, parentParts);
        }

        DeleteRelationshipRows(rel.Name);
        DeleteObject(rel.Name, ObjectType.Relationship);
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
    /// (via <see cref="DetachRelationship"/>). But a table still <em>referenced as a parent</em> by a surviving
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
        TableDefinition? table = _catalog.FindTable(tableName);
        if (table is null) return false;
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, table.Name);

        // The relationships this table owns as the child (referencing) side, each with its outgoing block's number
        // where it is enforced. They are detached once the table itself is gone, below.
        TableDefinition.Parts ownParts = TableDefinition.ReadParts(_channel, table.DefinitionPage);
        var ownRelationships = _catalog.Relationships
            .Where(r => string.Equals(r.Table, tableName, StringComparison.OrdinalIgnoreCase))
            .Select(r => (Relationship: r, Block: table.FindIndex(r.Name) is not null
                ? OutgoingBlockNumber(ownParts, r.Name)
                : (int?)null))
            .ToList();

        // A table still referenced by a surviving child (as the parent) cannot be dropped. One it references
        // itself is its own, and goes with it.
        if (_catalog.Relationships.Any(r =>
                string.Equals(r.ReferencedTable, tableName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.Table, tableName, StringComparison.OrdinalIgnoreCase)))
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

        // Re-fetch: dropping a complex column's flat table invalidates the catalog.
        table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");

        int tdefPage = table.DefinitionPage;
        var allocator = _channel.Allocator;
        var maps = new UsageMap(_channel, table);

        // Collect before freeing anything: the pointers are read out of the TDEF, which this method frees.
        var owned = new HashSet<int>();
        // Through the chain reader: a wide table's definition spans continuation pages, and parsing only the
        // first one throws on the declared length. (The map POINTERS below are at fixed offsets inside the
        // first page, so those are read from it directly, as UsageMap does.)
        var definition = new TableDefinition();
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

        // Then each real index's whole B-tree and its own map, in index order.
        foreach (IndexDef index in table.Indexes.GroupBy(i => i.RealIndexOrdinal).Select(g => g.First()))
            QueueIndexMap(index, maps, owned, retire);
        List<int> dataPages = maps.DataPages().ToList();
        owned.UnionWith(dataPages);
        owned.Add(tdefPage);
        // A wide table's definition continues on further pages. ACE frees them too, and leaves every byte of
        // them alone — only the first page is marked released.
        owned.UnionWith(TableDefinition.ReadChain(_channel, tdefPage).ContinuationPages);

        PageBuffer tdef = _channel.ReadPage(tdefPage);
        (int Row, int Page) dataOwned = tdef.ReadRecordPointer(_channel.Format.TdefOwnedPagesOffset);
        retire.Add((dataOwned, [dataOwned], dataPages));
        retire.Add((tdef.ReadRecordPointer(_channel.Format.TdefFreePagesOffset), [], []));

        RetireMapRecords(retire, maps, owned);

        // Access marks the released definition page itself: its type becomes 0x0108 and nothing else on
        // the page changes, so the old definition is still sitting there when Compact comes to reclaim it.
        // Measured across an ACE DROP TABLE: exactly one byte of the 4,096 differs, the type's low byte. Only
        // the TDEF is marked — the data, long-value and map-holder pages ACE frees keep their 0x0101.
        byte[] released = _channel.ReadPage(tdefPage).Span.ToArray();
        TableDefinition.MarkReleased(released);
        _channel.WritePage(tdefPage, released);

        foreach (int page in owned)
            allocator.Release(page);   // reusable only after this handle closes, as ACE holds them

        // Its CHECKs' objects go before its own row (measured, whole-file diff: dropping a table with one, ACE left
        // MSysObjects' page as deleting the check's row first leaves it, nothing slid).
        foreach ((string checkName, _) in table.CheckConstraints)
            DeleteObject(checkName, ObjectType.CheckConstraint);
        DeleteCatalogRows("MSysObjects", "Id", tdefPage);
        if (!keepPermissions) DeleteCatalogRows("MSysACEs", "ObjectId", tdefPage);

        // Then the relationships it owned as the child go — from the parent and the catalog, but not from this
        // table's own definition, which ACE leaves as it was apart from the released type; the foreign key's index
        // went with the other indexes above. Measured by whole-file diff (WholeFileParityProbeTest, DROP TABLE
        // Child): removing the foreign key's blocks first rewrote the TDEF where ACE changed one byte of it, and
        // detaching it before the table's own catalog rows left MSysObjects' data and index pages holding the
        // stale bytes of the other order.
        foreach ((ForeignKey relationship, int? block) in ownRelationships)
            DetachRelationship(relationship, table, block);
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
        TableDefinition definition, ColumnDef column, UsageMap maps, HashSet<int> owned, List<MapRetirement> retire)
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
    /// Queues what dropping <paramref name="index"/> releases — the whole B-tree, not just its root — and its own
    /// usage map's record for <see cref="RetireMapRecords"/>. Every page of a split index but the root is recorded
    /// only in that map, so releasing the root alone strands them, owned by nothing and never handed out again; and
    /// a map record left live keeps its holder from ever being freed. ACE frees the lot and takes the record off its
    /// holder as well (measured by whole-file diff: the bits cleared, the row tombstoned, the holder's free space
    /// back). Read from the index as the catalog has it, so before its data block goes. A zero root is a foreign
    /// key's index whose root is not allocated yet (see <see cref="TableDefinition.RealIndexes"/>), and has none to free.
    /// </summary>
    private static void QueueIndexMap(IndexDef index, UsageMap maps, HashSet<int> owned, List<MapRetirement> retire)
    {
        if (index.RootPage > 0) owned.Add(index.RootPage);
        if (index.UsageMap.Page == 0) return;
        List<int> pages = maps.PagesInMap(index.UsageMap.Row, index.UsageMap.Page).ToList();
        owned.UnionWith(pages);
        retire.Add((index.UsageMap, [index.UsageMap], pages));
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
        var usageMaps = new UsageMap(_channel);
        var holders = new List<int>();
        var retired = new HashSet<(int Row, int Page)>();
        foreach (((int Row, int Page) map, IReadOnlyList<(int Row, int Page)> clear, IReadOnlyList<int> pages) in retire)
        {
            if (map.Page <= 1 || map.Page >= _channel.PageCount || !retired.Add(map)) continue;
            foreach ((int Row, int Page) cleared in clear)
                foreach (int page in pages)
                    usageMaps.SetBit(cleared.Row, cleared.Page, page, set: false);

            // A reference-form record keeps its bitmap on dedicated pages, which go with it, zeroed.
            owned.UnionWith(usageMaps.ClearBitmapPages(maps.ReadRecordAt(map.Row, map.Page)));

            // In a user transaction ACE takes the record's bytes back only when it commits: until then its slot is
            // flagged and a record written meanwhile goes below it, to slide up at the commit (measured: a CREATE
            // INDEX after a DROP INDEX in one transaction leaves the new record's first image in the holder's free
            // space). A statement on its own takes them back at once.
            byte[] holderBytes = _channel.ReadPage(map.Page).Span.ToArray();
            DataPage.WriteSlot(holderBytes, _channel.Format, map.Row,
                DataPage.ReadSlot(holderBytes, _channel.Format, map.Row).Offset, RowSlotFlags.Deleted);
            _channel.WritePage(map.Page, holderBytes);
            _channel.ReclaimAtCommit(map.Page, map.Row);
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
    /// on a procedure and vice versa), so this handles either. The inverse of <c>JetCatalog</c>: deletes the
    /// object's MSysObjects row, its MSysQueries rows, and its two MSysACEs permission rows (index entries
    /// removed, not just soft-deleted). No pages to free (a query owns none — its MSysQueries rows live on the
    /// shared MSysQueries pages). Returns false if no such query object exists.
    /// </summary>
    public bool DropQueryObject(string name)
    {
        if (_catalog.FindObjectId(name, ObjectType.Query) is not { } objId) return false;
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, name);

        DeleteCatalogRows("MSysObjects", "Id", objId);
        DeleteCatalogRows("MSysQueries", "ObjectId", objId);
        DeleteCatalogRows("MSysACEs", "ObjectId", objId);
        _catalog.Invalidate();
        return true;
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
        TableDefinition? table = _catalog.FindTable(oldName);
        if (table is null) return false;
        // The same names Create refuses. A rename reaches the identical bytes by a different route, so
        // validating only on the way in left it open: renaming a COLUMN to over 64 characters makes the
        // whole database unreadable to ACE ("Unrecognized database format"), which is exactly what the
        // create-side check exists to prevent (RenameNameValidationAccessTests).
        JetName.Validate(newName, "table name");
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, table.Name);
        // The table being renamed is not a collision with itself: renaming to the same name is a no-op that ACE
        // allows (and EF's schema "move" degrades to exactly that on a schema-less engine), as is a case-only
        // change. Both verified — RenameFanOutProbeTest.
        if (ObjectNameExists(newName, exceptObjectId: table.DefinitionPage))
            throw new SchemaObjectExistsException(
                $"ALTER TABLE '{oldName}' RENAME TO '{newName}': a table or query named '{newName}' already exists.",
                newName);

        // The object's MSysObjects row — its Id is the table's TDEF page.
        UpdateCatalogRows("MSysObjects", "Id", table.DefinitionPage, required: true, ("Name", newName));
        RepointRelationships(columnOf: null, oldName, newName);
        return true;
    }

    /// <summary>Repoints every relationship that names a renamed table — or, with <paramref name="columnOf"/>, a
    /// renamed column of that table — on whichever side names it. Both sides are stored by name: the child
    /// (<c>szObject</c>, <c>szColumn</c>) and the parent (<c>szReferencedObject</c>, <c>szReferencedColumn</c>).
    /// A self-reference names the table twice, hence both sides in one pass over each row; and a column name is
    /// only unique within its table, so a side is matched on its table as well.</summary>
    private void RepointRelationships(string? columnOf, string oldName, string newName)
    {
        TableDefinition? def = _catalog.FindTable("MSysRelationships");
        if (def is null) return; // a database with no relationships has no catalog table to fix up

        (int Table, int Column)[] sides =
        [
            (def.RequireColumn("szObject").Index, def.RequireColumn("szColumn").Index),
            (def.RequireColumn("szReferencedObject").Index, def.RequireColumn("szReferencedColumn").Index),
        ];
        bool Names(object?[] v, (int Table, int Column) side) => columnOf is null
            ? NameMatches(v[side.Table], oldName)
            : NameMatches(v[side.Table], columnOf) && NameMatches(v[side.Column], oldName);
        var table = new Table(_channel, def, _catalog);

        foreach ((RowId id, object?[] values) in table.RowsWhere([.. sides.SelectMany(s => new[] { s.Table, s.Column })],
            v => sides.Any(s => Names(v, s))).ToList())
            SetCatalogValues(table, def, id, values, [.. sides.Where(s => Names(values, s))
                .Select(s => (columnOf is null ? s.Table : s.Column, (object?)newName))]);
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
        TableDefinition table = DesignTable(tableName);
        ColumnDef? col = table.Columns.FirstOrDefault(c => string.Equals(c.Name, oldName, StringComparison.OrdinalIgnoreCase));
        if (col is null) return false;
        JetName.Validate(newName, "column name");   // see RenameTable: unchecked, this corrupts the file
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, table.Name);
        if (table.Columns.Any(c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"ALTER TABLE '{tableName}' RENAME COLUMN '{oldName}' TO '{newName}': the table already has a column named '{newName}'.");

        // A complex column's catalog row names its column by NAME, and the catalog will not resolve a row
        // whose name the owning table no longer has — so a rename that left the row behind did not merely
        // misname the column, it disconnected it from its values. Read before the TDEF is rewritten, while
        // the catalog still resolves the old name.
        int? complexId = _catalog.FindComplexColumn(tableName, oldName)?.ComplexId;

        TableDefinition.Parts parts = TableDefinition.ReadParts(_channel, table.DefinitionPage); // stitches continuation pages for a multi-page TDEF
        // Only the name entry changes; the descriptor stays as it is, and WriteTdef re-lays the names after it.
        parts.Columns[col.Index] = (parts.Columns[col.Index].Descriptor, TableDefinition.NameEntry(newName, _channel.Format));
        WriteTdef(table.DefinitionPage, parts);
        RenameColumnProperties(table.DefinitionPage, oldName, newName);
        RepointRelationships(columnOf: tableName, oldName, newName);
        if (complexId is int id) SetComplexColumnName(id, newName);
        _catalog.Invalidate();
        return true;
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

    /// <summary>Replaces an object's extended-property blob with <paramref name="properties"/>. Not an
    /// <see cref="UpdateCatalogRows"/> call, because <c>LvProp</c> is a long value: the blob has to be stored first
    /// — inline or on a page — and the row given the descriptor for it.</summary>
    private void WriteObjectProperties(int objectId, byte[] properties)
    {
        (TableDefinition msys, Table table, int idIdx, ColumnDef lvProp) = ObjectProperties();
        foreach ((RowId id, object?[] values) in RowsKeyed(table, idIdx, objectId))
        {
            values[lvProp.Index] = new RowInserter(_channel, msys).StorePackedLongValue(lvProp.ColumnId, properties);
            table.Update(id, values, new HashSet<int> { lvProp.Index });
            return;
        }
        throw new InvalidOperationException($"MSysObjects row for object {objectId} was not found.");
    }

    /// <summary>The <c>LvProp</c> property holding an object's copy of its Name AutoCorrect map.</summary>
    private const string NameMapProperty = "NameMap";

    /// <summary>Every <c>MSysNameMap</c> row, its map decoded; empty when the file has no such table.</summary>
    public IReadOnlyList<NameMap.CatalogRow> ReadNameMapRows()
    {
        if (_catalog.FindTable("MSysNameMap") is not { } def) return [];
        int guid = def.RequireColumn("GUID").Index, id = def.RequireColumn("Id").Index,
            name = def.RequireColumn("Name").Index, type = def.RequireColumn("Type").Index,
            map = def.RequireColumn("NameMap").Index;
        return [.. new Table(_channel, def, _catalog).Rows().Select(r => new NameMap.CatalogRow(
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
        var table = new Table(_channel, def, _catalog);

        foreach ((RowId id, object?[] values) in table.RowsWhere([guidIndex], v => v[guidIndex] is Guid g && g == objectGuid).ToList())
        {
            _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, (values[nameIndex] as string)?.TrimEnd('\0'));
            // A long value, like LvProp: stored first, the row given the descriptor (see WriteObjectProperties).
            values[mapColumn.Index] = new RowInserter(_channel, def).StorePackedLongValue(mapColumn.ColumnId, map.WriteRow());
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
    public NameMap? ReadNameMapProperty(string objectName, ObjectType objectType)
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
    public void WriteNameMapProperty(string objectName, ObjectType objectType, NameMap? map)
    {
        int objectId = RequireObjectId(objectName, objectType);
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, objectName);
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
    private int RequireObjectId(string name, ObjectType type) =>
        _catalog.FindObjectId(name, type)
        ?? throw new InvalidOperationException($"There is no object '{name}' of type {type}.");

    /// <summary><c>MSysObjects</c> and the two columns every extended-property path works through: the
    /// <c>Id</c> it matches an object by, and the <c>LvProp</c> holding the blob.</summary>
    private (TableDefinition Definition, Table Table, int IdIndex, ColumnDef LvProp) ObjectProperties()
    {
        TableDefinition msys = _catalog.RequireTable("MSysObjects");
        return (msys, new Table(_channel, msys, _catalog), msys.RequireColumn("Id").Index, msys.RequireColumn("LvProp"));
    }

    /// <summary>The calculated columns of <paramref name="table"/> whose expression reads
    /// <paramref name="column"/>. A malformed expression counts as reading nothing rather than throwing:
    /// refusing to drop is a safeguard, and it must not turn into a refusal to drop anything at all because
    /// some other column's expression cannot be parsed.</summary>
    private static List<string> CalculatedColumnsReading(TableDefinition table, ColumnDef column)
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
            catch (Storage.Calculated.CalculatedExpressionException) { /* unparseable: reads nothing we can prove */ }
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
            string rewritten = Storage.Calculated.CalculatedExpression.RenameColumnReference(
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

    /// <summary>
    /// Whether a <b>table, saved query or linked table</b> already uses this name — the objects of the Tables
    /// container, whose names MSysObjects' unique <c>(ParentId, Name)</c> index keeps distinct. That is ACE's rule
    /// for a new or renamed table (measured, and <c>RenameFanOutProbeTest</c> for renames): a form, report, macro,
    /// module, relationship or database document of the same name does not collide. Found by the catalog's
    /// <c>(ParentId, Name)</c> lookup on the MSysObjects row itself, so a linked table or a query LibRed can't
    /// rebuild still counts.
    /// </summary>
    private bool ObjectNameExists(string name, int exceptObjectId)
    {
        int idIndex = _catalog.RequireTable("MSysObjects").RequireColumn("Id").Index;
        // Skip the object being renamed — it can't collide with itself (same-name and case-only renames).
        return _catalog.FindObjectRow(JetCatalog.ObjectContainerParentId, name) is { } row
            && (row[idIndex] is null || Convert.ToInt32(row[idIndex], CultureInfo.InvariantCulture) != exceptObjectId);
    }

    private static bool NameMatches(object? value, string name) =>
        value is string s && string.Equals(s, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rewrites some columns of one catalog row, keeping any index whose key covers a changed column in
    /// step — MSysObjects is uniquely indexed on (ParentId, Name), so a rename has to move that entry rather
    /// than just overwrite the value.</summary>
    private static void SetCatalogValues(
        Table table, TableDefinition def, RowId id, object?[] values, params (int Column, object? Value)[] updates)
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

        new RowInserter(table.Channel, def).Update(id, newValues, changed);
    }

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
    internal void UpdateCatalogRows(string catalogTable, string keyColumn, int keyValue,
        bool required, params (string Column, object? Value)[] updates)
    {
        TableDefinition? t = _catalog.FindTable(catalogTable);
        if (t is null)
        {
            if (!required) return;
            throw new InvalidOperationException($"{catalogTable} catalog table was not found.");
        }

        int key = t.RequireColumn(keyColumn).Index;
        var columns = updates.Select(u => (Column: t.RequireColumn(u.Column).Index, u.Value)).ToArray();
        var table = new Table(_channel, t, _catalog);

        foreach ((RowId id, object?[] values) in RowsKeyed(table, key, keyValue))
            SetCatalogValues(table, t, id, values, [.. columns]);
    }

    /// <summary>The rows of a catalog table whose key column holds <paramref name="keyValue"/>, materialised
    /// before the caller writes any of them back.</summary>
    private static List<(RowId Id, object?[] Values)> RowsKeyed(Table table, int keyColumn, int keyValue) =>
        [.. table.RowsWhere([keyColumn], values => values[keyColumn] is not null
            && Convert.ToInt32(values[keyColumn], CultureInfo.InvariantCulture) == keyValue)];

    /// <summary>Deletes every row of <paramref name="catalogTable"/> whose <paramref name="keyColumn"/> equals
    /// <paramref name="keyValue"/> (the object id) — used to remove a dropped table's MSysObjects and MSysACEs
    /// rows. A full delete: its <b>index entries are removed</b> (not just the slot soft-deleted) so, e.g., the
    /// MSysObjects <c>ParentIdName</c> unique index doesn't retain a stale entry that would then reject
    /// re-creating a same-named table.</summary>
    /// <remarks>The rows go in the order of the table's index led by the key column — key, then row pointer, as
    /// the index holds them — which is the order ACE reaches and deletes them in (measured by whole-file diff,
    /// <c>WholeFileParityProbeTest</c>: dropping a stored query, ACE deletes its MSysQueries rows in
    /// <c>(Attribute, Order)</c> order, whatever order they were written in, and each delete's slide shows it).
    /// Where the key's ties fall back on the pointer, as MSysACEs' <c>ObjectId</c> index does, that is the scan's
    /// own order.</remarks>
    private void DeleteCatalogRows(string catalogTable, string keyColumn, int keyValue)
    {
        TableDefinition t = _catalog.RequireTable(catalogTable);
        int idx = t.RequireColumn(keyColumn).Index;
        var table = new Table(_channel, t, _catalog);

        List<(RowId Id, object?[] Values)> rows = RowsKeyed(table, idx, keyValue);
        if (t.RealIndexes.FirstOrDefault(i => i.Columns.Count > 0 && i.Columns[0].Column.Index == idx) is { } byKey)
            rows = [.. rows.OrderBy(
                r => IndexTree.WithTrailer(IndexKeyCodec.Encode(byKey.Columns, r.Values), r.Id.Packed),
                Comparer<byte[]>.Create((a, b) => IndexTree.CompareBytes(a, b)))];
        foreach ((RowId id, object?[] values) in rows)
        {
            foreach (IndexDef index in t.RealIndexes)
                table.RemoveIndexEntry(index, values, id);
            new RowInserter(table.Channel, t).Delete(id);
        }
    }

    /// <summary>Follows a renamed complex column in its <c>MSysComplexColumns</c> row. The catalog matches the
    /// row to a column by name, so a stale one stops resolving altogether — the column keeps its values on
    /// disk and nothing can reach them.</summary>
    private void SetComplexColumnName(int complexId, string columnName) =>
        UpdateCatalogRows("MSysComplexColumns", "ComplexID", complexId, required: true, ("ColumnName", columnName));

    /// <summary>
    /// Drops a secondary/unique/primary index — <c>DROP INDEX index ON table</c>. Byte-faithful with ACE
    /// (probed): remove the index's statistics entry, index-data block, and its logical info block + name from
    /// the TDEF (decrementing counts and the data-ordinal ref of any block past it),
    /// and free its B-tree root page back to the global free map — the same index-removal path as DROP
    /// CONSTRAINT, minus the relationship linkage. A secondary index lives only in the TDEF (no MSys row).
    /// Returns false if no such index exists. Throws if the index backs a relationship (ACE rejects that —
    /// drop the relationship first). A multi-page TDEF is handled, and PK and unique indexes ARE droppable.
    /// </summary>
    public bool DropIndex(string tableName, string indexName)
    {
        TableDefinition table = DesignTable(tableName);
        IndexDef? index = table.FindIndex(indexName);
        if (index is null) return false;
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, table.Name);

        if (IndexParticipatesInRelationship(table, index, indexName))
            throw new InvalidOperationException(
                $"Cannot drop index '{indexName}': it is used in a relationship — drop the relationship first.");

        TableDefinition.Parts parts = TableDefinition.ReadParts(_channel, table.DefinitionPage); // stitches continuation pages for a multi-page TDEF

        var owned = new HashSet<int>();
        var retire = new List<MapRetirement>();
        var maps = new UsageMap(_channel, table);
        // Its B-tree goes with its data block, which another index sharing it keeps.
        if (TableDefinition.RemoveIndexBlocks(_channel.Format, parts, removeDataOrdinal: index.RealIndexOrdinal,
                removeLogical: b => NameOf(b.Name).Equals(indexName, StringComparison.OrdinalIgnoreCase)))
            QueueIndexMap(index, maps, owned, retire);
        WriteTdef(table.DefinitionPage, parts);

        RetireMapRecords(retire, maps, owned);
        var allocator = _channel.Allocator;
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
    /// unique/primary index over the referenced columns. <paramref name="name"/> is the one being dropped, which
    /// on a data block several logical indexes share need not be the name <paramref name="index"/> carries.</para></summary>
    private bool IndexParticipatesInRelationship(TableDefinition table, IndexDef index, string name)
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
                && string.Equals(name, r.Name, StringComparison.OrdinalIgnoreCase)) ||
            (string.Equals(r.ReferencedTable, table.Name, StringComparison.OrdinalIgnoreCase)
                && (index.IsUnique || index.IsPrimaryKey)
                && SameCols(r.Columns.Select(c => c.ReferencedColumn))));
    }

    /// <summary>
    /// Adds a column — <c>ALTER TABLE t ADD COLUMN c type</c>. A metadata TDEF edit (probed vs ACE, the
    /// inverse of DROP COLUMN): appends the column's descriptor + name, gives it the next column id
    /// from the <c>0x29</c> max-columns high-water (which keeps counting even past dropped ids), appends its
    /// fixed offset (end of the fixed region; 0 for a Yes/No, which has no fixed bytes) or variable index (current
    /// variable count), and bumps
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
        TableDefinition table = DesignTable(tableName);
        // A taken column name included (verified vs ACE).
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, table.Name);
        if (table.Columns.Any(c => string.Equals(c.Name, spec.Name, StringComparison.OrdinalIgnoreCase)))
            return false;
        JetFormatBase format = _channel.Format;
        if (table.Columns.Count >= format.MaxColumnsPerTable)
            throw new NotSupportedException($"Table '{tableName}' already has {format.MaxColumnsPerTable} columns, the most a table can have.");
        // As on the create path, a calculated column with a Memo RESULT needs the long-value maps even though
        // its declared type is Text — the value reaches a page through a descriptor either way (§3.4a).
        bool isLongValue = spec.Type is JetDataType.Memo or JetDataType.Ole
                           || spec.CalculatedResultType is JetDataType.Memo;
        TableDefinition.Parts parts = TableDefinition.ReadParts(_channel, table.DefinitionPage); // stitches continuation pages for a multi-page TDEF
        int colCount = parts.Columns.Count;

        // Even when the *live* count is under the cap (the guard above), the id space may not be. This only bumps
        // the high-waters in `parts`, so a refusal below still writes nothing.
        (int columnId, int varCount) = TableDefinition.TakeColumnId(parts, spec.IsFixedLength,
            $"Cannot add column '{spec.Name}' to '{tableName}'", format);

        // The width limits Create enforces through TableDefinition apply just as much to a column added later:
        // the new column's own width, and what it does to the widest record the table can now hold.
        int fixedEnd = RowCodec.ComputeFixedDataLength(table.Columns);
        RowCodec.ValidateFieldWidth(spec.Name, spec.Type, spec.Length);
        TableDefinition.ValidateNumericPrecision(spec);
        RowCodec.ValidateRecordFits(tableName,
            fixedEnd + (spec.OccupiesFixedData ? spec.Length : 0),
            varCount + (spec.IsFixedLength ? 0 : 1),
            columnId + 1,
            format);

        // Same single-counter rule the CREATE path and the promote path enforce; ADD COLUMN had neither. The
        // rule is about the header's seed/increment pair, so complex columns — flagged 0x04 but allocated from
        // 0x1C — are neither the existing counter nor a conflicting one.
        if (spec.IsAutoNumber && spec.Type != JetDataType.Complex
            && table.Columns.Any(c => c.IsAutoNumber && c.Type != JetDataType.Complex))
            throw new NotSupportedException(
                $"Cannot add AutoNumber column '{spec.Name}': table '{table.Name}' already has one, and a table "
                + "can have only one.");

        if (spec.ComplexTemplate is not null) spec = spec with { ComplexId = NextComplexId() };
        var newColumn = new ColumnDef
        {
            Name = spec.Name,
            Type = spec.Type,
            Index = colCount,
            ColumnId = columnId, // next id from the high-water (dropped ids are never reused)
            Length = spec.Length,
            FixedOffset = spec.OccupiesFixedData ? fixedEnd : 0,
            VariableIndex = spec.IsFixedLength ? -1 : varCount,
            // Descriptor 0x07. A VARIABLE column carries its own variable index, which is the 0x2B high-water
            // (measured vs ACE in VariableColumnHighWaterAccessTests — NOT the count of live variable columns,
            // which is lower once one has been dropped), so leave it unset and let TableDefinition use VariableIndex.
            // A FIXED column carries the same high-water: every variable column ever given a slot has a smaller
            // id than the one being added, the dropped ones included (verified vs ACE: a LONG added after a
            // TEXT was dropped takes 2 where two TEXT columns had been, one of them gone).
            VariableTableIndex = spec.IsFixedLength ? varCount : -1,
            IsFixedLength = spec.IsFixedLength,
            IsAutoNumber = spec.IsAutoNumber,
            // As CREATE TABLE stamps it: a declared 0 is ACE's 18, because a 0 on disk leaves a column ACE cannot read.
            Precision = TableDefinition.EffectivePrecision(spec),
            Scale = spec.Scale,
            IsNullable = spec.IsNullable,
            // WITH COMPRESSION: the capable flag ACE sets for a column declared so, on the SQL route as on CREATE TABLE.
            SupportsCompressedUnicode = spec.SupportsCompressedUnicode,
            Collation = spec.Collation ?? (spec.Type == JetDataType.FixedPoint ? Collation.GeneralLegacy : _collation),
            ComplexId = spec.ComplexId,
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
            Span<byte> ordinal = parts.Columns[i].Descriptor.AsSpan(format.ColumnSecondaryNumberOffset, 2);
            int value = BinaryPrimitives.ReadUInt16LittleEndian(ordinal);
            if (i == 0 || value != previous) rank++;
            previous = value;
            BinaryPrimitives.WriteUInt16LittleEndian(ordinal, (ushort)rank);
        }
        byte[] descriptor = TableDefinition.BuildColumnDescriptor(newColumn, format);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(format.ColumnSecondaryNumberOffset, 2), (ushort)(rank + 1));
        parts.Columns.Add((descriptor, TableDefinition.NameEntry(spec.Name, format)));

        LongValueMapPlacement? lvMaps = isLongValue ? PlaceLongValueMaps(parts, columnId) : null;

        // NOT NULL / DEFAULT go in the table's LvProp blob (DefaultValue before Required, matching ACE), the
        // same properties CREATE TABLE writes — appended to the existing blob without disturbing other columns'.
        // Built before anything is written, since a default can be refused.
        var props = new List<PropertyBlob.Property>();
        if (defaultValue is not null) props.Add(PropertyBlob.DefaultValue(spec.Name, spec.Type, defaultValue));
        if (!spec.IsNullable) props.Add(PropertyBlob.Bool(spec.Name, PropertyBlob.RequiredProperty, true));
        props.AddRange(CalculatedProperties(spec));

        RaiseFormatFor([spec]);
        WriteTdef(table.DefinitionPage, parts);

        if (lvMaps is { } placed) WriteLongValueMaps(placed);
        if (props.Count > 0) SetColumnProperties(table.DefinitionPage, spec.Name, props);

        _catalog.Invalidate();
        if (spec.IsAutoNumber) NumberExistingRows(tableName, spec);
        if (spec.ComplexTemplate is not null)
        {
            UpdateCatalogRows("MSysObjects", "Id", table.DefinitionPage, required: true,
                ("Flags", unchecked((int)(table.ObjectFlags | ObjectAttributes.OwnsComplexColumns))));
            AddComplexStorage(table.Name, spec);
        }
        return true;
    }

    /// <summary>Where a column's two long-value usage-map records go: a page and its owned and free rows, on a page
    /// of their own when the table's map page is full.</summary>
    private readonly record struct LongValueMapPlacement(int Page, int UsedRow, int FreeRow, bool Dedicated);

    /// <summary>
    /// Gives a column that has become Memo/OLE its §3.3.2 usage-map entry in <paramref name="parts"/> and says where
    /// its owned and free map records go — for <see cref="WriteLongValueMaps"/> to write once the TDEF is. ACE
    /// appends the two maps to the table's existing usage-map page right after the maps already there (verified,
    /// for ADD COLUMN and for an ALTER COLUMN to Memo alike), and adds the column's long-value map entry before the
    /// list's terminator.
    /// </summary>
    private LongValueMapPlacement PlaceLongValueMaps(TableDefinition.Parts parts, int columnId)
    {
        JetFormatBase format = _channel.Format;
        int primaryPage = PageBuffer.ReadRecordPointer(parts.Header, format.TdefOwnedPagesOffset).Page;
        byte[] primaryBytes = _channel.ReadPage(primaryPage).Span.ToArray();
        int primaryFree = DataPage.ReadFreeSpace(primaryBytes, format);

        LongValueMapPlacement placement;
        if (UsageMap.RecordsFitting(format, primaryFree) >= 2)
        {
            // Room on the table's usage-map page — append the two maps there (as ACE does).
            int used = DataPage.ReadRowCount(primaryBytes, format);
            placement = new LongValueMapPlacement(primaryPage, used, used + 1, Dedicated: false);
        }
        else
        {
            // Full — give the column its own usage-map page (owned = row 0, free = row 1), the same
            // fallback CREATE TABLE uses once its primary map page fills on a wide table.
            placement = new LongValueMapPlacement(_allocator.Allocate(), 0, 1, Dedicated: true);
        }
        TableDefinition.AddLongValueMapEntry(_channel.Format, parts, columnId, placement.UsedRow, placement.FreeRow, placement.Page);
        return placement;
    }

    /// <summary>Writes the two empty map records <see cref="PlaceLongValueMaps"/> placed. The TDEF already names
    /// their rows, so appending them anywhere else is refused rather than left pointing at another map.</summary>
    private void WriteLongValueMaps(LongValueMapPlacement placement)
    {
        JetFormatBase format = _channel.Format;
        if (placement.Dedicated)
        {
            _channel.WritePage(placement.Page, UsageMap.NewMapPage(format, 2)); // owned = row 0, free = row 1, both empty
            return;
        }
        foreach (int planned in new[] { placement.UsedRow, placement.FreeRow })
        {
            int row = AppendEmptyUsageMapRow(format, placement.Page);
            if (row != planned)
                throw new InvalidOperationException(
                    $"Usage-map page {placement.Page} took the long-value map at row {row}; the definition names row {planned}.");
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
        TableDefinition table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found after adding column '{spec.Name}'.");
        ColumnDef column = table.FindColumn(spec.Name)!;
        if (spec.Increment == 0) throw TableDefinition.ZeroIncrement(spec.Name);
        int increment = spec.Increment;

        var rows = new Table(_channel, table, _catalog).Rows().WithIds().ToList();
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

        if (column.Type != JetDataType.Complex)
        {
            ReseedCounter(table, column, spec.Seed == 1 && increment == 1 ? rows.Count + 1 : spec.Seed, increment);
            return;
        }
        // A complex column's high-water is 0x1C, which a column numbered before it may already have passed.
        byte[] tdef = _channel.ReadPage(table.DefinitionPage).Span.ToArray();
        TableDefinition.WriteComplexAutoNumber(tdef, _channel.Format,
            Math.Max(TableDefinition.ReadComplexAutoNumber(tdef, _channel.Format), rows.Count));
        _channel.WritePage(table.DefinitionPage, tdef);
        _catalog.Invalidate();
    }

    // The names ACE gives a complex column's storage, and the length it cuts the column name to in the owner
    // index's name, <column>_<GUID>.
    private const string FlatPrimaryIndex = "MSysComplexPKIndex";
    private const string FlatScalarIndex = "IdxFKPrimaryScalar";
    private const int OwnerIndexColumnLength = 31;

    /// <summary>The ComplexID the registry hands out next: its AutoNumber, so an id is never reused.</summary>
    private int NextComplexId() =>
        (_catalog.FindTable("MSysComplexColumns")
            ?? throw new NotSupportedException("Complex columns need an Access 2007 or later (.accdb) database."))
        .RequireColumn("ComplexID").Seed;

    /// <summary>Creates a complex column's storage, as DAO does: the flat table and its three indexes, the
    /// registry row, the owner's unique index over the column, and the flat table's <c>Required</c> property.</summary>
    private void AddComplexStorage(string ownerName, ColumnSpec column)
    {
        (_, ColumnSpec[] values, string key) = JetCatalog.MSysComplexTypeTables.Single(t => t.Name == column.ComplexTemplate);
        Comparer<string> byName = Comparer<string>.Create(JetTextComparer.For(_collation).Compare);
        static string Cut(string name) => name.Length <= JetName.MaxLength ? name : name[..JetName.MaxLength];
        static string NewGuidName() => Guid.NewGuid().ToString("N").ToUpperInvariant();

        // Value columns take ids in name order, then the value id, then the owner link; neither of those two
        // carries a LANGID.
        string link = Cut($"_{column.Name}");
        string valueId = $"{ownerName}_{column.Name}".Length <= JetName.MaxLength
            ? $"{ownerName}_{column.Name}" : Cut($"{ownerName[0]}_{column.Name}");
        ColumnSpec[] valueColumns = [.. values.OrderBy(v => v.Name, byName).Select((v, i) => v with { ColumnId = i })];
        ColumnSpec[] flatColumns =
        [
            .. valueColumns,
            new(valueId, JetDataType.Int32, sizeof(int), IsFixedLength: false, IsAutoNumber: true,
                ColumnId: valueColumns.Length, Ordinal: 0,
                ExtendedFlags: (byte)ColumnExtendedFlags.ComplexValueId) { Collation = default(Collation) },
            new(link, JetDataType.Int32, sizeof(int), IsFixedLength: false,
                ColumnId: valueColumns.Length + 1, Ordinal: 0,
                ExtendedFlags: (byte)ColumnExtendedFlags.ComplexOwnerLink) { Collation = default(Collation) },
        ];
        string flatName = Cut($"f_{NewGuidName()}_{column.Name}");
        Create(flatName, [.. flatColumns.OrderBy(c => c.Name, byName)], flatTable: true);
        AddIndex(flatName, FlatPrimaryIndex, [(valueId, false)], isUnique: true, isPrimary: true, disallowNull: true, ignoreNulls: false);
        AddIndex(flatName, link, [(link, false)], isUnique: false, isPrimary: false, disallowNull: true, ignoreNulls: false);
        AddIndex(flatName, FlatScalarIndex, [(link, false), (key, false)], isUnique: true, isPrimary: false, disallowNull: true, ignoreNulls: false);
        int flatPage = _catalog.RequireTable(flatName).DefinitionPage;

        TableDefinition registry = _catalog.RequireTable("MSysComplexColumns");
        var row = new object?[registry.Columns.Count];
        JetCatalog.Set(registry, row, "ColumnName", column.Name);
        JetCatalog.Set(registry, row, "ComplexID", column.ComplexId);
        JetCatalog.Set(registry, row, "ComplexTypeObjectID", _catalog.RequireTable(column.ComplexTemplate!).DefinitionPage);
        JetCatalog.Set(registry, row, "ConceptualTableID", _catalog.RequireTable(ownerName).DefinitionPage);
        JetCatalog.Set(registry, row, "FlatTableID", flatPage);
        new RowInserter(_channel, registry).Insert(row, updateIndexes: true);
        _catalog.Invalidate();

        AddIndex(ownerName, $"{column.Name[..Math.Min(column.Name.Length, OwnerIndexColumnLength)]}_{NewGuidName()}",
            [(column.Name, false)], isUnique: true, isPrimary: false, disallowNull: true, ignoreNulls: false);
        SetColumnProperties(flatPage, valueColumns[0].Name,
            [PropertyBlob.Bool(valueColumns[0].Name, PropertyBlob.RequiredProperty, false)]);
        _catalog.Invalidate();
    }



    /// <summary>Sets (replaces) a column's <c>DefaultValue</c> in the table's <c>MSysObjects.LvProp</c> blob —
    /// ALTER TABLE … ALTER COLUMN … DEFAULT. Reads all properties, drops any existing DefaultValue for the
    /// column, adds the new one, and rewrites the blob (preserving every other property).</summary>
    public void SetColumnDefault(string tableName, string columnName, string defaultSql)
        => MutateLvPropForColumn(tableName, columnName, (props, column) =>
        {
            props.RemoveAll(p => p.IsOwnedBy(columnName) && p.Name == PropertyBlob.DefaultValueProperty);
            props.Add(PropertyBlob.DefaultValue(columnName, column.Type, defaultSql));
        });

    /// <summary>Removes a column's <c>DefaultValue</c> from the table's <c>MSysObjects.LvProp</c> blob —
    /// ALTER TABLE … ALTER COLUMN … DROP DEFAULT. Drops only that property, so the column's type and its
    /// <c>Required</c> (NOT NULL) property survive — ACE-verified. A no-op if the column had no default.</summary>
    public void DropColumnDefault(string tableName, string columnName)
        => MutateLvPropForColumn(tableName, columnName, (props, _) =>
            props.RemoveAll(p => p.IsOwnedBy(columnName) && p.Name == PropertyBlob.DefaultValueProperty));

    /// <summary>Sets or clears a column's <c>Required</c> (NOT NULL) property in the table's
    /// <c>MSysObjects.LvProp</c> blob — ALTER TABLE … ALTER COLUMN … NOT NULL / NULL. A required column carries
    /// a boolean <c>Required</c> property; a nullable one simply has none, so this drops any existing one and
    /// re-adds it only when <paramref name="required"/> (matching the CREATE-side write, and read back into
    /// <see cref="ColumnDef.IsNullable"/>). ACE-verified: ACE writes the same property for
    /// <c>ALTER COLUMN … NOT NULL</c> and enforces it.</summary>
    public void SetColumnRequired(string tableName, string columnName, bool required)
        => MutateLvPropForColumn(tableName, columnName, (props, _) =>
        {
            props.RemoveAll(p => p.IsOwnedBy(columnName) && p.Name == PropertyBlob.RequiredProperty);
            if (required) props.Add(PropertyBlob.Bool(columnName, PropertyBlob.RequiredProperty, true));
        });

    /// <summary>Reads the table's <c>MSysObjects.LvProp</c> property blob, applies <paramref name="mutate"/>,
    /// and rewrites it — the shared read-modify-write behind ALTER COLUMN … SET/DROP DEFAULT.</summary>
    private void MutateLvPropForColumn(string tableName, string columnName,
        Action<List<PropertyBlob.Property>, ColumnDef> mutate)
    {
        (TableDefinition table, ColumnDef column) = CheckAlterColumn(tableName, columnName);
        int tdefPage = table.DefinitionPage;

        byte[] blob = ReadObjectProperties(tdefPage) ?? [];
        var props = PropertyBlob.Read(blob).ToList();
        mutate(props, column);
        WriteObjectProperties(tdefPage, PropertyBlob.Write(props, blob));
    }

    /// <summary>Appends a column's extended properties (DefaultValue/Required) to its table's
    /// <c>MSysObjects.LvProp</c> blob and re-stores it — the add-side counterpart of
    /// <see cref="RemoveColumnProperties"/>.</summary>
    private void SetColumnProperties(int tdefPage, string columnName, IReadOnlyList<PropertyBlob.Property> props)
    {
        byte[] blob = ReadObjectProperties(tdefPage) ?? [];
        WriteObjectProperties(tdefPage, PropertyBlob.AddColumnProperties(blob, columnName, props));
    }

    /// <summary>Adds a table-level CHECK to the table's <c>MSysObjects.LvProp</c> blob — ALTER TABLE ADD
    /// CONSTRAINT … CHECK. Merges with any existing checks: reads the current <c>CheckConstraints</c> property,
    /// appends the new (name, expression), and rewrites the single empty-owner table block (RemoveOwner + re-add),
    /// keeping the name pool and every column block intact. The check is enforced by the engine from the
    /// re-loaded <c>TableDefinition.CheckConstraints</c>.</summary>
    /// <param name="tableName">The table the check is on.</param>
    /// <param name="checkName">The check's name, unique in the Relationships container.</param>
    /// <param name="expression">The check's expression, as written.</param>
    /// <param name="reads">What the expression reads (<see cref="CheckReadsText"/>), for the <c>MSysObjects</c>
    /// object ACE records the check with (<see cref="JetCatalog.CreateCheckObject"/>). This layer cannot parse the
    /// expression, so the caller finds them.</param>
    public void AddCheckConstraint(string tableName, string checkName, string expression,
        IReadOnlyList<(string Table, IReadOnlyList<string> Columns)> reads)
    {
        JetName.Validate(checkName, "check constraint name");
        TableDefinition target = DesignTable(tableName);
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, target.Name);
        int tdefPage = target.DefinitionPage;

        byte[] blob = ReadObjectProperties(tdefPage) ?? [];
        var checks = PropertyBlob.ReadCheckConstraints(PropertyBlob.Read(blob)).ToList();
        checks.Add((checkName, expression));

        // Replace only the CheckConstraints entry. Dropping the whole table-owned block and re-adding one
        // property takes every OTHER table-level property with it — ValidationRule / ValidationText above
        // all, which LibRed reads and reports but does not re-emit, so an Access-authored table validation
        // rule vanished on the first CHECK anyone added. The column-property paths already do it this way.
        WriteObjectProperties(tdefPage, ReplaceTableProperty(blob, PropertyBlob.CheckConstraintsProperty,
            checks.Count > 0 ? PropertyBlob.WriteCheckList(checks) : null));
        _catalog.CreateCheckObject(checkName, target.Name, expression, CheckReadsText(reads));
    }

    /// <summary>
    /// A CHECK's <c>MSysObjects.Database</c>: what its expression reads, as ACE records it (measured, whole-file
    /// diff: <c>WholeFileParityProbeTest</c>). One character counts the tables; each table follows as a character
    /// giving its name's length, the name, a character counting its columns, and each column as its length and
    /// name. Its own table comes first, then each table a subquery names, in the order the expression names them;
    /// a table's columns in the table's own order, each once, and a table read for none (<c>COUNT(*)</c>) counts 0.
    /// <c>[Level] &lt; (SELECT MAX(Credit) FROM Customer WHERE City = 'Rome') + 10</c> on <c>Setting</c> is
    /// <c>\u0002 \u0007Setting \u0001 \u0005Level \u0008Customer \u0002 \u0004City \u0006Credit</c>. Stored as
    /// any Memo value is, so a short one is compressed and one holding a NUL is not.
    /// </summary>
    internal static string CheckReadsText(IReadOnlyList<(string Table, IReadOnlyList<string> Columns)> reads)
    {
        var text = new StringBuilder().Append((char)reads.Count);
        foreach ((string table, IReadOnlyList<string> columns) in reads)
        {
            text.Append((char)table.Length).Append(table).Append((char)columns.Count);
            foreach (string column in columns) text.Append((char)column.Length).Append(column);
        }
        return text.ToString();
    }

    /// <summary>Drops a named table-level CHECK — ALTER TABLE … DROP CONSTRAINT. Removes the matching entry from
    /// the <c>CheckConstraints</c> list in the table's <c>MSysObjects.LvProp</c> blob (the inverse of
    /// <see cref="AddCheckConstraint"/>): if any remain, rewrites the list; if it was the last one, drops the
    /// whole table-level property block. ACE-verified: after the drop ACE stops enforcing the check. Returns
    /// false if no CHECK of that name exists (so the caller can try other constraint kinds).</summary>
    public bool DropCheckConstraint(string tableName, string checkName)
    {
        TableDefinition target = DesignTable(tableName);
        int tdefPage = target.DefinitionPage;

        byte[] blob = ReadObjectProperties(tdefPage) ?? [];
        var checks = PropertyBlob.ReadCheckConstraints(PropertyBlob.Read(blob)).ToList();
        if (checks.RemoveAll(c => string.Equals(c.Name, checkName, StringComparison.OrdinalIgnoreCase)) == 0)
            return false; // no CHECK of that name — let the caller try FK/PK/unique
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, target.Name);

        // Rewrite the list, or remove the entry when that was the last check — leaving every other
        // table-level property (ValidationRule, ValidationText, …) untouched. See AddCheckConstraint.
        WriteObjectProperties(tdefPage, ReplaceTableProperty(blob, PropertyBlob.CheckConstraintsProperty,
            checks.Count > 0 ? PropertyBlob.WriteCheckList(checks) : null));
        DeleteObject(checkName, ObjectType.CheckConstraint);
        return true;
    }

    /// <summary>Deletes a relationship's or a CHECK's <c>MSysObjects</c> object and its <c>MSysACEs</c> rows (a
    /// check has none), as ACE does when it goes (verified). One written before LibRed recorded the object has
    /// none to delete.</summary>
    private void DeleteObject(string name, ObjectType type)
    {
        if (_catalog.FindObjectId(name, type) is not { } objectId) return;
        DeleteCatalogRows("MSysObjects", "Id", objectId);
        DeleteCatalogRows("MSysACEs", "ObjectId", objectId);
    }

    /// <summary>The table and column an ALTER COLUMN changes, and then the read-only refusal (verified vs ACE — a
    /// missing column is ACE's "Could not find field" first).</summary>
    private (TableDefinition Table, ColumnDef Column) CheckAlterColumn(string tableName, string columnName)
    {
        TableDefinition table = DesignTable(tableName);
        ColumnDef column = table.FindColumn(columnName)
            ?? throw new InvalidOperationException($"Column '{columnName}' does not exist in '{tableName}'.");
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, table.Name);
        return (table, column);
    }

    /// <summary>Changes a column's declared type — ALTER TABLE … ALTER COLUMN. Apart from the counter edits and
    /// a re-declaration that changes nothing, every type or length change — a text column's length included, as
    /// ACE makes it — is a full column rewrite, handled in place by <see cref="AlterColumnTypeInPlace"/>
    /// (byte-faithful with ACE), Memo/OLE included. Nothing here throws NotSupported for a storage-type
    /// change.</summary>
    public void AlterColumn(string tableName, string columnName, ColumnSpec newSpec)
    {
        (TableDefinition table, ColumnDef col) = CheckAlterColumn(tableName, columnName);
        EnsureColumnIsNotInRelationship(table, col);

        // Widening a column reaches the same bytes as declaring it wide in the first place, so the limits
        // Create enforces apply here too. This sits ahead of the identity and counter short-circuits below
        // deliberately: re-declaring an already-oversized column should report the problem, not wave it on.
        RowCodec.ValidateFieldWidth(newSpec.Name, newSpec.Type, newSpec.Length);
        TableDefinition.ValidateNumericPrecision(newSpec);
        // A pre-check, so an oversized re-declaration reports rather than being waved on by the short-circuits
        // below; AlterColumnTypeInPlace makes the same check. The old slot stays as dead space, so nothing is
        // subtracted for it. The variable-slot count comes from the stored 0x2B high-water — it never decrements,
        // so deriving it from the live columns under-counts on a table that has dropped or retyped one.
        // A type or length change burns a fresh column id, so the bitmap can widen by one.
        RowCodec.ValidateRecordFits(tableName,
            RowCodec.ComputeFixedDataLength(table.Columns) + (newSpec.OccupiesFixedData ? newSpec.Length : 0),
            table.VariableColumnCount + (newSpec.IsFixedLength ? 0 : 1),
            table.ColumnIdHighWater + (col.Type == newSpec.Type && col.Length == newSpec.Length ? 0 : 1),
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

        // Promote a plain Int32 column to an AutoNumber, or demote a counter back to a plain Int32 — a counter is
        // stored identically (both a 4-byte Int32); the only differences are the column's 0x04 flag and the
        // header's seed/increment. So either way it's a metadata edit, not a rebuild. ACE/SQL Server reject
        // promotion (PostgreSQL/MySQL and LibRed allow it — see spec); ACE *allows* demotion, which resets the
        // header to a non-AutoNumber table's state (0x14 = 0, 0x18 = 1), keeps existing values, and stops the
        // column auto-assigning.
        if (col.IsAutoNumber != newSpec.IsAutoNumber && col.Type == newSpec.Type)
        {
            SetAutoNumber(table, col, newSpec.IsAutoNumber,
                newSpec.IsAutoNumber ? newSpec.Seed : 1, newSpec.IsAutoNumber ? newSpec.Increment : 1);
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

        // Every other change — numeric type, a fixed size, fixed↔variable, to or from a long value, and a text or
        // binary length, widening as much as narrowing — edits the descriptor in place under a fresh id and
        // re-lays every row, rebuilding only the indexes over the column. ACE has no cheap length-only path.
        AlterColumnTypeInPlace(tableName, columnName, newSpec);
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

    /// <summary>Whether the column is either end of a relationship — the child's FK column or the parent's
    /// referenced key. ACE refuses to alter or drop such a column; the two callers differ only in the message
    /// they raise, so the rule itself lives here.</summary>
    private bool ColumnIsInRelationship(TableDefinition table, ColumnDef column)
    {
        const StringComparison oic = StringComparison.OrdinalIgnoreCase;
        return _catalog.Relationships.Any(r =>
            (string.Equals(r.Table, table.Name, oic) &&
             r.Columns.Any(c => string.Equals(c.Column, column.Name, oic))) ||
            (string.Equals(r.ReferencedTable, table.Name, oic) &&
             r.Columns.Any(c => string.Equals(c.ReferencedColumn, column.Name, oic))));
    }

    /// <summary>ACE rejects every type/length alteration of a relationship column, on either the
    /// referencing or referenced side. Keep this check ahead of all specialized ALTER paths so none of
    /// them can bypass it.</summary>
    private void EnsureColumnIsNotInRelationship(TableDefinition table, ColumnDef column)
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
    private void ReseedCounter(TableDefinition table, ColumnDef col, int seed, int increment)
    {
        EnsureColumnIsNotInRelationship(table, col);

        if (increment == 0) throw TableDefinition.ZeroIncrement(col.Name);
        byte[] tdef = _channel.ReadPage(table.DefinitionPage).Span.ToArray();
        TableDefinition.WriteCounter(tdef, _channel.Format, seed, increment);
        _channel.WritePage(table.DefinitionPage, tdef);
        _catalog.Invalidate();
    }

    /// <summary>Turns a plain Int32 column into an AutoNumber in place, or an AutoNumber back into a plain Int32 —
    /// ALTER COLUMN c COUNTER(seed, increment) where c is a plain integer, or ALTER COLUMN c LONG where c is a
    /// counter. A counter is stored identically to a Long Integer, so this only sets or clears the column
    /// descriptor's <c>0x04</c> AutoNumber flag and writes the header's seed/increment (<c>0x14</c>/<c>0x18</c>) —
    /// for a demotion, seed 1 and increment 1, a non-AutoNumber table's state. Existing values are untouched; a
    /// demoted column just stops auto-assigning.
    /// <para>Promotion carries ACE's refusals: only one column may draw on that pair, so a second is rejected —
    /// complex columns are flagged <c>0x04</c> too but allocate from <c>0x1C</c>, so they do not count as the
    /// existing one — and (like the reseed path) a column in a relationship is rejected. Demotion has neither:
    /// ACE permits it, unlike int→counter promotion.</para></summary>
    private void SetAutoNumber(TableDefinition table, ColumnDef col, bool autoNumber, int seed, int increment)
    {
        if (autoNumber)
        {
            if (table.Columns.Any(c => c.IsAutoNumber && c.Type != JetDataType.Complex && c.ColumnId != col.ColumnId))
                throw new InvalidOperationException(
                    $"Cannot make '{col.Name}' an AutoNumber: table '{table.Name}' already has one, and a table "
                    + "can have only one.");
            EnsureColumnIsNotInRelationship(table, col);
            if (increment == 0) throw TableDefinition.ZeroIncrement(col.Name);
        }

        JetFormatBase format = _channel.Format;
        TableDefinition.Parts parts = TableDefinition.ReadParts(_channel, table.DefinitionPage);
        byte[] descriptor = parts.Columns[col.Index].Descriptor;
        var flags = (ColumnFlags)descriptor[format.ColumnFlagsOffset];
        descriptor[format.ColumnFlagsOffset] =
            (byte)(autoNumber ? flags | ColumnFlags.AutoNumber : flags & ~ColumnFlags.AutoNumber);
        TableDefinition.WriteCounter(parts.Header, format, seed, increment);
        WriteTdef(table.DefinitionPage, parts);
        _catalog.Invalidate();

        // COUNTER(seed, increment) is a *sequential* counter. A surviving GenUniqueID() default would instead
        // make it a "Random" AutoNumber (IsRandomAutoNumber) — assigning random ids and ignoring the seed — so
        // clear it to honour the requested sequence. Other (literal) defaults are inert on a counter (the insert
        // path skips defaults for AutoNumber columns) and are left as-is.
        if (autoNumber && PropertyBlob.IsGenUniqueId(col.DefaultValue))
            DropColumnDefault(table.Name, col.Name);
    }



    /// <summary>Full in-place column type change, byte-for-byte like ACE for fixed and variable columns and
    /// targets, fixed↔variable, indexed targets, and a Memo/OLE source or target (whose long-value maps are placed
    /// and retired as ADD and DROP COLUMN do). Edits the TDEF in place (<see cref="TableDefinition.EditTargetDescriptor"/>) and re-lays every row — the target's
    /// OLD fixed slot is kept as dead space, its converted value appended at the new offset, count + null bitmap
    /// updated. Converts values in memory first (throws on bad data before any write); runs in a transaction.</summary>
    public void AlterColumnTypeInPlace(string tableName, string columnName, ColumnSpec newSpec)
    {
        (TableDefinition oldDef, ColumnDef oldTarget) = CheckAlterColumn(tableName, columnName);
        EnsureColumnIsNotInRelationship(oldDef, oldTarget);
        if (newSpec.Type is JetDataType.Ole or JetDataType.BigBinary
            && oldDef.Indexes.Any(i => i.Columns.Any(c => c.Column.ColumnId == oldTarget.ColumnId)))
            RejectOleIndexColumns([oldTarget.Name], _ => newSpec.Type);

        // Also reached directly, not only through AlterColumn, so it carries the width limits itself.
        // The record-fits check needs the true fixed-region end, so it runs once that is measured, below.
        RowCodec.ValidateFieldWidth(newSpec.Name, newSpec.Type, newSpec.Length);
        TableDefinition.ValidateNumericPrecision(newSpec);

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
            .GroupBy(i => i.RealIndexOrdinal).ToDictionary(g => g.Key, g => g.First());
        int oldTargetId = oldTarget.ColumnId;

        // 1. Materialize (id + raw bytes + values) before touching disk; conversion throws here on bad data.
        var reader = new RowInserter(_channel, oldDef);
        var rows = new Table(_channel, oldDef, _catalog).Rows().WithIds()
            .Select(r => (r.Id, Raw: reader.ReadRow(r.Id), Values: (object?[])r.Values.Clone()))
            .ToList();
        foreach (var r in rows)
            r.Values[oldTarget.Index] = ConvertValue(r.Values[oldTarget.Index], newSpec.Type, newSpec.Name);

        // The new slot goes where the live fixed columns end, not past a dead slot an existing row still carries:
        // ACE writes it over a dropped or retyped-away column's bytes (verified vs ACE). A row whose own region is
        // longer keeps it — see BuildRelaidRecord.
        int oldFixedLen = RowCodec.ComputeFixedDataLength(oldDef.Columns);

        // Now the widest-record check, against what this path actually produces: the re-lay KEEPS the old target's
        // fixed slot as dead space rather than reclaiming it (so nothing is subtracted), and the variable-slot
        // count is the 0x2B high-water, which never decrements (so a variable→fixed retype leaves it where it is).
        int newFixedLen = oldFixedLen + (newSpec.OccupiesFixedData ? newSpec.Length : 0);
        RowCodec.ValidateRecordFits(tableName,
            newFixedLen,
            oldDef.VariableColumnCount + (newSpec.IsFixedLength ? 0 : 1),
            oldDef.ColumnIdHighWater + 1,   // the type change burns a fresh id
            _channel.Format);

        bool ownTx = !_channel.InTransaction;
        if (ownTx) _channel.BeginTransaction();
        try
        {
            RaiseFormatFor([newSpec]);
            JetFormatBase format = _channel.Format;

            // 2. One TDEF edit for the whole modify: patch only the target descriptor (bump 0x29 / 0x2B — its
            //    appended fixed offset is where the live fixed columns end) AND re-point every
            //    index over the target, all into the SAME parts, then write the TDEF a single time. Each index
            //    re-point needs the fresh root allocated + owned-map recycled first (page work off the TDEF).
            TableDefinition.Parts parts = TableDefinition.ReadParts(_channel, oldDef.DefinitionPage);
            int newTargetId = TableDefinition.EditTargetDescriptor(parts, oldTarget, newSpec, oldFixedLen, _collation, format);

            // A column becoming a long value gets its entry, placed as ADD COLUMN places it, and its two map records
            // ahead of the index rebuilds' records on the usage-map page (verified vs ACE).
            if (toLongValue) WriteLongValueMaps(PlaceLongValueMaps(parts, newTargetId));

            // ACE rebuilds the indexes over the target in logical-block (name) order, allocating their roots in that
            // order, and hands them back the real-index slots they held in that order too.
            List<int> rebuildOrder = parts.Logical
                .Select(b => TableDefinition.ReadInfoBlock(b.Info, format, NameOf(b.Name)).DataOrdinal)
                .Distinct().Where(affectedIndexes.ContainsKey).ToList();
            var pending = new List<(string Name, int OldRoot, int NewRoot, bool IgnoreNulls)>();
            foreach (int ordinal in rebuildOrder)
            {
                IndexDef index = affectedIndexes[ordinal];
                int newRoot = PrepareIndexRebuild(parts, oldDef, index, oldTargetId, newTargetId);
                pending.Add((index.Name, index.RootPage, newRoot, index.IgnoreNulls));
            }
            ReassignRebuiltIndexSlots(parts, rebuildOrder);

            // The old column's long-value maps, read before the TDEF loses them, as DROP COLUMN reads them.
            UsageMap? oldMaps = null;
            var released = new HashSet<int>();
            var retire = new List<MapRetirement>();
            if (fromLongValue)
            {
                var oldDefinition = new TableDefinition();
                oldDefinition.Read(_channel, oldDef.DefinitionPage);
                oldMaps = new UsageMap(_channel, oldDef);
                QueueLongValueMaps(oldDefinition, oldTarget, oldMaps, released, retire);
                TableDefinition.RemoveLongValueMapEntry(_channel.Format, parts, oldTargetId);
            }

            WriteTdef(oldDef.DefinitionPage, parts);
            _catalog.Invalidate();

            if (oldMaps is not null)
            {
                RetireMapRecords(retire, oldMaps, released);
                foreach (int page in released) _allocator.Release(page);   // reusable only after this handle closes
            }

            TableDefinition newDef = _catalog.FindTable(tableName)!;
            ColumnDef newTarget = newDef.FindColumn(columnName)!;
            int newMaxId = RowCodec.MaxColumnId(newDef.Columns, newDef.ColumnIdHighWater);

            // Slots per row comes from the TDEF high-water, exactly as RowCodec derives it — never from a
            // row's own stored numVar. A row written before a variable ADD COLUMN carries fewer slots than the
            // table has, and appending the retyped column onto such a row lands it at the wrong index while its
            // descriptor names the high-water one, which is the "A column Id is incorrect" file ACE rejects.
            int newVarCount = RowCodec.VariableSlotCount(newDef.Columns, newDef.VariableColumnCount);
            var writer = new RowInserter(_channel, newDef);

            // 3. Re-lay each row: old fixed region + old var chunks verbatim (incl. the dead old slot), target
            //    appended (a new fixed slot, or a new variable chunk); count/var-table/null-bitmap rebuilt.
            //    A long value is stored first, as an insert would store it, so the chunk appended is its descriptor.
            //    ACE builds every row in one buffer it never clears: the old record is copied in and the new one
            //    written over it, so a byte the new record does not write holds whatever last reached it.
            var buffer = new byte[format.PageSize];
            foreach (var r in rows)
            {
                if (toLongValue)
                    r.Values[newTarget.Index] = writer.MaterializeLongValue(newTarget, r.Values[newTarget.Index]);
                r.Raw.CopyTo(buffer, 0);
                byte[] record = BuildRelaidRecord(
                    r.Raw, oldDef, newTarget, r.Values, newDef.Columns, newMaxId, newFixedLen, newVarCount, buffer);
                record.CopyTo(buffer, 0);
                writer.RewriteRowRaw(r.Id, record);
            }

            // 4. Finish each index rebuild: backfill the fresh B-tree with new-type keys, then free the old root
            //    (last, so the new root got the appended page rather than reusing this one) — as ACE does.
            foreach (var p in pending)
            {
                BackfillIndex(newDef, p.Name, p.IgnoreNulls, validateUnique: true);
                _allocator.Release(p.OldRoot);
            }

            if (ownTx) _channel.CommitTransaction();
        }
        catch when (ownTx) { _channel.RollbackTransaction(); _catalog.Invalidate(); throw; }
        _catalog.Invalidate();
    }

    /// <summary>Builds the re-laid row record, matching ACE's in-place modify byte-for-byte: written over the old
    /// row as an UPDATE is — its fixed bytes position for position and its variable chunks verbatim, so the dead
    /// old-target slot/chunk keeps its stale bytes — with the converted target in its new fixed slot or variable
    /// chunk, and the leading count (= max id + 1), variable-offset table + numVar (omitted if none), and null
    /// bitmap (each dead id's bit carried over from the old row) rebuilt. The fixed region is at least
    /// <paramref name="fixedLength"/> — the old live region plus the target's new slot, which a row predating a fixed
    /// ADD COLUMN grows to even when the target becomes variable (verified vs ACE) — and past the end of a short old
    /// record holds what <paramref name="buffer"/>, the re-lay's buffer with the old record copied in, holds there;
    /// so does the padding of a null-bitmap byte the old row did not have.</summary>
    private byte[] BuildRelaidRecord(byte[] oldRow, TableDefinition oldDef, ColumnDef newTarget, object?[] values,
        IReadOnlyList<ColumnDef> newCols, int newMaxId, int fixedLength, int newVarCount, byte[] buffer)
    {
        object? tv = values[newTarget.Index];

        // Whether THIS row has a variable trailer is the old table's question — see RowCodec.Layout.HasVariableSection.
        JetFormatBase format = _channel.Format;
        RowCodec.Layout oldLayout = RowCodec.ParseLayout(oldDef, format, oldRow);

        // Fixed region: the target written into its new slot if it occupies fixed data — a Yes/No does not, and
        // AssembleRow sets its null-bitmap bit from the value. A NULL target writes nothing: its slot holds whatever
        // the old record had there.
        byte[] newFixed = RowCodec.FixedRegionOver(oldLayout, fixedLength, format, buffer);
        if (newTarget.OccupiesFixedData && tv is not null)
            Storage.Types.JetTypeCodec.Encode(newTarget, tv, format).CopyTo(newFixed, newTarget.FixedOffset);

        // Variable chunks: padded out to the table's slot count so the target lands on the index its descriptor
        // names, then the target placed at that index.
        byte[][] chunks = RowCodec.VarChunksOver(oldLayout, newVarCount);
        if (!newTarget.IsFixedLength)
            chunks[newTarget.VariableIndex] = tv is null ? [] : Storage.Types.JetTypeCodec.Encode(newTarget, tv, format);

        // Assemble via the shared row layout (count + var table + null bitmap identical to a fresh encode), each
        // dead id's bit carried over from the old row's bitmap.
        return RowCodec.AssembleRow(format, newMaxId, newFixed, chunks, newCols, values,
            priorBitmap: oldLayout.NullBitmap, beneath: buffer);
    }

    /// <summary>Hands the rebuilt indexes back the real-index slots they held, in the order they were rebuilt: the
    /// lowest slot to the first rebuilt, and so on, their stats and data blocks moving together, and every logical
    /// block's data ordinal (<c>+0x08</c>) following its index. The logical blocks over them hand their numbers
    /// (<c>+0x04</c>) round the same way: pooled, sorted, and given back in logical-block order. An index the ALTER
    /// does not touch keeps its slot and its number, so with <c>IX_Z</c>, <c>IX_E</c>, <c>IX_A</c> in slots 1–3 and
    /// <c>E</c> unaffected, a retype of the column under <c>IX_Z</c> and <c>IX_A</c> leaves <c>IX_A</c>,
    /// <c>IX_E</c>, <c>IX_Z</c> (all verified vs ACE, including logical indexes sharing one real index — the primary
    /// key moves like any other index). Only plain indexes and the primary key get here: a relationship's column
    /// cannot be retyped.</summary>
    private void ReassignRebuiltIndexSlots(TableDefinition.Parts parts, List<int> rebuildOrder)
    {
        JetFormatBase format = _channel.Format;
        List<int> slots = [.. rebuildOrder.Order()];
        byte[][] stats = [.. rebuildOrder.Select(o => parts.Stats[o])];
        byte[][] blocks = [.. rebuildOrder.Select(o => parts.DataBlocks[o])];
        for (int k = 0; k < slots.Count; k++)
        {
            parts.Stats[slots[k]] = stats[k];
            parts.DataBlocks[slots[k]] = blocks[k];
        }
        List<(byte[] Block, TableDefinition.LogicalIndexSpec Info)> moved = [.. parts.Logical
            .Select(b => (b.Info, Info: TableDefinition.ReadInfoBlock(b.Info, format, NameOf(b.Name))))
            .Where(b => rebuildOrder.Contains(b.Info.DataOrdinal))];
        List<int> numbers = [.. moved.Select(b => b.Info.Number).Order()];
        for (int i = 0; i < moved.Count; i++)
            TableDefinition.WriteInfoBlock(moved[i].Block, format, moved[i].Info with
            {
                DataOrdinal = slots[rebuildOrder.IndexOf(moved[i].Info.DataOrdinal)],
                Number = numbers[i],
            });
    }

    /// <summary>Prepares one index rebuild over a just-modified column, matching ACE's reconstruction: allocate a
    /// fresh empty root leaf (appended — the old root is left orphaned) and extend/recycle the owned usage map to
    /// track it, then re-point the index-data block within <paramref name="parts"/> to the new root with the
    /// target's burned column id and the new usage-map row (bumping the stats block). The caller writes the TDEF
    /// once, then backfills the fresh B-tree and frees the old root. Returns the new root page.</summary>
    private int PrepareIndexRebuild(TableDefinition.Parts parts, TableDefinition table, IndexDef index, int oldTargetId, int newTargetId)
    {
        JetFormatBase format = _channel.Format;

        // A fresh empty root leaf, appended; the old root is freed by the caller afterwards (ACE reuses it on the
        // next alloc). This and the owned-map recycle touch pages OFF the TDEF, so they happen before the single
        // TDEF write; only the index-data block + stats mutations below go into the shared parts.
        int newRoot = AllocateIndexRoot(format, table.DefinitionPage);

        // Recycle the index's owned-map row (ACE soft-deletes the old row and reuses its space for a new row
        // tracking the new root), reading the current pointer from the (as-yet-unwritten) data block. The map's
        // holder is the page that pointer names, not the table's own owned map's.
        Span<byte> block = parts.DataBlocks[index.RealIndexOrdinal];
        (int Row, int Page) map = index.UsageMap;
        int newRow = RecycleOwnedMapRow(format, map.Page, map.Row, newRoot);

        // Re-point the index-data block: the target's burned id in its column slot, the new root, the new
        // usage-map row. Its statistics are set by the backfill that follows, from the rows it then holds.
        for (int slot = 0; slot < format.IndexDataMaxColumns; slot++)
        {
            (short columnId, IndexColumnOrder order) = TableDefinition.ReadIndexSlot(block, format, slot);
            if (columnId == oldTargetId) TableDefinition.WriteIndexSlot(block, format, slot, newTargetId, order);
        }
        TableDefinition.WriteIndexTree(block, format, newRoot, newRow, map.Page);
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
        int rowCount = DataPage.ReadRowCount(_channel.ReadPage(usageMapPage).Span, format);
        if (oldRow < 0 || oldRow >= rowCount)
            throw new InvalidDataException(
                $"Usage-map row {usageMapPage}:{oldRow} does not exist; the page has {rowCount} rows.");

        // (1) ACE's first write, kept verbatim: the appended row is where the new root's bit is set, and the
        // bytes it leaves behind are part of the file ACE produces.
        int newRow = AppendEmptyUsageMapRow(format, usageMapPage);
        new UsageMap(_channel).SetBit(newRow, usageMapPage, newRoot, set: true);

        // (2) Re-lay the live records. Starting from the page as it stands keeps everything this does not
        // write — the abandoned append included — exactly where ACE leaves it.
        byte[] page = _channel.ReadPage(usageMapPage).Span.ToArray();
        var holder = new DataPage();
        holder.Read(_channel.ReadPage(usageMapPage), format);

        // The recycled row becomes a zero-length deleted + overflow tombstone; every other slot keeps its flags.
        var records = new byte[rowCount + 1][];
        var flags = new RowSlotFlags[rowCount + 1];
        for (int i = 0; i <= rowCount; i++)
        {
            records[i] = i == oldRow ? [] : page.AsSpan(holder.Rows[i].Offset, holder.Rows[i].Length).ToArray();
            flags[i] = i == oldRow ? RowSlotFlags.Deleted | RowSlotFlags.Overflow : DataPage.Flags(holder.Rows[i]);
        }

        if (!DataPage.LayRows(page, format, records, flags))
            throw new InvalidOperationException(
                $"Usage-map page {usageMapPage} has no room to recycle row {oldRow}: {rowCount} rows already. "
                + "The new map belongs on a page of its own.");
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
        value = Storage.Types.JetTypeCodec.NumberFromText(type, value);
        return type switch
        {
            JetDataType.Boolean => Storage.RowCodec.IsTruthy(value),
            JetDataType.Byte => Convert.ToByte(value, inv),
            JetDataType.Int16 => Convert.ToInt16(value, inv),
            JetDataType.Int32 => Convert.ToInt32(value, inv),
            JetDataType.Int64 => Convert.ToInt64(value, inv),
            JetDataType.Single => JetDecimalConverter.ToSingle(value, inv),
            JetDataType.Double => JetDecimalConverter.ToDouble(value, inv),
            JetDataType.Currency or JetDataType.FixedPoint => JetDecimalConverter.ToDecimal(value, inv),
            JetDataType.DateTime => Storage.Types.JetTypeCodec.ToDate(value, inv),
            JetDataType.Text or JetDataType.Memo => Storage.Types.VbaText.Of(value),
            JetDataType.Guid => value is Guid g ? g : Guid.Parse(value.ToString()!),
            JetDataType.Binary or JetDataType.BigBinary or JetDataType.Ole => value as byte[] ?? System.Text.Encoding.Unicode.GetBytes(value.ToString()!),
            _ => value,
        };
    }


    /// <summary>
    /// Drops a column byte-faithfully with ACE (probed): a **metadata-only TDEF edit** — removes the
    /// column's descriptor and its name, and decrements the live <c>ColumnCount</c> (0x2D). It does
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
        TableDefinition table = DesignTable(tableName);
        ColumnDef? col = table.Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
        if (col is null) return false;
        _channel.RefuseIfReadOnly(ReadOnlyWrite.Design, table.Name);

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
        var definition = new TableDefinition();
        definition.Read(_channel, table.DefinitionPage);
        var maps = new UsageMap(_channel, table);
        var owned = new HashSet<int>();
        var retire = new List<MapRetirement>();
        QueueLongValueMaps(definition, col, maps, owned, retire);

        TableDefinition.Parts parts = TableDefinition.ReadParts(_channel, table.DefinitionPage); // stitches continuation pages for a multi-page TDEF
        // The descriptor and its name go; the live column count (0x2D) follows in WriteTdef. The variable-column
        // count (0x2B) is deliberately left — ACE keeps it as a high-water mark (verified).
        parts.Columns.RemoveAt(col.Index);
        TableDefinition.RemoveLongValueMapEntry(_channel.Format, parts, col.ColumnId);
        WriteTdef(table.DefinitionPage, parts);

        RetireMapRecords(retire, maps, owned);
        var allocator = _channel.Allocator;
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

    /// <summary>A version-history template's timestamp column, <c>Modified_&lt;GUID&gt;</c>, beside the value column
    /// it holds for each append-only memo.</summary>
    private const string VersionHistoryTimestampPrefix = "Modified_";

    /// <summary>The table-level property Access sets while a table has an append-only memo.</summary>
    private const string AppendOnlyProperty = "AppendOnly";

    /// <summary>A table's version-history column as it stands for one of its append-only memos: the complex column,
    /// its template's name, and whether that memo is the last whose history it keeps.</summary>
    private sealed record VersionHistory(string ColumnName, int ComplexId, string FlatTable, string Template, bool IsLast);

    /// <summary>The version history keeping <paramref name="memoName"/>'s history, or null when the column is not an
    /// append-only memo. A table has at most one version-history column, the one its descriptor's extended flag
    /// <c>0x20</c> marks; its template carries a value column named for each append-only memo.</summary>
    private VersionHistory? VersionHistoryOf(string tableName, string memoName)
    {
        if (_catalog.ComplexColumns.FirstOrDefault(c =>
                string.Equals(c.OwnerTable.Name, tableName, StringComparison.OrdinalIgnoreCase)
                && c.OwnerTable.FindColumn(c.ColumnName)?.IsVersionHistory == true) is not { } history)
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
            && _catalog.RequireTable(tableName).ObjectFlags is var flags && (flags & ObjectAttributes.OwnsComplexColumns) != 0)
            UpdateCatalogRows("MSysObjects", "Id", tdefPage, required: true,
                ("Flags", unchecked((int)(flags & ~ObjectAttributes.OwnsComplexColumns))));
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
        if (ReadObjectProperties(tdefPage) is not { Length: > 0 } blob) return;

        byte[] cleaned = PropertyBlob.RemoveOwner(blob, columnName);
        if (alsoTableAppendOnly) cleaned = ReplaceTableProperty(cleaned, AppendOnlyProperty, null);
        if (cleaned.Length == blob.Length) return; // nothing to remove
        WriteObjectProperties(tdefPage, cleaned);
    }

    /// <summary>True if <paramref name="block"/> is the incoming relationship block that cross-links to the
    /// child's outgoing block number on the child's TDEF page.</summary>
    private bool IsIncomingBlockFor((byte[] Info, byte[] Name) block, int childBlockNum, int childPage)
    {
        TableDefinition.LogicalIndexSpec info = TableDefinition.ReadInfoBlock(block.Info, _channel.Format, NameOf(block.Name));
        return info.FkType == ForeignKeyType.Incoming && (int)info.FkNumber == childBlockNum && info.FkTablePage == childPage;
    }

    /// <summary>Soft-deletes every MSysRelationships row for the named relationship.</summary>
    private void DeleteRelationshipRows(string name)
    {
        TableDefinition msys = _catalog.RequireTable("MSysRelationships");
        int nameIdx = msys.RequireColumn("szRelationship").Index;

        // A real delete, as the other catalog rows take (and as ACE's own DROP CONSTRAINT leaves the page —
        // measured by whole-file diff: the row's space back in the page's free count and the table's row count
        // down). Flagging the slot alone also left the index entries standing, pointing at a dead row.
        var table = new Table(_channel, msys, _catalog);
        var rows = table.RowsWhere([nameIdx],
            values => string.Equals(values[nameIdx] as string, name, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach ((RowId id, object?[] values) in rows)
        {
            foreach (IndexDef index in msys.RealIndexes)
                table.RemoveIndexEntry(index, values, id);
            new RowInserter(table.Channel, msys).Delete(id);
        }
    }

    /// <summary>The name text of a logical index's TDEF name entry.</summary>
    private string NameOf(byte[] nameEntry) =>
        TableDefinition.ReadName(new PageBuffer(nameEntry, 0), 0, "logical index", _channel.Format).Name;


    /// <summary>Advances the object's <c>MSysObjects.DateUpdate</c>, leaving <c>DateCreate</c> where it is —
    /// measured: an ACE <c>ALTER TABLE … ADD COLUMN</c> moves the one and not the other. A no-op for an object
    /// with no catalog row yet, which is every table mid-CREATE.</summary>
    private void TouchObject(int objectId)
    {
        // Stamping MSysObjects from its own update path would recurse.
        if (_catalog.FindTable("MSysObjects") is { } msys && msys.DefinitionPage == objectId) return;
        UpdateCatalogRows("MSysObjects", "Id", objectId, required: false, ("DateUpdate", DateTime.Now));
    }

    private void WriteTdef(int tdefPage, TableDefinition.Parts parts)
    {
        TableDefinition.WriteParts(_channel, _allocator, tdefPage, parts);
        // Every definition edit also advances its catalog timestamp.
        TouchObject(tdefPage);
    }

    /// <summary>The data-block ordinal of a table's own index over the FK's referenced columns (for a
    /// self-reference — normally the primary key).</summary>
    private static int ReferencedOrdinalIn(TableDefinition table, RelationshipSpec fk) =>
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
        IReadOnlyList<IndexDef> candidates, IReadOnlyList<string> refColumns, string parentTable) =>
        candidates[ParentKeyPosition(
            [.. candidates.Select(ix => (ix.Columns.Select(c => c.Column.Name), ix.IsUnique || ix.IsPrimaryKey))],
            refColumns, parentTable)];

    /// <summary>Where among <paramref name="candidates"/> — each an index's columns and whether it is unique or
    /// primary — the parent-side key of <see cref="FindParentKeyIndex"/> is.</summary>
    private static int ParentKeyPosition(
        IReadOnlyList<(IEnumerable<string> Columns, bool IsKey)> candidates, IReadOnlyList<string> refColumns,
        string parentTable)
    {
        bool MatchesColumns((IEnumerable<string> Columns, bool IsKey) ix) =>
            ix.Columns.SequenceEqual(refColumns, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < candidates.Count; i++)
            if (MatchesColumns(candidates[i]) && candidates[i].IsKey) return i;

        // Distinguish "no index at all" from "an index, but not a unique one" — ACE's own message names the
        // second case, and it is the one a caller can fix by declaring the key unique.
        throw new InvalidOperationException(candidates.Any(MatchesColumns)
            ? $"No unique index found for the referenced field of the primary table: '{parentTable}' "
              + $"({string.Join(", ", refColumns)}) is indexed, but not uniquely."
            : $"Referenced table '{parentTable}' has no index over ({string.Join(", ", refColumns)}).");
    }


    /// <summary>
    /// Adds an incoming-relationship logical index-info block (§3.6) to a parent table's already-written
    /// TDEF: it reuses the parent's referenced-key data block (no new data block), links back to the
    /// child's outgoing block, and grows the logical-index list by one (kept name-sorted). The definition is
    /// rewritten through <see cref="WriteTdef"/>, so it may span or spill onto continuation pages.
    /// </summary>
    private void AddIncomingRelationshipBlock(IncomingRelationship inc)
    {
        TableDefinition.Parts parts = TableDefinition.ReadParts(_channel, inc.ParentPage); // stitches continuation pages for a multi-page TDEF

        // An incoming relationship adds a logical block and no data block, so this is the path a referenced
        // table overruns: 0x33 stays where it was while 0x2F climbs with every table that points here.
        EnsureIndexCapacity(
            _catalog.TableWithId(inc.ParentPage)?.Name ?? $"page {inc.ParentPage}",
            "an incoming relationship", parts.DataBlocks.Count, parts.Logical.Count + 1);

        string newName = HiddenRelationshipName(inc.Number);
        InsertLogicalBlock(parts, newName, inc.Spec(newName));

        WriteTdef(inc.ParentPage, parts);
    }

    /// <summary>Inserts a logical block and its name where they sort: by name, in the database's collation (§3.6).</summary>
    private void InsertLogicalBlock(TableDefinition.Parts parts, string name, TableDefinition.LogicalIndexSpec spec)
    {
        JetTextComparer names = JetTextComparer.For(_collation);
        int k = parts.Logical.Count(b => names.Compare(NameOf(b.Name), name) < 0);
        parts.Logical.Insert(k, (TableDefinition.BuildInfoBlock(_channel.Format, spec), TableDefinition.NameEntry(name, _channel.Format)));
    }

    /// <summary>An incoming block's hidden name: <c>.r</c> and index_num in base 16 with the letters <c>A</c>–<c>P</c>
    /// as digits, least significant first, as ACE names it (verified in CREATE TABLE for 1–30: 1 → <c>.rB</c>,
    /// 15 → <c>.rP</c>, 16 → <c>.rAB</c>, 30 → <c>.rOB</c>; in ALTER TABLE for 1–3).</summary>
    private static string HiddenRelationshipName(int number)
    {
        var name = new System.Text.StringBuilder(".r");
        do
        {
            name.Append((char)('A' + number % 16));
            number /= 16;
        }
        while (number > 0);
        return name.ToString();
    }


    // A user table's owner + grantee SIDs — Users owns user objects; Users + Admin get the grants — read from
    // the file being written, since an on-disk SID is masked per file (page-00 §2.3). Baking in the pair this
    // engine creates files with meant a table added to an ACE-authored file carried an owner that decodes to
    // no account in it (ACE writes 690C in Northwind where LibRed wrote its own 26CD).
}