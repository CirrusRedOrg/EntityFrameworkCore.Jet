using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Resolves a long value (Memo / OLE) from its 12-byte in-row descriptor to the full
/// byte payload, following LVAL pages as needed.
/// </summary>
/// <remarks>
/// Descriptor layout: bytes 0-3 = length with storage flags in the high two bits, bytes 4-7 = a
/// row+page pointer to the first LVAL chunk, bytes 8-11 the chain stamp (chained form only, checked against
/// the first chain page — see <see cref="VerifyChainStamp"/>). Flags:
/// 0x80 = inline (payload follows the descriptor); 0x40 = single LVAL page (the row is
/// the whole payload); otherwise the payload is chained across LVAL pages, each row
/// beginning with a 4-byte pointer to the next chunk.
/// </remarks>
public sealed class LongValueReader(PageChannel channel)
{
    private readonly PageChannel _channel = channel;

    public byte[] Resolve(ReadOnlySpan<byte> descriptor) => ResolveWithPages(descriptor, out _);

    internal byte[] ResolveWithPages(ReadOnlySpan<byte> descriptor, out IReadOnlyList<int> pages)
    {
        if (descriptor.Length < LongValueFormat.DescriptorSize)
            throw new InvalidDataException(
                $"Long-value descriptor has {descriptor.Length} bytes; expected at least {LongValueFormat.DescriptorSize}.");

        int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(descriptor) & LongValueFormat.LengthMask;
        byte flags = (byte)(descriptor[3] & LongValueFormat.FlagMask);
        if (flags is not (LongValueFormat.FlagInline or LongValueFormat.FlagSinglePage or LongValueFormat.FlagChained))
            throw new InvalidDataException($"Long-value descriptor has unsupported flags 0x{flags:X2}.");

        if ((flags & LongValueFormat.FlagInline) != 0)
        {
            if (length > descriptor.Length - 12)
                throw new InvalidDataException(
                    $"Inline long value declares {length} bytes but only {descriptor.Length - 12} are present.");
            pages = [];
            return descriptor.Slice(12, length).ToArray();
        }

        int row = descriptor[4];
        int page = descriptor[5] | (descriptor[6] << 8) | (descriptor[7] << 16);

        if ((flags & LongValueFormat.FlagSinglePage) != 0)
        {
            byte[] value = ReadLvalRow(page, row);
            if (value.Length != length)
                throw new InvalidDataException(
                    $"Single-page long value declares {length} bytes but row {page}:{row} has {value.Length}.");
            pages = [page];
            return value;
        }

        VerifyChainStamp(descriptor, page);
        return ReadChain(page, row, length, out pages);
    }

    /// <summary>
    /// A chained descriptor and the <b>first</b> page of its chain carry the same 4-byte stamp, minted when
    /// the chain was written. Disagreement means the page is no longer the one this descriptor was written
    /// against — the chain was rewritten, or its pages were freed and reused under another value — so the
    /// bytes behind the pointer belong to something else and must not be returned as this value.
    /// </summary>
    /// <remarks>
    /// ACE enforces this and reports it as <i>"you and another user are attempting to change the same data at
    /// the same time"</i>; the diagnosis is the point, even if the wording is about the cause rather than what
    /// was found. LibRed is single-writer, so it cannot produce the interleaving ACE guards against, but it
    /// can be handed a file another engine wrote and must not read a stale chain as though it were live.
    /// Only the entry page is checked, because every chunk after it is reached from a page already validated.
    /// </remarks>
    private void VerifyChainStamp(ReadOnlySpan<byte> descriptor, int page)
    {
        if (page <= 0 || page >= _channel.PageCount) return;   // ReadChain reports the bad pointer itself

        uint declared = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
            descriptor.Slice(LongValueFormat.ChainStampOffset, 4));
        uint stored = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
            _channel.ReadPageShared(page).Span.Slice(_channel.Format.DataChainStampOffset, 4));
        if (declared != stored)
            throw new InvalidDataException(
                $"Long-value chain at page {page} carries stamp 0x{stored:X8} but its descriptor declares "
                + $"0x{declared:X8}; the chain is not the one this row was written against.");
    }

    private byte[] ReadChain(int page, int row, int length, out IReadOnlyList<int> pages)
    {
        var result = new byte[length];
        int written = 0;
        var visited = new HashSet<(int Page, int Row)>();
        var chainPages = new List<int>();

        while (written < length)
        {
            if (page == 0)
                throw new InvalidDataException(
                    $"Long-value chain ended after {written} of {length} declared bytes.");
            if (!visited.Add((page, row)))
                throw new InvalidDataException($"Long-value chain contains a cycle at row {page}:{row}.");
            chainPages.Add(page);

            byte[] chunk = ReadLvalRow(page, row);
            if (chunk.Length < 4)
                throw new InvalidDataException(
                    $"Long-value chain row {page}:{row} has {chunk.Length} bytes; at least 4 are required for its next pointer.");

            // Each chained chunk starts with a 4-byte pointer (row + 3-byte page) to the next.
            int nextRow = chunk[0];
            int nextPage = chunk[1] | (chunk[2] << 8) | (chunk[3] << 16);

            int copy = chunk.Length - 4;
            if (copy == 0)
                throw new InvalidDataException($"Long-value chain row {page}:{row} makes no payload progress.");
            if (copy > length - written)
                throw new InvalidDataException(
                    $"Long-value chain row {page}:{row} exceeds the declared length by {copy - (length - written)} bytes.");
            Array.Copy(chunk, 4, result, written, copy);
            written += copy;

            if (written == length && nextPage != 0)
                throw new InvalidDataException(
                    $"Long-value chain continues to page {nextPage} after its declared {length} bytes.");
            page = nextPage;
            row = nextRow;
        }

        pages = chainPages;
        return result;
    }

    private byte[] ReadLvalRow(int page, int row)
    {
        if (page <= 0 || page >= _channel.PageCount)
            throw new InvalidDataException(
                $"Long-value page pointer {page} is outside the file's 1..{_channel.PageCount - 1} range.");

        var lval = new DataPage();
        lval.Read(_channel.ReadPage(page), _channel.Format);
        if (!lval.IsLongValuePage)
            throw new InvalidDataException($"Long-value pointer {page}:{row} targets a non-LVAL data page.");
        if (row < 0 || row >= lval.RowCount)
            throw new InvalidDataException(
                $"Long-value row pointer {page}:{row} is outside the page's 0..{lval.RowCount - 1} range.");
        RowSlot slot = lval.Rows[row];
        if (slot.IsDeleted || slot.HasOverflow)
            throw new InvalidDataException(
                $"Long-value pointer {page}:{row} targets a deleted or overflow row slot.");
        return lval.GetRow(row).ToArray();
    }
}