using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using System.Buffers.Binary;

namespace LibRed.Storage;

/// <summary>
/// Reads and writes a long value (Memo / OLE), owning its in-row descriptor and full
/// byte payload, following LVAL pages as needed.
/// </summary>
/// <remarks>
/// The descriptor (<see cref="Read"/>) carries the length and its <see cref="StorageKind"/>, a
/// record pointer to the first LVAL chunk, and the chain stamp (chained form only, checked against the first chain
/// page — see <see cref="VerifyChainStamp"/>). An inline payload follows the descriptor; a single-page value is the
/// whole of its row; a chained one runs across LVAL pages, each row beginning with a record pointer to the next
/// chunk.
/// </remarks>
public sealed class LongValueStore(PageChannel channel)
{
    /// <summary>
    /// A pre-built long-value (memo/OLE) in-row descriptor, written verbatim by the row encoder instead of
    /// inlining. Produced by <see cref="LongValueStore"/> when a value is stored on an LVAL page.
    /// </summary>
    internal sealed record DescriptorValue(byte[] Bytes);

    private readonly PageChannel _channel = channel;
    private readonly PageAllocator _allocator = channel.Allocator;

    public byte[] Resolve(ReadOnlySpan<byte> descriptor) => ResolveWithPages(descriptor, out _);

