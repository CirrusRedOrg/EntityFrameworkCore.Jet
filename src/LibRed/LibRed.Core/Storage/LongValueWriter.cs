using System.Buffers.Binary;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// A pre-built long-value (memo/OLE) in-row descriptor, written verbatim by the row encoder instead of
/// inlining. Produced by <see cref="LongValueWriter"/> when a value is stored on an LVAL page.
/// </summary>
public sealed record LongValueDescriptor(byte[] Bytes);

/// <summary>
/// The result of writing a long value to LVAL page(s): the 12-byte in-row descriptor, the pages it now
/// occupies (to record in the column's owned-pages map), and the one page that still has spare room (the
/// last, partially-filled chunk — recorded in the free-pages map).
/// </summary>
public sealed record LongValueResult(byte[] Descriptor, IReadOnlyList<int> OwnedPages, int FreePage);

/// <summary>
/// Writes a long value (memo/OLE) to LVAL page(s) and returns its 12-byte in-row reference descriptor.
/// A value up to one page is a <b>single page</b> (flag <c>0x40</c>): the row <i>is</i> the payload. A
/// larger value is <b>chained</b> (flag <c>0x00</c>) across several pages: each page holds one chunk row
/// that begins with a 4-byte <c>[row:1][page:3]</c> pointer to the next chunk (zero on the last).
/// </summary>
/// <remarks>
/// An LVAL page is a data page (type <c>0x01</c>) whose owner field is the ASCII marker "LVAL"; a chunk is
/// stored as its row 0. Access's property loader (and general long-value reads) require this page form —
/// an inline value is not recognised for object properties (<c>LvProp</c>).
/// </remarks>
public sealed class LongValueWriter(PageChannel channel)
{

    // Access caps an LVAL page row at MAX_LONG_VALUE_ROW_SIZE (Jackcess) — 4076 on Jet4 (Jet3 = 2032),
    // 4 bytes short of the page's usable space. A single-page value up to this fits in one row; a chained
    // chunk row is this size (a 4-byte next-pointer + up to 4072 data bytes). Verified against ACE's own
    // chained OLE values (Northwind Employee photos: 4076, 4076, 2606-byte chunk rows).
    private const int MaxLvalRowSize = 4076;
    private const int MaxChunkData = MaxLvalRowSize - 4;

    private readonly PageChannel _channel = channel;
    private readonly PageAllocator _allocator = new(channel);

    /// <summary>Writes <paramref name="payload"/> across one or more LVAL pages, returning its descriptor
    /// and the pages used (all owned; the last is also free, having spare room).</summary>
    public LongValueResult Write(byte[] payload)
    {
        LongValueFormat.ValidateLength(payload.Length);
        if (payload.Length <= LongValueFormat.MaxSinglePageValue)
        {
            int page = _allocator.Allocate();
            WriteChunkPage(page, payload); // a single-page row is the payload itself (no next pointer)
            return new LongValueResult(Descriptor(payload.Length, LongValueFormat.FlagSinglePage, page), [page], page);
        }

        // Chained: split into chunks that each fit on a page after a 4-byte next-pointer.
        int chunkCount = (payload.Length + MaxChunkData - 1) / MaxChunkData;
        var pages = new int[chunkCount];
        for (int i = 0; i < chunkCount; i++) pages[i] = _allocator.Allocate();

        for (int i = 0; i < chunkCount; i++)
        {
            int start = i * MaxChunkData;
            int len = Math.Min(MaxChunkData, payload.Length - start);
            int nextPage = i + 1 < chunkCount ? pages[i + 1] : 0;

            var row = new byte[4 + len];
            row[0] = 0;                       // next row (always row 0 — one chunk per page)
            row[1] = (byte)nextPage;
            row[2] = (byte)(nextPage >> 8);
            row[3] = (byte)(nextPage >> 16);
            payload.AsSpan(start, len).CopyTo(row.AsSpan(4));
            WriteChunkPage(pages[i], row);
        }

        return new LongValueResult(Descriptor(payload.Length, LongValueFormat.FlagChained, pages[0]), pages, pages[^1]);
    }

