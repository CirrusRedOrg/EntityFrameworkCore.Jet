using System.Buffers.Binary;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PageAllocator handles a reference-type (0x01) global free-pages map — here built on page 1 of a synthetic file and
// named by its page 0 pointers: it finds a free page by
// scanning the dedicated bitmap pages (type 0x0105), clears its bit, and returns it — and Free() sets the bit
// back. A SET bit is a FREE page (the global map's sense, opposite of a per-table owned map). Verified here
// against a hand-crafted two-slot reference map: a real reference-type global map only appears in a large
// (>~130 MB) pre-existing ACE file, impractical to grow in a unit test, and the bit↔page math is the same
// one the per-table reference map is byte-verified against (WideTableUsageMapTests).
public class GlobalReferenceFreeMapTests
{
    [Fact]
    public void Allocate_and_free_through_a_reference_type_global_map()
    {
        var format = JetFormatBase.FromVersionByte(0x02); // ACE 12
        int pageSize = format.PageSize;

        var file = new byte[6 * pageSize];
        int p1 = pageSize;

        // Page 0 — use a valid unencrypted header. A bare identifier/version leaves the masked database-key
        // field invalid and makes PageChannel correctly treat this synthetic file as encrypted. Its map pointers
        // name the map page built below; the file has no catalog, so the catalog pointers are left zero.
        DatabaseDefinitionPage.Build(
            0x02, isAccdb: true, codePage: 1252, collation: LibRed.Catalog.Collation.GeneralLegacy,
            creationDays: 45000, globalMapPage: p1 / pageSize, objectsPage: 0, acesPage: 0, queriesPage: 0,
            relationshipsPage: 0, accountsPage: 0, groupsPage: 0).CopyTo(file, 0);

        // Page 1 — a data page whose row 0 is a reference-type global free map: slot 0 → bitmap page 2,
        // slot 1 → bitmap page 3. (The 69-byte record is packed at the page end, as ACE packs rows.) Row 1 is the
        // released-pages map page 0 points at (0x1C), empty, as every real file carries it.
        PageHeader.WriteType(file.AsSpan(p1), PageType.DataPage);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(p1 + format.DataRowCountOffset, 2), 2);
        int mapOffset = pageSize - format.UsageMapReferenceRecordSize;
        DataPage.WriteSlot(file.AsSpan(p1, pageSize), format, 0, mapOffset, RowSlotFlags.None);
        DataPage.WriteSlot(file.AsSpan(p1, pageSize), format, 1, mapOffset - format.UsageMapInlineRecordSize, RowSlotFlags.None);
        Span<byte> map = file.AsSpan(p1 + mapOffset, format.UsageMapReferenceRecordSize);
        UsageMap.NewReferenceRecord(format).CopyTo(map);
        UsageMap.WriteReferencePointer(map, 0, format, 2); // slot 0 → page 2
        UsageMap.WriteReferencePointer(map, 1, format, 3); // slot 1 → page 3

        // Page 2 — bitmap page for slot 0; physical free page 5. Page 3 is an empty slot-1 bitmap.
        WriteBitmapPage(file, 2 * pageSize, format, inRangeBit: 5);
        WriteBitmapPage(file, 3 * pageSize, format, inRangeBit: null);

        string path = TemporaryDatabase.CreatePath("libred-globalref-");
        File.WriteAllBytes(path, file);
        try
        {
            using var channel = PageChannel.Open(path, readOnly: false);
            var alloc = channel.Allocator;

            Assert.Equal(5, alloc.Allocate());                     // slot 0's physical free page
            Assert.Equal(6, alloc.Allocate());                     // nothing free left → grows contiguously

            alloc.Free(5);
            Assert.Equal(5, alloc.Allocate());                     // page 5 is free again
        }
        finally
        {
            TemporaryDatabase.Delete(path);
        }
    }

    /// <summary>Writes a type-0x0105 usage-bitmap page at <paramref name="offset"/> with one free bit set (the
    /// bitmap starts 4 bytes past the page header; a set bit marks a free page).</summary>
    private static void WriteBitmapPage(byte[] file, int offset, JetFormatBase format, int? inRangeBit)
    {
        byte[] page = UsageMap.NewBitmapPage(format);
        if (inRangeBit is int bit)
            BitmapBits.Set(UsageMap.BitmapPageBits(page, format), bit, true);
        page.CopyTo(file, offset);
    }
}