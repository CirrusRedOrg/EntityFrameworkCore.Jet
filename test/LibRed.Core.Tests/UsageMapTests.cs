using LibRed;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class UsageMapTests
{
    [Fact]
    public void Inline_usage_map_lists_owned_data_pages()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var pages = db.OpenTable("MSysObjects").UsageMap.DataPages().ToList();

        Assert.Equal([17, 274, 323], pages);
    }

    [Fact]
    public void Usage_map_excludes_stale_orphan_pages()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        // MSysNavPaneObjectIDs has an orphan page (stale owner stamp) that a naive
        // owner-scan would double-count. The real usage map excludes it, so the decoded
        // row count matches the TDEF's own count exactly.
        var table = db.OpenTable("MSysNavPaneObjectIDs");
        int tdefRows = db.ReadTableDefinition(table.Definition.DefinitionPage).RowCount;

        Assert.Equal(tdefRows, table.Rows().Count());
    }

    [Fact]
    public void Reference_usage_map_rejects_a_record_larger_than_the_format_shape()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "bad-usage-map-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var table = db.OpenTable("MSysObjects");
            PageBuffer tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
            (int mapRow, int mapPage) = tdef.ReadRecordPointer(db.Format.TdefOwnedPagesOffset);
            Assert.Equal(0, mapRow); // row 0 ends at the page boundary, making its length deterministic

            byte[] page = table.Channel.ReadPage(mapPage).Span.ToArray();
            int originalOffset = DataPage.ReadSlot(page, db.Format, 0).Offset;
            int oversizedOffset = originalOffset - sizeof(int);
            DataPage.WriteSlot(page, db.Format, 0, oversizedOffset, RowSlotFlags.None);
            page.AsSpan(oversizedOffset, db.Format.PageSize - oversizedOffset).Clear();
            page[oversizedOffset] = (byte)UsageMapType.Reference;
            table.Channel.WritePage(mapPage, page);

            Assert.Throws<InvalidDataException>(() => table.UsageMap.DataPages().ToList());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Exact_empty_reference_usage_map_remains_valid()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "empty-reference-map-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var table = db.OpenTable("MSysObjects");
            PageBuffer tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
            int mapPage = tdef.ReadRecordPointer(db.Format.TdefOwnedPagesOffset).Page;
            byte[] page = table.Channel.ReadPage(mapPage).Span.ToArray();
            int offset = DataPage.ReadSlot(page, db.Format, 0).Offset;
            Assert.Equal(db.Format.UsageMapReferenceRecordSize, db.Format.PageSize - offset);
            UsageMap.NewReferenceRecord(db.Format).CopyTo(page, offset);
            table.Channel.WritePage(mapPage, page);

            Assert.Empty(table.UsageMap.DataPages());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Reference_usage_map_rejects_a_pointer_to_a_non_bitmap_page()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "bad-bitmap-pointer-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var table = db.OpenTable("MSysObjects");
            PageBuffer tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
            int mapPage = tdef.ReadRecordPointer(db.Format.TdefOwnedPagesOffset).Page;
            byte[] page = table.Channel.ReadPage(mapPage).Span.ToArray();
            int offset = DataPage.ReadSlot(page, db.Format, 0).Offset;
            Span<byte> record = page.AsSpan(offset, db.Format.UsageMapReferenceRecordSize);
            UsageMap.NewReferenceRecord(db.Format).CopyTo(record);
            UsageMap.WriteReferencePointer(record, 0, db.Format, mapPage); // data page, not 0x05
            table.Channel.WritePage(mapPage, page);

            Assert.Throws<InvalidDataException>(() => table.UsageMap.DataPages().ToList());
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}