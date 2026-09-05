using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// A relocation slot normally holds exactly the 4-byte forward pointer — both ACE's DML and LibRed's writer
// trim it. Real files contain wider ones anyway: Northwind's MSysAccessStorage keeps the pre-move row and
// stamps the pointer over its first 4 bytes, which LibRed used to reject outright, making that table
// unreadable. See page-01-data-and-rows.md for the evidence.
//
// No write path produces the wide form — not text growth, not repeated re-relocation, not an OLE column going
// from NULL to a value — so the wide case is exercised by handing the resolver a wider source span directly
// rather than by trying to manufacture the on-disk shape.
public class RelocationSlotWidthTests
{
    // A row whose OLE column starts NULL and is then given a value grows and must move; that is the cheapest
    // reliable way to get real relocations to resolve against.
    private static string CreateStoreWithRelocations()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "relocw-");
        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable("R",
            [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsNullable: false),
             new ColumnSpec("Nm", JetDataType.Text, 100, IsFixedLength: false),
             new ColumnSpec("Lv", JetDataType.Ole, 0, IsFixedLength: false)],
            primaryKey: ["Id"]);

        Table table = db.OpenTable("R");
        for (int id = 1; id <= 80; id++)
            table.Insert([id, $"name-{id}", null]);
        foreach ((RowId rowId, object?[] values) in table.Rows().WithIds().ToList())
        {
            object?[] updated = (object?[])values.Clone();
            updated[2] = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
            table.Update(rowId, updated, new HashSet<int> { 2 });
        }
        return path;
    }

    /// <summary>The first live overflow slot in the table, as (page, slot index, offset, length).</summary>
    private static (int Page, int Index, int Offset, int Length) FirstOverflowSlot(Table table)
    {
        PageChannel channel = table.Channel;
        int dir = channel.Format.DataRowDirectoryOffset;
        foreach (int pageNumber in table.UsageMap.DataPages())
        {
            PageBuffer page = channel.ReadPage(pageNumber);
            int rowCount = page.ReadUInt16(channel.Format.DataRowCountOffset);
            int prevEnd = page.Length;
            for (int i = 0; i < rowCount; i++)
            {
                int raw = page.ReadUInt16(dir + i * 2);
                int offset = raw & RowPointer.OffsetMask;
                int length = prevEnd - offset;
                prevEnd = offset;
                if ((raw & RowPointer.DeletedFlag) == 0 && (raw & RowPointer.OverflowFlag) != 0)
                    return (pageNumber, i, offset, length);
            }
        }
        throw new InvalidOperationException("no live overflow slot was produced");
    }

    // The invariant the format doc records: our own writer trims. If this ever stops holding, the note in
    // page-01-data-and-rows.md is wrong and the wide-slot tolerance below is doing more than it claims.
    [Fact]
    public void LibRed_writes_relocation_slots_exactly_four_bytes_wide()
    {
        string path = CreateStoreWithRelocations();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: true);
            Table table = db.OpenTable("R");
            PageChannel channel = table.Channel;
            int dir = channel.Format.DataRowDirectoryOffset;

            var widths = new List<int>();
            foreach (int pageNumber in table.UsageMap.DataPages())
            {
                PageBuffer page = channel.ReadPage(pageNumber);
                int rowCount = page.ReadUInt16(channel.Format.DataRowCountOffset);
                int prevEnd = page.Length;
                for (int i = 0; i < rowCount; i++)
                {
                    int raw = page.ReadUInt16(dir + i * 2);
                    int offset = raw & RowPointer.OffsetMask;
                    int length = prevEnd - offset;
                    prevEnd = offset;
                    if ((raw & RowPointer.DeletedFlag) == 0 && (raw & RowPointer.OverflowFlag) != 0)
                        widths.Add(length);
                }
            }

            Assert.NotEmpty(widths);
            Assert.All(widths, w => Assert.Equal(4, w));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The fix: a source wider than the pointer resolves to the same row as the trimmed form. The trailing
    // bytes are the pre-move row and carry no meaning, so filling them with anything must change nothing.
    [Fact]
    public void A_source_wider_than_the_pointer_resolves_to_the_same_row()
    {
        string path = CreateStoreWithRelocations();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: true);
            Table table = db.OpenTable("R");
            PageChannel channel = table.Channel;
            (int pageNumber, _, int offset, int length) = FirstOverflowSlot(table);
            Assert.Equal(4, length);

            byte[] pointer = channel.ReadPage(pageNumber).Slice(offset, 4).ToArray();

            byte[] trimmed = RowRelocationReader.Resolve(
                channel, table.Definition.DefinitionPage,
                new RowSlot(offset, 4, IsDeleted: false, HasOverflow: true), pointer).Bytes.ToArray();

            // The Northwind shape: the pointer followed by 51 bytes of the row as it was before it moved.
            byte[] wide = new byte[55];
            pointer.CopyTo(wide, 0);
            for (int i = 4; i < wide.Length; i++) wide[i] = (byte)(i * 7);

            byte[] fromWide = RowRelocationReader.Resolve(
                channel, table.Definition.DefinitionPage,
                new RowSlot(offset, wide.Length, IsDeleted: false, HasOverflow: true), wide).Bytes.ToArray();

            Assert.Equal(trimmed, fromWide);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Still rejected: a source too short to hold a pointer at all. Tolerating a wide slot must not turn into
    // tolerating a truncated one.
    [Fact]
    public void A_source_shorter_than_the_pointer_is_still_rejected()
    {
        string path = CreateStoreWithRelocations();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: true);
            Table table = db.OpenTable("R");
            (_, _, int offset, _) = FirstOverflowSlot(table);

            var error = Assert.Throws<InvalidDataException>(() => RowRelocationReader.Resolve(
                table.Channel, table.Definition.DefinitionPage,
                new RowSlot(offset, 3, IsDeleted: false, HasOverflow: true), new byte[3]));
            Assert.Contains("4-byte pointer", error.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The symptom that started this: MSysAccessStorage in the Northwind fixture could not be read at all.
    // Weaker as a guard than the tests above — a compact/repair changes how many wide slots that table has
    // (observed going from 7 to 5) and could in principle leave none — so it is the reported bug, not the
    // contract.
    [Fact]
    public void The_northwind_system_storage_table_can_be_read()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb, readOnly: true);
        Table table = db.OpenTable("MSysAccessStorage");
        Assert.NotEmpty(table.Rows().ToList());
    }
}
