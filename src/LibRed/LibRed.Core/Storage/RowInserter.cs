using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Inserts a row into an existing data page of a table. This first cut only fills free space on
/// the table's already-owned pages — it does not yet allocate a new page or update indexes — so
/// it is valid for tables whose last data page has room. The page's slot directory grows forward
/// while row data is packed from the page end backward (see <see cref="DataPage"/>).
/// </summary>
public sealed class RowInserter(PageChannel channel, TableDef table)
{
    private const int RowOffsetMask = 0x1FFF;
    private const int DeletedFlag = 0x8000;
    private const int OverflowFlag = 0x4000;

    private readonly PageChannel _channel = channel;
    private readonly TableDef _table = table;

    /// <summary>Encodes and writes <paramref name="values"/> (aligned to column Index) into the table.</summary>
    public void Insert(object?[] values)
    {
        JetFormatBase format = _channel.Format;

        // Find a page with room and a reference row to mirror the on-disk fixed-region length.
        (int pageNumber, byte[] page, int fixedDataLength) = FindWritablePage(format);

        var encoder = new RowEncoder(_table.Columns, format, fixedDataLength);
        byte[] record = encoder.Encode(values);

        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2));
        int freeSpace = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2));

        if (freeSpace < record.Length + 2)
            throw new InvalidOperationException("Chosen page lacks room — page allocation is not implemented yet.");

        // Rows are packed from the page end backward, with strictly decreasing slot offsets,
        // so the new row goes just below the current lowest row start.
        int lowestOffset = LowestRowOffset(page, format, rowCount);
        int newOffset = lowestOffset - record.Length;
        record.CopyTo(page.AsSpan(newOffset));

        // Append the slot, bump the row count, shrink free space by row + slot-entry bytes.
        // The new row's slot index is the old row count, giving its row id on this page.
        BinaryPrimitives.WriteUInt16LittleEndian(
            page.AsSpan(format.DataRowDirectoryOffset + rowCount * 2, 2), (ushort)(newOffset & RowOffsetMask));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)(rowCount + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2), (ushort)(freeSpace - record.Length - 2));

        _channel.WritePage(pageNumber, page);

        BumpTableRowCount(format);
        UpdateIndexes(values, new RowId(pageNumber, rowCount));
    }

    /// <summary>Adds the new row to every index B-tree (deduped by root page, since relationship
    /// indexes share a real index's data) so indexed lookups — and Access — find it.</summary>
    private void UpdateIndexes(object?[] values, RowId rowId)
    {
        var writer = new IndexWriter(_channel);
        foreach (IndexDef index in _table.Indexes
            .Where(i => i.RootPage > 0)
            .GroupBy(i => i.RootPage)
            .Select(g => g.First()))
        {
            writer.AddEntry(index, values, rowId);
        }
    }

    private (int PageNumber, byte[] Page, int FixedDataLength) FindWritablePage(JetFormatBase format)
    {
        foreach (int pageNumber in new UsageMap(_channel, _table).DataPages())
        {
            byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
            int freeSpace = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2));
            int? fixedLength = InferFixedDataLength(page, format);

            // Need a reference row (to mirror the fixed-region length) and enough free space.
            if (fixedLength is { } len && freeSpace > 2)
                return (pageNumber, page, len);
        }

        throw new InvalidOperationException(
            "No existing data page with a reference row was found; empty-table / new-page insert is not implemented yet.");
    }

    private static int LowestRowOffset(byte[] page, JetFormatBase format, int rowCount)
    {
        int lowest = format.PageSize;
        for (int i = 0; i < rowCount; i++)
        {
            int raw = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + i * 2, 2));
            lowest = Math.Min(lowest, raw & RowOffsetMask);
        }
        return lowest;
    }

    /// <summary>
    /// Reads the fixed-region length straight off an existing inline row: its variable-offset
    /// table's last entry is the variable-data start (= columnCount field + fixed region), so the
    /// fixed length is that minus the leading column-count field.
    /// </summary>
    private int? InferFixedDataLength(byte[] page, JetFormatBase format)
    {
        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2));
        int columnCount = _table.Columns.Count;
        int nullBitmapSize = (columnCount + 7) / 8;

        int prevEnd = format.PageSize;
        for (int i = 0; i < rowCount; i++)
        {
            int raw = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + i * 2, 2));
            int offset = raw & RowOffsetMask;
            int length = prevEnd - offset;
            prevEnd = offset;

            if ((raw & (DeletedFlag | OverflowFlag)) != 0) continue;
            if (length < format.RowColumnCountSize + 2 + 2 + nullBitmapSize) continue; // not an inline record

            ReadOnlySpan<byte> row = page.AsSpan(offset, length);
            int numVar = BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(row.Length - nullBitmapSize - 2, 2));
            int varTableStart = row.Length - nullBitmapSize - 2 - (numVar + 1) * 2;
            int varDataStart = BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(varTableStart + numVar * 2, 2));
            return varDataStart - format.RowColumnCountSize;
        }

        return null;
    }

    private void BumpTableRowCount(JetFormatBase format)
    {
        byte[] tdef = _channel.ReadPage(_table.DefinitionPage).Span.ToArray();
        int count = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(format.TdefRowCountOffset, 4));
        BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(format.TdefRowCountOffset, 4), count + 1);
        _channel.WritePage(_table.DefinitionPage, tdef);
    }
}
