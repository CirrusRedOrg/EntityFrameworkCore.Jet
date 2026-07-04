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
    private const byte FlagSinglePage = 0x40;
    private const byte FlagChained = 0x00;
    private const uint LongValueMarker = 0x4C41564C; // "LVAL" (little-endian bytes 4C 56 41 4C)

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
        if (payload.Length <= MaxLvalRowSize)
        {
            int page = _allocator.Allocate();
            WriteChunkPage(page, payload); // a single-page row is the payload itself (no next pointer)
            return new LongValueResult(Descriptor(payload.Length, FlagSinglePage, page), [page], page);
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

        return new LongValueResult(Descriptor(payload.Length, FlagChained, pages[0]), pages, pages[^1]);
    }

    /// <summary>Writes <paramref name="payload"/> to a fresh single LVAL page and returns its descriptor
    /// (single-page only — throws if it does not fit). Used for small property blobs.</summary>
    public byte[] WriteSinglePage(byte[] payload)
    {
        if (payload.Length > MaxLvalRowSize)
            throw new NotSupportedException(
                $"Long value of {payload.Length} bytes exceeds a single LVAL page; use Write for chaining.");
        int page = _allocator.Allocate();
        WriteChunkPage(page, payload);
        return Descriptor(payload.Length, FlagSinglePage, page);
    }

    /// <summary>Writes one row (<paramref name="row"/>) to a fresh LVAL data page, packed from the page end.</summary>
    private void WriteChunkPage(int pageNumber, byte[] row)
    {
        JetFormatBase format = _channel.Format;
        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        page[1] = 0x01; // page flags (observed constant)
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(format.DataOwnerOffset, 4), LongValueMarker);

        int offset = format.PageSize - row.Length;
        row.CopyTo(page.AsSpan(offset));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset, 2), (ushort)offset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(offset - format.DataRowDirectoryOffset - 2));
        _channel.WritePage(pageNumber, page);
    }

    /// <summary>Builds the 12-byte in-row descriptor: <c>[length:3][flag:1][row:1=0][page:3][4 reserved]</c>.</summary>
    private static byte[] Descriptor(int length, byte flag, int firstPage)
    {
        var d = new byte[12];
        d[0] = (byte)length;
        d[1] = (byte)(length >> 8);
        d[2] = (byte)(length >> 16);
        d[3] = flag;
        d[4] = 0; // first chunk is row 0
        d[5] = (byte)firstPage;
        d[6] = (byte)(firstPage >> 8);
        d[7] = (byte)(firstPage >> 16);
        return d;
    }
}
