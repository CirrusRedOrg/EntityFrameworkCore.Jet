using System.Buffers.Binary;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PageAllocator handles the reference-type (0x01) global free-pages map on page 1: it finds a free page by
// scanning the dedicated bitmap pages (type 0x05), clears its bit, and returns it — and Free() sets the bit
// back. A SET bit is a FREE page (the global map's sense, opposite of a per-table owned map). Verified here
// against a hand-crafted two-slot reference map: a real reference-type global map only appears in a large
// (>~130 MB) pre-existing ACE file, impractical to grow in a unit test, and the bit↔page math is the same
// one the per-table reference map is byte-verified against (WideTableUsageMapTests).
public class GlobalReferenceFreeMapTests
{
    [Fact]
    public void Allocate_and_free_through_a_reference_type_global_map()
    {
        const int pageSize = 4096;
        const int pagesPerBitmap = (pageSize - 4) * 8; // 32,736 — one bitmap page's span
        var format = JetFormatBase.FromVersionByte(0x02); // ACE 12

        var file = new byte[4 * pageSize];

        // Page 0 — just enough for JetFormatBase.Detect: the ACE identifier + version byte.
        System.Text.Encoding.ASCII.GetBytes("Standard ACE DB").CopyTo(file, 0x04);
        file[0x14] = 0x02;

        // Page 1 — a data page whose row 0 is a reference-type global free map: slot 0 → bitmap page 2,
        // slot 1 → bitmap page 3. (The 69-byte record is packed at the page end, as ACE packs rows.)
        int p1 = pageSize;
        file[p1] = 0x01; // page type: data page
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(p1 + format.DataRowCountOffset, 2), 1);
        int mapOffset = pageSize - 69;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(p1 + format.DataRowDirectoryOffset, 2), (ushort)mapOffset);
        file[p1 + mapOffset] = 0x01; // reference map type
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(p1 + mapOffset + 1 + 0 * 4, 4), 2); // slot 0 → page 2
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(p1 + mapOffset + 1 + 1 * 4, 4), 3); // slot 1 → page 3

        // Page 2 — bitmap page for slot 0; free page 5.        Page 3 — bitmap page for slot 1; free page 32,746.
        WriteBitmapPage(file, 2 * pageSize, inRangeBit: 5);
        WriteBitmapPage(file, 3 * pageSize, inRangeBit: 10); // slot 1 base (32,736) + 10 = global page 32,746

        string path = Path.Combine(Path.GetTempPath(), $"libred-globalref-{Guid.NewGuid():N}.accdb");
        File.WriteAllBytes(path, file);
        try
        {
            using var channel = PageChannel.Open(path, readOnly: false);
            var alloc = new PageAllocator(channel);

            Assert.Equal(5, alloc.Allocate());                     // slot 0's free page
            Assert.Equal(pagesPerBitmap + 10, alloc.Allocate());   // slot 1's free page (base = 1 × 32,736)
            Assert.Equal(4, alloc.Allocate());                     // nothing free left → grows the file (page 4)

            alloc.Free(5);
            Assert.Equal(5, alloc.Allocate());                     // page 5 is free again

            alloc.Free(pagesPerBitmap + 10);
            Assert.Equal(pagesPerBitmap + 10, alloc.Allocate());   // and the slot-1 page too
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Writes a type-0x05 usage-bitmap page at <paramref name="offset"/> with one free bit set (the
    /// bitmap starts 4 bytes past the page header; a set bit marks a free page).</summary>
    private static void WriteBitmapPage(byte[] file, int offset, int inRangeBit)
    {
        file[offset] = 0x05;
        file[offset + 1] = 0x01;
        file[offset + 4 + inRangeBit / 8] |= (byte)(1 << (inRangeBit % 8));
    }
}
