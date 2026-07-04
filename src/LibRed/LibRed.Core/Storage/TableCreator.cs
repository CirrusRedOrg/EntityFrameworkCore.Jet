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
        IReadOnlyList<(string Name, string Expression)>? checkConstraints = null)
    {
        relationships ??= [];
        uniqueConstraints ??= [];
        columnDefaults ??= [];
        checkConstraints ??= [];

        // A table name is unique (case-insensitively) across the database; reject a duplicate rather
        // than writing a second MSysObjects row that shadows the existing table.
        if (_catalog.FindTable(name) is not null)
            throw new InvalidOperationException($"Table '{name}' already exists.");

        JetFormatBase format = _channel.Format;

        // Allocate the pages the table needs through the global free-pages map (so Access accounts
        // for them). Like Access, a fresh table has NO data page — the first is allocated lazily on
        // the first insert — so its usage maps start empty.
        int tdefPage = _allocator.Allocate();
        int usageMapPage = _allocator.Allocate();

        // Usage-map records live on one page, in the order Access writes them: row 0 = table owned,
        // row 1 = table free, then two rows (owned + free) per long-value (memo/OLE) column, then one
        // row per index. All start empty — a fresh table owns no data, LVAL, or index pages yet.
        var longValueCols = columns.Select((c, i) => (Column: c, Id: i))
            .Where(x => x.Column.Type is JetDataType.Memo or JetDataType.Ole)
            .ToList();
        int columnMapRows = longValueCols.Count * 2;
        int firstIndexMapRow = 2 + columnMapRows; // usage-map row of the first index

        // The table's data-block indexes: the primary key (unique), then a unique index per UNIQUE
        // constraint, then one non-unique index per foreign key over its child columns — Access enforces
        // a relationship through an index on the FK columns. Each carries the relationship (if any) it backs.
        var indexPlans = new List<(string Name, IReadOnlyList<string> Columns, bool IsPk, bool IsUnique, RelationshipSpec? Fk)>();
        if (primaryKey is { Count: > 0 })
            indexPlans.Add(("PrimaryKey", primaryKey, true, true, null));
        foreach (UniqueIndexSpec unique in uniqueConstraints)
            indexPlans.Add((unique.Name, unique.Columns, false, true, null));
        foreach (RelationshipSpec fk in relationships)
            indexPlans.Add((fk.Name, fk.Columns.Select(c => c.Column).ToList(), false, false, fk));

        WriteUsageMaps(format, usageMapPage, mapCount: 2 + columnMapRows + indexPlans.Count);

        // §3.3.2 entries: each long-value column gets used/free maps at rows 2+2j / 3+2j.
        var longValueSpecs = longValueCols
            .Select((x, j) => new LongValueColumnSpec(x.Id, UsedRow: 2 + 2 * j, FreeRow: 3 + 2 * j, MapPage: usageMapPage))
            .ToList();

        // Each index is an empty leaf root, populated as rows are inserted.
        var indexes = new List<IndexSpec>(indexPlans.Count);
        for (int i = 0; i < indexPlans.Count; i++)
        {
            var plan = indexPlans[i];
            int rootPage = _allocator.Allocate();
            WriteEmptyLeafIndexPage(format, rootPage, owner: tdefPage);
            indexes.Add(new IndexSpec(plan.Name, plan.Columns, plan.IsPk, plan.IsUnique,
                rootPage, UsageMapRow: firstIndexMapRow + i, UsageMapPage: usageMapPage));
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
            byte upd = fk.CascadeUpdate ? CascadeAction : NoCascadeAction;
            byte del = fk.CascadeDelete ? CascadeAction : NoCascadeAction;
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
        _channel.WritePage(tdefPage, tdef);

        AddCatalogRow(name, tdefPage, columnDefaults, checkConstraints);
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
        JetFormatBase format = _channel.Format;
        TableDef table = _catalog.FindTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' was not found.");

        // Read the whole definition (stitching any existing continuation pages) so the surgical insert
        // works in absolute coordinates; the old continuation pages are reused when we write it back.
        (LibRed.IO.PageBuffer buf, IReadOnlyList<int> existingContinuations) = ReadDefinition(table.DefinitionPage);
        if (buf.ReadInt32(format.TdefRowCountOffset) != 0)
            throw new NotSupportedException($"Adding an index to the non-empty table '{tableName}' is not supported yet.");

        var columnIdByName = table.Columns.ToDictionary(c => c.Name, c => c.ColumnId, StringComparer.OrdinalIgnoreCase);
        var slots = columns.Select(c => columnIdByName.TryGetValue(c.Column, out int id) ? (Id: id, Ascending: !c.Descending)
            : throw new InvalidOperationException($"Column '{c.Column}' does not exist in '{tableName}'.")).ToList();

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
        WriteUsageMaps(format, usageMapPage, mapCount: newIndexUsageRow + 1); // empty table: all maps empty

        // Assemble the new definition: header + existing stats, a new stats block, columns + names +
        // existing data blocks, the new data block, then the logical blocks (new one inserted, name-sorted)
        // and their names, and finally the unchanged long-value region.
        bool required = isPrimary || disallowNull;
        bool unique = isUnique || isPrimary;
        byte[] newData = BuildIndexDataBlock(slots, rootPage, newIndexUsageRow, usageMapPage, unique, required, ignoreNulls);
        byte[] newInfo = BuildPlainInfoBlock(maxNum + 1, dataOrdinal: dataCount, isPrimary);

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
        const int BitmapBytes = 64;
        const int MapLength = 1 + 4 + BitmapBytes;

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
        IReadOnlyList<(string Column, string DefaultSql)> columnDefaults,
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

        // Column DEFAULTs (per-column properties) and CHECK constraints (a table property) both live in
        // the object's extended-properties (LvProp) blob.
        var props = columnDefaults
            .Select(d => new PropertyBlob.Property(d.Column, PropertyBlob.DefaultValueProperty, d.DefaultSql))
            .ToList();
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
