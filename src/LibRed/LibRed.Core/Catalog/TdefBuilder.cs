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
    bool IsNullable = true);

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
/// <see cref="TableDefinitionPage"/>. This first cut builds a single-page definition with no
/// indexes; index-data blocks and continuation pages come later. Fixed columns are packed in
/// declaration order; variable columns are ranked by column id for the row var-offset table.
/// </summary>
public static class TdefBuilder
{
    private const byte PageTypeTableDefinition = 0x02;
    private const byte ColumnFlagUpdatable = 0x02;

    // TDEF header fields Access validates that the reader currently ignores (verified vs ACE).
    private const int TdefHeaderFlagsOffset = 0x01;   // observed 0x01
    private const int TdefFreeSpaceOffset = 0x02;     // bytes free in this page
    private const int TdefLengthOffset = 0x08;        // total definition length
    private const int TdefMarkerOffset = 0x0C;        // 0x00000659 record marker
    private const int TdefConstantOffset = 0x18;      // observed constant 0x00000001
    private const int TdefMaxColumnsOffset = 0x29;    // maximum column count
    private const uint TdefRecordMarker = 0x659;
    private const int TdefContinuationReserve = 8;    // free space excludes the 8-byte continuation header

    // Column-descriptor sub-offsets the reader doesn't consume but Access does.
    private const int ColumnRecordMarkerOffset = 0x01; // 0x0659
    private const int ColumnNumber2Offset = 0x09;      // duplicate column id
    private const int ColumnVariableIndexOffset = 0x07;
    private const int ColumnLocaleLowOffset = 0x0B;    // non-numeric: en-US locale 0x0409
    private const int ColumnLocaleHighOffset = 0x0C;
    private const byte LocaleLow = 0x09;
    private const byte LocaleHigh = 0x04;

    // Index-data block (52 bytes): a 0x783 marker, 10 column slots, root page, unique flag.
    private const int IndexBlockSize = 52;
    private const int IndexMaxColumns = 10;
    private const int MaxIndexesPerTable = 32; // Jet/ACE limit, counting keys- and relationship-backing indexes
    private const int IndexColumnSlotSize = 3;
    private const int IndexColumnsOffset = 0x04;
    private const int IndexUsageMapRowOffset = 0x22;  // 1-byte row + 3-byte page for the index's pages
    private const int IndexRootPageOffset = 0x26;
    private const int IndexFlagsOffset = 0x2E;
    private const short IndexColumnUnused = -1; // 0xFFFF
    private const byte IndexColumnAscending = 0x01;
    private const ushort IndexFlagUnique = 0x0001;
    private const ushort IndexFlagRequired = 0x0008;
    private const ushort IndexFlagAlwaysSet = 0x0080; // Access 2000+
    private const uint IndexDataMarker = 0x783;

