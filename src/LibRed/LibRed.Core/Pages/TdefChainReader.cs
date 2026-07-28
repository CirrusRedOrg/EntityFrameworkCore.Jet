using LibRed.Formats;
using LibRed.IO;

namespace LibRed.Pages;

/// <summary>Reads one TDEF chain into its absolute logical coordinate space.</summary>
internal static class TdefChainReader
{
    // A valid Jet/ACE table is constrained to 255 columns, 32 real indexes, and 64-character names.
    // One MiB is deliberately generous while still preventing a hostile 32-bit length from driving
    // process-scale allocation. This is an implementation safety budget, not an on-disk field width.
    internal const int MaxDefinitionLength = 1024 * 1024;

    internal static (PageBuffer Buffer, IReadOnlyList<int> ContinuationPages) Read(
        PageChannel channel, int firstPage)
    {
        JetFormatBase format = channel.Format;
        ValidatePageNumber(channel, firstPage, "TDEF root");
        PageBuffer first = channel.ReadPage(firstPage);
        if (first.ReadByte(0) != (byte)PageType.TableDefinition || first.ReadByte(1) != 0x01)
            throw new InvalidDataException(
                $"TDEF root page {firstPage} has header " +
                $"[{first.ReadByte(0):X2} {first.ReadByte(1):X2}], expected [02 01].");

        int definitionLength = first.ReadInt32(format.TdefLengthOffset);
        if (definitionLength < format.TdefRealIndexBlockOffset || definitionLength > MaxDefinitionLength)
            throw new InvalidDataException(
                $"TDEF page {firstPage} declares length {definitionLength}; supported validated range is " +
                $"{format.TdefRealIndexBlockOffset} through {MaxDefinitionLength} bytes.");

        int pageSize = format.PageSize;
        int bodySize = pageSize - JetFormatBase.TdefContinuationHeaderSize;
        int continuationCount = definitionLength <= pageSize
            ? 0
            : (definitionLength - pageSize + bodySize - 1) / bodySize;

        int next = first.ReadInt32(format.TdefNextPageOffset);
        var continuationPages = new List<int>(continuationCount);
        var continuationBuffers = new List<PageBuffer>(continuationCount);
        var visited = new HashSet<int> { firstPage };

        for (int i = 0; i < continuationCount; i++)
        {
            if (next == 0)
                throw new InvalidDataException(
                    $"TDEF page {firstPage} ends after {i} continuation pages but its declared length requires {continuationCount}.");
            ValidatePageNumber(channel, next, "TDEF continuation");
            if (!visited.Add(next))
                throw new InvalidDataException($"TDEF page {firstPage} contains a continuation cycle at page {next}.");

            PageBuffer continuation = channel.ReadPage(next);
            if (continuation.ReadByte(0) != (byte)PageType.TableDefinition || continuation.ReadByte(1) != 0x01)
                throw new InvalidDataException(
                    $"TDEF continuation page {next} has header " +
                    $"[{continuation.ReadByte(0):X2} {continuation.ReadByte(1):X2}], expected [02 01].");

            continuationPages.Add(next);
            continuationBuffers.Add(continuation);
            next = continuation.ReadInt32(format.TdefNextPageOffset);
        }

        if (next != 0)
            throw new InvalidDataException(
                $"TDEF page {firstPage} has more continuation pages than its declared length permits.");

        var assembled = new byte[definitionLength];
        int written = Math.Min(pageSize, definitionLength);
        first.Span[..written].CopyTo(assembled);
        foreach (PageBuffer continuation in continuationBuffers)
        {
            int take = Math.Min(bodySize, definitionLength - written);
            continuation.Span.Slice(JetFormatBase.TdefContinuationHeaderSize, take)
                .CopyTo(assembled.AsSpan(written));
            written += take;
        }

        return (new PageBuffer(assembled, firstPage), continuationPages);
    }

    private static void ValidatePageNumber(PageChannel channel, int pageNumber, string role)
    {
        if (pageNumber <= 0 || pageNumber >= channel.PageCount)
            throw new InvalidDataException(
                $"{role} page {pageNumber} is outside the database's {channel.PageCount} pages.");
    }
}
