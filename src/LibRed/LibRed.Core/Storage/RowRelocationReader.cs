using System.Buffers.Binary;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>A validated, page-backed relocated-row target. The bytes remain zero-copy for index seeks.</summary>
internal readonly record struct RelocatedRow(PageBuffer Buffer, RowSlot Slot, int RowNumber)
{
    public ReadOnlySpan<byte> Bytes => Buffer.Slice(Slot.Offset, Slot.Length);
}

/// <summary>
/// Validates and follows the forward pointer at the START of a live overflow row slot.
/// </summary>
/// <remarks>
/// The slot is normally exactly 4 bytes: ACE's DML and <see cref="RowInserter"/> both trim it down to the
/// pointer when a row is relocated. Measured over 317 relocations with no exception, across ACE x64, the
/// ACE 2010 x86 runtime, and LibRed's own writer, under growing and shrinking text, repeated re-relocation,
/// page fragmentation by interleaved deletes, and an OLE column going from NULL to a value.
///
/// Real files nevertheless contain longer ones. Northwind's <c>MSysAccessStorage</c> has live overflow slots
/// of 45-63 bytes, and their contents are the row as it was BEFORE it moved, with only the leading 4 bytes
/// replaced by the pointer: every field lands where the row format puts it once those 4 bytes are discounted,
/// the keys match the row it forwards to, and the remnant's null bitmap differs from its target's in exactly
/// the OLE column's bit — the value whose arrival grew the row and forced the move. The slot simply kept the
/// old row's width.
///
/// What wrote them is NOT known: no write path reproduces the shape, including the OLE-column transition the
/// bytes themselves record. So this reads the leading pointer and ignores whatever follows, rather than
/// asserting a width. The checks that matter are unchanged and do the real work — the target must be in the
/// file, owned by the same table, and a nonempty hidden inline row.
/// </remarks>
internal static class RowRelocationReader
{
    public static RelocatedRow Resolve(PageChannel channel, int owningTablePage,
        RowSlot sourceSlot, ReadOnlySpan<byte> sourceBytes)
    {
        if (sourceSlot.IsDeleted || !sourceSlot.HasOverflow)
            throw new InvalidDataException("A relocation source must be a live overflow row slot.");
        if (sourceBytes.Length < 4)
            throw new InvalidDataException(
                $"A relocation source must begin with a 4-byte pointer; found {sourceBytes.Length} bytes.");

        int pointer = BinaryPrimitives.ReadInt32LittleEndian(sourceBytes[..4]);
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