    /// <summary>Allocates a fresh LVAL page, writes <paramref name="row"/> as its row 0, and returns the
    /// page number — the caller records it in the column's usage maps.</summary>
    public int WriteNewPage(byte[] row)
    {
        int page = _allocator.Allocate();
        WriteChunkPage(page, row);
        return page;
    }

    /// <summary>Appends <paramref name="row"/> to an existing LVAL page if it has room, returning the new
    /// row index and the page's remaining free space (null if it does not fit). Lets several small long
    /// values share one page, the way Access packs them.</summary>
    public (int Row, int RemainingFree)? TryAppend(int pageNumber, byte[] row)
    {
        JetFormatBase format = _channel.Format;
        if (pageNumber <= 0 || pageNumber >= _channel.PageCount)
            throw new InvalidDataException($"LVAL append page {pageNumber} is outside the physical file.");
        if (row.Length > MaxLvalRowSize)
            throw new ArgumentOutOfRangeException(nameof(row),
                $"An LVAL row cannot exceed {MaxLvalRowSize} bytes.");

        PageBuffer buffer = _channel.ReadPage(pageNumber);
        var parsed = new DataPage();
        parsed.Read(buffer, format);
        if (!parsed.IsLongValuePage)
            throw new InvalidDataException($"LVAL append target {pageNumber} is not owned by the LVAL store.");

        int rowCount = parsed.RowCount;
        int lowest = parsed.Rows.Count == 0 ? format.PageSize : parsed.Rows.Min(r => r.Offset);
        int directoryEnd = format.DataRowDirectoryOffset + rowCount * 2;
        int physicalFree = lowest - directoryEnd;
        if (parsed.FreeSpace != physicalFree)
            throw new InvalidDataException(
                $"LVAL page {pageNumber} declares {parsed.FreeSpace} free bytes but its row geometry has {physicalFree}.");
        if (physicalFree < row.Length + 2) return null; // row data + its 2-byte directory entry

        byte[] page = buffer.Span.ToArray();

        int offset = lowest - row.Length;
        row.CopyTo(page.AsSpan(offset));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + rowCount * 2, 2), (ushort)offset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)(rowCount + 1));
        int remaining = physicalFree - row.Length - 2;
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2), (ushort)remaining);
        _channel.WritePage(pageNumber, page);
        return (rowCount, remaining);
    }

    /// <summary>The single-page (<c>0x40</c>) descriptor for a value stored at (<paramref name="page"/>,
    /// <paramref name="row"/>) — used when a value is packed onto an existing page at a non-zero row.</summary>
    public static byte[] SinglePageDescriptor(int length, int page, int row) =>
        Descriptor(length, LongValueFormat.FlagSinglePage, page, row);

    /// <summary>Writes one row (<paramref name="row"/>) to a fresh LVAL data page, packed from the page end.</summary>
    private void WriteChunkPage(int pageNumber, byte[] row)
    {
        JetFormatBase format = _channel.Format;
        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        page[1] = 0x01; // page flags (observed constant)
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(format.DataOwnerOffset, 4), LongValueFormat.LvalMarker);

        int offset = format.PageSize - row.Length;
        row.CopyTo(page.AsSpan(offset));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset, 2), (ushort)offset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(offset - format.DataRowDirectoryOffset - 2));
        _channel.WritePage(pageNumber, page);
    }

    /// <summary>Builds the 12-byte descriptor: 4-byte length with storage flags, row/page, reserved.</summary>
    private static byte[] Descriptor(int length, byte flag, int firstPage, int row = 0)
    {
        LongValueFormat.ValidateLength(length);
        var d = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(d, (uint)length | ((uint)flag << 24));
        d[4] = (byte)row;
        d[5] = (byte)firstPage;
        d[6] = (byte)(firstPage >> 8);
        d[7] = (byte)(firstPage >> 16);
        return d;
    }
}
