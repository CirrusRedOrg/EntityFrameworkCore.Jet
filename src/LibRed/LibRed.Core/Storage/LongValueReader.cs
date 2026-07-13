using System.Buffers.Binary;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Resolves a long value (Memo / OLE) from its 12-byte in-row descriptor to the full
/// byte payload, following LVAL pages as needed.
/// </summary>
/// <remarks>
/// Descriptor layout: bytes 0-2 = length (24-bit), byte 3 = flags, bytes 4-7 = a
/// row+page pointer to the first LVAL chunk, bytes 8-11 reserved. Flags:
/// 0x80 = inline (payload follows the descriptor); 0x40 = single LVAL page (the row is
/// the whole payload); otherwise the payload is chained across LVAL pages, each row
/// beginning with a 4-byte pointer to the next chunk.
/// </remarks>
public sealed class LongValueReader(PageChannel channel)
{
    private readonly PageChannel _channel = channel;

    public byte[] Resolve(ReadOnlySpan<byte> descriptor)
    {
        int length = descriptor[0] | (descriptor[1] << 8) | (descriptor[2] << 16);
        byte flags = descriptor[3];

        if ((flags & LongValueFormat.FlagInline) != 0)
            return descriptor.Slice(12, length).ToArray();

        int row = descriptor[4];
        int page = descriptor[5] | (descriptor[6] << 8) | (descriptor[7] << 16);

        return (flags & LongValueFormat.FlagSinglePage) != 0
            ? ReadLvalRow(page, row)[..length]
            : ReadChain(page, row, length);
    }

    private byte[] ReadChain(int page, int row, int length)
    {
        var result = new byte[length];
        int written = 0;

        while (page != 0 && written < length)
        {
            byte[] chunk = ReadLvalRow(page, row);

            // Each chained chunk starts with a 4-byte pointer (row + 3-byte page) to the next.
            int nextRow = chunk[0];
            int nextPage = chunk[1] | (chunk[2] << 8) | (chunk[3] << 16);

            int copy = Math.Min(chunk.Length - 4, length - written);
            Array.Copy(chunk, 4, result, written, copy);
            written += copy;

            page = nextPage;
            row = nextRow;
        }

        return result;
    }

    private byte[] ReadLvalRow(int page, int row)
    {
        var lval = new DataPage();
        lval.Read(_channel.ReadPage(page), _channel.Format);
        return lval.GetRow(row).ToArray();
    }
}
