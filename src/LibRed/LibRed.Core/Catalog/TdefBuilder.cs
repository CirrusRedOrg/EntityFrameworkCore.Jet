using System.Buffers.Binary;
using System.Text;
using LibRed.Formats;
using LibRed.Pages;

namespace LibRed.Catalog;

/// <summary>A column to create: its name, type, and (for fixed/text) byte length.</summary>
public sealed record ColumnSpec(
    string Name,
    JetDataType Type,
    int Length,
    bool IsFixedLength,
    bool IsAutoNumber = false,
    byte Precision = 0,
    byte Scale = 0,
    bool IsNullable = true,
    // AutoNumber (COUNTER) seed and increment — the first generated id is Seed, then +Increment each row.
    // Default 1/1 (a plain COUNTER). Stored in the TDEF header: last-value 0x14 = Seed-Increment, 0x18 = Increment.
    int Seed = 1,
    int Increment = 1,
    // Faithful-rebuild passthrough (ALTER COLUMN): an explicit column id (else the column's position is used)
    // and the column's original 25-byte descriptor, re-emitted verbatim except for the fields LibRed manages
    // so a rebuild preserves unmodeled bytes. Both null for an ordinary CREATE/ADD column. See ColumnDef.RawDescriptor.
    int? ColumnId = null,
    byte[]? RawDescriptor = null,
    // Undocumented flag bits (0x0F) Access sets on system-table columns: 0x10 marks a system-catalog column,
    // 0x20 additionally marks a security-identifier column (MSysObjects.Owner, MSysACEs.SID). User-table
    // columns leave these clear. Verified against real files; the desktop engine expects them on MSys* columns.
    byte SystemFlags = 0,
    // WITH COMPRESSION on a Text/Memo column: the 0x10 extended flag bit 0x01. Off unless asked for, which
    // is what ACE does for a column declared without it (LongTextStorageAccessTests).
    bool SupportsCompressedUnicode = false);

/// <summary>An index to create over the named columns, anchored at an already-allocated root page.</summary>
public sealed record IndexSpec(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsPrimaryKey,
    bool IsUnique,
    int RootPage,
    int UsageMapRow = 0,
    int UsageMapPage = 0);

/// <summary>
/// The trailing §3.3.2 column-usage-map entry for a long-value (memo/OLE) column: its column id and
/// pointers to the owned- and free-pages usage maps that will track the column's LVAL pages.
/// </summary>
public sealed record LongValueColumnSpec(int ColumnId, int UsedRow, int FreeRow, int MapPage);

/// <summary>
/// Serializes a table schema into a Jet 4 / ACE table-definition (TDEF) page — the inverse of
/// <see cref="TableDefinitionPage"/>, covering the whole definition: column descriptors and names, index
/// statistics, index-data and logical index-info blocks with their relationship linkage, and the trailing
/// long-value column-usage list. The result is one contiguous buffer in the absolute coordinate space the
/// descriptors use, which may exceed a page — splitting it across continuation pages is the caller's job
/// (<c>TableCreator.WriteDefinition</c>). Fixed columns are packed in declaration order; variable columns
/// are ranked by column id for the row var-offset table.
/// </summary>
public static class TdefBuilder
{
    // TDEF header offsets + the record marker / continuation-header size live on JetFormatBase (shared,
    // version-aware); the column-descriptor sub-offsets Access needs but the reader ignores are below.
    private const int ColumnRecordMarkerOffset = 0x01; // 0x0659
    private const int ColumnNumber2Offset = 0x09;      // duplicate column id

    // Index-data and index-info block layout + flags are shared with the reader via IndexBlockFormat / IndexFlags.
    private const int MaxIndexesPerTable = 32; // Jet/ACE limit, counting keys- and relationship-backing indexes
    private const int MaxColumnsPerTable = 255;
    private const int MaxNameBytes = 128; // verified Access limit: 64 UTF-16 code units

