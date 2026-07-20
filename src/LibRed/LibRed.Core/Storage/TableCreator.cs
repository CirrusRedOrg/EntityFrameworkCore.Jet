using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Creates a new (heap) table in an existing database: allocates and writes its TDEF page, an
/// empty data page, and an owned-pages usage map, then records it in MSysObjects so the catalog
/// finds it. This first cut creates a no-index table and writes the catalog row heap-only (the
/// catalog is read by table scan), enough for LibRed to round-trip create → insert → query.
/// </summary>
public sealed class TableCreator(PageChannel channel, JetCatalog catalog, Collation? collation = null)
{
    private readonly PageChannel _channel = channel;
    private readonly JetCatalog _catalog = catalog;
    private readonly PageAllocator _allocator = new(channel);

    // The database's default collating order, written into new non-numeric columns. Defaults to General
    // legacy for callers that don't create columns (most alter operations).
    private readonly Collation _collation = collation ?? Collation.GeneralLegacy;

    public void Create(
        string name,
        IReadOnlyList<ColumnSpec> columns,
        IReadOnlyList<string>? primaryKey = null,
        IReadOnlyList<RelationshipSpec>? relationships = null,
        IReadOnlyList<UniqueIndexSpec>? uniqueConstraints = null,
        IReadOnlyList<(string Column, string DefaultSql)>? columnDefaults = null,
        IReadOnlyList<(string Name, string Expression)>? checkConstraints = null,
        string? primaryKeyName = null)
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
        foreach ((string checkName, _) in checkConstraints) JetName.Validate(checkName, "check constraint name");

        // A table name is unique (case-insensitively) across the database; reject a duplicate rather
        // than writing a second MSysObjects row that shadows the existing table.
        if (_catalog.FindTable(name) is not null)
            throw new InvalidOperationException($"Table '{name}' already exists.");

        // Jet/ACE caps a table at 255 columns. The count/id fields are 2 bytes wide so we could physically
        // write more, but Access would refuse to open the table — fail early with a clear message instead.
        if (columns.Count > MaxColumnsPerTable)
            throw new InvalidOperationException(
                $"Table '{name}' has {columns.Count} columns; Jet/ACE tables are limited to {MaxColumnsPerTable}.");

        JetFormatBase format = _channel.Format;

        // Allocate the pages the table needs through the global free-pages map (so Access accounts
        // for them). Like Access, a fresh table has NO data page — the first is allocated lazily on
        // the first insert — so its usage maps start empty.
        int tdefPage = _allocator.Allocate();
        int usageMapPage = _allocator.Allocate();

        var longValueCols = columns.Select((c, i) => (Column: c, Id: i))
            .Where(x => x.Column.Type is JetDataType.Memo or JetDataType.Ole)
            .ToList();

        // The table's data-block indexes: the primary key (unique), then a unique index per UNIQUE
        // constraint, then one non-unique index per foreign key over its child columns — Access enforces
        // a relationship through an index on the FK columns. Each carries the relationship (if any) it backs.
        var indexPlans = new List<(string Name, IReadOnlyList<string> Columns, bool IsPk, bool IsUnique, RelationshipSpec? Fk)>();
        if (primaryKey is { Count: > 0 })
            // Name the PK index after the CONSTRAINT if one was given (ACE does the same, and the scaffolder
            // round-trips it). If unnamed, LibRed picks the stable "PrimaryKey" (the DAO/Access-UI convention)
            // — an engine choice, since ACE-via-SQL instead generates a random "Index_<hex>" with no fixed
            // value to reproduce, and nothing downstream depends on the exact name.
            indexPlans.Add((primaryKeyName ?? "PrimaryKey", primaryKey, true, true, null));
        foreach (UniqueIndexSpec unique in uniqueConstraints)
            indexPlans.Add((unique.Name, unique.Columns, false, true, null));
        foreach (RelationshipSpec fk in relationships)
            indexPlans.Add((fk.Name, fk.Columns.Select(c => c.Column).ToList(), false, false, fk));

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

        // §3.3.2 entries: a long-value column's maps are on the primary page (if it fit) or a dedicated page.
        var longValueSpecs = new List<LongValueColumnSpec>(longValueCols.Count);
        for (int j = 0; j < longValueCols.Count; j++)
        {
            int colId = longValueCols[j].Id;
            if (j < colsOnPrimary)
                longValueSpecs.Add(new LongValueColumnSpec(
                    colId, UsedRow: primaryRecords + 2 * j, FreeRow: primaryRecords + 2 * j + 1, MapPage: usageMapPage));
            else
            {
                int columnMapPage = _allocator.Allocate();
                WriteUsageMaps(format, columnMapPage, mapCount: 2); // owned = row 0, free = row 1
                longValueSpecs.Add(new LongValueColumnSpec(colId, UsedRow: 0, FreeRow: 1, MapPage: columnMapPage));
            }
        }