    internal byte[] ResolveWithPages(ReadOnlySpan<byte> descriptor, out IReadOnlyList<int> pages)
    {
        JetFormatBase format = _channel.Format;
        (int length, StorageKind storage, int row, int page, uint stamp) = Read(descriptor, format);

        if (storage == StorageKind.Inline)
        {
            int size = format.LongValueDescriptorSize;
            if (length > descriptor.Length - size)
                throw new InvalidDataException(
                    $"Inline long value declares {length} bytes but only {descriptor.Length - size} are present.");
            pages = [];
            return descriptor.Slice(size, length).ToArray();
        }

        if (storage == StorageKind.SinglePage)
        {
            byte[] value = ReadLvalRow(page, row);
            if (value.Length != length)
                throw new InvalidDataException(
                    $"Single-page long value declares {length} bytes but row {page}:{row} has {value.Length}.");
            pages = [page];
            return value;
        }

        VerifyChainStamp(stamp, page);
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
    private void VerifyChainStamp(uint declared, int page)
    {
        if (page <= 0 || page >= _channel.PageCount) return;   // ReadChain reports the bad pointer itself

        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(
            _channel.ReadPageShared(page).Span.Slice(_channel.Format.DataChainStampOffset, sizeof(uint)));
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
            if (chunk.Length < PageBuffer.RecordPointerSize)
                throw new InvalidDataException(
                    $"Long-value chain row {page}:{row} has {chunk.Length} bytes; at least "
                    + $"{PageBuffer.RecordPointerSize} are required for its next pointer.");

            // Each chained chunk starts with a record pointer to the next.
            (int nextRow, int nextPage) = PageBuffer.ReadRecordPointer(chunk, 0);

            int copy = chunk.Length - PageBuffer.RecordPointerSize;
            if (copy == 0)
                throw new InvalidDataException($"Long-value chain row {page}:{row} makes no payload progress.");
            if (copy > length - written)
                throw new InvalidDataException(
                    $"Long-value chain row {page}:{row} exceeds the declared length by {copy - (length - written)} bytes.");
            Array.Copy(chunk, PageBuffer.RecordPointerSize, result, written, copy);
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
        DataPage.RowSlot slot = lval.Rows[row];
        if (slot.IsDeleted || slot.HasOverflow)
            throw new InvalidDataException(
                $"Long-value pointer {page}:{row} targets a deleted or overflow row slot.");
        return lval.GetRow(row).ToArray();
    }

    /// <summary>Writes <paramref name="payload"/> across one or more LVAL pages, returning its descriptor
    /// and the pages used (all owned; the last is also free, having spare room).</summary>
    internal (byte[] Descriptor, IReadOnlyList<int> OwnedPages, int FreePage) Write(byte[] payload)
    {
        JetFormatBase format = _channel.Format;
        ValidateLength(payload.Length, format);
        if (payload.Length <= format.LongValueMaxSinglePage)
        {
            int page = _allocator.Allocate();
            WriteChunkPage(page, payload); // a single-page row is the payload itself (no next pointer)
            return (
                Descriptor(format, payload.Length, StorageKind.SinglePage, page), [page], page);
        }

        // Chained: split into chunks that each fit a row after the record pointer to the next.
        int maxChunkData = format.LongValueMaxRowSize - PageBuffer.RecordPointerSize;
        int chunkCount = (payload.Length + maxChunkData - 1) / maxChunkData;
        var pages = new int[chunkCount];
        for (int i = 0; i < chunkCount; i++) pages[i] = _allocator.Allocate();

        // The chain stamp goes in two places and must match in both: here on the first chunk page and in the
        // descriptor below. It is a version tag on the CHAIN, minted per write of it, so a reader arriving
        // through a descriptor can tell that the pages it is about to follow are the ones that descriptor was
        // written against and not a later value's. Only the entry page carries it — every chunk after that is
        // reached from a page already validated. ACE uses GetTickCount(); the value is arbitrary and only the
        // agreement is checked, so matching its choice keeps our pages the shape Access produces.
        uint stamp = (uint)Environment.TickCount;

        for (int i = 0; i < chunkCount; i++)
        {
            int start = i * maxChunkData;
            int len = Math.Min(maxChunkData, payload.Length - start);
            int nextPage = i + 1 < chunkCount ? pages[i + 1] : 0;

            var row = new byte[PageBuffer.RecordPointerSize + len];
            PageBuffer.WriteRecordPointer(row, 0, row: 0, nextPage); // always row 0 — one chunk per page
            payload.AsSpan(start, len).CopyTo(row.AsSpan(PageBuffer.RecordPointerSize));
            WriteChunkPage(pages[i], row, i == 0 ? stamp : 0);
        }

        return (
            Descriptor(format, payload.Length, StorageKind.Chained, pages[0], stamp: stamp),
            pages, FreePage: 0);
    }

    /// <summary>Allocates a fresh LVAL page, writes <paramref name="row"/> as its row 0, and returns the
    /// page number — the caller records it in the column's usage maps. <paramref name="uncompressed"/> is as
    /// for <see cref="TryAppend"/>.</summary>
    internal int WriteNewPage(byte[] row, byte[]? uncompressed = null)
    {
        int page = _allocator.Allocate();
        WriteChunkPage(page, row, uncompressed: uncompressed);
        return page;
    }

    /// <summary>Appends <paramref name="row"/> to an existing LVAL page if it has room, returning the new
    /// row index and the page's remaining free space (null if it does not fit). Lets several small long
    /// values share one page, the way Access packs them.</summary>
    /// <remarks>
    /// <paramref name="uncompressed"/> is the value before compression, when <paramref name="row"/> is its
    /// compressed form. ACE places such a value as though it were uncompressed — the page must have room for the
    /// uncompressed bytes — writes those bytes where they would go, then the compressed row over their upper end,
    /// so the rest of the uncompressed image stays behind in the page's free space. Both are measured
    /// (long-values.md).
    /// </remarks>
    internal (int Row, int RemainingFree)? TryAppend(int pageNumber, byte[] row, byte[]? uncompressed = null)
    {
        JetFormatBase format = _channel.Format;
        if (pageNumber <= 0 || pageNumber >= _channel.PageCount)
            throw new InvalidDataException($"LVAL append page {pageNumber} is outside the physical file.");
        if (row.Length > format.LongValueMaxRowSize)
            throw new ArgumentOutOfRangeException(nameof(row),
                $"An LVAL row cannot exceed {format.LongValueMaxRowSize} bytes.");

        PageBuffer buffer = _channel.ReadPage(pageNumber);
        var parsed = new DataPage();
        parsed.Read(buffer, format);
        if (!parsed.IsLongValuePage)
            throw new InvalidDataException($"LVAL append target {pageNumber} is not owned by the LVAL store.");

        int rowCount = parsed.RowCount;
        // A long-value descriptor addresses its row with a ONE-BYTE field (the row byte of its record pointer),
        // exactly as an index entry addresses a data row — so the same 256-slot ceiling applies, and passing it
        // would alias one value's descriptor onto another's row with no error. Today it is unreachable: a
        // payload of 64 bytes or less inlines and never arrives here, and a page leaves the free-pages map once
        // it has no room left for a MinLvalValue-byte value and its slot (RowInserter), which caps a page at 104 rows
        // even for the smallest thing
        // that can reach it (a 33-character memo compressed to 35 bytes — compression is applied AFTER the
        // inline test, so the floor is lower than the 65-byte inline limit suggests). That margin is emergent,
        // not stated: it moves if the inline limit or the free-map threshold changes. Refusing the page here
        // costs nothing and makes the ceiling structural — the caller allocates a fresh page, as it does when
        // the page is out of room.
        if (rowCount >= format.MaxRowsPerPage) return null;

        int slotSize = format.DataRowDirectoryEntrySize;
        byte[] page = buffer.Span.ToArray();
        int lowest = DataPage.LowestRowOffset(page, format);
        int physicalFree = lowest - DataPage.DirectoryEnd(format, rowCount);
        if (parsed.FreeSpace != physicalFree)
            throw new InvalidDataException(
                $"LVAL page {pageNumber} declares {parsed.FreeSpace} free bytes but its row geometry has {physicalFree}.");
        // Row data + its directory entry; a compressed value needs room for its uncompressed bytes.
        if (physicalFree < Math.Max(row.Length, uncompressed?.Length ?? 0) + slotSize) return null;

        uncompressed?.CopyTo(page.AsSpan(lowest - uncompressed.Length));
        DataPage.TryAppendRow(page, format, row, RowSlotFlags.None, out int newRow); // room proven above
        _channel.WritePage(pageNumber, page);
        return (newRow, DataPage.ReadFreeSpace(page, format));
    }

    /// <summary>Writes one row (<paramref name="row"/>) to a fresh LVAL data page, packed from the page end.
    /// <paramref name="stamp"/> is the chain stamp for the first page of a chain, and zero everywhere else —
    /// which is what ACE writes on a single-page value and on every chunk after the first.
    /// <paramref name="uncompressed"/> is as for <see cref="TryAppend"/>.</summary>
    private void WriteChunkPage(int pageNumber, byte[] row, uint stamp = 0, byte[]? uncompressed = null)
    {
        JetFormatBase format = _channel.Format;
        byte[] page = DataPage.NewPage(format, JetFormatBase.LongValuePageMarker);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(format.DataChainStampOffset, 4), stamp);

        uncompressed?.CopyTo(page.AsSpan(format.PageSize - uncompressed.Length));
        DataPage.LayRows(page, format, [row], [RowSlotFlags.None]); // one row on an empty page always fits
        _channel.WritePage(pageNumber, page);
    }


