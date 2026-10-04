using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class RowRelocationCorruptionTests
{
    [Theory]
    [InlineData("short-source")]
    [InlineData("page-outside-file")]
    [InlineData("row-outside-page")]
    [InlineData("target-not-hidden")]
    [InlineData("target-is-overflow")]
    [InlineData("target-wrong-owner")]
    public void Corrupt_relocation_is_rejected_during_table_scan(string corruption)
    {
        (string path, RowId source) = CreateRelocatedRow();
        try
        {
            Corrupt(path, source, corruption);

            using var db = JetDatabase.Open(path);
            Table table = db.OpenTable("T");
            Assert.Throws<InvalidDataException>(() => table.Rows().ToList());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Corrupt_relocation_is_rejected_during_index_seek()
    {
        (string path, RowId source) = CreateRelocatedRow();
        try
        {
            Corrupt(path, source, "target-not-hidden");

            using var db = JetDatabase.Open(path);
            Table table = db.OpenTable("T");
            IndexDef primaryKey = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
            Assert.Throws<InvalidDataException>(() => table.SeekRows(primaryKey, [3]).ToList());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Corrupt_relocation_is_rejected_before_raw_rewrite()
    {
        (string path, RowId source) = CreateRelocatedRow();
        try
        {
            Corrupt(path, source, "target-not-hidden");

            using var db = JetDatabase.Open(path, readOnly: false);
            Table table = db.OpenTable("T");
            Assert.Throws<InvalidDataException>(() =>
                new RowInserter(table.Channel, table.Definition).RewriteRowRaw(source, [1, 0, 0]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static (string Path, RowId Source) CreateRelocatedRow()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "reloc-corrupt-");
        string mid = new('m', 80), big = new('X', 255);

        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable("T",
        [
            new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
            new("A", JetDataType.Text, 510, IsFixedLength: false),
            new("B", JetDataType.Text, 510, IsFixedLength: false),
            new("C", JetDataType.Text, 510, IsFixedLength: false),
        ], primaryKey: ["Id"]);
        Table table = db.OpenTable("T");
        for (int i = 0; i < 7; i++) table.Insert([null, mid, mid, mid]);

        int idIndex = table.Definition.FindColumn("Id")!.Index;
        (RowId source, object?[] old) = table.Rows().WithIds()
            .First(x => Convert.ToInt32(x.Values[idIndex]) == 3);
        var updated = (object?[])old.Clone();
        foreach (string name in new[] { "A", "B", "C" })
            updated[table.Definition.FindColumn(name)!.Index] = big;
        table.Update(source, updated);
        return (path, source);
    }

    private static void Corrupt(string path, RowId source, string corruption)
    {
        JetFormatBase format = TestDatabases.FormatOf(path);
        int pageSize = format.PageSize;
        byte[] file = File.ReadAllBytes(path);
        Span<byte> sourcePage = file.AsSpan(source.Page * pageSize, pageSize);
        (int sourceOffset, RowSlotFlags sourceFlags) = DataPage.ReadSlot(sourcePage, format, source.Row);
        int sourceEnd = source.Row == 0 ? pageSize : DataPage.ReadSlot(sourcePage, format, source.Row - 1).Offset;
        (int targetRow, int targetPageNumber) = PageBuffer.ReadRecordPointer(sourcePage, sourceOffset);

        switch (corruption)
        {
            case "short-source":
                DataPage.WriteSlot(sourcePage, format, source.Row, sourceEnd - 3, sourceFlags);
                break;
            case "page-outside-file":
                PageBuffer.WriteRecordPointer(sourcePage, sourceOffset, row: 0, file.Length / pageSize + 1);
                break;
            case "row-outside-page":
                PageBuffer.WriteRecordPointer(sourcePage, sourceOffset, row: 0xFF, targetPageNumber);
                break;
            case "target-not-hidden":
            case "target-is-overflow":
            case "target-wrong-owner":
                Span<byte> targetPage = file.AsSpan(targetPageNumber * pageSize, pageSize);
                if (corruption == "target-wrong-owner")
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(targetPage[format.DataOwnerOffset..], 2);
                    break;
                }
                (int targetOffset, RowSlotFlags targetFlags) = DataPage.ReadSlot(targetPage, format, targetRow);
                DataPage.WriteSlot(targetPage, format, targetRow, targetOffset, corruption == "target-not-hidden"
                    ? targetFlags & ~RowSlotFlags.Deleted
                    : targetFlags | RowSlotFlags.Deleted | RowSlotFlags.Overflow);
                break;
        }

        File.WriteAllBytes(path, file);
    }
}