        // Each index is an empty leaf root, populated as rows are inserted. Its usage map is on the primary
        // page right after the two data-page maps (row 2 + i).
        var indexes = new List<IndexSpec>(indexPlans.Count);
        for (int i = 0; i < indexPlans.Count; i++)
        {
            var plan = indexPlans[i];
            int rootPage = _allocator.Allocate();
            WriteEmptyLeafIndexPage(format, rootPage, owner: tdefPage);
            // Record the root in the index's own pages usage map — Access does this at CREATE, before any
            // row exists (verified: a freshly created empty index has exactly its root bit set). As the tree
            // grows, IndexWriter adds each page it allocates, so the map covers the whole B-tree.
            new UsageMapWriter(_channel).SetBit(2 + i, usageMapPage, rootPage, set: true);
            indexes.Add(new IndexSpec(plan.Name, plan.Columns, plan.IsPk, plan.IsUnique,
                rootPage, UsageMapRow: 2 + i, UsageMapPage: usageMapPage));
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
            byte upd = fk.CascadeUpdate ? CascadeAction : NoCascadeAction;
            byte del = fk.CascadeDelete ? CascadeAction : fk.DeleteSetNull ? SetNullAction : NoCascadeAction;
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
                    Type: IndexBlockFormat.TypeForeign, Name: NextHiddenRelationshipName(childLogical.Select(l => l.Name).ToList())));
                continue;
            }

            (int parentPage, int refOrd, int parentLogicalCount) = ResolveParent(fk, tdefPage);
            int parentNum = parentLogicalCount + parentAdds.GetValueOrDefault(parentPage);
            parentAdds[parentPage] = parentAdds.GetValueOrDefault(parentPage) + 1;

            childLogical.Add(new TdefBuilder.LogicalIndexSpec(
                Number: i, DataOrdinal: i, FkType: outgoingType,
                FkNumber: (uint)parentNum, FkTablePage: parentPage, UpdateAction: upd, DeleteAction: del,
                Type: IndexBlockFormat.TypeForeign, Name: fk.Name));
            incoming.Add(new IncomingRelationship(parentPage, parentNum, refOrd,
                ChildBlockNumber: (uint)i, ChildPage: tdefPage, upd, del));
        }

        // Access stores logical blocks sorted by name (with their names in the same order).
        childLogical.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        // Build the definition and point it at the usage maps: owned-pages = row 0, free-pages =
        // row 1, both on the usage-map page.
        byte[] tdef = TdefBuilder.Build(format, TableType.User, columns, indexes, longValueSpecs, childLogical, _collation).Page;
        tdef[format.TdefOwnedPagesOffset] = 0; // owned map record row
        WriteInt24(tdef, format.TdefOwnedPagesOffset + 1, usageMapPage);
        tdef[format.TdefFreePagesOffset] = 1; // free map record row
        WriteInt24(tdef, format.TdefFreePagesOffset + 1, usageMapPage);
        // A wide table's definition can exceed one page; write it split across continuation pages if needed.
        int defEnd = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(format.TdefLengthOffset, 4));
        WriteDefinition(tdefPage, tdef[..defEnd], []);

        // Per-column extended properties, in column order with DefaultValue before Required (matching ACE):
        // a DEFAULT is a memo property; a NOT NULL column carries a boolean Required property (Access omits
        // it for a nullable column, and — verified — for an AutoNumber, which is implicitly required).
        var columnProps = new List<PropertyBlob.Property>();
        foreach (ColumnSpec col in columns)
        {
            var def = columnDefaults.FirstOrDefault(d => string.Equals(d.Column, col.Name, StringComparison.OrdinalIgnoreCase));
            if (def.DefaultSql is not null)
                columnProps.Add(new PropertyBlob.Property(col.Name, PropertyBlob.DefaultValueProperty, def.DefaultSql));
            if (!col.IsNullable && !col.IsAutoNumber)
                columnProps.Add(PropertyBlob.Bool(col.Name, PropertyBlob.RequiredProperty, true));
        }

        AddCatalogRow(name, tdefPage, columnProps, checkConstraints);
        AddPermissionRows(tdefPage);
        foreach (RelationshipSpec fk in relationships)
            AddRelationshipRows(name, fk);
        foreach (IncomingRelationship inc in incoming)
            AddIncomingRelationshipBlock(inc);
    }

    // Index-info block field values (§3.6), verified against ACE-created relationships.
    // (PlainAction and the index-type bytes are shared with the reader via IndexBlockFormat.)
    private const byte NoCascadeAction = 0x00;    // relationship without ON UPDATE/DELETE CASCADE
    private const byte CascadeAction = 0x01;       // relationship with cascade
    private const byte SetNullAction = 0x02;       // ON DELETE SET NULL (verified vs ACE, index-info block +0x16)

    /// <summary>ON UPDATE SET NULL pathway: the docs list it, but the ACE OLE DB provider rejects it via SQL,
    /// so its on-disk storage (the grbit flag + the index-info +0x15 action byte) is unverified. Rather than
    /// guess the bytes, fail loudly until a UI/DAO-created sample can be probed.</summary>
    private static NotImplementedException UpdateSetNullNotImplemented() => new(
        "ON UPDATE SET NULL is not implemented: its Jet storage bytes are unverified (the ACE OLE DB provider " +
        "rejects the DDL, so they could not be probed). Only ON UPDATE {NO ACTION | CASCADE} are supported.");
    private const byte FkTypeIncoming = 0x01;         // this table is the parent/referenced end
    private const byte FkTypeOutgoing = 0x02;         // this table is the child/referencing end (indexed)
    private const byte FkTypeOutgoingNoIndex = 0x03;  // child/referencing end declared FOREIGN KEY NO INDEX

    /// <summary>An incoming-relationship logical block to add to a parent table's TDEF.</summary>
    private readonly record struct IncomingRelationship(
        int ParentPage, int Number, int ReferencedOrdinal, uint ChildBlockNumber, int ChildPage,
        byte UpdateAction, byte DeleteAction);

    /// <summary>The data-block ordinal of the index over a self-reference's referenced columns, found
    /// among the indexes being created for this table (the table is not in the catalog yet).</summary>
    private static int SelfReferencedOrdinal(
        List<(string Name, IReadOnlyList<string> Columns, bool IsPk, bool IsUnique, RelationshipSpec? Fk)> indexPlans,
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
    /// index over the referenced columns (normally the PK), and the parent's current logical-index count
    /// (used to number the incoming block we will add). Self-references are handled by the caller before
    /// this is reached (the table is not yet in the catalog).
    /// </summary>
    private (int Page, int ReferencedOrdinal, int LogicalCount) ResolveParent(RelationshipSpec fk, int childPage)
    {
        TableDef parent = _catalog.FindTable(fk.ReferencedTable)
            ?? throw new InvalidOperationException($"Referenced table '{fk.ReferencedTable}' was not found.");
        if (parent.DefinitionPage == childPage)
            throw new InvalidOperationException($"Self-referencing foreign key '{fk.Name}' should have been handled inline.");

        var ptdef = new Pages.TableDefinitionPage();
        ptdef.Read(_channel, parent.DefinitionPage);
        var refColumns = fk.Columns.Select(c => c.ReferencedColumn).ToList();
        IndexDef refIndex = ptdef.Indexes.FirstOrDefault(ix =>
                ix.Columns.Select(c => c.Column.Name).SequenceEqual(refColumns, StringComparer.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Referenced table '{fk.ReferencedTable}' has no index over ({string.Join(", ", refColumns)}).");
        return (parent.DefinitionPage, refIndex.RealIndexOrdinal, ptdef.RealIndexCount);
    }

    /// <summary>
    /// Writes the <c>MSysRelationships</c> rows for one relationship — one row per column pair, with
    /// <c>ccolumn</c> = the pair count, <c>icolumn</c> = the 0-based pair index, and <c>grbit</c>
    /// encoding enforce/cascade (verified against Access: an enforced no-cascade FK stores grbit 0).
    /// </summary>
    private void AddRelationshipRows(string childTable, RelationshipSpec fk)
    {
        TableDef msys = _catalog.FindTable("MSysRelationships")
            ?? throw new InvalidOperationException("MSysRelationships catalog table was not found.");

        int grbit = 0;
        if (!fk.IsEnforced) grbit |= RelationshipFlags.DontEnforce;
        if (fk.CascadeUpdate) grbit |= RelationshipFlags.UpdateCascade;
        if (fk.CascadeDelete) grbit |= RelationshipFlags.DeleteCascade;
        if (fk.DeleteSetNull) grbit |= RelationshipFlags.DeleteSetNull;

        for (int i = 0; i < fk.Columns.Count; i++)
        {
            var (column, referencedColumn) = fk.Columns[i];
            var values = new object?[msys.Columns.Count];
            SetByName(msys, values, "szRelationship", fk.Name);
            SetByName(msys, values, "szObject", childTable);
            SetByName(msys, values, "szColumn", column);
            SetByName(msys, values, "szReferencedObject", fk.ReferencedTable);
            SetByName(msys, values, "szReferencedColumn", referencedColumn);
            SetByName(msys, values, "ccolumn", fk.Columns.Count);
            SetByName(msys, values, "icolumn", i);
            SetByName(msys, values, "grbit", grbit);
            new RowInserter(_channel, msys).Insert(values, updateIndexes: true);
        }
    }


    /// <summary>
    /// Adds an index to an existing (empty) table for CREATE INDEX. Surgically inserts a statistics
    /// block, an index-data block and a logical index-info block into the TDEF (preserving the existing
    /// columns, indexes, relationship linkage and long-value entries byte-for-byte), grows the usage-map
    /// page by one row and writes an empty B-tree root. Single-page, empty-table only.
    /// </summary>
    public void AddIndex(string tableName, string indexName, IReadOnlyList<(string Column, bool Descending)> columns,
        bool isUnique, bool isPrimary, bool disallowNull, bool ignoreNulls)
    {
        JetName.Validate(indexName, "index name");
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");
        var slots = ResolveSlots(table, columns.Select(c => (c.Column, Ascending: !c.Descending)));
        InsertIndex(table, indexName, slots,
            unique: isUnique || isPrimary, required: isPrimary || disallowNull, ignoreNulls,
            (num, ord) => BuildPlainInfoBlock(num, ord, isPrimary));
    }

    /// <summary>Resolves index column names to (columnId, ascending) slots against a table.</summary>
    private static IReadOnlyList<(int Id, bool Ascending)> ResolveSlots(
        TableDef table, IEnumerable<(string Column, bool Ascending)> columns)
    {
        var byName = table.Columns.ToDictionary(c => c.Name, c => c.ColumnId, StringComparer.OrdinalIgnoreCase);
        return columns.Select(c => byName.TryGetValue(c.Column, out int id) ? (Id: id, c.Ascending)
            : throw new InvalidOperationException($"Column '{c.Column}' does not exist in '{table.Name}'.")).ToList();
    }

    /// <summary>Surgically inserts one data index and its logical info block into an existing (empty)
    /// table's TDEF, name-sorted. <paramref name="buildInfo"/> gets (block number, data-block ordinal) and
    /// returns the 28-byte info block — a plain index or an outgoing-FK block. Returns the new block number.</summary>
    private int InsertIndex(TableDef table, string indexName, IReadOnlyList<(int Id, bool Ascending)> slots,
        bool unique, bool required, bool ignoreNulls, Func<int, int, byte[]> buildInfo)
    {
        JetFormatBase format = _channel.Format;

        // Read the whole definition (stitching any existing continuation pages) so the surgical insert
        // works in absolute coordinates; the old continuation pages are reused when we write it back.
        (LibRed.IO.PageBuffer buf, IReadOnlyList<int> existingContinuations) = ReadDefinition(table.DefinitionPage);
        int existingRowCount = buf.ReadInt32(format.TdefRowCountOffset);

        int dataCount = buf.ReadInt32(format.TdefIndexCountOffset);
        int logicalCount = buf.ReadInt32(format.TdefRealIndexCountOffset);
        int colCount = buf.ReadUInt16(format.TdefColumnCountOffset);

        // Walk the TDEF regions: stats -> column descriptors -> column names -> data blocks -> info blocks.
        int afterStats = format.TdefRealIndexBlockOffset + dataCount * format.RealIndexEntrySize;
        int pos = afterStats + colCount * format.ColumnDescriptorSize;
        for (int i = 0; i < colCount; i++) pos += 2 + buf.ReadUInt16(pos);
        int afterColumns = pos;                                   // start of the data blocks
        int afterDataBlocks = afterColumns + dataCount * IndexBlockFormat.DataBlockSize;
        int infoStart = afterDataBlocks;

        // Existing logical blocks and names, plus the max index_num, so the new block gets a fresh number.
        int namePos = infoStart + logicalCount * IndexBlockFormat.InfoBlockSize;
        var blocks = new List<byte[]>(logicalCount + 1);
        int maxNum = -1;
        for (int i = 0; i < logicalCount; i++)
        {
            byte[] block = buf.Slice(infoStart + i * IndexBlockFormat.InfoBlockSize, IndexBlockFormat.InfoBlockSize).ToArray();
            maxNum = Math.Max(maxNum, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(4, 4)));
            blocks.Add(block);
        }
        var names = new List<string>(logicalCount + 1);
        var nameBytes = new List<byte[]>(logicalCount + 1);
        for (int i = 0; i < logicalCount; i++)
        {
            int len = buf.ReadUInt16(namePos);
            nameBytes.Add(buf.Slice(namePos, 2 + len).ToArray());
            names.Add(System.Text.Encoding.Unicode.GetString(buf.Slice(namePos + 2, len)));
            namePos += 2 + len;
        }

        int defEnd = buf.ReadInt32(format.TdefLengthOffset);
        byte[] lvalRegion = buf.Slice(namePos, defEnd - namePos).ToArray(); // §3.3.2 list + 0xFFFF terminator
        int lvalCount = (lvalRegion.Length - 2) / 10;                        // 10 bytes per entry, then 0xFFFF

        // Allocate the new index's root (empty leaf) and its usage-map row (appended after existing rows).
        int rootPage = _allocator.Allocate();
        WriteEmptyLeafIndexPage(format, rootPage, owner: table.DefinitionPage);
        int usageMapPage = ReadInt24(buf, format.TdefOwnedPagesOffset + 1);
        int newIndexUsageRow = 2 + lvalCount * 2 + dataCount;
        // Append the new index's (empty) usage-map row, preserving every existing record. The data maps are
        // empty on an empty table but the *existing indexes'* maps already carry their root bits (set at
        // creation), so we must not rewrite the page from scratch even when the table has no rows.
        AppendEmptyUsageMapRow(format, usageMapPage, newIndexUsageRow);

        // Record this index's own root, as Access does at CREATE INDEX (the empty root is the index's sole
        // page until it splits, after which IndexWriter adds each page it allocates).
        new UsageMapWriter(_channel).SetBit(newIndexUsageRow, usageMapPage, rootPage, set: true);

        // Assemble the new definition: header + existing stats, a new stats block, columns + names +
        // existing data blocks, the new data block, then the logical blocks (new one inserted, name-sorted)
        // and their names, and finally the unchanged long-value region.
        byte[] newData = BuildIndexDataBlock(slots, rootPage, newIndexUsageRow, usageMapPage, unique, required, ignoreNulls);
        byte[] newInfo = buildInfo(maxNum + 1, dataCount);

        int k = names.Count(n => string.CompareOrdinal(n, indexName) < 0); // name-sorted insert position
        blocks.Insert(k, newInfo);
        nameBytes.Insert(k, EncodeName(indexName));

        int newDefEnd = infoStart + IndexBlockFormat.DataBlockSize          // one new data block shifts info start
                        + blocks.Count * IndexBlockFormat.InfoBlockSize + nameBytes.Sum(n => n.Length) + lvalRegion.Length
                        + format.RealIndexEntrySize;             // one new stats block at the front

        // Build the full definition buffer (may exceed one page — split across continuation pages below).
        var def = new byte[newDefEnd];
        var src = buf.Span;
        int w = 0;
        void Append(ReadOnlySpan<byte> s) { s.CopyTo(def.AsSpan(w)); w += s.Length; }

        Append(src[..afterStats]);                              // header + existing stats blocks
        Append(new byte[format.RealIndexEntrySize]);            // new (zero) stats block
        Append(src[afterStats..afterDataBlocks]);               // columns + names + existing data blocks
        Append(newData);                                        // new index-data block
        foreach (byte[] b in blocks) Append(b);                 // logical blocks (new inserted, sorted)
        foreach (byte[] n in nameBytes) Append(n);              // their names, same order
        Append(lvalRegion);                                     // §3.3.2 list + terminator (unchanged)

        // Bump the two index counts and the definition length in the header.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(def.AsSpan(format.TdefIndexCountOffset, 4), dataCount + 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(def.AsSpan(format.TdefRealIndexCountOffset, 4), logicalCount + 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(def.AsSpan(format.TdefLengthOffset, 4), newDefEnd);

        WriteDefinition(table.DefinitionPage, def, existingContinuations);
        _catalog.Invalidate();

        // Back-fill the new (empty) index B-tree with an entry per existing row, so the index is complete.
        if (existingRowCount != 0)
            BackfillIndex(table.Name, indexName, ignoreNulls);
        return maxNum + 1;
    }

    /// <summary>Populates a freshly added index over a table's existing rows: scans every live row and
    /// inserts its key (IndexWriter handles B-tree growth). Rows with a null in an IGNORE NULL index's key
    /// are skipped, matching the per-insert path.</summary>
    private void BackfillIndex(string tableName, string indexName, bool ignoreNulls)
    {
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found after adding the index.");
        IndexDef index = table.Indexes.First(ix => string.Equals(ix.Name, indexName, StringComparison.OrdinalIgnoreCase));
        var keyColumnIds = index.Columns.Select(c => c.Column.Index).ToArray();
        var writer = new IndexWriter(_channel, table);

        foreach ((RowId id, object?[] values) in new Table(_channel, table).Rows().WithIds())
        {
            if (ignoreNulls && keyColumnIds.Any(i => values[i] is null)) continue;
            writer.AddEntry(index, values, id);
        }
    }

    /// <summary>Appends one empty inline usage-map record (row <paramref name="newRow"/>) to an existing
    /// usage-map page, preserving every existing record. The new index tracks no pages here (IndexWriter
    /// navigates the B-tree structurally), so an empty bitmap is correct.</summary>
    private void AppendEmptyUsageMapRow(JetFormatBase format, int pageNumber, int newRow)
    {
        const int MapLength = 1 + 4 + 64; // inline type + start page + 64-byte bitmap (matches WriteUsageMaps)
        byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2));

        // Reuse a row slot left orphaned by a prior index drop — DropConstraint/DropIndex leave the dropped
        // index's usage-map row in place (matching ACE), and a re-added index takes the same slot number. Clear
        // its bitmap in place rather than appending a duplicate. (Needed when a parent-side ALTER COLUMN rewrite
        // drops and re-adds a child's foreign key.)
        if (newRow < rowCount)
        {
            int existing = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + newRow * 2, 2));
            Array.Clear(page, existing, MapLength);
            _channel.WritePage(pageNumber, page);
            return;
        }
        if (rowCount != newRow)
            throw new InvalidOperationException(
                $"Usage-map page has {rowCount} rows; expected {newRow} before appending the new index's map.");

        int minOffset = format.PageSize;
        for (int r = 0; r < rowCount; r++)
            minOffset = Math.Min(minOffset, BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + r * 2, 2)));

        int newOffset = minOffset - MapLength;
        Array.Clear(page, newOffset, MapLength); // inline type 0x00, start page 0, zero bitmap
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + newRow * 2, 2), (ushort)newOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)(rowCount + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(newOffset - format.DataRowDirectoryOffset - (rowCount + 1) * 2));
        _channel.WritePage(pageNumber, page);
    }

    /// <summary>
    /// Adds a foreign key to an existing (empty) child table: a backing non-unique index over the child
    /// columns carrying an outgoing-relationship block, an incoming block on the parent's TDEF, and the
    /// MSysRelationships rows. The child index and parent block are written the same way inline-FK creation
    /// does (verified byte-faithful vs ACE). A self-reference hosts both ends in the one table; FOREIGN KEY
    /// NO INDEX is not yet handled.
    /// </summary>
    public void AddForeignKey(string childTable, RelationshipSpec fk)
    {
        JetName.Validate(fk.Name, "foreign key name");
        TableDef child = _catalog.FindTable(childTable)
            ?? throw new InvalidOperationException($"Table '{childTable}' was not found.");
        if (fk.NoIndex)
            throw new NotSupportedException("ALTER TABLE ADD FOREIGN KEY … NO INDEX is not supported yet.");
        if (fk.UpdateSetNull) throw UpdateSetNullNotImplemented();

        byte upd = fk.CascadeUpdate ? CascadeAction : NoCascadeAction;
        byte del = fk.CascadeDelete ? CascadeAction : fk.DeleteSetNull ? SetNullAction : NoCascadeAction;
        var slots = ResolveSlots(child, fk.Columns.Select(c => (c.Column, Ascending: true)));

        // A self-reference (child == parent) hosts both ends in the same TDEF: the outgoing block links to
        // an incoming block whose number is one past the outgoing block's (mirrors inline self-ref creation).
        if (string.Equals(fk.ReferencedTable, childTable, StringComparison.OrdinalIgnoreCase))
        {
            int selfRefOrdinal = ReferencedOrdinalIn(child, fk);
            int outNum = InsertIndex(child, fk.Name, slots,
                unique: false, required: false, ignoreNulls: false,
                (num, ord) => BuildOutgoingInfoBlock(num, ord, FkTypeOutgoing, num + 1, child.DefinitionPage, upd, del));
            AddIncomingRelationshipBlock(new IncomingRelationship(
                child.DefinitionPage, outNum + 1, selfRefOrdinal, (uint)outNum, child.DefinitionPage, upd, del));
            AddRelationshipRows(childTable, fk);
            _catalog.Invalidate();
            return;
        }

        (int parentPage, int refOrdinal, int parentLogicalCount) = ResolveParent(fk, child.DefinitionPage);
        int parentNum = parentLogicalCount; // the incoming block gets the next free logical number on the parent
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

            new PageAllocator(_channel).Free(fkIndex.RootPage);
        }

        SoftDeleteRelationshipRows(name);
        _catalog.Invalidate();
        return true;
    }

    /// <summary>
    /// Drops a table — <c>DROP TABLE table</c>. Removes the object's <c>MSysObjects</c> row and its
    /// <c>MSysACEs</c> permission rows (soft-delete, as ACE does), and frees the table's pages back to the
    /// global free map so a later create reuses them (verified vs ACE): its index B-tree roots, its data
    /// pages (owned-pages usage map), and the TDEF page. Returns false if the table doesn't exist.
    ///
    /// A table that is the <em>child</em> (referencing) side of relationships can be dropped directly: ACE
    /// lets you drop the referencing table while the parent stays, so each such relationship is removed first
    /// (via <see cref="DropConstraint"/>). But a table still <em>referenced as a parent</em> by a surviving
    /// child cannot be dropped — drop the referencing table (or the relationship) first. EF drops FKs before
    /// tables, but database-first scaffolding cleanup drops child tables directly, which must work.
    /// </summary>
    /// <remarks>Multi-page TDEFs, multi-level index trees (non-root pages), memo/OLE LVAL pages and dedicated
    /// usage-map pages are not yet freed (they leak until Compact); the catalog removal is complete regardless,
    /// so the table disappears and Access opens the file.</remarks>
    public bool DropTable(string tableName)
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

        // Re-fetch: DropConstraint above rewrote this table's TDEF (removed FK indexes) and invalidated the catalog.
        table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");

        int tdefPage = table.DefinitionPage;
        var allocator = new PageAllocator(_channel);
        foreach (IndexDef index in table.Indexes.Where(i => i.RootPage > 0).GroupBy(i => i.RootPage).Select(g => g.First()))
            allocator.Free(index.RootPage);
        foreach (int dataPage in new UsageMap(_channel, table).DataPages())
            allocator.Free(dataPage);
        allocator.Free(tdefPage);

        DeleteCatalogRows("MSysObjects", "Id", tdefPage);
        DeleteCatalogRows("MSysACEs", "ObjectId", tdefPage);
        _catalog.Invalidate();
        return true;
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
        TableDef mo = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");
        int idIdx = (mo.FindColumn("Id") ?? throw new InvalidOperationException("MSysObjects is missing 'Id'.")).Index;
        int nameIdx = (mo.FindColumn("Name") ?? throw new InvalidOperationException("MSysObjects is missing 'Name'.")).Index;
        int typeIdx = (mo.FindColumn("Type") ?? throw new InvalidOperationException("MSysObjects is missing 'Type'.")).Index;

        int? objId = null;
        foreach (object?[] values in new Table(_channel, mo).Rows())
            if (string.Equals(values[nameIdx] as string, name, StringComparison.OrdinalIgnoreCase)
                && Convert.ToInt16(values[typeIdx] ?? (short)0) == StoredQueryFormat.ObjectTypeQuery)
            { objId = Convert.ToInt32(values[idIdx]); break; }
        if (objId is null) return false;

        DeleteCatalogRows("MSysObjects", "Id", objId.Value);
        DeleteCatalogRows("MSysQueries", "ObjectId", objId.Value);
        DeleteCatalogRows("MSysACEs", "ObjectId", objId.Value);
        _catalog.Invalidate();
        return true;
    }


    /// <summary>Deletes every row of <paramref name="catalogTable"/> whose <paramref name="keyColumn"/> equals
    /// <paramref name="keyValue"/> (the object id) — used to remove a dropped table's MSysObjects and MSysACEs
    /// rows. A full delete: its **index entries are removed** (not just the slot soft-deleted) so, e.g., the
    /// MSysObjects <c>ParentIdName</c> unique index doesn't retain a stale entry that would then reject
    /// re-creating a same-named table.</summary>
    private void DeleteCatalogRows(string catalogTable, string keyColumn, int keyValue)
    {
        TableDef t = _catalog.FindTable(catalogTable)
            ?? throw new InvalidOperationException($"{catalogTable} catalog table was not found.");
        int idx = (t.FindColumn(keyColumn) ?? throw new InvalidOperationException($"{catalogTable} is missing '{keyColumn}'.")).Index;
        var table = new Table(_channel, t);

        var rows = table.Rows().WithIds()
            .Where(r => r.Values[idx] is not null && Convert.ToInt32(r.Values[idx]) == keyValue)
            .ToList();
        foreach ((RowId id, object?[] values) in rows)
        {
            foreach (IndexDef index in t.Indexes.Where(i => i.RootPage > 0).GroupBy(i => i.RootPage).Select(g => g.First()))
                table.RemoveIndexEntry(index, values, id);
            table.Delete(id);
        }
    }

    /// <summary>
    /// Drops a secondary/unique/primary index — <c>DROP INDEX index ON table</c>. Byte-faithful with ACE
    /// (probed): remove the index's 12-byte stats block, 52-byte index-data block, and its 28-byte logical
    /// info block + name from the TDEF (decrementing counts and the data-ordinal ref of any block past it),
    /// and free its B-tree root page back to the global free map — the same index-removal path as DROP
    /// CONSTRAINT, minus the relationship linkage. A secondary index lives only in the TDEF (no MSys row).
    /// Returns false if no such index exists. Throws if the index backs a relationship (ACE rejects that —
    /// drop the relationship first) or the TDEF is multi-page. (PK and unique indexes ARE droppable.)
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
        RemoveTdefBlocks(parts, removeDataOrdinal: index.RealIndexOrdinal,
            removeLogical: b => NameOf(b.Name).Equals(indexName, StringComparison.OrdinalIgnoreCase));
        WriteTdef(table.DefinitionPage, parts);
        new PageAllocator(_channel).Free(index.RootPage);
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
            throw new NotSupportedException($"Table '{tableName}' already has {MaxColumnsPerTable} columns (Jet/ACE limit).");
        JetFormatBase format = _channel.Format;
        bool isLongValue = spec.Type is JetDataType.Memo or JetDataType.Ole;
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
                "have been used over this table's lifetime (Jet/ACE caps the id high-water; a compact is required to reclaim dropped ids).");

        var newColumn = new ColumnDef
        {
            Name = spec.Name,
            Type = spec.Type,
            Index = colCount,
            ColumnId = maxCols, // next id from the high-water (dropped ids are never reused)
            Length = spec.Length,
            FixedOffset = spec.IsFixedLength
                ? table.Columns.Where(c => c.IsFixedLength).Select(c => c.FixedOffset + c.Length).DefaultIfEmpty(0).Max()
                : 0,
            VariableIndex = spec.IsFixedLength ? -1 : varCount,
            IsFixedLength = spec.IsFixedLength,
            IsAutoNumber = spec.IsAutoNumber,
            Precision = spec.Precision,
            Scale = spec.Scale,
            IsNullable = spec.IsNullable,
            Collation = spec.Type == JetDataType.FixedPoint ? Collation.GeneralLegacy : _collation,
        };

        AppendColumnToParts(parts, colCount, TdefBuilder.BuildColumnDescriptor(newColumn, format), spec.Name, format);

        BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefColumnCountOffset, 2), (ushort)(colCount + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefMaxColumnsOffset, 2), (ushort)(maxCols + 1));
        if (!spec.IsFixedLength)
            BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefVariableColumnsOffset, 2), (ushort)(varCount + 1));

        // A memo/OLE column needs a §3.3.2 usage-map entry (its owned + free page maps). ACE appends the two
        // maps to the table's existing usage-map page right after the data/index maps (verified), and adds the
        // 10-byte entry before the list's 0xFFFF terminator.
        int lvMapPage = 0, lvUsedRow = 0, lvFreeRow = 0;
        bool lvDedicated = false;
        if (isLongValue)
        {
            int o = format.TdefOwnedPagesOffset + 1;
            int primaryPage = parts.Header[o] | (parts.Header[o + 1] << 8) | (parts.Header[o + 2] << 16);
            byte[] primaryBytes = _channel.ReadPage(primaryPage).Span.ToArray();
            int primaryFree = BinaryPrimitives.ReadUInt16LittleEndian(primaryBytes.AsSpan(format.DataFreeSpaceOffset, 2));

            if (primaryFree >= 2 * (UsageMapRecordLength + 2))
            {
                // Room on the table's usage-map page — append the two maps there (as ACE does).
                lvMapPage = primaryPage;
                lvUsedRow = BinaryPrimitives.ReadUInt16LittleEndian(primaryBytes.AsSpan(format.DataRowCountOffset, 2));
                lvFreeRow = lvUsedRow + 1;
            }
            else
            {
                // Full — give the column its own usage-map page (owned = row 0, free = row 1), the same
                // fallback CREATE TABLE uses once its primary map page fills on a wide table.
                lvMapPage = _allocator.Allocate();
                lvUsedRow = 0; lvFreeRow = 1; lvDedicated = true;
            }
            AddLongValueMapEntry(parts, maxCols, lvUsedRow, lvFreeRow, lvMapPage);
        }

        WriteTdef(table.DefinitionPage, parts);

        if (isLongValue)
        {
            if (lvDedicated)
                WriteUsageMaps(format, lvMapPage, mapCount: 2); // owned = row 0, free = row 1, both empty
            else
            {
                AppendEmptyUsageMapRow(format, lvMapPage, lvUsedRow);
                AppendEmptyUsageMapRow(format, lvMapPage, lvFreeRow);
            }
        }

        // NOT NULL / DEFAULT go in the table's LvProp blob (DefaultValue before Required, matching ACE), the
        // same properties CREATE TABLE writes — appended to the existing blob without disturbing other columns'.
        var props = new List<PropertyBlob.Property>();
        if (defaultValue is not null) props.Add(new PropertyBlob.Property(spec.Name, PropertyBlob.DefaultValueProperty, defaultValue));
        if (!spec.IsNullable) props.Add(PropertyBlob.Bool(spec.Name, PropertyBlob.RequiredProperty, true));
        if (props.Count > 0) SetColumnProperties(table.DefinitionPage, spec.Name, props);

        _catalog.Invalidate();
        return true;
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

    /// <summary>Appends a column's extended properties (DefaultValue/Required) to its table's
    /// <c>MSysObjects.LvProp</c> blob and re-stores it — the add-side counterpart of
    /// <see cref="RemoveColumnProperties"/>.</summary>
    /// <summary>Sets (replaces) a column's <c>DefaultValue</c> in the table's <c>MSysObjects.LvProp</c> blob —
    /// ALTER TABLE … ALTER COLUMN … DEFAULT. Reads all properties, drops any existing DefaultValue for the
    /// column, adds the new one, and rewrites the blob (preserving every other property).</summary>
    public void SetColumnDefault(string tableName, string columnName, string defaultSql)
        => MutateLvPropForColumn(tableName, columnName, props =>
        {
            props.RemoveAll(p => string.Equals(p.Owner, columnName, StringComparison.OrdinalIgnoreCase)
                && p.Name == PropertyBlob.DefaultValueProperty);
            props.Add(new PropertyBlob.Property(columnName, PropertyBlob.DefaultValueProperty, defaultSql));
        });

    /// <summary>Removes a column's <c>DefaultValue</c> from the table's <c>MSysObjects.LvProp</c> blob —
    /// ALTER TABLE … ALTER COLUMN … DROP DEFAULT. Drops only that property, so the column's type and its
    /// <c>Required</c> (NOT NULL) property survive — ACE-verified. A no-op if the column had no default.</summary>
    public void DropColumnDefault(string tableName, string columnName)
        => MutateLvPropForColumn(tableName, columnName, props =>
            props.RemoveAll(p => string.Equals(p.Owner, columnName, StringComparison.OrdinalIgnoreCase)
                && p.Name == PropertyBlob.DefaultValueProperty));

    /// <summary>Sets or clears a column's <c>Required</c> (NOT NULL) property in the table's
    /// <c>MSysObjects.LvProp</c> blob — ALTER TABLE … ALTER COLUMN … NOT NULL / NULL. A required column carries
    /// a boolean <c>Required</c> property; a nullable one simply has none, so this drops any existing one and
    /// re-adds it only when <paramref name="required"/> (matching the CREATE-side write, and read back into
    /// <see cref="ColumnDef.IsNullable"/>). ACE-verified: ACE writes the same property for
    /// <c>ALTER COLUMN … NOT NULL</c> and enforces it.</summary>
    public void SetColumnRequired(string tableName, string columnName, bool required)
        => MutateLvPropForColumn(tableName, columnName, props =>
        {
            props.RemoveAll(p => string.Equals(p.Owner, columnName, StringComparison.OrdinalIgnoreCase)
                && p.Name == PropertyBlob.RequiredProperty);
            if (required) props.Add(PropertyBlob.Bool(columnName, PropertyBlob.RequiredProperty, true));
        });

    /// <summary>Reads the table's <c>MSysObjects.LvProp</c> property blob, applies <paramref name="mutate"/>,
    /// and rewrites it — the shared read-modify-write behind ALTER COLUMN … SET/DROP DEFAULT.</summary>
    private void MutateLvPropForColumn(string tableName, string columnName, Action<List<PropertyBlob.Property>> mutate)
    {
        TableDef target = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' does not exist.");
        int tdefPage = target.DefinitionPage;

        TableDef msys = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");
        int idIdx = (msys.FindColumn("Id") ?? throw new InvalidOperationException("MSysObjects is missing 'Id'.")).Index;
        ColumnDef lvProp = msys.FindColumn("LvProp") ?? throw new InvalidOperationException("MSysObjects is missing 'LvProp'.");
        var table = new Table(_channel, msys);

        foreach ((RowId id, object?[] values) in table.Rows().WithIds())
        {
            if (values[idIdx] is null || Convert.ToInt32(values[idIdx]) != tdefPage) continue;
            byte[] blob = values[lvProp.Index] as byte[] ?? [];
            var props = PropertyBlob.Read(blob).ToList();
            mutate(props);
            byte[] updated = PropertyBlob.Write(props);
            byte[] descriptor = new RowInserter(_channel, msys).StorePackedLongValue(lvProp.ColumnId, updated);
            values[lvProp.Index] = new LongValueDescriptor(descriptor);
            table.Update(id, values, new HashSet<int> { lvProp.Index });
            return;
        }
        throw new InvalidOperationException($"MSysObjects row for table '{tableName}' (page {tdefPage}) was not found.");
    }

    private void SetColumnProperties(int tdefPage, string columnName, IReadOnlyList<PropertyBlob.Property> props)
    {
        TableDef msys = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");
        int idIdx = (msys.FindColumn("Id") ?? throw new InvalidOperationException("MSysObjects is missing 'Id'.")).Index;
        ColumnDef lvProp = msys.FindColumn("LvProp") ?? throw new InvalidOperationException("MSysObjects is missing 'LvProp'.");
        var table = new Table(_channel, msys);

        foreach ((RowId id, object?[] values) in table.Rows().WithIds())
        {
            if (values[idIdx] is null || Convert.ToInt32(values[idIdx]) != tdefPage) continue;
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

        TableDef msys = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");
        int idIdx = (msys.FindColumn("Id") ?? throw new InvalidOperationException("MSysObjects is missing 'Id'.")).Index;
        ColumnDef lvProp = msys.FindColumn("LvProp") ?? throw new InvalidOperationException("MSysObjects is missing 'LvProp'.");
        var table = new Table(_channel, msys);

        foreach ((RowId id, object?[] values) in table.Rows().WithIds())
        {
            if (values[idIdx] is null || Convert.ToInt32(values[idIdx]) != tdefPage) continue;
            byte[] blob = values[lvProp.Index] as byte[] ?? [];

            var checks = PropertyBlob.ReadCheckConstraints(blob).ToList();
            checks.Add((checkName, expression));

            byte[] withoutTableBlock = PropertyBlob.RemoveOwner(blob, "");
            var checkProp = new PropertyBlob.Property("", PropertyBlob.CheckConstraintsProperty,
                PropertyBlob.WriteCheckList(checks));
            byte[] updated = PropertyBlob.AddColumnProperties(withoutTableBlock, "", [checkProp]);

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

        TableDef msys = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");
        int idIdx = (msys.FindColumn("Id") ?? throw new InvalidOperationException("MSysObjects is missing 'Id'.")).Index;
        ColumnDef lvProp = msys.FindColumn("LvProp") ?? throw new InvalidOperationException("MSysObjects is missing 'LvProp'.");
        var table = new Table(_channel, msys);

        foreach ((RowId id, object?[] values) in table.Rows().WithIds())
        {
            if (values[idIdx] is null || Convert.ToInt32(values[idIdx]) != tdefPage) continue;
            byte[] blob = values[lvProp.Index] as byte[] ?? [];

            var checks = PropertyBlob.ReadCheckConstraints(blob).ToList();
            if (checks.RemoveAll(c => string.Equals(c.Name, checkName, StringComparison.OrdinalIgnoreCase)) == 0)
                return false; // no CHECK of that name — let the caller try FK/PK/unique

            byte[] updated = PropertyBlob.RemoveOwner(blob, ""); // drop the table-level block…
            if (checks.Count > 0)                                // …and re-add it only if checks remain
                updated = PropertyBlob.AddColumnProperties(updated, "",
                    [new PropertyBlob.Property("", PropertyBlob.CheckConstraintsProperty, PropertyBlob.WriteCheckList(checks))]);

            byte[] descriptor = new RowInserter(_channel, msys).StorePackedLongValue(lvProp.ColumnId, updated);
            values[lvProp.Index] = new LongValueDescriptor(descriptor);
            table.Update(id, values, new HashSet<int> { lvProp.Index });
            return true;
        }
        throw new InvalidOperationException($"MSysObjects row for table '{tableName}' (page {tdefPage}) was not found.");
    }

    /// <summary>Changes a column's declared type — ALTER TABLE … ALTER COLUMN. Supports changing a **variable
    /// text/binary column's max length** (a descriptor-length edit at <c>ColumnLengthOffset</c>; variable columns
    /// store each row's actual length, so no rows need rewriting — works on empty and populated tables). Changing
    /// the storage type (numeric type, a fixed column's size, or fixed↔variable) would require an Access-style
    /// full column rewrite (read all rows, convert values, re-lay-out the row) and throws NotSupported.</summary>
    public void AlterColumn(string tableName, string columnName, ColumnSpec newSpec)
    {
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' does not exist.");
        ColumnDef col = table.FindColumn(columnName)
            ?? throw new InvalidOperationException($"Column '{columnName}' does not exist in '{tableName}'.");
        EnsureColumnIsNotInRelationship(table, col);

        // A pure reseed of an existing counter — ALTER COLUMN c COUNTER(seed, increment) where c is already an
        // AutoNumber of the same storage type — changes only the next id, not the data or layout. It's an
        // in-place TDEF header edit (0x14/0x18), exactly what ACE does; RewriteColumn would needlessly rebuild
        // the whole table. (Changing the numeric type still rebuilds.)
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

        bool variableLengthChange =
            !col.IsFixedLength && !newSpec.IsFixedLength && col.Type == newSpec.Type &&
            newSpec.Type is JetDataType.Text or JetDataType.Binary;
        // A variable text/binary length change is a cheap in-place descriptor edit (below). A storage-type change
        // (numeric type, fixed size, fixed↔variable) is a full column rewrite: the byte-faithful in-place edit
        // where it applies (all-fixed non-indexed target), else the logical rebuild (AlterColumnTypeInPlace picks).
        if (!variableLengthChange)
        {
            AlterColumnTypeInPlace(tableName, columnName, newSpec);
            return;
        }

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

    /// <summary>ACE rejects every type/length alteration of a relationship column, on either the
    /// referencing or referenced side. Keep this check ahead of all specialized ALTER paths so an
    /// in-place descriptor edit cannot bypass the same rule enforced by a logical table rebuild.</summary>
    private void EnsureColumnIsNotInRelationship(TableDef table, ColumnDef column)
    {
        const StringComparison oic = StringComparison.OrdinalIgnoreCase;
        if (_catalog.Relationships.Any(r =>
                (string.Equals(r.Table, table.Name, oic) &&
                 r.Columns.Any(c => string.Equals(c.Column, column.Name, oic))) ||
                (string.Equals(r.ReferencedTable, table.Name, oic) &&
                 r.Columns.Any(c => string.Equals(c.ReferencedColumn, column.Name, oic)))))
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
        const StringComparison oic = StringComparison.OrdinalIgnoreCase;
        if (_catalog.Relationships.Any(r =>
                (string.Equals(r.Table, table.Name, oic) && r.Columns.Any(c => string.Equals(c.Column, col.Name, oic))) ||
                (string.Equals(r.ReferencedTable, table.Name, oic) && r.Columns.Any(c => string.Equals(c.ReferencedColumn, col.Name, oic)))))
            throw new InvalidOperationException($"Cannot change field '{col.Name}'. It is part of one or more relationships.");

        if (increment == 0) increment = 1;
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
    /// existing values are untouched. Jet allows only one AutoNumber per table, so it rejects a second; and (like
    /// the reseed path) a column in a relationship is rejected, matching ACE.</summary>
    private void PromoteColumnToCounter(TableDef table, ColumnDef col, int seed, int increment)
    {
        const StringComparison oic = StringComparison.OrdinalIgnoreCase;
        if (table.Columns.Any(c => c.IsAutoNumber && c.ColumnId != col.ColumnId))
            throw new InvalidOperationException(
                $"Cannot make '{col.Name}' an AutoNumber: table '{table.Name}' already has one (Jet allows a single AutoNumber column per table).");
        if (_catalog.Relationships.Any(r =>
                (string.Equals(r.Table, table.Name, oic) && r.Columns.Any(c => string.Equals(c.Column, col.Name, oic))) ||
                (string.Equals(r.ReferencedTable, table.Name, oic) && r.Columns.Any(c => string.Equals(c.ReferencedColumn, col.Name, oic)))))
            throw new InvalidOperationException($"Cannot change field '{col.Name}'. It is part of one or more relationships.");

        if (increment == 0) increment = 1;
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

    /// <summary>Changes a column's storage type, matching ACE's column-modify semantics (verified): the column
    /// keeps its <b>position</b> but is internally a <b>new column</b> — it gets a fresh id burned from the 0x29
    /// high-water, while every other column keeps its id and its <b>original descriptor bytes</b> (so fields
    /// LibRed doesn't model are preserved, per the faithful round-trip rule). All target values are converted
    /// <b>in memory first</b> (an unconvertible value fails before anything is written), then the rebuild — drop,
    /// recreate with the new type, re-insert, recreate secondary indexes, re-add relationships — runs inside a
    /// page-level <b>transaction</b> that rolls back atomically on any later failure. Column order, the primary
    /// key, unique/secondary indexes, CHECK constraints, defaults, and AutoNumber values are preserved. Rejects a
    /// table whose target column is in a relationship (drop the FK first). This is a logical rebuild (a fresh TDEF
    /// page, not ACE's byte-exact in-place edit), but the resulting column layout — position, burned id, and the
    /// untouched columns' bytes — matches what ACE produces.</summary>
    private void RewriteColumn(string tableName, string columnName, ColumnSpec newColumnSpec)
    {
        TableDef def = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' does not exist.");
        ColumnDef target = def.FindColumn(columnName)
            ?? throw new InvalidOperationException($"Column '{columnName}' does not exist in '{tableName}'.");

        const StringComparison oic = StringComparison.OrdinalIgnoreCase;

        // ACE rejects altering a column that is itself part of a relationship (an FK column or the referenced
        // key column) — "Cannot change field 'X'. It is part of one or more relationships." (verified).
        if (_catalog.Relationships.Any(r =>
                (string.Equals(r.Table, tableName, oic) && r.Columns.Any(c => string.Equals(c.Column, columnName, oic))) ||
                (string.Equals(r.ReferencedTable, tableName, oic) && r.Columns.Any(c => string.Equals(c.ReferencedColumn, columnName, oic)))))
            throw new InvalidOperationException($"Cannot change field '{columnName}'. It is part of one or more relationships.");

        // The rebuild drops + recreates the table, so every relationship it touches is captured and restored
        // afterwards. OUTGOING FKs (this table is the child) are cascaded away by DropTable and re-added; their
        // backing indexes are recreated with them, so they're excluded from `secondary`. INCOMING FKs (this table
        // is the referenced parent) are dropped up front so the parent can be dropped, then re-added — this is
        // what makes a parent-side rewrite work. Self-references count as outgoing only.
        var foreignKeys = _catalog.ForeignKeysOf(tableName).ToList();
        var incoming = _catalog.Relationships
            .Where(r => string.Equals(r.ReferencedTable, tableName, oic) && !string.Equals(r.Table, tableName, oic))
            .ToList();
        var fkColumnSets = foreignKeys.Select(fk => fk.Columns.Select(c => c.Column).ToArray()).ToList();

        // 1. Materialise all rows (values indexed by column position) before dropping the table.
        var rows = new Table(_channel, def).Rows().Select(r => (object?[])r.Clone()).ToList();

        // 2. Reconstruct the schema — column order preserved, the target re-typed. Every OTHER column keeps its
        //    original descriptor bytes (RawDescriptor passthrough), so fields LibRed doesn't model survive the
        //    rewrite (the faithful round-trip rule); the target builds fresh (RawDescriptor null). Column ids stay
        //    contiguous by position — NOT burned like ACE — because the row codec's null bitmap is currently
        //    keyed by column id, which only agrees with ACE's (position-keyed) bitmap when id == position. Burning
        //    the id needs the codec switched to position-keying first (verified vs an ACE-modified file). TODO.
        int targetIndex = target.Index;
        var specs = def.Columns.Select(c => c.Index == targetIndex
            ? newColumnSpec with { IsNullable = target.IsNullable, RawDescriptor = null }
            : new ColumnSpec(c.Name, c.Type, c.Length, c.IsFixedLength, c.IsAutoNumber, c.Precision, c.Scale,
                c.IsNullable, c.Seed, c.Increment, RawDescriptor: c.RawDescriptor)).ToList();

        IndexDef? pk = def.Indexes.FirstOrDefault(i => i.IsPrimaryKey);
        IReadOnlyList<string>? primaryKey = pk?.Columns.Select(c => c.Column.Name).ToList();
        // Secondary indexes to recreate — excluding the primary key and any FK-backing index (re-added with its FK).
        var secondary = def.Indexes
            .Where(i => !i.IsPrimaryKey)
            .Where(i => !fkColumnSets.Any(cols =>
                cols.SequenceEqual(i.Columns.Select(c => c.Column.Name), StringComparer.OrdinalIgnoreCase)))
            .ToList();
        var checks = def.CheckConstraints.ToList();
        var defaults = def.Columns.Where(c => c.DefaultValue is not null)
            .Select(c => (Column: c.Name, DefaultSql: c.DefaultValue!)).ToList();

        // 3. Pre-check: convert every target value in memory BEFORE touching disk. An unconvertible value
        //    (e.g. non-numeric text → INT) throws here, with nothing written — the caller sees a clean failure.
        foreach (object?[] row in rows)
            row[targetIndex] = ConvertValue(row[targetIndex], newColumnSpec.Type);

        // 4. Apply the rebuild atomically: wrap it in a page-level transaction so any failure that slips past the
        //    pre-check (a unique-index collision after narrowing, NOT NULL, an I/O or allocation error) rolls the
        //    whole operation back and leaves the table byte-unchanged — never a half-converted table.
        bool ownTransaction = !_channel.InTransaction;
        if (ownTransaction) _channel.BeginTransaction();
        try
        {
            // Drop incoming relationships (so the parent becomes unreferenced) → drop → recreate (PK only) →
            // re-insert → recreate secondary indexes → re-add outgoing then incoming relationships.
            foreach (ForeignKey r in incoming) { DropConstraint(r.Table, r.Name); _catalog.Invalidate(); }
            DropTable(tableName);
            _catalog.Invalidate();
            Create(tableName, specs, primaryKey, relationships: null, uniqueConstraints: null,
                columnDefaults: defaults, checkConstraints: checks, primaryKeyName: pk?.Name);
            _catalog.Invalidate();

            var dest = new Table(_channel, _catalog.FindTable(tableName)!);
            foreach (object?[] row in rows) dest.Insert(row);

            foreach (IndexDef ix in secondary)
            {
                AddIndex(tableName, ix.Name, ix.Columns.Select(c => (c.Column.Name, !c.Ascending)).ToList(),
                    ix.IsUnique, isPrimary: false, disallowNull: false, ignoreNulls: false);
                _catalog.Invalidate();
            }

            // Re-add the outgoing foreign keys (recreates their backing index + linkage + MSysRelationships rows).
            foreach (ForeignKey fk in foreignKeys)
            {
                AddForeignKey(tableName, new RelationshipSpec(fk.Name, fk.ReferencedTable, fk.Columns.ToList(),
                    fk.IsEnforced, fk.CascadeUpdate, fk.CascadeDelete, NoIndex: false,
                    DeleteSetNull: fk.DeleteSetNull, UpdateSetNull: fk.UpdateSetNull));
                _catalog.Invalidate();
            }

            // Re-add the incoming relationships — each child's FK back to the rebuilt parent.
            foreach (ForeignKey r in incoming)
            {
                AddForeignKey(r.Table, new RelationshipSpec(r.Name, r.ReferencedTable, r.Columns.ToList(),
                    r.IsEnforced, r.CascadeUpdate, r.CascadeDelete, NoIndex: false,
                    DeleteSetNull: r.DeleteSetNull, UpdateSetNull: r.UpdateSetNull));
                _catalog.Invalidate();
            }

            if (ownTransaction) _channel.CommitTransaction();
        }
        catch when (ownTransaction)
        {
            _channel.RollbackTransaction();
            _catalog.Invalidate(); // the in-memory catalog cache is stale after the pages are restored
            throw;
        }
    }

    /// <summary>The TDEF-page step of the in-place column type change: bump the <c>0x29</c> high-water and rewrite
    /// ONLY the target descriptor (type, burned id, fixed-offset appended to the end of the fixed region with the
    /// old slot left dead, length), leaving the TDEF page number and every other descriptor byte-identical to ACE.
    /// This alone is not a self-consistent change — <see cref="AlterColumnTypeInPlace"/> wraps it with the row
    /// re-lay and index rebuild; this entry point exists so a byte-diff test can isolate the TDEF page.</summary>
    public void AlterColumnTypeInPlaceTdef(string tableName, string columnName, ColumnSpec newSpec, int? fixedEndOverride = null)
    {
        TableDef def = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' does not exist.");
        ColumnDef target = def.FindColumn(columnName)
            ?? throw new InvalidOperationException($"Column '{columnName}' does not exist in '{tableName}'.");
        EnsureColumnIsNotInRelationship(def, target);
        JetFormatBase format = _channel.Format;

        TdefParts parts = ParseTdef(def.DefinitionPage);
        // The fixed-region end must include dead slots, so callers with rows pass the row-derived length.
        int fixedEnd = fixedEndOverride ?? def.Columns.Where(c => c.IsFixedLength && c.Type != JetDataType.Boolean)
            .Select(c => c.FixedOffset + c.Length).DefaultIfEmpty(0).Max();

        EditTargetDescriptor(parts, target, newSpec, fixedEnd, format);
        WriteTdef(def.DefinitionPage, parts);
        _catalog.Invalidate();
    }

    /// <summary>Applies ACE's in-place column retype to the target descriptor within <paramref name="parts"/>
    /// (no page write — the caller writes the TDEF once): the target becomes a NEW column with a fresh id from the
    /// <c>0x29</c> high-water and its fixed data appended to the END of the current fixed region (its old slot left
    /// as dead space — ACE does not compact); <c>0x29</c> bumps, and <c>0x2B</c> too for a variable retype. Only
    /// the target descriptor changes; every other descriptor stays byte-identical. Returns the burned new id.</summary>
    private static int EditTargetDescriptor(TdefParts parts, ColumnDef target, ColumnSpec newSpec, int fixedEnd, JetFormatBase format)
    {
        int maxCols = BinaryPrimitives.ReadUInt16LittleEndian(parts.Header.AsSpan(format.TdefMaxColumnsOffset, 2));
        int varCount = BinaryPrimitives.ReadUInt16LittleEndian(parts.Header.AsSpan(format.TdefVariableColumnsOffset, 2));

        Span<byte> d = parts.Columns.AsSpan(target.Index * format.ColumnDescriptorSize, format.ColumnDescriptorSize);
        d[format.ColumnTypeOffset] = (byte)newSpec.Type;
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnNumberOffset..], (ushort)maxCols); // +0x05 id burned
        // The target's var-index (+0x07) becomes the old variable-column count — the next var slot — for BOTH a
        // fixed and a variable retype (verified vs ACE); a variable retype also bumps the 0x2B var-column count.
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnVariableIndexOffset..], (ushort)varCount);
        // The duplicate id at +0x09 is deliberately left unchanged — verified ACE does not update it.
        byte flags = d[format.ColumnFlagsOffset];
        flags = newSpec.IsFixedLength ? (byte)(flags | JetFormatBase.ColumnFlagFixedLength)
                                      : (byte)(flags & ~JetFormatBase.ColumnFlagFixedLength);
        flags = newSpec.IsAutoNumber ? (byte)(flags | JetFormatBase.ColumnFlagAutoNumber)
                                     : (byte)(flags & ~JetFormatBase.ColumnFlagAutoNumber);
        d[format.ColumnFlagsOffset] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnFixedOffsetOffset..], (ushort)(newSpec.IsFixedLength ? fixedEnd : 0)); // +0x15
        BinaryPrimitives.WriteUInt16LittleEndian(d[format.ColumnLengthOffset..], (ushort)newSpec.Length); // +0x17
        if (newSpec.Type == JetDataType.FixedPoint)   // Decimal/Numeric: precision/scale share the 0x0B/0x0C bytes
        {
            d[format.ColumnPrecisionOffset] = newSpec.Precision;
            d[format.ColumnScaleOffset] = newSpec.Scale;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefMaxColumnsOffset, 2), (ushort)(maxCols + 1)); // 0x29++
        if (!newSpec.IsFixedLength)
            BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefVariableColumnsOffset, 2), (ushort)(varCount + 1)); // 0x2B++
        return maxCols;
    }

    /// <summary>Full in-place column type change, byte-for-byte like ACE (currently: an all-fixed, non-boolean
    /// table whose target stays fixed and is not indexed; falls back to <see cref="RewriteColumn"/> otherwise).
    /// Edits the TDEF in place (<see cref="AlterColumnTypeInPlaceTdef"/>) and re-lays every row — the target's
    /// OLD fixed slot is kept as dead space, its converted value appended at the new offset, count + null bitmap
    /// updated. Converts values in memory first (throws on bad data before any write); runs in a transaction.</summary>
    public void AlterColumnTypeInPlace(string tableName, string columnName, ColumnSpec newSpec)
    {
        TableDef oldDef = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' does not exist.");
        ColumnDef oldTarget = oldDef.FindColumn(columnName)
            ?? throw new InvalidOperationException($"Column '{columnName}' does not exist in '{tableName}'.");
        EnsureColumnIsNotInRelationship(oldDef, oldTarget);

        // A long-value (Memo/OLE) target — or converting one away — needs long-value column mechanics (a §3.3.2
        // usage-map entry, LVAL pages, freeing the old value). That's out of scope for the byte-faithful in-place
        // edit; the logical rebuild (Create handles long-value columns) does it correctly, if not byte-exactly.
        if (newSpec.Type is JetDataType.Memo or JetDataType.Ole || oldTarget.Type is JetDataType.Memo or JetDataType.Ole)
        {
            RewriteColumn(tableName, columnName, newSpec);
            return;
        }

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
        foreach (var r in rows) r.Values[oldTarget.Index] = ConvertValue(r.Values[oldTarget.Index], newSpec.Type);

        // The fixed-region length is authoritative from an existing row (its var-data-start), NOT the live
        // column descriptors — those diverge once a high-offset column has been retyped to variable and left a
        // dead fixed slot at the end. Fall back to the schema for an empty table (encoder derives the same).
        bool hasVar = oldDef.Columns.Any(c => !c.IsFixedLength);
        int oldFixedLen = rows.Count > 0
            ? FixedRegionLength(rows[0].Raw, hasVar)
            : oldDef.Columns.Where(c => c.IsFixedLength && c.Type != JetDataType.Boolean)
                .Select(c => c.FixedOffset + c.Length).DefaultIfEmpty(0).Max();

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
            int newTargetId = EditTargetDescriptor(parts, oldTarget, newSpec, oldFixedLen, format);

            var pending = new List<(string Name, int OldRoot, int NewRoot, bool IgnoreNulls)>();
            foreach (string ixName in affectedIndexes)
            {
                IndexDef index = oldDef.Indexes.First(i => string.Equals(i.Name, ixName, StringComparison.OrdinalIgnoreCase));
                int newRoot = PrepareIndexRebuild(parts, oldDef, index, oldTargetId, newTargetId);
                pending.Add((ixName, index.RootPage, newRoot, index.IgnoreNulls));
            }

            WriteTdef(oldDef.DefinitionPage, parts);
            _catalog.Invalidate();

            TableDef newDef = _catalog.FindTable(tableName)!;
            ColumnDef newTarget = newDef.FindColumn(columnName)!;
            int newMaxId = newDef.Columns.Max(c => c.ColumnId);
            int newFixedLen = newTarget.IsFixedLength ? oldFixedLen + newTarget.Length : oldFixedLen;
            var writer = new RowInserter(_channel, newDef);

            // 3. Re-lay each row: old fixed region + old var chunks verbatim (incl. the dead old slot), target
            //    appended (a new fixed slot, or a new variable chunk); count/var-table/null-bitmap rebuilt.
            foreach (var r in rows)
                writer.RewriteRowRaw(r.Id, BuildRelaidRecord(r.Raw, oldFixedLen, hasVar, newTarget, r.Values, newDef.Columns, newMaxId, newFixedLen));

            // 4. Finish each index rebuild: backfill the fresh B-tree with new-type keys, then free the old root
            //    (last, so the new root got the appended page rather than reusing this one) — as ACE does.
            foreach (var p in pending)
            {
                BackfillIndex(tableName, p.Name, p.IgnoreNulls);
                _allocator.Free(p.OldRoot);
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
    /// (dead-id bits set present) are rebuilt.</summary>
    private static byte[] BuildRelaidRecord(byte[] oldRow, int oldFixedLen, bool hasVar, ColumnDef newTarget,
        object?[] values, IReadOnlyList<ColumnDef> newCols, int newMaxId, int newFixedLen)
    {
        object? tv = values[newTarget.Index];
        byte[] targetBytes = tv is null
            ? (newTarget.IsFixedLength ? new byte[newTarget.Length] : [])
            : Types.JetTypeCodec.Encode(newTarget, tv);

        // Fixed region: old fixed bytes verbatim (incl. a dead fixed slot); append the target if it is fixed.
        var newFixed = new byte[newFixedLen];
        Array.Copy(oldRow, 2, newFixed, 0, oldFixedLen);
        if (newTarget.IsFixedLength && tv is not null)
            Array.Copy(targetBytes, 0, newFixed, newTarget.FixedOffset, newTarget.Length);

        // Variable chunks: old chunks verbatim (incl. a dead variable chunk); append the target if it is variable.
        List<byte[]> chunks = ExtractVarChunks(oldRow, hasVar);
        if (!newTarget.IsFixedLength) chunks.Add(targetBytes);

        // Assemble via the shared row layout (count + var table + null bitmap identical to a fresh encode).
        _ = newFixedLen; // == newFixed.Length
        return RowEncoder.AssembleRow(newMaxId, newFixed, chunks, newCols, values);
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
        // usage-map row; bump the stats block (+0x00, observed 0→1 on ACE's rebuild).
        for (int slot = 0; slot < IndexBlockFormat.MaxColumns; slot++)
        {
            int at = IndexBlockFormat.ColumnsOffset + slot * IndexBlockFormat.ColumnSlotSize;
            if (BinaryPrimitives.ReadInt16LittleEndian(block.Slice(at, 2)) == oldTargetId)
                BinaryPrimitives.WriteInt16LittleEndian(block.Slice(at, 2), (short)newTargetId);
        }
        block[IndexBlockFormat.UsageMapRowOffset] = (byte)newRow;
        BinaryPrimitives.WriteInt32LittleEndian(block.Slice(IndexBlockFormat.RootPageOffset, 4), newRoot);
        Span<byte> stats = parts.Stats[index.RealIndexOrdinal];
        BinaryPrimitives.WriteInt32LittleEndian(stats, BinaryPrimitives.ReadInt32LittleEndian(stats) + 1);
        return newRoot;
    }

    /// <summary>Recycles an index's owned-pages usage-map row exactly the way ACE does on a rebuild: append a
    /// fresh row and set the new root's bit (ACE's first write, at the appended slot), then MOVE that map into
    /// the old row's freed slot and soft-delete the old row (a 0-length deleted+overflow tombstone) — leaving
    /// the appended slot's bytes stale in free space, byte-for-byte as ACE does. Returns the new row number.</summary>
    private int RecycleOwnedMapRow(JetFormatBase format, int usageMapPage, int oldRow, int newRoot)
    {
        const int MapLength = 1 + 4 + 64;
        int dir = format.DataRowDirectoryOffset;
        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(_channel.ReadPage(usageMapPage).Span.Slice(format.DataRowCountOffset, 2));
        int newRow = rowCount;

        // ACE's first write: append a fresh row and set the new root's bit (this copy is later left stale).
        AppendEmptyUsageMapRow(format, usageMapPage, newRow);
        new UsageMapWriter(_channel).SetBit(newRow, usageMapPage, newRoot, set: true);

        // Then move that map into the old row's freed slot and turn the old row into a tombstone; the appended
        // slot's bytes are left in place (stale, in free space) — matching ACE's leftover.
        byte[] page = _channel.ReadPage(usageMapPage).Span.ToArray();
        int freshOffset = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(dir + newRow * 2, 2)) & RowPointer.OffsetMask;
        int oldOffset = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(dir + oldRow * 2, 2)) & RowPointer.OffsetMask;
        int aboveOffset = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(dir + (oldRow - 1) * 2, 2)) & RowPointer.OffsetMask;

        Array.Copy(page, freshOffset, page, oldOffset, MapLength);                 // move the map into the old slot
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(dir + newRow * 2, 2), (ushort)oldOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(dir + oldRow * 2, 2),
            (ushort)(aboveOffset | RowPointer.DeletedFlag | RowPointer.OverflowFlag)); // old row → 0-length tombstone
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(oldOffset - (dir + (rowCount + 1) * 2)));
        _channel.WritePage(usageMapPage, page);
        return newRow;
    }

    /// <summary>Converts a stored value to the CLR type for a new column type (ALTER COLUMN). NULL stays NULL;
    /// an unconvertible value throws (as ACE's rewrite would).</summary>
    private static object? ConvertValue(object? value, JetDataType type)
    {
        if (value is null) return null;
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
            JetDataType.Currency or JetDataType.FixedPoint => Convert.ToDecimal(value, inv),
            JetDataType.DateTime => value is DateTime d ? d : Convert.ToDateTime(value, inv),
            JetDataType.Text or JetDataType.Memo => Convert.ToString(value, inv),
            JetDataType.Guid => value is Guid g ? g : Guid.Parse(value.ToString()!),
            JetDataType.Binary or JetDataType.Ole => value as byte[] ?? System.Text.Encoding.Unicode.GetBytes(value.ToString()!),
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
    /// that backs an index/key (drop that first) or a memo/OLE column (its long-value usage-map entry/pages
    /// aren't handled yet).
    /// </summary>
    public bool DropColumn(string tableName, string columnName)
    {
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");
        ColumnDef? col = table.Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
        if (col is null) return false;

        // ACE rejects dropping a column that participates in a relationship (as the child FK column or the
        // referenced parent key) — even a NO INDEX FK with no backing index — with "It is part of one or more
        // relationships"; you must drop the relationship first. This is correct, permanent behaviour (not a
        // gap), so mirror it. Verified vs ACE.
        if (_catalog.Relationships.Any(r =>
                (string.Equals(r.Table, tableName, StringComparison.OrdinalIgnoreCase)
                    && r.Columns.Any(c => string.Equals(c.Column, columnName, StringComparison.OrdinalIgnoreCase))) ||
                (string.Equals(r.ReferencedTable, tableName, StringComparison.OrdinalIgnoreCase)
                    && r.Columns.Any(c => string.Equals(c.ReferencedColumn, columnName, StringComparison.OrdinalIgnoreCase)))))
            throw new InvalidOperationException(
                $"Cannot drop column '{columnName}': it is part of one or more relationships — drop the relationship first.");

        // ACE likewise rejects dropping an indexed/keyed column ("part of an index or is needed by the
        // system"); the index must be dropped first. Also correct, permanent behaviour. Verified vs ACE.
        if (table.Indexes.Any(ix => ix.Columns.Any(c => c.Column.ColumnId == col.ColumnId)))
            throw new InvalidOperationException(
                $"Cannot drop column '{columnName}': it is part of an index or key — drop the index/constraint first.");

        if (col.Type is JetDataType.Memo or JetDataType.Ole)
            throw new NotSupportedException(
                $"DROP COLUMN '{columnName}': dropping a memo/OLE (long-value) column is not supported yet.");

        TdefParts parts = ParseTdef(table.DefinitionPage); // stitches continuation pages for a multi-page TDEF
        RemoveColumnFromParts(parts, table.Columns.Count, col.Index, _channel.Format);
        WriteTdef(table.DefinitionPage, parts);
        RemoveColumnProperties(table.DefinitionPage, columnName); // drop its DefaultValue/Required from LvProp (ACE does)
        _catalog.Invalidate();
        return true;
    }

    /// <summary>Removes a dropped column's extended-property block (DefaultValue, Required, …) from its
    /// table's <c>MSysObjects.LvProp</c> blob — what ACE does on DROP COLUMN (verified). Surgically removes
    /// just that column's block (keeps the name pool + other columns' blocks), re-stores the smaller blob on
    /// an LvProp page and updates the row. No-op when the column had no properties.</summary>
    private void RemoveColumnProperties(int tdefPage, string columnName)
    {
        TableDef msys = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");
        int idIdx = (msys.FindColumn("Id") ?? throw new InvalidOperationException("MSysObjects is missing 'Id'.")).Index;
        ColumnDef lvProp = msys.FindColumn("LvProp") ?? throw new InvalidOperationException("MSysObjects is missing 'LvProp'.");
        var table = new Table(_channel, msys);

        foreach ((RowId id, object?[] values) in table.Rows().WithIds())
        {
            if (values[idIdx] is null || Convert.ToInt32(values[idIdx]) != tdefPage) continue;
            if (values[lvProp.Index] is not byte[] { Length: > 0 } blob) return;

            byte[] cleaned = PropertyBlob.RemoveOwner(blob, columnName);
            if (cleaned.Length == blob.Length) return; // the column had no property block — nothing to remove

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
    private void SoftDeleteRelationshipRows(string name)
    {
        TableDef msys = _catalog.FindTable("MSysRelationships")
            ?? throw new InvalidOperationException("MSysRelationships catalog table was not found.");
        int nameIdx = (msys.FindColumn("szRelationship")
            ?? throw new InvalidOperationException("MSysRelationships is missing the 'szRelationship' column.")).Index;

        var rows = new List<RowId>();
        foreach ((RowId id, object?[] values) in new Table(_channel, msys).Rows().WithIds())
            if (string.Equals(values[nameIdx] as string, name, StringComparison.OrdinalIgnoreCase))
                rows.Add(id);
        foreach (RowId id in rows) SoftDeleteRow(id);
    }

    /// <summary>The name text of a TDEF name entry (2-byte UTF-16 length, then the chars).</summary>
    private static string NameOf(byte[] nameEntry) =>
        System.Text.Encoding.Unicode.GetString(nameEntry, 2, BinaryPrimitives.ReadUInt16LittleEndian(nameEntry.AsSpan(0, 2)));

    /// <summary>The parsed regions of a single-page table definition, for surgical block removal.</summary>
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

        int dataCount = buf.ReadInt32(format.TdefIndexCountOffset);
        int logicalCount = buf.ReadInt32(format.TdefRealIndexCountOffset);
        int colCount = buf.ReadUInt16(format.TdefColumnCountOffset);

        int statsStart = format.TdefRealIndexBlockOffset;
        int afterStats = statsStart + dataCount * format.RealIndexEntrySize;
        int pos = afterStats + colCount * format.ColumnDescriptorSize;
        for (int i = 0; i < colCount; i++) pos += 2 + buf.ReadUInt16(pos);
        int afterColumns = pos;
        int infoStart = afterColumns + dataCount * IndexBlockFormat.DataBlockSize;
        int namePos = infoStart + logicalCount * IndexBlockFormat.InfoBlockSize;
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
        if (removeDataOrdinal is int ord)
        {
            parts.Stats.RemoveAt(ord);
            parts.DataBlocks.RemoveAt(ord);
            foreach ((byte[] info, _) in parts.Logical)
            {
                int num2 = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(0x08, 4));
                if (num2 > ord) BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(0x08, 4), num2 - 1);
            }
        }
        parts.Logical.RemoveAll(b => removeLogical(b));
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
        BinaryPrimitives.WriteInt32LittleEndian(def.AsSpan(format.TdefRealIndexCountOffset, 4), parts.Logical.Count);
        BinaryPrimitives.WriteInt32LittleEndian(def.AsSpan(format.TdefLengthOffset, 4), defEnd);

        // Write across the first page and continuation pages as needed (reusing the existing ones) — handles a
        // definition that shrinks to one page, stays multi-page, or grows past a page (e.g. ADD COLUMN).
        WriteDefinition(tdefPage, def, parts.Continuations);
    }

    /// <summary>Marks a row deleted by setting the deleted flag (0x8000) on its slot-directory entry — a
    /// Jet soft delete: the row bytes stay but scans (and Access) skip it.</summary>
    private void SoftDeleteRow(RowId id)
    {
        JetFormatBase format = _channel.Format;
        byte[] page = _channel.ReadPage(id.Page).Span.ToArray();
        int dirOffset = format.DataRowDirectoryOffset + id.Row * 2;
        ushort entry = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(dirOffset, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(dirOffset, 2), (ushort)(entry | 0x8000));
        _channel.WritePage(id.Page, page);
    }

    /// <summary>The data-block ordinal of a table's own index over the FK's referenced columns (for a
    /// self-reference — normally the primary key).</summary>
    private static int ReferencedOrdinalIn(TableDef table, RelationshipSpec fk)
    {
        var refColumns = fk.Columns.Select(c => c.ReferencedColumn).ToList();
        IndexDef refIndex = table.Indexes.FirstOrDefault(ix =>
                ix.Columns.Select(c => c.Column.Name).SequenceEqual(refColumns, StringComparer.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Self-referencing foreign key '{fk.Name}' references ({string.Join(", ", refColumns)}), which is not a key or index of '{table.Name}'.");
        return refIndex.RealIndexOrdinal;
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
    /// Existing continuation pages are reused before allocating new ones.
    /// </summary>
    private void WriteDefinition(int firstPage, byte[] def, IReadOnlyList<int> reusePages)
    {
        JetFormatBase format = _channel.Format;
        int ps = format.PageSize;
        int nextOffset = format.TdefNextPageOffset;

        if (def.Length + JetFormatBase.TdefContinuationHeaderSize <= ps)
        {
            var only = new byte[ps];
            def.CopyTo(only, 0);
            BinaryPrimitives.WriteInt32LittleEndian(only.AsSpan(nextOffset, 4), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(only.AsSpan(format.TdefFreeSpaceOffset, 2), (ushort)(ps - def.Length - JetFormatBase.TdefContinuationHeaderSize));
            _channel.WritePage(firstPage, only);
            return;
        }

        // Plan the continuation chunks: each holds up to (ps - header) data; the last also leaves the reserve.
        var chunks = new List<(int Offset, int Length)>();
        for (int offset = ps; offset < def.Length;)
        {
            int remaining = def.Length - offset;
            int length = remaining <= ps - JetFormatBase.TdefContinuationHeaderSize - JetFormatBase.TdefContinuationHeaderSize
                ? remaining
                : ps - JetFormatBase.TdefContinuationHeaderSize;
            chunks.Add((offset, length));
            offset += length;
        }

        int reuse = 0;
        int[] pageNumbers = chunks.Select(_ => reuse < reusePages.Count ? reusePages[reuse++] : _allocator.Allocate()).ToArray();

        var page1 = new byte[ps];
        Array.Copy(def, 0, page1, 0, ps); // page 1 is completely full in a multi-page definition
        BinaryPrimitives.WriteInt32LittleEndian(page1.AsSpan(nextOffset, 4), pageNumbers[0]);
        BinaryPrimitives.WriteUInt16LittleEndian(page1.AsSpan(format.TdefFreeSpaceOffset, 2), 0);
        _channel.WritePage(firstPage, page1);

        for (int i = 0; i < chunks.Count; i++)
        {
            var (offset, length) = chunks[i];
            var page = new byte[ps];
            page[0] = (byte)PageType.TableDefinition;
            page[1] = 0x01;
            Array.Copy(def, offset, page, JetFormatBase.TdefContinuationHeaderSize, length);
            int next = i + 1 < pageNumbers.Length ? pageNumbers[i + 1] : 0;
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(nextOffset, 4), next);
            int free = next != 0 ? 0 : ps - JetFormatBase.TdefContinuationHeaderSize - length - JetFormatBase.TdefContinuationHeaderSize;
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefFreeSpaceOffset, 2), (ushort)free);
            _channel.WritePage(pageNumbers[i], page);
        }
    }

    private static byte[] BuildIndexDataBlock(IReadOnlyList<(int Id, bool Ascending)> columns, int rootPage, int usageRow, int usagePage, bool unique, bool required, bool ignoreNulls)
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
    /// child's outgoing block, and grows the logical-index list by one (kept name-sorted). Single
    /// definition page only (throws if the definition spans continuation pages).
    /// </summary>
    private void AddIncomingRelationshipBlock(IncomingRelationship inc)
    {
        JetFormatBase format = _channel.Format;
        var buf = _channel.ReadPage(inc.ParentPage);
        if (buf.ReadInt32(format.TdefNextPageOffset) != 0)
            throw new NotSupportedException("Adding a relationship to a multi-page table definition is not supported yet.");

        int dataCount = buf.ReadInt32(format.TdefIndexCountOffset);        // 0x33 real data blocks
        int logicalCount = buf.ReadInt32(format.TdefRealIndexCountOffset); // 0x2F logical blocks
        int colCount = buf.ReadUInt16(format.TdefColumnCountOffset);

        // Walk to the logical index-info blocks: stats + column descriptors -> column names -> data blocks.
        int pos = format.TdefRealIndexBlockOffset + dataCount * format.RealIndexEntrySize
                  + colCount * format.ColumnDescriptorSize;
        for (int i = 0; i < colCount; i++) pos += 2 + buf.ReadUInt16(pos);
        int infoStart = pos + dataCount * IndexBlockFormat.DataBlockSize;

        var blocks = new List<byte[]>(logicalCount + 1);
        for (int i = 0; i < logicalCount; i++)
            blocks.Add(buf.Slice(infoStart + i * IndexBlockFormat.InfoBlockSize, IndexBlockFormat.InfoBlockSize).ToArray());

        int namePos = infoStart + logicalCount * IndexBlockFormat.InfoBlockSize;
        var names = new List<string>(logicalCount + 1);
        var nameBytes = new List<byte[]>(logicalCount + 1);
        for (int i = 0; i < logicalCount; i++)
        {
            int len = buf.ReadUInt16(namePos);
            nameBytes.Add(buf.Slice(namePos, 2 + len).ToArray());
            names.Add(System.Text.Encoding.Unicode.GetString(buf.Slice(namePos + 2, len)));
            namePos += 2 + len;
        }

        int defEnd = buf.ReadInt32(format.TdefLengthOffset);
        byte[] lvalRegion = buf.Slice(namePos, defEnd - namePos).ToArray(); // §3.3.2 list + 0xFFFF terminator

        string newName = NextHiddenRelationshipName(names);
        int k = names.Count(n => string.CompareOrdinal(n, newName) < 0); // name-sorted insert position
        blocks.Insert(k, BuildIncomingInfoBlock(inc));
        nameBytes.Insert(k, EncodeName(newName));

        int newDefEnd = infoStart + blocks.Count * IndexBlockFormat.InfoBlockSize + nameBytes.Sum(n => n.Length) + lvalRegion.Length;
        if (newDefEnd > format.PageSize - JetFormatBase.TdefContinuationHeaderSize)
            throw new NotSupportedException("No room in the table definition for another relationship (needs a continuation page).");

        var page = buf.Span.ToArray();
        int w = infoStart;
        foreach (byte[] b in blocks) { b.CopyTo(page.AsSpan(w)); w += b.Length; }
        foreach (byte[] n in nameBytes) { n.CopyTo(page.AsSpan(w)); w += n.Length; }
        lvalRegion.CopyTo(page.AsSpan(w));

        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefRealIndexCountOffset, 4), logicalCount + 1);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefLengthOffset, 4), newDefEnd);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefFreeSpaceOffset, 2),
            (ushort)(format.PageSize - newDefEnd - JetFormatBase.TdefContinuationHeaderSize));
        _channel.WritePage(inc.ParentPage, page);
    }

    private byte[] BuildIncomingInfoBlock(IncomingRelationship inc)
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

    /// <summary>The hidden name Access gives an incoming relationship index: ".r" + a letter unique
    /// among the table's index names (Access starts at 'B').</summary>
    private static string NextHiddenRelationshipName(IReadOnlyCollection<string> existing)
    {
        for (char c = 'B'; c <= 'Z'; c++)
        {
            string candidate = $".r{c}";
            if (!existing.Contains(candidate)) return candidate;
        }
        return $".r{Guid.NewGuid():N}"[..8];
    }

    /// <summary>Writes an empty B-tree leaf (no entries) to serve as a fresh index root.</summary>
    private void WriteEmptyLeafIndexPage(JetFormatBase format, int pageNumber, int owner)
    {
        const int EntryDataOffset = 0x1E0;
        const int OwnerOffset = 0x04;

        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.LeafIndexPage;
        page[1] = 0x01; // page flags (observed constant)
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
        page[0] = (byte)PageType.DataPage;
        page[1] = 0x01; // page flags (observed constant)
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

    // A user table's parent object is the database's "Tables" container; observed constant.

    // A user table's owner + grantee SIDs, matching the cluster DatabaseCreator seeds for the system objects
    // (Users owns user objects; Users + Admin get the user-table grants).
    private static readonly byte[] DefaultOwner = DatabaseCreator.SidUsers;  // Users/Engine (per-file masked)
    private static readonly byte[] AdminSid = DatabaseCreator.SidAdmin;      // Admin user (per-file masked)

    /// <summary>
    /// Adds the MSysObjects row describing the new table so Access (and the catalog) see it: Id =
    /// TDEF page, ParentId = Tables container, Type = table, Name, Flags, Owner, and create/update
    /// dates. Any column DEFAULT values are written into the extended-properties blob (LvProp, an OLE
    /// long value) as DefaultValue properties. MSysObjects' own indexes (Id, and the composite
    /// ParentId+Name used for name resolution) are maintained so Access can open the table by name.
    /// </summary>
    private void AddCatalogRow(string name, int tdefPage,
        IReadOnlyList<PropertyBlob.Property> columnProps,
        IReadOnlyList<(string Name, string Expression)> checkConstraints)
    {
        TableDef msysObjects = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");

        DateTime now = DateTime.Now;
        var values = new object?[msysObjects.Columns.Count];
        SetByName(msysObjects, values, "Id", tdefPage);
        SetByName(msysObjects, values, "ParentId", CatalogFormat.ObjectContainerParentId);
        SetByName(msysObjects, values, "Type", (short)1); // table object
        SetByName(msysObjects, values, "Name", name);
        SetByName(msysObjects, values, "Flags", 0);
        SetByName(msysObjects, values, "Owner", DefaultOwner);
        SetByName(msysObjects, values, "DateCreate", now);
        SetByName(msysObjects, values, "DateUpdate", now);

        // Per-column properties (DefaultValue / Required) and CHECK constraints (a table property) both
        // live in the object's extended-properties (LvProp) blob.
        var props = columnProps.ToList();
        if (checkConstraints.Count > 0)
            props.Add(new PropertyBlob.Property("", PropertyBlob.CheckConstraintsProperty,
                PropertyBlob.WriteCheckList(checkConstraints)));

        var inserter = new RowInserter(_channel, msysObjects);
        if (props.Count > 0)
        {
            // Access reads object properties only from an LVAL-page long value, not an inline one, so
            // store the blob on a page (packed onto a shared LvProp page like Access) and keep the descriptor.
            int lvPropColumn = (msysObjects.FindColumn("LvProp")
                ?? throw new InvalidOperationException("MSysObjects is missing the 'LvProp' column.")).ColumnId;
            byte[] reference = inserter.StorePackedLongValue(lvPropColumn, PropertyBlob.Write(props));
            SetByName(msysObjects, values, "LvProp", new LongValueDescriptor(reference));
        }

        inserter.Insert(values, updateIndexes: true);
    }

    // A new user table's per-object-class masks (verified against DAO-created files): Users get read/write
    // data (0x0F00FE), Admin gets full-access-minus-ownership (0x0FFEFF).
    private const int UserTableUsersMask = 0x0F00FE;
    private const int UserTableAdminMask = 0x0FFEFF;

    /// <summary>
    /// Adds the two MSysACEs permission rows Access writes for a new user table (Users + Admin, with the
    /// user-table masks), maintaining the table's ObjectId index so Access's security check sees them.
    /// </summary>
    private void AddPermissionRows(int objectId)
    {
        TableDef msysAces = _catalog.FindTable("MSysACEs")
            ?? throw new InvalidOperationException("MSysACEs catalog table was not found.");

        foreach ((byte[] sid, int acm) in new[] { (DefaultOwner, UserTableUsersMask), (AdminSid, UserTableAdminMask) })
        {
            var values = new object?[msysAces.Columns.Count];
            SetByName(msysAces, values, "ACM", acm);
            SetByName(msysAces, values, "FInheritable", false);
            SetByName(msysAces, values, "ObjectId", objectId);
            SetByName(msysAces, values, "SID", sid);
            new RowInserter(_channel, msysAces).Insert(values, updateIndexes: true);
        }
    }

    private static void SetByName(TableDef table, object?[] values, string column, object value)
    {
        ColumnDef def = table.FindColumn(column)
            ?? throw new InvalidOperationException($"MSysObjects is missing the '{column}' column.");
        values[def.Index] = value;
    }

    private static void WriteInt24(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
    }
}