    // Index-info block (28 bytes, one per logical index): links a name to a data block.
    private const int IndexInfoBlockSize = 28;
    private const int IndexInfoMarkerOffset = 0x00;       // 0x0659
    private const int IndexInfoNumberOffset = 0x04;
    private const int IndexInfoDataNumberOffset = 0x08;
    private const int IndexInfoFkTypeOffset = 0x0C;       // 0=none, 1=incoming, 2=outgoing relationship
    private const int IndexInfoFkNumberOffset = 0x0D;     // 0xFFFFFFFF = no foreign key
    private const int IndexInfoFkTablePageOffset = 0x11;  // the other table's TDEF page (0 = none)
    private const int IndexInfoUpdateActionOffset = 0x15;
    private const int IndexInfoDeleteActionOffset = 0x16;
    private const byte IndexActionDefault = 0x04;         // observed on a plain index (no relationship)
    private const int IndexInfoTypeOffset = 0x17;
    private const byte IndexTypeSecondary = 0x00;
    private const byte IndexTypePrimary = 0x01;
    private const uint NoForeignKey = 0xFFFFFFFF;

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
        IReadOnlyList<LogicalIndexSpec>? logicalIndexes = null)
    {
        indexes ??= [];
        longValueColumns ??= [];
        // Jet/ACE caps a table at 32 indexes, counting those backing primary keys, unique constraints
        // and relationships (§3.5 index-data blocks, the 0x33 count). Reject rather than write a bad TDEF.
        if (indexes.Count > MaxIndexesPerTable)
            throw new NotSupportedException(
                $"Table has {indexes.Count} indexes; Jet/ACE allows at most {MaxIndexesPerTable} per table (including those backing keys and relationships).");
        var columns = ResolveColumns(format, specs);
        // The definition (descriptors + names + index blocks) can exceed one page for a wide table; build it
        // into a buffer sized generously for the worst case, then the caller splits it across continuation
        // pages when writing. 512 bytes/column comfortably covers a 25-byte descriptor + a long name.
        var page = new byte[format.PageSize + columns.Count * 512 + indexes.Count * 512];

        page[0] = PageTypeTableDefinition;
        page[TdefHeaderFlagsOffset] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(TdefMarkerOffset, 4), TdefRecordMarker);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(TdefConstantOffset, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefNextPageOffset, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefRowCountOffset, 4), 0);
        page[format.TdefTableTypeOffset] = (byte)tableType;
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(TdefMaxColumnsOffset, 2), (ushort)columns.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefVariableColumnsOffset, 2),
            (ushort)columns.Count(c => !c.IsFixedLength));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.TdefColumnCountOffset, 2), (ushort)columns.Count);
        // Logical index count (0x2F) may exceed the real data-block count (0x33): a relationship adds
        // a logical block that shares a data block. Without explicit logical specs the two are equal.
        int logicalCount = logicalIndexes?.Count ?? indexes.Count;
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefRealIndexCountOffset, 4), logicalCount);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefIndexCountOffset, 4), indexes.Count);

        // The per-index statistics blocks (12 bytes each, one per data block) precede the columns.
        int columnBlock = format.TdefRealIndexBlockOffset + indexes.Count * format.RealIndexEntrySize;
        WriteColumnDescriptors(page, format, columns, columnBlock);
        int afterNames = WriteColumnNames(page, format, columns, columnBlock + columns.Count * format.ColumnDescriptorSize);

        int definitionEnd = WriteIndexes(page, format, columns, indexes, logicalIndexes, longValueColumns, afterNames);

        // Definition length and remaining free space (Access reserves an 8-byte continuation header). For a
        // multi-page definition the caller recomputes the first page's free space, so clamp at 0 here.
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(TdefLengthOffset, 4), definitionEnd);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(TdefFreeSpaceOffset, 2),
            (ushort)Math.Max(0, format.PageSize - definitionEnd - TdefContinuationReserve));

        return new Result(page, columns);
    }

    /// <summary>Writes the index structures and returns the offset just past them (the definition end).</summary>
    private static int WriteIndexes(byte[] page, JetFormatBase format, List<ColumnDef> columns, IReadOnlyList<IndexSpec> indexes, IReadOnlyList<LogicalIndexSpec>? logicalIndexes, IReadOnlyList<LongValueColumnSpec> longValueColumns, int dataBlockStart)
    {
        var columnIdByName = columns.ToDictionary(c => c.Name, c => c.ColumnId, StringComparer.OrdinalIgnoreCase);

        // The index-data block (§3.5) has a fixed array of exactly IndexMaxColumns column slots and no
        // count field, so an index — hence any PRIMARY KEY / UNIQUE / FOREIGN KEY — spans at most that
        // many columns. Reject an over-wide index rather than silently truncating it.
        foreach (IndexSpec ix in indexes)
            if (ix.Columns.Count > IndexMaxColumns)
                throw new NotSupportedException(
                    $"Index '{ix.Name}' spans {ix.Columns.Count} columns; Jet/ACE indexes (and the keys built on them) are limited to {IndexMaxColumns}.");

        // 1. Index-data blocks: columns, root page, unique flag.
        for (int i = 0; i < indexes.Count; i++)
        {
            IndexSpec index = indexes[i];
            int block = dataBlockStart + i * IndexBlockSize;

            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(block, 4), IndexDataMarker);
            for (int slot = 0; slot < IndexMaxColumns; slot++)
            {
                int entry = block + IndexColumnsOffset + slot * IndexColumnSlotSize;
                if (slot < index.Columns.Count)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(page.AsSpan(entry, 2), (short)columnIdByName[index.Columns[slot]]);
                    page[entry + 2] = IndexColumnAscending;
                }
                else
                {
                    BinaryPrimitives.WriteInt16LittleEndian(page.AsSpan(entry, 2), IndexColumnUnused);
                }
            }
            page[block + IndexUsageMapRowOffset] = (byte)index.UsageMapRow;
            page[block + IndexUsageMapRowOffset + 1] = (byte)index.UsageMapPage;
            page[block + IndexUsageMapRowOffset + 2] = (byte)(index.UsageMapPage >> 8);
            page[block + IndexUsageMapRowOffset + 3] = (byte)(index.UsageMapPage >> 16);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexRootPageOffset, 4), index.RootPage);
            ushort flags = IndexFlagAlwaysSet;
            if (index.IsUnique) flags |= IndexFlagUnique;
            if (index.IsPrimaryKey) flags |= IndexFlagRequired;
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(block + IndexFlagsOffset, 2), flags);
        }

        // 2. Index-info blocks (one per logical index) and 3. their names. Without explicit logical
        // specs each data block maps 1:1 to a plain info block (back-compat); with them, relationship
        // blocks are included and stored name-sorted (matching Access).
        var logical = logicalIndexes ?? indexes.Select((ix, i) => new LogicalIndexSpec(
            Number: i, DataOrdinal: i, FkType: 0, FkNumber: NoForeignKey, FkTablePage: 0,
            UpdateAction: IndexActionDefault, DeleteAction: IndexActionDefault,
            Type: ix.IsPrimaryKey ? IndexTypePrimary : IndexTypeSecondary, Name: ix.Name)).ToList();

        int infoStart = dataBlockStart + indexes.Count * IndexBlockSize;
        for (int i = 0; i < logical.Count; i++)
        {
            LogicalIndexSpec li = logical[i];
            int block = infoStart + i * IndexInfoBlockSize;
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(block + IndexInfoMarkerOffset, 4), TdefRecordMarker);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexInfoNumberOffset, 4), li.Number);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexInfoDataNumberOffset, 4), li.DataOrdinal);
            page[block + IndexInfoFkTypeOffset] = li.FkType;
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(block + IndexInfoFkNumberOffset, 4), li.FkNumber);
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(block + IndexInfoFkTablePageOffset, 4), li.FkTablePage);
            page[block + IndexInfoUpdateActionOffset] = li.UpdateAction;
            page[block + IndexInfoDeleteActionOffset] = li.DeleteAction;
            page[block + IndexInfoTypeOffset] = li.Type;
        }

        int namePos = infoStart + logical.Count * IndexInfoBlockSize;
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

    private static List<ColumnDef> ResolveColumns(JetFormatBase format, IReadOnlyList<ColumnSpec> specs)
    {
        _ = format;
        // Variable columns are addressed in ascending column-id order; column id = declaration order.
        var variableRank = new Dictionary<int, int>();
        int rank = 0;
        for (int id = 0; id < specs.Count; id++)
            if (!specs[id].IsFixedLength)
                variableRank[id] = rank++;

        var columns = new List<ColumnDef>(specs.Count);
        int fixedOffset = 0;
        for (int i = 0; i < specs.Count; i++)
        {
            ColumnSpec s = specs[i];
            // Booleans live in the null bitmap and occupy no fixed-data bytes, so they don't
            // advance the fixed offset (matching how the row codec skips them).
            bool occupiesFixedData = s.IsFixedLength && s.Type != JetDataType.Boolean;
            columns.Add(new ColumnDef
            {
                Name = s.Name,
                Type = s.Type,
                Index = i,
                ColumnId = i,
                Length = s.Length,
                FixedOffset = occupiesFixedData ? fixedOffset : 0,
                VariableIndex = s.IsFixedLength ? -1 : variableRank[i],
                IsFixedLength = s.IsFixedLength,
                IsAutoNumber = s.IsAutoNumber,
                Precision = s.Precision,
                Scale = s.Scale,
            });
            if (occupiesFixedData) fixedOffset += s.Length;
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
        var d = new byte[format.ColumnDescriptorSize];
        d[format.ColumnTypeOffset] = (byte)c.Type;
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(ColumnRecordMarkerOffset, 2), (ushort)TdefRecordMarker);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(format.ColumnNumberOffset, 2), (ushort)c.ColumnId);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(ColumnNumber2Offset, 2), (ushort)c.ColumnId);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(ColumnVariableIndexOffset, 2),
            (ushort)(c.IsFixedLength ? 0 : c.VariableIndex));
        if (c.Type == JetDataType.FixedPoint)
        {
            d[format.ColumnPrecisionOffset] = c.Precision;
            d[format.ColumnScaleOffset] = c.Scale;
        }
        else
        {
            // Non-numeric columns store the en-US locale (0x0409) in the precision/scale bytes.
            d[ColumnLocaleLowOffset] = LocaleLow;
            d[ColumnLocaleHighOffset] = LocaleHigh;
        }
        d[format.ColumnFlagsOffset] = (byte)(
            ColumnFlagUpdatable
            | (c.IsFixedLength ? JetFormatBase.ColumnFlagFixedLength : 0)
            | (c.IsAutoNumber ? JetFormatBase.ColumnFlagAutoNumber : 0));
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
