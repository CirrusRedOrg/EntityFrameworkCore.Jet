using System.Buffers.Binary;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>A validated, page-backed relocated-row target. The bytes remain zero-copy for index seeks.</summary>
internal readonly record struct RelocatedRow(PageBuffer Buffer, RowSlot Slot, int RowNumber)
{
    public ReadOnlySpan<byte> Bytes => Buffer.Slice(Slot.Offset, Slot.Length);
}

/// <summary>Validates and follows the 4-byte forward pointer stored in a live overflow row slot.</summary>
internal static class RowRelocationReader
{
    public static RelocatedRow Resolve(PageChannel channel, int owningTablePage,
        RowSlot sourceSlot, ReadOnlySpan<byte> sourceBytes)
    {
        if (sourceSlot.IsDeleted || !sourceSlot.HasOverflow)
            throw new InvalidDataException("A relocation source must be a live overflow row slot.");
        if (sourceBytes.Length != 4)
            throw new InvalidDataException(
                $"A relocation source must contain exactly one 4-byte pointer; found {sourceBytes.Length} bytes.");

        int pointer = BinaryPrimitives.ReadInt32LittleEndian(sourceBytes);
        int pageNumber = pointer >> 8;
        int rowNumber = pointer & 0xFF;
        if (pageNumber <= 0 || pageNumber >= channel.PageCount)
            throw new InvalidDataException(
                $"Relocation pointer targets page {pageNumber}, outside the file's 1..{channel.PageCount - 1} range.");

        PageBuffer targetBuffer = channel.ReadPageShared(pageNumber);
        uint owner = targetBuffer.ReadUInt32(channel.Format.DataOwnerOffset);
        if (owner != (uint)owningTablePage)
            throw new InvalidDataException(
                $"Relocation target page {pageNumber} belongs to TDEF {owner}, not TDEF {owningTablePage}.");
        if (!DataPage.TryReadRow(targetBuffer, channel.Format, rowNumber, out RowSlot targetSlot, out _))
            throw new InvalidDataException(
                $"Relocation pointer targets missing row {rowNumber} on page {pageNumber}.");
        if (!targetSlot.IsDeleted || targetSlot.HasOverflow || targetSlot.Length == 0)
            throw new InvalidDataException(
                $"Relocation target {pageNumber}:{rowNumber} is not a nonempty hidden inline row.");

        return new RelocatedRow(targetBuffer, targetSlot, rowNumber);
    }
}
