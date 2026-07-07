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
public sealed class TableCreator(PageChannel channel, JetCatalog catalog)
{
    private readonly PageChannel _channel = channel;
    private readonly JetCatalog _catalog = catalog;
    private readonly PageAllocator _allocator = new(channel);

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
                    UpdateAction: PlainIndexAction, DeleteAction: PlainIndexAction,
                    Type: plan.IsPk ? IndexTypePrimary : IndexTypeSecondary, Name: plan.Name));
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
                    Type: IndexTypeForeign, Name: fk.Name));
                // Incoming block (this table's parent side), hidden ".r" name unique within the table.
                childLogical.Add(new TdefBuilder.LogicalIndexSpec(
                    Number: inNum, DataOrdinal: refOrdinal, FkType: FkTypeIncoming, FkNumber: (uint)i,
                    FkTablePage: tdefPage, UpdateAction: upd, DeleteAction: del,
                    Type: IndexTypeForeign, Name: NextHiddenRelationshipName(childLogical.Select(l => l.Name).ToList())));
                continue;
            }

            (int parentPage, int refOrd, int parentLogicalCount) = ResolveParent(fk, tdefPage);
            int parentNum = parentLogicalCount + parentAdds.GetValueOrDefault(parentPage);
            parentAdds[parentPage] = parentAdds.GetValueOrDefault(parentPage) + 1;

            childLogical.Add(new TdefBuilder.LogicalIndexSpec(
                Number: i, DataOrdinal: i, FkType: outgoingType,
                FkNumber: (uint)parentNum, FkTablePage: parentPage, UpdateAction: upd, DeleteAction: del,
                Type: IndexTypeForeign, Name: fk.Name));
            incoming.Add(new IncomingRelationship(parentPage, parentNum, refOrd,
                ChildBlockNumber: (uint)i, ChildPage: tdefPage, upd, del));
        }

        // Access stores logical blocks sorted by name (with their names in the same order).
        childLogical.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        // Build the definition and point it at the usage maps: owned-pages = row 0, free-pages =
        // row 1, both on the usage-map page.
        byte[] tdef = TdefBuilder.Build(format, TableType.User, columns, indexes, longValueSpecs, childLogical).Page;
        const int FreePagesOffset = 0x3B;
        tdef[format.TdefOwnedPagesOffset] = 0; // owned map record row
        WriteInt24(tdef, format.TdefOwnedPagesOffset + 1, usageMapPage);
        tdef[FreePagesOffset] = 1; // free map record row
        WriteInt24(tdef, FreePagesOffset + 1, usageMapPage);
        // A wide table's definition can exceed one page; write it split across continuation pages if needed.
        int defEnd = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(TdefLengthOffset, 4));
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
    private const byte PlainIndexAction = 0x04;   // update/delete action on a non-relationship index
    private const byte NoCascadeAction = 0x00;    // relationship without ON UPDATE/DELETE CASCADE
    private const byte CascadeAction = 0x01;       // relationship with cascade
    private const byte SetNullAction = 0x02;       // ON DELETE SET NULL (verified vs ACE, index-info block +0x16)

    /// <summary>ON UPDATE SET NULL pathway: the docs list it, but the ACE OLE DB provider rejects it via SQL,
    /// so its on-disk storage (the grbit flag + the index-info +0x15 action byte) is unverified. Rather than
    /// guess the bytes, fail loudly until a UI/DAO-created sample can be probed.</summary>
    private static NotImplementedException UpdateSetNullNotImplemented() => new(
        "ON UPDATE SET NULL is not implemented: its Jet storage bytes are unverified (the ACE OLE DB provider " +
        "rejects the DDL, so they could not be probed). Only ON UPDATE {NO ACTION | CASCADE} are supported.");
    private const byte IndexTypeSecondary = 0x00;
    private const byte IndexTypePrimary = 0x01;
    private const byte IndexTypeForeign = 0x02;
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

    // MSysRelationships.grbit flags (DAO RelationAttributeEnum), mirroring JetCatalog's read side.
    private const int RelationshipDontEnforce = 0x00000002;
    private const int RelationshipDeleteSetNull = 0x00002000;
    private const int RelationshipUpdateCascade = 0x00000100;
    private const int RelationshipDeleteCascade = 0x00001000;

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
        if (!fk.IsEnforced) grbit |= RelationshipDontEnforce;
        if (fk.CascadeUpdate) grbit |= RelationshipUpdateCascade;
        if (fk.CascadeDelete) grbit |= RelationshipDeleteCascade;
        if (fk.DeleteSetNull) grbit |= RelationshipDeleteSetNull;

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

    private const int TdefLengthOffset = 0x08;
    private const int TdefFreeSpaceOffset = 0x02;
    private const int IndexDataBlockSize = 52;
    private const int IndexInfoBlockSize = 28;
    private const int TdefContinuationReserve = 8;
    private const uint TdefRecordMarker = 0x659;
    private const uint IndexDataMarker = 0x783;
    private const int IndexMaxColumnSlots = 10;

    /// <summary>
    /// Adds an index to an existing (empty) table for CREATE INDEX. Surgically inserts a statistics
    /// block, an index-data block and a logical index-info block into the TDEF (preserving the existing
    /// columns, indexes, relationship linkage and long-value entries byte-for-byte), grows the usage-map
    /// page by one row and writes an empty B-tree root. Single-page, empty-table only.
    /// </summary>
    public void AddIndex(string tableName, string indexName, IReadOnlyList<(string Column, bool Descending)> columns,
        bool isUnique, bool isPrimary, bool disallowNull, bool ignoreNulls)
    {
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
        int afterDataBlocks = afterColumns + dataCount * IndexDataBlockSize;
        int infoStart = afterDataBlocks;

        // Existing logical blocks and names, plus the max index_num, so the new block gets a fresh number.
        int namePos = infoStart + logicalCount * IndexInfoBlockSize;
        var blocks = new List<byte[]>(logicalCount + 1);
        int maxNum = -1;
        for (int i = 0; i < logicalCount; i++)
        {
            byte[] block = buf.Slice(infoStart + i * IndexInfoBlockSize, IndexInfoBlockSize).ToArray();
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

        int defEnd = buf.ReadInt32(TdefLengthOffset);
        byte[] lvalRegion = buf.Slice(namePos, defEnd - namePos).ToArray(); // §3.3.2 list + 0xFFFF terminator
        int lvalCount = (lvalRegion.Length - 2) / 10;                        // 10 bytes per entry, then 0xFFFF

        // Allocate the new index's root (empty leaf) and its usage-map row (appended after existing rows).
        int rootPage = _allocator.Allocate();
        WriteEmptyLeafIndexPage(format, rootPage, owner: table.DefinitionPage);
        int usageMapPage = ReadInt24(buf, format.TdefOwnedPagesOffset + 1);
        int newIndexUsageRow = 2 + lvalCount * 2 + dataCount;
        if (existingRowCount == 0)
            WriteUsageMaps(format, usageMapPage, mapCount: newIndexUsageRow + 1); // empty table: all maps empty
        else
            // A populated table's data/index usage maps must be preserved; append just the new index's row.
            AppendEmptyUsageMapRow(format, usageMapPage, newIndexUsageRow);

        // Assemble the new definition: header + existing stats, a new stats block, columns + names +
        // existing data blocks, the new data block, then the logical blocks (new one inserted, name-sorted)
        // and their names, and finally the unchanged long-value region.
        byte[] newData = BuildIndexDataBlock(slots, rootPage, newIndexUsageRow, usageMapPage, unique, required, ignoreNulls);
        byte[] newInfo = buildInfo(maxNum + 1, dataCount);

        int k = names.Count(n => string.CompareOrdinal(n, indexName) < 0); // name-sorted insert position
        blocks.Insert(k, newInfo);
        nameBytes.Insert(k, EncodeName(indexName));

        int newDefEnd = infoStart + IndexDataBlockSize          // one new data block shifts info start
                        + blocks.Count * IndexInfoBlockSize + nameBytes.Sum(n => n.Length) + lvalRegion.Length
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
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(def.AsSpan(TdefLengthOffset, 4), newDefEnd);

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
                && Convert.ToInt16(values[typeIdx] ?? (short)0) == ObjectTypeQuery)
            { objId = Convert.ToInt32(values[idIdx]); break; }
        if (objId is null) return false;

        DeleteCatalogRows("MSysObjects", "Id", objId.Value);
        DeleteCatalogRows("MSysQueries", "ObjectId", objId.Value);
        DeleteCatalogRows("MSysACEs", "ObjectId", objId.Value);
        _catalog.Invalidate();
        return true;
    }

    private const short ObjectTypeQuery = 5;

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

    /// <summary>True if the index backs a relationship — as the child FK backing index (its columns are a
    /// relationship's child columns on this table) or the referenced parent key (its columns are a
    /// relationship's referenced columns on this table). ACE rejects dropping such an index.</summary>
    private bool IndexParticipatesInRelationship(TableDef table, IndexDef index)
    {
        var cols = index.Columns.Select(c => c.Column.Name).ToList();
        bool SameCols(IEnumerable<string> other) =>
            other.Select(x => x).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                 .SequenceEqual(cols.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        return _catalog.Relationships.Any(r =>
            (string.Equals(r.Table, table.Name, StringComparison.OrdinalIgnoreCase) && SameCols(r.Columns.Select(c => c.Column))) ||
            (string.Equals(r.ReferencedTable, table.Name, StringComparison.OrdinalIgnoreCase) && SameCols(r.Columns.Select(c => c.ReferencedColumn))));
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
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");
        if (table.Columns.Any(c => string.Equals(c.Name, spec.Name, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (table.Columns.Count >= MaxColumnsPerTable)
            throw new NotSupportedException($"Table '{tableName}' already has {MaxColumnsPerTable} columns (Jet/ACE limit).");
        JetFormatBase format = _channel.Format;
        bool isLongValue = spec.Type is JetDataType.Memo or JetDataType.Ole;
        TdefParts parts = ParseTdef(table.DefinitionPage); // stitches continuation pages for a multi-page TDEF

        int maxCols = BinaryPrimitives.ReadUInt16LittleEndian(parts.Header.AsSpan(TdefMaxColumnsOffset, 2));
        int varCount = BinaryPrimitives.ReadUInt16LittleEndian(parts.Header.AsSpan(format.TdefVariableColumnsOffset, 2));
        int colCount = BinaryPrimitives.ReadUInt16LittleEndian(parts.Header.AsSpan(format.TdefColumnCountOffset, 2));

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
        };

        AppendColumnToParts(parts, colCount, TdefBuilder.BuildColumnDescriptor(newColumn, format), spec.Name, format);

        BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(format.TdefColumnCountOffset, 2), (ushort)(colCount + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(parts.Header.AsSpan(TdefMaxColumnsOffset, 2), (ushort)(maxCols + 1));
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

        bool variableLengthChange =
            !col.IsFixedLength && !newSpec.IsFixedLength && col.Type == newSpec.Type &&
            newSpec.Type is JetDataType.Text or JetDataType.Binary;
        if (!variableLengthChange)
            throw new NotSupportedException(
                $"ALTER COLUMN '{tableName}.{columnName}': only changing a variable text/binary column's length " +
                "is supported yet; changing the storage type requires a full column rewrite (not implemented).");

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

    private const int TdefMaxColumnsOffset = 0x29;

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
        info[0x0C] == FkTypeIncoming &&
        (int)BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(0x0D, 4)) == childBlockNum &&
        BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(0x11, 4)) == childPage;

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
        int infoStart = afterColumns + dataCount * IndexDataBlockSize;
        int namePos = infoStart + logicalCount * IndexInfoBlockSize;
        int defEnd = buf.ReadInt32(TdefLengthOffset);

        var stats = new List<byte[]>(dataCount);
        for (int i = 0; i < dataCount; i++) stats.Add(buf.Slice(statsStart + i * format.RealIndexEntrySize, format.RealIndexEntrySize).ToArray());
        var dataBlocks = new List<byte[]>(dataCount);
        for (int i = 0; i < dataCount; i++) dataBlocks.Add(buf.Slice(afterColumns + i * IndexDataBlockSize, IndexDataBlockSize).ToArray());

        var logical = new List<(byte[], byte[])>(logicalCount);
        int np = namePos;
        for (int i = 0; i < logicalCount; i++)
        {
            byte[] info = buf.Slice(infoStart + i * IndexInfoBlockSize, IndexInfoBlockSize).ToArray();
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
        BinaryPrimitives.WriteInt32LittleEndian(def.AsSpan(TdefLengthOffset, 4), defEnd);

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
        var b = new byte[IndexInfoBlockSize];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x00, 4), TdefRecordMarker);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x04, 4), number);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x08, 4), dataOrdinal);
        b[0x0C] = fkType;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x0D, 4), (uint)fkNumber);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x11, 4), fkTablePage);
        b[0x15] = upd;
        b[0x16] = del;
        b[0x17] = IndexTypeForeign;
        return b;
    }

    private const int ContinuationHeaderSize = 8;

    /// <summary>Reads a table definition, stitching continuation pages into one contiguous buffer (in
    /// the absolute coordinate space the descriptors use), and returns the continuation page numbers.</summary>
    private (LibRed.IO.PageBuffer Buffer, IReadOnlyList<int> ContinuationPages) ReadDefinition(int firstPage)
    {
        JetFormatBase format = _channel.Format;
        LibRed.IO.PageBuffer first = _channel.ReadPage(firstPage);
        int next = first.ReadInt32(format.TdefNextPageOffset);
        if (next == 0) return (first, []);

        var continuations = new List<int>();
        var assembled = new List<byte>(first.Span.ToArray());
        while (next != 0)
        {
            continuations.Add(next);
            LibRed.IO.PageBuffer cont = _channel.ReadPage(next);
            next = cont.ReadInt32(format.TdefNextPageOffset);
            assembled.AddRange(cont.Span[ContinuationHeaderSize..].ToArray());
        }
        return (new LibRed.IO.PageBuffer(assembled.ToArray(), firstPage), continuations);
    }

    /// <summary>
    /// Writes a definition buffer across the first page and, if it overflows, continuation pages (each
    /// <c>[0x02][0x01][free:2][next:4]</c> then data). The first page carries the whole definition in its
    /// coordinate space; each continuation contributes <see cref="ContinuationHeaderSize"/>-offset data.
    /// Existing continuation pages are reused before allocating new ones.
    /// </summary>
    private void WriteDefinition(int firstPage, byte[] def, IReadOnlyList<int> reusePages)
    {
        JetFormatBase format = _channel.Format;
        int ps = format.PageSize;
        int nextOffset = format.TdefNextPageOffset;

        if (def.Length + TdefContinuationReserve <= ps)
        {
            var only = new byte[ps];
            def.CopyTo(only, 0);
            BinaryPrimitives.WriteInt32LittleEndian(only.AsSpan(nextOffset, 4), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(only.AsSpan(TdefFreeSpaceOffset, 2), (ushort)(ps - def.Length - TdefContinuationReserve));
            _channel.WritePage(firstPage, only);
            return;
        }

        // Plan the continuation chunks: each holds up to (ps - header) data; the last also leaves the reserve.
        var chunks = new List<(int Offset, int Length)>();
        for (int offset = ps; offset < def.Length;)
        {
            int remaining = def.Length - offset;
            int length = remaining <= ps - ContinuationHeaderSize - TdefContinuationReserve
                ? remaining
                : ps - ContinuationHeaderSize;
            chunks.Add((offset, length));
            offset += length;
        }

        int reuse = 0;
        int[] pageNumbers = chunks.Select(_ => reuse < reusePages.Count ? reusePages[reuse++] : _allocator.Allocate()).ToArray();

        var page1 = new byte[ps];
        Array.Copy(def, 0, page1, 0, ps); // page 1 is completely full in a multi-page definition
        BinaryPrimitives.WriteInt32LittleEndian(page1.AsSpan(nextOffset, 4), pageNumbers[0]);
        BinaryPrimitives.WriteUInt16LittleEndian(page1.AsSpan(TdefFreeSpaceOffset, 2), 0);
        _channel.WritePage(firstPage, page1);

        for (int i = 0; i < chunks.Count; i++)
        {
            var (offset, length) = chunks[i];
            var page = new byte[ps];
            page[0] = (byte)PageType.TableDefinition;
            page[1] = 0x01;
            Array.Copy(def, offset, page, ContinuationHeaderSize, length);
            int next = i + 1 < pageNumbers.Length ? pageNumbers[i + 1] : 0;
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(nextOffset, 4), next);
            int free = next != 0 ? 0 : ps - ContinuationHeaderSize - length - TdefContinuationReserve;
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(TdefFreeSpaceOffset, 2), (ushort)free);
            _channel.WritePage(pageNumbers[i], page);
        }
    }

    private static byte[] BuildIndexDataBlock(IReadOnlyList<(int Id, bool Ascending)> columns, int rootPage, int usageRow, int usagePage, bool unique, bool required, bool ignoreNulls)
    {
        var b = new byte[IndexDataBlockSize];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0, 4), IndexDataMarker);
        for (int slot = 0; slot < IndexMaxColumnSlots; slot++)
        {
            int entry = 0x04 + slot * 3;
            if (slot < columns.Count)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(entry, 2), (short)columns[slot].Id);
                b[entry + 2] = columns[slot].Ascending ? (byte)0x01 : (byte)0x00; // 0x01 = ascending, 0x00 = descending
            }
            else System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(entry, 2), -1);
        }
        b[0x22] = (byte)usageRow;
        b[0x23] = (byte)usagePage; b[0x24] = (byte)(usagePage >> 8); b[0x25] = (byte)(usagePage >> 16);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x26, 4), rootPage);
        ushort flags = 0x0080; // always-set
        if (unique) flags |= 0x0001;
        if (ignoreNulls) flags |= 0x0002;
        if (required) flags |= 0x0008;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x2E, 2), flags);
        return b;
    }

    private static byte[] BuildPlainInfoBlock(int number, int dataOrdinal, bool isPrimary)
    {
        var b = new byte[IndexInfoBlockSize];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x00, 4), TdefRecordMarker);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x04, 4), number);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x08, 4), dataOrdinal);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x0D, 4), 0xFFFFFFFF); // no foreign key
        b[0x15] = PlainIndexAction;
        b[0x16] = PlainIndexAction;
        b[0x17] = isPrimary ? IndexTypePrimary : IndexTypeSecondary;
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
        int infoStart = pos + dataCount * IndexDataBlockSize;

        var blocks = new List<byte[]>(logicalCount + 1);
        for (int i = 0; i < logicalCount; i++)
            blocks.Add(buf.Slice(infoStart + i * IndexInfoBlockSize, IndexInfoBlockSize).ToArray());

        int namePos = infoStart + logicalCount * IndexInfoBlockSize;
        var names = new List<string>(logicalCount + 1);
        var nameBytes = new List<byte[]>(logicalCount + 1);
        for (int i = 0; i < logicalCount; i++)
        {
            int len = buf.ReadUInt16(namePos);
            nameBytes.Add(buf.Slice(namePos, 2 + len).ToArray());
            names.Add(System.Text.Encoding.Unicode.GetString(buf.Slice(namePos + 2, len)));
            namePos += 2 + len;
        }

        int defEnd = buf.ReadInt32(TdefLengthOffset);
        byte[] lvalRegion = buf.Slice(namePos, defEnd - namePos).ToArray(); // §3.3.2 list + 0xFFFF terminator

        string newName = NextHiddenRelationshipName(names);
        int k = names.Count(n => string.CompareOrdinal(n, newName) < 0); // name-sorted insert position
        blocks.Insert(k, BuildIncomingInfoBlock(inc));
        nameBytes.Insert(k, EncodeName(newName));

        int newDefEnd = infoStart + blocks.Count * IndexInfoBlockSize + nameBytes.Sum(n => n.Length) + lvalRegion.Length;
        if (newDefEnd > format.PageSize - TdefContinuationReserve)
            throw new NotSupportedException("No room in the table definition for another relationship (needs a continuation page).");

        var page = buf.Span.ToArray();
        int w = infoStart;
        foreach (byte[] b in blocks) { b.CopyTo(page.AsSpan(w)); w += b.Length; }
        foreach (byte[] n in nameBytes) { n.CopyTo(page.AsSpan(w)); w += n.Length; }
        lvalRegion.CopyTo(page.AsSpan(w));

        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefRealIndexCountOffset, 4), logicalCount + 1);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(TdefLengthOffset, 4), newDefEnd);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(TdefFreeSpaceOffset, 2),
            (ushort)(format.PageSize - newDefEnd - TdefContinuationReserve));
        _channel.WritePage(inc.ParentPage, page);
    }

    private byte[] BuildIncomingInfoBlock(IncomingRelationship inc)
    {
        var b = new byte[IndexInfoBlockSize];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x00, 4), TdefRecordMarker);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x04, 4), inc.Number);            // index_num
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x08, 4), inc.ReferencedOrdinal); // index_num2 -> referenced-key data block
        b[0x0C] = FkTypeIncoming;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x0D, 4), inc.ChildBlockNumber); // cross-link to child block
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x11, 4), inc.ChildPage);
        b[0x15] = inc.UpdateAction;
        b[0x16] = inc.DeleteAction;
        b[0x17] = IndexTypeForeign;
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
    private const int TablesContainerParentId = 0x0F000001;

    // The creating user's owner SID; for a workgroup-less database this 2-byte value is constant
    // across all user tables (verified on Northwind).
    private static readonly byte[] DefaultOwner = [0x69, 0x0C];

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
        SetByName(msysObjects, values, "ParentId", TablesContainerParentId);
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

    // Permissions for a newly created table object: the owner (SID 0x690C) and the Admin/Users
    // SID (0x680C), each with full access (verified against an ACE-created table).
    private const int FullAccessMask = 1048319; // 0xFFEFF
    private static readonly byte[] AdminSid = [0x68, 0x0C];

    /// <summary>
    /// Adds the two MSysACEs permission rows Access writes for a new table object (owner + admin,
    /// full access), maintaining the table's ObjectId index so Access's security check sees them.
    /// </summary>
    private void AddPermissionRows(int objectId)
    {
        TableDef msysAces = _catalog.FindTable("MSysACEs")
            ?? throw new InvalidOperationException("MSysACEs catalog table was not found.");

        foreach (byte[] sid in new[] { DefaultOwner, AdminSid })
        {
            var values = new object?[msysAces.Columns.Count];
            SetByName(msysAces, values, "ACM", FullAccessMask);
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
