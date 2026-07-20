using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class RowRelocationCorruptionTests
{
    private const int PageSize = 4096;
    private const int RowDirectoryOffset = 0x0E;
    private const int OffsetMask = 0x1FFF;
    private const int DeletedFlag = 0x8000;
    private const int OverflowFlag = 0x4000;

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
        finally { File.Delete(path); }
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
        finally { File.Delete(path); }
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
        finally { File.Delete(path); }
    }

    private static (string Path, RowId Source) CreateRelocatedRow()
    {
        string path = Path.Combine(Path.GetTempPath(), $"reloc-corrupt-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
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
        byte[] file = File.ReadAllBytes(path);
        Span<byte> sourcePage = file.AsSpan(source.Page * PageSize, PageSize);
        int sourceEntryPos = RowDirectoryOffset + source.Row * 2;
        int raw = BinaryPrimitives.ReadUInt16LittleEndian(sourcePage[sourceEntryPos..]);
        int sourceOffset = raw & OffsetMask;
        int sourceEnd = source.Row == 0
            ? PageSize
            : BinaryPrimitives.ReadUInt16LittleEndian(sourcePage[(sourceEntryPos - 2)..]) & OffsetMask;
        int pointer = BinaryPrimitives.ReadInt32LittleEndian(sourcePage[sourceOffset..]);
        int targetPageNumber = pointer >> 8;
        int targetRow = pointer & 0xFF;

        switch (corruption)
        {
            case "short-source":
                BinaryPrimitives.WriteUInt16LittleEndian(sourcePage[sourceEntryPos..],
                    (ushort)((raw & ~OffsetMask) | (sourceEnd - 3)));
                break;
            case "page-outside-file":
                BinaryPrimitives.WriteInt32LittleEndian(sourcePage[sourceOffset..],
                    ((file.Length / PageSize) + 1) << 8);
                break;
            case "row-outside-page":
                BinaryPrimitives.WriteInt32LittleEndian(sourcePage[sourceOffset..], (targetPageNumber << 8) | 0xFF);
                break;
            case "target-not-hidden":
            case "target-is-overflow":
            case "target-wrong-owner":
                Span<byte> targetPage = file.AsSpan(targetPageNumber * PageSize, PageSize);
                if (corruption == "target-wrong-owner")
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(targetPage[4..], 2);
                    break;
                }
                int targetEntryPos = RowDirectoryOffset + targetRow * 2;
                int targetRaw = BinaryPrimitives.ReadUInt16LittleEndian(targetPage[targetEntryPos..]);
                targetRaw = corruption == "target-not-hidden"
                    ? targetRaw & ~DeletedFlag
                    : targetRaw | DeletedFlag | OverflowFlag;
                BinaryPrimitives.WriteUInt16LittleEndian(targetPage[targetEntryPos..], (ushort)targetRaw);
                break;
        }

        File.WriteAllBytes(path, file);
    }
}
