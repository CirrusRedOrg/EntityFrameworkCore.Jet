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
/// Writes a long value onto its own LVAL page and returns the 12-byte in-row reference descriptor.
/// Access's property loader (and general long-value reads) require this page form; an inline value
/// (see <see cref="Types.JetTypeCodec"/>) is not recognised for object properties (<c>LvProp</c>).
/// </summary>
/// <remarks>
/// An LVAL page is a data page (type <c>0x01</c>) whose owner field is the ASCII marker "LVAL"; the
/// payload is stored as row 0. The descriptor is <c>[length:3][flags:1][row:1][page:3][4 reserved]</c>
/// with flag <c>0x40</c> = single page (the whole payload is that one row). Payloads larger than a page
/// need a chained (<c>0x00</c>) descriptor, which is not written yet.
/// </remarks>
public sealed class LongValueWriter(PageChannel channel)
{
    private const byte FlagSinglePage = 0x40;
    private const uint LongValueMarker = 0x4C41564C; // "LVAL" (little-endian bytes 4C 56 41 4C)

    private readonly PageChannel _channel = channel;
    private readonly PageAllocator _allocator = new(channel);

    /// <summary>Writes <paramref name="payload"/> to a fresh single LVAL page and returns its descriptor.</summary>
    public byte[] WriteSinglePage(byte[] payload)
    {
        JetFormatBase format = _channel.Format;
        if (payload.Length > format.PageSize - format.DataRowDirectoryOffset - 2)
            throw new NotSupportedException(
                $"Long value of {payload.Length} bytes exceeds a single LVAL page; chained pages are not written yet.");

        int pageNumber = _allocator.Allocate();

        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        page[1] = 0x01; // page flags (observed constant)
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(format.DataOwnerOffset, 4), LongValueMarker);

        int offset = format.PageSize - payload.Length; // one row packed from the page end
        payload.CopyTo(page.AsSpan(offset));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset, 2), (ushort)offset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(offset - format.DataRowDirectoryOffset - 2));
        _channel.WritePage(pageNumber, page);

        var descriptor = new byte[12];
        descriptor[0] = (byte)payload.Length;
        descriptor[1] = (byte)(payload.Length >> 8);
        descriptor[2] = (byte)(payload.Length >> 16);
        descriptor[3] = FlagSinglePage;
        descriptor[4] = 0; // row 0
        descriptor[5] = (byte)pageNumber;
        descriptor[6] = (byte)(pageNumber >> 8);
        descriptor[7] = (byte)(pageNumber >> 16);
        return descriptor;
    }
}