    /// <summary>
    /// One logical index-info block (§3.6). Several logical indexes may share a data block: a plain
    /// index has one, and a relationship adds one that reuses this table's side of the foreign key.
    /// <paramref name="DataOrdinal"/> is the data-block index (<c>index_num2</c>); <paramref name="Number"/>
    /// is the logical id (<c>index_num</c>). For a relationship, <paramref name="FkType"/> is 1 (incoming)
    /// or 2 (outgoing), <paramref name="FkNumber"/> is the other end's <c>index_num</c>, and
    /// <paramref name="FkTablePage"/> is the other table's TDEF page.
    /// </summary>
    public sealed record LogicalIndexSpec(
        int Number,
        int DataOrdinal,
        byte FkType,
        uint FkNumber,
        int FkTablePage,
        byte UpdateAction,
        byte DeleteAction,
        byte Type,
        string Name);

    public sealed record Result(byte[] Page, IReadOnlyList<ColumnDef> Columns);

    public static Result Build(
        JetFormatBase format,
        TableType tableType,
        IReadOnlyList<ColumnSpec> specs,
        IReadOnlyList<IndexSpec>? indexes = null,
        IReadOnlyList<LongValueColumnSpec>? longValueColumns = null,
        IReadOnlyList<LogicalIndexSpec>? logicalIndexes = null,
        Collation? collation = null,
        int complexAutoNumber = 0)
    {
        indexes ??= [];
        longValueColumns ??= [];
        ValidateColumnSpecs(format, specs);
        // Jet/ACE caps a table at 32 indexes, counting those backing primary keys, unique constraints
        // and relationships (§3.5 index-data blocks, the 0x33 count). Reject rather than write a bad TDEF.
        if (indexes.Count > MaxIndexesPerTable)
            throw new NotSupportedException(
                $"Table has {indexes.Count} indexes; Jet/ACE allows at most {MaxIndexesPerTable} per table (including those backing keys and relationships).");
        var columns = ResolveColumns(format, specs, collation ?? Collation.GeneralLegacy);
        IReadOnlyList<LogicalIndexSpec> logical = logicalIndexes ?? indexes.Select((ix, i) => new LogicalIndexSpec(
            Number: i, DataOrdinal: i, FkType: 0, FkNumber: IndexBlockFormat.NoForeignKey, FkTablePage: 0,
            UpdateAction: IndexBlockFormat.PlainAction, DeleteAction: IndexBlockFormat.PlainAction,
            Type: ix.IsPrimaryKey ? IndexBlockFormat.TypePrimary : IndexBlockFormat.TypeSecondary, Name: ix.Name)).ToList();
        ValidateIndexAndLongValueSpecs(columns, indexes, logical, longValueColumns);

        int definitionSize = DefinitionSize(format, columns, indexes, logical, longValueColumns);
        var page = new byte[Math.Max(format.PageSize, definitionSize)];

        page[0] = (byte)PageType.TableDefinition;
        page[format.TdefHeaderFlagsOffset] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(format.TdefRecordMarkerOffset, 4), JetFormatBase.TdefRecordMarker);
        // AutoNumber (COUNTER) config lives in the TDEF header: 0x18 = increment (default 1), and 0x14 =
        // the last-assigned value initialized to Seed-Increment so the first insert yields Seed. A table has
        // at most one AutoNumber column; with none, these stay at the plain-counter defaults (increment 1,
        // last 0). Verified vs ACE (COUNTER(1000, 7) → 0x18=7, 0x14=993).
        ColumnSpec? counter = specs.FirstOrDefault(s => s.IsAutoNumber);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefAutoNumberIncrementOffset, 4), counter?.Increment ?? 1);
        // Complex-type AutoNumber high-water (0x1C) — 0 for a table with no complex column, carried through on
        // a rebuild for faithful round-trip.
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefComplexAutoNumberOffset, 4), complexAutoNumber);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefNextPageOffset, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefRowCountOffset, 4), 0);
        if (counter is not null)
            BinaryPrimitives.WriteInt32LittleEndian(
                page.AsSpan(format.TdefLastAutoNumberOffset, 4), counter.Seed - counter.Increment);
        page[format.TdefTableTypeOffset] = (byte)tableType;
        // The 0x29 high-water is the next column id to hand out = max existing id + 1. For contiguous ids this
        // equals the column count; when a rebuild carries a burned id (ALTER COLUMN) it exceeds the count.
        int maxColumnId = columns.Select(c => c.ColumnId).DefaultIfEmpty(-1).Max();
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefMaxColumnsOffset, 2), (ushort)(maxColumnId + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefVariableColumnsOffset, 2),
            (ushort)columns.Count(c => !c.IsFixedLength));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefColumnCountOffset, 2), (ushort)columns.Count);
        // Logical index count (0x2F) may exceed the real data-block count (0x33): a relationship adds
        // a logical block that shares a data block. Without explicit logical specs the two are equal.
        int logicalCount = logical.Count;
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefLogicalIndexCountOffset, 4), logicalCount);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefIndexCountOffset, 4), indexes.Count);

        // The per-index statistics blocks (12 bytes each, one per data block) precede the columns.
        int columnBlock = format.TdefRealIndexBlockOffset + indexes.Count * format.RealIndexEntrySize;
        WriteColumnDescriptors(page, format, columns, columnBlock);
        int afterNames = WriteColumnNames(page, format, columns, columnBlock + columns.Count * format.ColumnDescriptorSize);

        int definitionEnd = WriteIndexes(page, format, columns, indexes, logical, longValueColumns, afterNames);
        if (definitionEnd != definitionSize)
            throw new InvalidOperationException(
                $"TDEF sizing preflight calculated {definitionSize} bytes but serialization wrote {definitionEnd}.");

        // Definition length and remaining free space (Access reserves an 8-byte continuation header). For a
        // multi-page definition the caller recomputes the first page's free space, so clamp at 0 here.
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefLengthOffset, 4), definitionEnd);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefFreeSpaceOffset, 2),
            (ushort)Math.Max(0, format.PageSize - definitionEnd - JetFormatBase.TdefContinuationHeaderSize));

        return new Result(page, columns);
    }

    private static void ValidateColumnSpecs(JetFormatBase format, IReadOnlyList<ColumnSpec> specs)
    {
        if (specs.Count > MaxColumnsPerTable)
            throw new NotSupportedException(
                $"Table has {specs.Count} columns; Jet/ACE allows at most {MaxColumnsPerTable} per table.");

        var ids = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long fixedBytes = 0;
        int variableColumns = 0, highWater = -1;
        for (int i = 0; i < specs.Count; i++)
        {
            ColumnSpec spec = specs[i];
            ValidateNameLength(spec.Name, "Column");
            if (!names.Add(spec.Name))
                throw new NotSupportedException($"Column name '{spec.Name}' is used more than once.");
            if (spec.RawDescriptor is { } raw && raw.Length != format.ColumnDescriptorSize)
                throw new NotSupportedException(
                    $"Column '{spec.Name}' carries a {raw.Length}-byte raw descriptor; this format requires {format.ColumnDescriptorSize} bytes.");
            int id = spec.ColumnId ?? i;
            if (id is < 0 or >= MaxColumnsPerTable)
                throw new NotSupportedException(
                    $"Column '{spec.Name}' has id {id}; Jet/ACE column ids range from 0 through {MaxColumnsPerTable - 1}.");
            if (!ids.Add(id))
                throw new NotSupportedException($"Column id {id} is used more than once.");
            if (spec.Length is < 0 or > ushort.MaxValue)
                throw new NotSupportedException(
                    $"Column '{spec.Name}' has byte length {spec.Length}, which does not fit the TDEF field.");
            RecordLayout.ValidateFieldWidth(spec.Name, spec.Type, spec.Length);
            if (spec.IsFixedLength && spec.Type != JetDataType.Boolean)
                fixedBytes += spec.Length;
            if (!spec.IsFixedLength) variableColumns++;
            highWater = Math.Max(highWater, id);
        }

        // The fixed region used to be checked only against the TDEF's 2-byte offset fields (65535). ACE's real
        // limit is far tighter — the widest record the declaration allows must still be storable — and it
        // subsumes that one, since no column may now exceed 510 bytes. Without this a plain CreateTable of
        // 252 GUID columns writes a database Access will not open at all.
        RecordLayout.ValidateRecordFits(null, (int)fixedBytes, variableColumns, highWater + 1, format);
    }

    private static void ValidateIndexAndLongValueSpecs(
        IReadOnlyList<ColumnDef> columns,
        IReadOnlyList<IndexSpec> indexes,
        IReadOnlyList<LogicalIndexSpec> logical,
        IReadOnlyList<LongValueColumnSpec> longValueColumns)
    {
        if (logical.Count > MaxIndexesPerTable)
            throw new NotSupportedException(
                $"Table has {logical.Count} logical indexes; Jet/ACE allows at most {MaxIndexesPerTable}.");

        var columnByName = columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (IndexSpec index in indexes)
        {
            ValidateNameLength(index.Name, "Index");
            if (index.Columns.Count > IndexBlockFormat.MaxColumns)
                throw new NotSupportedException(
                    $"Index '{index.Name}' spans {index.Columns.Count} columns; Jet/ACE allows {IndexBlockFormat.MaxColumns}.");
            foreach (string column in index.Columns)
                if (!columnByName.ContainsKey(column))
                    throw new NotSupportedException($"Index '{index.Name}' refers to unknown column '{column}'.");
            ValidateUsageMapPointer(index.UsageMapRow, index.UsageMapPage, $"index '{index.Name}'", allowNull: true);
        }
        foreach (LogicalIndexSpec index in logical) ValidateNameLength(index.Name, "Logical index");

        var columnById = columns.ToDictionary(c => c.ColumnId);
        var seen = new HashSet<int>();
        foreach (LongValueColumnSpec value in longValueColumns)
        {
            if (!seen.Add(value.ColumnId))
                throw new NotSupportedException($"Long-value column id {value.ColumnId} has more than one usage-map entry.");
            if (!columnById.TryGetValue(value.ColumnId, out ColumnDef? column)
                || column.Type is not (JetDataType.Memo or JetDataType.Ole))
                throw new NotSupportedException(
                    $"Long-value usage-map entry {value.ColumnId} does not identify a Memo/OLE column.");
            ValidateUsageMapPointer(value.UsedRow, value.MapPage, $"long-value column '{column.Name}' owned map", allowNull: false);
            ValidateUsageMapPointer(value.FreeRow, value.MapPage, $"long-value column '{column.Name}' free map", allowNull: false);
        }
    }

    private static void ValidateUsageMapPointer(int row, int page, string owner, bool allowNull)
    {
        if (allowNull && row == 0 && page == 0) return;
        if (row is < 0 or > byte.MaxValue || page is <= 0 or > 0xFFFFFF)
            throw new NotSupportedException(
                $"The {owner} usage-map pointer ({row}, {page}) does not fit its 1-byte row / 3-byte page fields.");
    }

    private static void ValidateNameLength(string name, string kind)
    {
        int length = Encoding.Unicode.GetByteCount(name);
        if (length is 0 or > MaxNameBytes)
            throw new NotSupportedException(
                $"{kind} name is {length} UTF-16 bytes; Jet/ACE names must use 1 through {MaxNameBytes / 2} characters.");
    }

    private static int DefinitionSize(
        JetFormatBase format,
        IReadOnlyList<ColumnDef> columns,
        IReadOnlyList<IndexSpec> indexes,
        IReadOnlyList<LogicalIndexSpec> logical,
        IReadOnlyList<LongValueColumnSpec> longValueColumns)
    {
        long size = format.TdefRealIndexBlockOffset
            + (long)indexes.Count * format.RealIndexEntrySize
            + (long)columns.Count * format.ColumnDescriptorSize
            + columns.Sum(c => 2L + Encoding.Unicode.GetByteCount(c.Name))
            + (long)indexes.Count * IndexBlockFormat.DataBlockSize
            + (long)logical.Count * IndexBlockFormat.InfoBlockSize
            + logical.Sum(i => 2L + Encoding.Unicode.GetByteCount(i.Name))
            + (long)longValueColumns.Count * 10
            + 2;
        if (size > TdefChainReader.MaxDefinitionLength)
            throw new NotSupportedException(
                $"The serialized table definition requires {size} bytes; LibRed's validated TDEF budget is {TdefChainReader.MaxDefinitionLength}.");
        return (int)size;
    }

    /// <summary>Writes the index structures and returns the offset just past them (the definition end).</summary>
    private static int WriteIndexes(byte[] page, JetFormatBase format, List<ColumnDef> columns, IReadOnlyList<IndexSpec> indexes, IReadOnlyList<LogicalIndexSpec> logical, IReadOnlyList<LongValueColumnSpec> longValueColumns, int dataBlockStart)
    {
        var columnIdByName = columns.ToDictionary(c => c.Name, c => c.ColumnId, StringComparer.OrdinalIgnoreCase);

        // The index-data block (§3.5) has a fixed array of exactly IndexBlockFormat.MaxColumns column slots and no
        // count field, so an index — hence any PRIMARY KEY / UNIQUE / FOREIGN KEY — spans at most that
        // many columns. Reject an over-wide index rather than silently truncating it.
        foreach (IndexSpec ix in indexes)
            if (ix.Columns.Count > IndexBlockFormat.MaxColumns)
                throw new NotSupportedException(
                    $"Index '{ix.Name}' spans {ix.Columns.Count} columns; Jet/ACE indexes (and the keys built on them) are limited to {IndexBlockFormat.MaxColumns}.");

        // 1. Index-data blocks: columns, root page, unique flag.
        for (int i = 0; i < indexes.Count; i++)
        {
            IndexSpec index = indexes[i];
            int block = dataBlockStart + i * IndexBlockFormat.DataBlockSize;

            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(block, 4), IndexBlockFormat.DataMarker);
            for (int slot = 0; slot < IndexBlockFormat.MaxColumns; slot++)
            {
                int entry = block + IndexBlockFormat.ColumnsOffset + slot * IndexBlockFormat.ColumnSlotSize;
                if (slot < index.Columns.Count)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(page.AsSpan(entry, 2), (short)columnIdByName[index.Columns[slot]]);
                    page[entry + 2] = IndexBlockFormat.ColumnAscending;
                }
                else
                {
                    BinaryPrimitives.WriteInt16LittleEndian(page.AsSpan(entry, 2), IndexBlockFormat.ColumnUnused);
                }
            }
            page[block + IndexBlockFormat.UsageMapRowOffset] = (byte)index.UsageMapRow;
            page[block + IndexBlockFormat.UsageMapRowOffset + 1] = (byte)index.UsageMapPage;
            page[block + IndexBlockFormat.UsageMapRowOffset + 2] = (byte)(index.UsageMapPage >> 8);
            page[block + IndexBlockFormat.UsageMapRowOffset + 3] = (byte)(index.UsageMapPage >> 16);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexBlockFormat.RootPageOffset, 4), index.RootPage);
            ushort flags = IndexFlags.AlwaysSet;
            if (index.IsUnique) flags |= IndexFlags.Unique;
            if (index.IsPrimaryKey) flags |= IndexFlags.Required;
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(block + IndexBlockFormat.FlagsOffset, 2), flags);
        }

        // 2. Index-info blocks (one per logical index) and 3. their names. Without explicit logical
        // specs each data block maps 1:1 to a plain info block (back-compat); with them, relationship
        // blocks are included and stored name-sorted (matching Access).
        int infoStart = dataBlockStart + indexes.Count * IndexBlockFormat.DataBlockSize;
        for (int i = 0; i < logical.Count; i++)
        {
            LogicalIndexSpec li = logical[i];
            int block = infoStart + i * IndexBlockFormat.InfoBlockSize;
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(block + IndexBlockFormat.InfoMarkerOffset, 4), JetFormatBase.TdefRecordMarker);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexBlockFormat.InfoNumberOffset, 4), li.Number);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexBlockFormat.InfoDataNumberOffset, 4), li.DataOrdinal);
            page[block + IndexBlockFormat.InfoFkTypeOffset] = li.FkType;
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(block + IndexBlockFormat.InfoFkNumberOffset, 4), li.FkNumber);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexBlockFormat.InfoFkTablePageOffset, 4), li.FkTablePage);
            page[block + IndexBlockFormat.InfoUpdateActionOffset] = li.UpdateAction;
            page[block + IndexBlockFormat.InfoDeleteActionOffset] = li.DeleteAction;
            page[block + IndexBlockFormat.InfoTypeOffset] = li.Type;
        }

        int namePos = infoStart + logical.Count * IndexBlockFormat.InfoBlockSize;
        foreach (LogicalIndexSpec li in logical)
        {
            byte[] name = System.Text.Encoding.Unicode.GetBytes(li.Name);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), (ushort)name.Length);
            namePos += 2;
            name.CopyTo(page.AsSpan(namePos));
            namePos += name.Length;
        }

        // After the index names comes a per-long-value-column (memo/OLE) usage-map list (spec §3.3.2):
        // one 10-byte entry {col_num:2, used_pages:4, free_pages:4} per column, in ascending column
        // order, terminated by col_num 0xFFFF. Each pointer is a 1-byte usage-map row + 3-byte page.
        // The terminator is mandatory even when the list is empty; the definition length includes it.
        foreach (LongValueColumnSpec lv in longValueColumns.OrderBy(l => l.ColumnId))
        {
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), (ushort)lv.ColumnId);
            WriteUsageMapPointer(page, namePos + 2, lv.UsedRow, lv.MapPage);
            WriteUsageMapPointer(page, namePos + 6, lv.FreeRow, lv.MapPage);
            namePos += 10;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), 0xFFFF);
        namePos += 2;
        return namePos;
    }

    /// <summary>Writes a 4-byte usage-map pointer: a 1-byte record row followed by a 3-byte page.</summary>
    private static void WriteUsageMapPointer(byte[] page, int offset, int row, int mapPage)
    {
        page[offset] = (byte)row;
        page[offset + 1] = (byte)mapPage;
        page[offset + 2] = (byte)(mapPage >> 8);
        page[offset + 3] = (byte)(mapPage >> 16);
    }

    private static List<ColumnDef> ResolveColumns(JetFormatBase format, IReadOnlyList<ColumnSpec> specs, Collation collation)
    {
        // A column's id is its declaration position unless the spec pins one explicitly (ALTER COLUMN burns a
        // fresh id at the same position, so ids can be non-contiguous — the row codec reads var-index from the
        // descriptor, not from id arithmetic). Variable columns are addressed in ascending column-id order.
        int EffectiveId(int i) => specs[i].ColumnId ?? i;

        var variableRank = new Dictionary<int, int>();
        int rank = 0;
        foreach (int i in Enumerable.Range(0, specs.Count).Where(i => !specs[i].IsFixedLength).OrderBy(EffectiveId))
            variableRank[i] = rank++;

        // Descriptor offset 7 ("variable-table index") = number of variable columns with a smaller column-id.
        // Access stores this on EVERY column (fixed columns included) and its strict row reader relies on it;
        // writing 0 on fixed columns yields a file Access rejects with "record(s) cannot be read".
        int VarTableIndex(int i) =>
            Enumerable.Range(0, specs.Count).Count(j => !specs[j].IsFixedLength && EffectiveId(j) < EffectiveId(i));

        // Fixed-data offsets are assigned in ascending column-id order, NOT declaration order — Access lays the
        // fixed columns out by column id (e.g. MSysACEs stores ObjectId(id0) at offset 0 even though its
        // descriptor is written after ACM). Only differs from declaration order when ids are reordered (MSys*).
        bool Occupies(int i) => specs[i].IsFixedLength && specs[i].Type != JetDataType.Boolean;
        var fixedOffsets = new Dictionary<int, int>();
        int running = 0;
        foreach (int i in Enumerable.Range(0, specs.Count).Where(Occupies).OrderBy(EffectiveId))
        {
            fixedOffsets[i] = running;
            running += specs[i].Length;
        }

        var columns = new List<ColumnDef>(specs.Count);
        for (int i = 0; i < specs.Count; i++)
        {
            ColumnSpec s = specs[i];
            // Booleans live in the null bitmap and occupy no fixed-data bytes, so they don't
            // advance the fixed offset (matching how the row codec skips them).
            bool occupiesFixedData = s.IsFixedLength && s.Type != JetDataType.Boolean;
            // The documented flag bits LibRed doesn't drive from the spec (updatable, GUID-autonumber, hyperlink,
            // compressed-Unicode, calculated) are carried through a rebuild by reading them off the original
            // descriptor. A fresh column (no raw) is updatable with the rest clear — the CREATE default.
            bool hasRaw = s.RawDescriptor is { } r && r.Length == format.ColumnDescriptorSize;
            byte rawFlags = hasRaw ? s.RawDescriptor![format.ColumnFlagsOffset] : JetFormatBase.ColumnFlagUpdatable;
            byte rawExt = hasRaw ? s.RawDescriptor![format.ColumnExtendedFlagsOffset] : (byte)0;
            columns.Add(new ColumnDef
            {
                Name = s.Name,
                Type = s.Type,
                Index = i,
                ColumnId = EffectiveId(i),
                Length = s.Length,
                FixedOffset = occupiesFixedData ? fixedOffsets[i] : 0,
                VariableIndex = s.IsFixedLength ? -1 : variableRank[i],
                VariableTableIndex = VarTableIndex(i),
                IsFixedLength = s.IsFixedLength,
                IsAutoNumber = s.IsAutoNumber,
                IsUpdatable = (rawFlags & JetFormatBase.ColumnFlagUpdatable) != 0,
                IsGuidAutoNumber = (rawFlags & JetFormatBase.ColumnFlagGuidAutoNumber) != 0,
                IsHyperlink = (rawFlags & JetFormatBase.ColumnFlagHyperlink) != 0,
                SupportsCompressedUnicode = s.SupportsCompressedUnicode
                    || (rawExt & JetFormatBase.ColumnExtFlagCompressedUnicode) != 0,
                IsCalculated = (rawExt & JetFormatBase.ColumnExtFlagCalculated) != 0,
                SystemFlags = s.SystemFlags,
                Precision = s.Precision,
                Scale = s.Scale,
                // Numeric columns carry no collation (their 0x0B/0x0C bytes are precision/scale); every
                // other column inherits the database's collating order.
                Collation = s.Type == JetDataType.FixedPoint ? Collation.GeneralLegacy : collation,
                RawDescriptor = s.RawDescriptor,
            });
        }
        return columns;
    }

    private static void WriteColumnDescriptors(byte[] page, JetFormatBase format, List<ColumnDef> columns, int columnBlock)
    {
        for (int i = 0; i < columns.Count; i++)
            BuildColumnDescriptor(columns[i], format).CopyTo(page.AsSpan(columnBlock + i * format.ColumnDescriptorSize));
    }

    /// <summary>Builds one column's fixed-size (25-byte Jet4) descriptor. Shared by CREATE TABLE and
    /// ALTER TABLE ADD COLUMN.</summary>
    public static byte[] BuildColumnDescriptor(ColumnDef c, JetFormatBase format)
    {
        // Faithful round-trip: when we have the column's original bytes (a rebuild of a read column that we're
        // NOT retyping), start from them and overwrite every field LibRed models, so the only bytes that survive
        // untouched are the genuinely reserved/unknown ones — the reserved words 0x03/0x11 and the undocumented
        // bits of the two flag bytes. A fresh column (RawDescriptor null — CREATE, ADD COLUMN, or the
        // deliberately-retyped ALTER target) builds from zero.
        byte[] d = c.RawDescriptor is { } raw && raw.Length == format.ColumnDescriptorSize
            ? (byte[])raw.Clone()
            : new byte[format.ColumnDescriptorSize];
        d[format.ColumnTypeOffset] = (byte)c.Type;
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(ColumnRecordMarkerOffset, 2), (ushort)JetFormatBase.TdefRecordMarker);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnNumberOffset, 2), (ushort)c.ColumnId);
        // Offset 0x09 repeats the id on a user column. Every creator does it — ACE's SQL DDL, DAO's object
        // model and DAO-executed SQL — and every user table in every fixture carries it, while only the
        // engine's own bootstrap tables (MSysObjects and friends) leave it zero, which is what a system
        // column keeps here. An earlier comment claimed real files store zero; that had been read off the
        // system tables alone. On a rebuild the original value survives untouched, because it stops
        // tracking 0x05 once an ALTER COLUMN type change burns a new id there (§3.8).
        if (c.RawDescriptor is null or { Length: 0 })
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnSecondaryNumberOffset, 2),
                (ushort)(c.SystemFlags != 0 ? 0 : c.ColumnId));
        // Offset 7 = variable-table index (count of variable columns with a smaller id), stored on fixed columns
        // too. Prefer the precomputed value; fall back to the legacy rule (0 for fixed) when unset (ADD COLUMN).
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnVariableIndexOffset, 2),
            (ushort)(c.VariableTableIndex >= 0 ? c.VariableTableIndex : (c.IsFixedLength ? 0 : c.VariableIndex)));
        if (c.Type == JetDataType.FixedPoint)
        {
            d[format.ColumnPrecisionOffset] = c.Precision;
            d[format.ColumnScaleOffset] = c.Scale;
        }
        else if (c.Type == JetDataType.DateTimeExtended)
        {
            // Date/Time Extended is 42 bytes of ASCII with nothing to collate, and ACE writes only the LOW
            // byte of the LANGID here, clearing the sublanguage half — the primary language id on its own,
            // with sort id and version zero. Measured across five collating orders: 0x0409 and 0x0809 both
            // give 0x0009, 0x0407 gives 0x0007, 0x040E 0x000E, 0x041D 0x001D, while a Text column in the
            // same table carries the full LANGID each time. (On an en-US database this looks like a
            // constant 0x0009, which is how it was first mis-read.)
            BinaryPrimitives.WriteUInt16LittleEndian(
                d.AsSpan(format.ColumnLocaleOffset, 2), (ushort)((ushort)c.Collation.Order & 0x00FF));
            d[format.ColumnCollationSortIdOffset] = 0;
            d[format.ColumnCollationVersionOffset] = 0;
        }
        else
        {
            // Non-numeric columns use the precision/scale bytes (0x0B/0x0C) onward for the text collation:
            // 0x0B/0x0C LANGID, 0x0D sort id, 0x0E sort-order version. Together a 32-bit LCID with the version
            // in its unused top byte. General legacy is LANGID 1033 (0x0409), sort id 0, version 0.
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnLocaleOffset, 2), (ushort)c.Collation.Order);
            d[format.ColumnCollationSortIdOffset] = c.Collation.SortId;
            d[format.ColumnCollationVersionOffset] = c.Collation.Version;
        }
        // Compose the flag byte (0x0F) from EVERY documented bit; only the undocumented bits survive from the
        // original (zero in every file observed). Likewise the extended-flag byte (0x10).
        byte flags = (byte)(
            (c.IsUpdatable ? JetFormatBase.ColumnFlagUpdatable : 0)
            | (c.IsFixedLength ? JetFormatBase.ColumnFlagFixedLength : 0)
            | (c.IsAutoNumber ? JetFormatBase.ColumnFlagAutoNumber : 0)
            | (c.IsGuidAutoNumber ? JetFormatBase.ColumnFlagGuidAutoNumber : 0)
            | (c.IsHyperlink ? JetFormatBase.ColumnFlagHyperlink : 0));
        d[format.ColumnFlagsOffset] = (byte)((d[format.ColumnFlagsOffset] & ~JetFormatBase.ColumnFlagsDocumented) | flags | c.SystemFlags);

        byte extFlags = (byte)(
            (c.SupportsCompressedUnicode ? JetFormatBase.ColumnExtFlagCompressedUnicode : 0)
            | (c.IsCalculated ? JetFormatBase.ColumnExtFlagCalculated : 0));
        d[format.ColumnExtendedFlagsOffset] = (byte)((d[format.ColumnExtendedFlagsOffset] & ~JetFormatBase.ColumnExtFlagsDocumented) | extFlags);

        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnFixedOffsetOffset, 2), (ushort)c.FixedOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnLengthOffset, 2), (ushort)c.Length);
        return d;
    }

    private static int WriteColumnNames(byte[] page, JetFormatBase format, List<ColumnDef> columns, int namePos)
    {
        _ = format;
        foreach (ColumnDef c in columns)
        {
            byte[] name = Encoding.Unicode.GetBytes(c.Name);
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), (ushort)name.Length);
            namePos += 2;
            name.CopyTo(page.AsSpan(namePos));
            namePos += name.Length;
        }
        return namePos;
    }
}
