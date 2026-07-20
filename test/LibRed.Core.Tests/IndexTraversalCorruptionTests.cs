using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class IndexTraversalCorruptionTests
{
    private const int PageSize = 4096;
    private const int OwnerOffset = 0x04;
    private const int NextPageOffset = 0x10;
    private const int ChildTailOffset = 0x14;
    private const int EntryMaskOffset = 0x1B;
    private const int EntryDataOffset = 0x1E0;

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("child-outside-file")]
    [InlineData("leaf-next-outside-file")]
    [InlineData("entry-shorter-than-trailer")]
    [InlineData("wrong-page-type")]
    [InlineData("descent-cycle")]
    [InlineData("leaf-cycle")]
    [InlineData("child-wrong-owner")]
    [InlineData("leaf-next-nonleaf")]
    [InlineData("compressed-prefix-too-long")]
    public void Malformed_index_traversal_is_rejected_as_corruption(string corruption)
    {
        string path = Path.Combine(Path.GetTempPath(), $"index-corrupt-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            (int root, int owner) = IndexIdentity(path);
            byte[] file = File.ReadAllBytes(path);
            int pageCount = file.Length / PageSize;
            Span<byte> rootPage = Page(file, root);
            switch (corruption)
            {
                case "wrong-owner":
                    BinaryPrimitives.WriteInt32LittleEndian(rootPage[OwnerOffset..], owner + 1);
                    break;
                case "child-outside-file":
                    BinaryPrimitives.WriteInt32LittleEndian(rootPage[ChildTailOffset..], pageCount + 1);
                    break;
                case "leaf-next-outside-file":
                    int leaf = LeftmostLeaf(file, root);
                    BinaryPrimitives.WriteInt32LittleEndian(Page(file, leaf)[NextPageOffset..], pageCount + 1);
                    break;
                case "entry-shorter-than-trailer":
                    rootPage[EntryMaskOffset..EntryDataOffset].Clear();
                    rootPage[EntryMaskOffset] = 0x02; // first entry ends after one byte, before its 4-byte trailer
                    break;
                case "wrong-page-type":
                    rootPage[0] = 0x01;
                    break;
                case "descent-cycle":
                    BinaryPrimitives.WriteInt32LittleEndian(rootPage[ChildTailOffset..], root);
                    break;
                case "leaf-cycle":
                    int cycleLeaf = LeftmostLeaf(file, root);
                    BinaryPrimitives.WriteInt32LittleEndian(Page(file, cycleLeaf)[NextPageOffset..], cycleLeaf);
                    break;
                case "child-wrong-owner":
                    int foreignLeaf = LeftmostLeaf(file, root);
                    BinaryPrimitives.WriteInt32LittleEndian(Page(file, foreignLeaf)[OwnerOffset..], owner + 1);
                    break;
                case "leaf-next-nonleaf":
                    int linkedLeaf = LeftmostLeaf(file, root);
                    BinaryPrimitives.WriteInt32LittleEndian(Page(file, linkedLeaf)[NextPageOffset..], root);
                    break;
                case "compressed-prefix-too-long":
                    BinaryPrimitives.WriteUInt16LittleEndian(rootPage[0x18..], ushort.MaxValue);
                    break;
            }
            File.WriteAllBytes(path, file);

            using var db = JetDatabase.Open(path);
            Table table = db.OpenTable("Orders");
            IndexDef index = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
            Assert.Throws<InvalidDataException>(() =>
                corruption is "leaf-next-outside-file" or "leaf-cycle" or "child-wrong-owner" or "leaf-next-nonleaf"
                    ? table.SeekRangeRows(index, null, null).ToList()
                    : table.SeekRows(index, [int.MaxValue]).ToList());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Full_index_cursor_rejects_a_child_owned_by_another_table()
    {
        string path = Path.Combine(Path.GetTempPath(), $"index-cursor-corrupt-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            (int root, int owner) = IndexIdentity(path);
            byte[] file = File.ReadAllBytes(path);
            int leaf = LeftmostLeaf(file, root);
            BinaryPrimitives.WriteInt32LittleEndian(Page(file, leaf)[OwnerOffset..], owner + 1);
            File.WriteAllBytes(path, file);

            using var db = JetDatabase.Open(path);
            Table table = db.OpenTable("Orders");
            Assert.Throws<InvalidDataException>(() => new IndexCursor(table.Channel, root).RowIds().ToList());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Full_index_cursor_rejects_a_cycle_without_recursive_descent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"index-cursor-cycle-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            (int root, _) = IndexIdentity(path);
            byte[] file = File.ReadAllBytes(path);
            BinaryPrimitives.WriteInt32LittleEndian(Page(file, root)[ChildTailOffset..], root);
            File.WriteAllBytes(path, file);

            using var db = JetDatabase.Open(path);
            Table table = db.OpenTable("Orders");
            Assert.Throws<InvalidDataException>(() => new IndexCursor(table.Channel, root).RowIds().ToList());
        }
        finally { File.Delete(path); }
    }

    private static (int Root, int Owner) IndexIdentity(string path)
    {
        using var db = JetDatabase.Open(path);
        Table table = db.OpenTable("Orders");
        return (table.Definition.Indexes.Single(i => i.IsPrimaryKey).RootPage, table.Definition.DefinitionPage);
    }

    private static int LeftmostLeaf(byte[] file, int pageNumber)
    {
        while (Page(file, pageNumber)[0] == 0x03)
        {
            ReadOnlySpan<byte> page = Page(file, pageNumber);
            int end = FirstEntryEnd(page);
            pageNumber = BinaryPrimitives.ReadInt32BigEndian(page.Slice(EntryDataOffset + end - 4, 4));
        }
        return pageNumber;
    }

    private static int FirstEntryEnd(ReadOnlySpan<byte> page)
    {
        for (int i = EntryMaskOffset; i < EntryDataOffset; i++)
            for (int bit = 0; bit < 8; bit++)
                if ((page[i] & (1 << bit)) != 0)
                    return (i - EntryMaskOffset) * 8 + bit;
        throw new InvalidDataException("Expected a nonempty index node.");
    }

    private static Span<byte> Page(byte[] file, int pageNumber) => file.AsSpan(pageNumber * PageSize, PageSize);
}