    internal static void ValidateLength(int length, JetFormatBase format)
    {
        if ((uint)length > (uint)format.LongValueLengthMask)
            throw new ArgumentOutOfRangeException(nameof(length), "The long-value length would overwrite its storage flags.");
    }

    /// <summary>A descriptor: the length with its <paramref name="storage"/> bits, the record pointer to the value's
    /// row or first chunk, and the chain stamp. The pointer is zero on an inline value, and the stamp is non-zero
    /// only on a chained one, where it repeats the first chain page's own.</summary>
    internal static byte[] Descriptor(JetFormatBase format, int length, StorageKind storage,
        int page = 0, int row = 0, uint stamp = 0)
    {
        ValidateLength(length, format);
        var d = new byte[format.LongValueDescriptorSize];
        BinaryPrimitives.WriteUInt32LittleEndian(d, (uint)length | (uint)storage);
        PageBuffer.WriteRecordPointer(d, format.LongValueDescriptorPointerOffset, row, page);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(format.LongValueDescriptorChainStampOffset, sizeof(uint)), stamp);
        return d;
    }

    /// <summary>A descriptor's length, storage, record pointer and chain stamp, after proving it has all its bytes
    /// and a storage form that exists. Byte 3 carries the length's top byte AND the storage bits — the length runs
    /// to <see cref="JetFormatBase.LongValueLengthMask"/>, and a 16 MB value puts 0x01 there — so the two are
    /// masked apart.</summary>
    internal static (int Length, StorageKind Storage, int Row, int Page, uint Stamp) Read(
        ReadOnlySpan<byte> descriptor, JetFormatBase format)
    {
        if (descriptor.Length < format.LongValueDescriptorSize)
            throw new InvalidDataException(
                $"Long-value descriptor has {descriptor.Length} bytes; expected at least {format.LongValueDescriptorSize}.");

        uint head = BinaryPrimitives.ReadUInt32LittleEndian(descriptor);
        var storage = (StorageKind)(head & ~(uint)format.LongValueLengthMask);
        if (storage is not (StorageKind.Inline or StorageKind.SinglePage or StorageKind.Chained))
            throw new InvalidDataException($"Long-value descriptor has unsupported flags 0x{(uint)storage >> 24:X2}.");
        (int row, int page) = PageBuffer.ReadRecordPointer(descriptor, format.LongValueDescriptorPointerOffset);
        uint stamp = BinaryPrimitives.ReadUInt32LittleEndian(
            descriptor.Slice(format.LongValueDescriptorChainStampOffset, sizeof(uint)));
        return ((int)(head & (uint)format.LongValueLengthMask), storage, row, page, stamp);
    }

    /// <summary>How a long value is stored: the bits of its descriptor's first 4 bytes above
    /// <see cref="JetFormatBase.LongValueLengthMask"/>.</summary>
    internal enum StorageKind : uint
    {
        /// <summary>Across several LVAL pages, one chunk per page, each beginning with a record pointer to the
        /// next.</summary>
        Chained = 0x00000000,

        /// <summary>A single LVAL page row: the row is the whole payload.</summary>
        SinglePage = 0x40000000,

        /// <summary>In the row, after the descriptor: no LVAL page.</summary>
        Inline = 0x80000000,
    }

}