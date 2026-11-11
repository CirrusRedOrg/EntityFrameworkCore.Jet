using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class IndexTraversalCorruptionTests
{
    private static readonly JetFormatBase Format = TestDatabases.FormatOf(TestDatabases.NorthwindAccdb);

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("child-outside-file")]
    [InlineData("child-zero")]
    [InlineData("leaf-previous-outside-file")]
    [InlineData("leaf-next-outside-file")]
    [InlineData("leaf-row-outside-file")]
    [InlineData("entry-shorter-than-trailer")]
    [InlineData("wrong-page-type")]
    [InlineData("descent-cycle")]
    [InlineData("leaf-cycle")]
    [InlineData("child-wrong-owner")]
    [InlineData("leaf-next-nonleaf")]
    [InlineData("compressed-prefix-too-long")]
    public void Malformed_index_traversal_is_rejected_as_corruption(string corruption)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "index-corrupt-");
        try
        {
            (int root, int owner) = IndexIdentity(path);
            byte[] file = File.ReadAllBytes(path);
            int pageCount = file.Length / Format.PageSize;
            Span<byte> rootPage = Page(file, root);
            switch (corruption)
            {
                case "wrong-owner":
                    BinaryPrimitives.WriteInt32LittleEndian(rootPage[Format.IndexOwnerOffset..], owner + 1);
                    break;
                case "child-outside-file":
                    BinaryPrimitives.WriteInt32LittleEndian(rootPage[Format.IndexChildTailOffset..], pageCount + 1);
                    break;
                case "child-zero":
                    BinaryPrimitives.WriteInt32LittleEndian(rootPage[Format.IndexChildTailOffset..], 0);
                    break;
                case "leaf-previous-outside-file":
                    int previousLeaf = LeftmostLeaf(file, root);
                    BinaryPrimitives.WriteInt32LittleEndian(Page(file, previousLeaf)[Format.IndexPrevPageOffset..], pageCount + 1);
                    break;
                case "leaf-next-outside-file":
                    int leaf = LeftmostLeaf(file, root);
                    BinaryPrimitives.WriteInt32LittleEndian(Page(file, leaf)[Format.IndexNextPageOffset..], pageCount + 1);
                    break;
                case "leaf-row-outside-file":
                    int rowLeaf = LeftmostLeaf(file, root);
                    Span<byte> rowLeafPage = Page(file, rowLeaf);
                    int rowEnd = FirstEntryEnd(rowLeafPage);
                    BinaryPrimitives.WriteInt32BigEndian(
                        rowLeafPage.Slice(Format.IndexEntryDataOffset + rowEnd - Format.IndexEntryTrailerSize, Format.IndexEntryTrailerSize),
                        new RowId(pageCount + 1, 0).Packed);
                    break;
                case "entry-shorter-than-trailer":
                    rootPage[Format.IndexEntryMaskOffset..Format.IndexEntryDataOffset].Clear();
                    rootPage[Format.IndexEntryMaskOffset] = 0x02; // first entry ends after one byte, before its 4-byte trailer
                    break;
                case "wrong-page-type":
                    PageHeader.WriteType(rootPage, PageType.DataPage);
                    break;
                case "descent-cycle":
                    BinaryPrimitives.WriteInt32LittleEndian(rootPage[Format.IndexChildTailOffset..], root);
                    break;
                case "leaf-cycle":
                    int cycleLeaf = LeftmostLeaf(file, root);
                    BinaryPrimitives.WriteInt32LittleEndian(Page(file, cycleLeaf)[Format.IndexNextPageOffset..], cycleLeaf);
                    break;
                case "child-wrong-owner":
                    int foreignLeaf = LeftmostLeaf(file, root);
                    BinaryPrimitives.WriteInt32LittleEndian(Page(file, foreignLeaf)[Format.IndexOwnerOffset..], owner + 1);
                    break;
                case "leaf-next-nonleaf":
                    int linkedLeaf = LeftmostLeaf(file, root);
                    BinaryPrimitives.WriteInt32LittleEndian(Page(file, linkedLeaf)[Format.IndexNextPageOffset..], root);
                    break;
                case "compressed-prefix-too-long":
                    BinaryPrimitives.WriteUInt16LittleEndian(rootPage[Format.IndexCompressedByteCountOffset..], ushort.MaxValue);
                    break;
            }
            File.WriteAllBytes(path, file);

            using var db = JetDatabase.Open(path);
            Table table = db.OpenTable("Orders");
            IndexDef index = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
            Assert.Throws<InvalidDataException>(() =>
                corruption is "leaf-previous-outside-file" or "leaf-next-outside-file" or "leaf-row-outside-file"
                    or "leaf-cycle" or "child-wrong-owner" or "leaf-next-nonleaf"
                    ? table.SeekRangeRows(index, null, null).ToList()
                    : table.SeekRows(index, [int.MaxValue]).ToList());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Full_index_cursor_rejects_a_child_owned_by_another_table()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "index-cursor-corrupt-");
        try
        {
            (int root, int owner) = IndexIdentity(path);
            byte[] file = File.ReadAllBytes(path);
            int leaf = LeftmostLeaf(file, root);
            BinaryPrimitives.WriteInt32LittleEndian(Page(file, leaf)[Format.IndexOwnerOffset..], owner + 1);
            File.WriteAllBytes(path, file);

            using var db = JetDatabase.Open(path);
            Table table = db.OpenTable("Orders");
            Assert.Throws<InvalidDataException>(() => new IndexCursor(table.Channel, root).RowIds().ToList());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Full_index_cursor_rejects_a_cycle_without_recursive_descent()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "index-cursor-cycle-");
        try
        {
            (int root, _) = IndexIdentity(path);
            byte[] file = File.ReadAllBytes(path);
            BinaryPrimitives.WriteInt32LittleEndian(Page(file, root)[Format.IndexChildTailOffset..], root);
            File.WriteAllBytes(path, file);

            using var db = JetDatabase.Open(path);
            Table table = db.OpenTable("Orders");
            Assert.Throws<InvalidDataException>(() => new IndexCursor(table.Channel, root).RowIds().ToList());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // B's variable index patched to A's, so the two columns share one slot: B reads A's value, while B's index still
    // holds the row under the value B was written with. A write of that row looks for the entry under the value it
    // reads and finds none. ACE refuses the same write on the same damage — Customers.City moved onto CompanyName's
    // slot — with "The search key was not found in any record." and leaves the table as it was (measured), so this is
    // damage, not a refused value, and the statement rolls back.
    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public void A_write_whose_index_entry_the_row_no_longer_matches_is_rejected_as_corruption(string statement)
    {
        string path = TemporaryDatabase.CreatePath("index-orphaned-");
        try
        {
            JetDatabase.Create(path);
            int page;
            byte[] a, b;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T", [new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("A", JetDataType.Text, 40, IsFixedLength: false),
                    new ColumnSpec("B", JetDataType.Text, 40, IsFixedLength: false)]);
                db.CreateIndex("T", "ix", [("B", false)]);
                db.OpenTable("T").Insert([1, "aa", "bb"]);
                TableDefinition t = db.Catalog.FindTable("T")!;
                (page, a, b) = (t.DefinitionPage, t.FindColumn("A")!.RawDescriptor!, t.FindColumn("B")!.RawDescriptor!);
            }
            byte[] file = File.ReadAllBytes(path);
            JetFormatBase format = JetFormatBase.FromVersionByte(file[0x14]);
            int at = file.AsSpan(page * format.PageSize, format.PageSize).IndexOf(b);
            Assert.True(at >= 0);
            a.AsSpan(format.ColumnVariableIndexOffset, 2).CopyTo(file.AsSpan(page * format.PageSize + at + format.ColumnVariableIndexOffset));
            File.WriteAllBytes(path, file);

            using var damaged = JetDatabase.Open(path, readOnly: false);
            Table table = damaged.OpenTable("T");
            (RowId id, object?[] row) = table.Rows().WithIds().Single();
            Assert.Equal([1, "aa", "aa"], row);
            if (statement == "delete")
                Assert.Throws<InvalidDataException>(() => table.Delete(id));
            else
            {
                var changed = (object?[])row.Clone();
                changed[2] = "cc";
                Assert.Throws<InvalidDataException>(() => table.Update(id, changed));
            }
            Assert.Equal([1, "aa", "aa"], table.Rows().Single());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static (int Root, int Owner) IndexIdentity(string path)
    {
        using var db = JetDatabase.Open(path);
        Table table = db.OpenTable("Orders");
        return (table.Definition.Indexes.Single(i => i.IsPrimaryKey).RootPage, table.Definition.DefinitionPage);
    }

    private static int LeftmostLeaf(byte[] file, int pageNumber)
    {
        while (PageHeader.ReadType(Page(file, pageNumber)) == PageType.IntermediateIndexPage)
        {
            ReadOnlySpan<byte> page = Page(file, pageNumber);
            int end = FirstEntryEnd(page);
            pageNumber = BinaryPrimitives.ReadInt32BigEndian(
                page.Slice(Format.IndexEntryDataOffset + end - Format.IndexEntryTrailerSize, Format.IndexEntryTrailerSize));
        }
        return pageNumber;
    }

    private static int FirstEntryEnd(ReadOnlySpan<byte> page)
    {
        int end = BitmapBits.NextSetBit(page[Format.IndexEntryMaskOffset..Format.IndexEntryDataOffset], 0);
        if (end < 0)
            throw new InvalidDataException("Expected a nonempty index node.");
        return end;
    }

    private static Span<byte> Page(byte[] file, int pageNumber) => file.AsSpan(pageNumber * Format.PageSize, Format.PageSize);
}