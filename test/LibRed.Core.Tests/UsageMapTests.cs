using LibRed;
using LibRed.IO;
using LibRed.Pages;
using System.Buffers.Binary;
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
        string path = Path.Combine(Path.GetTempPath(), $"bad-usage-map-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var table = db.OpenTable("MSysObjects");
            PageBuffer tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
            int mapRow = tdef.ReadByte(db.Format.TdefOwnedPagesOffset);
            int mapPage = tdef.ReadInt24(db.Format.TdefOwnedPagesOffset + 1);
            Assert.Equal(0, mapRow); // row 0 ends at the page boundary, making its length deterministic

            byte[] page = table.Channel.ReadPage(mapPage).Span.ToArray();
            int originalOffset = BinaryPrimitives.ReadUInt16LittleEndian(
                page.AsSpan(db.Format.DataRowDirectoryOffset, 2)) & 0x1FFF;
            int oversizedOffset = originalOffset - 4;
            BinaryPrimitives.WriteUInt16LittleEndian(
                page.AsSpan(db.Format.DataRowDirectoryOffset, 2), (ushort)oversizedOffset);
            page.AsSpan(oversizedOffset, db.Format.PageSize - oversizedOffset).Clear();
            page[oversizedOffset] = 0x01;
            table.Channel.WritePage(mapPage, page);

            Assert.Throws<InvalidDataException>(() => table.UsageMap.DataPages().ToList());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Exact_empty_reference_usage_map_remains_valid()
    {
        string path = Path.Combine(Path.GetTempPath(), $"empty-reference-map-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var table = db.OpenTable("MSysObjects");
            PageBuffer tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
            int mapPage = tdef.ReadInt24(db.Format.TdefOwnedPagesOffset + 1);
            byte[] page = table.Channel.ReadPage(mapPage).Span.ToArray();
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(
                page.AsSpan(db.Format.DataRowDirectoryOffset, 2)) & 0x1FFF;
            Assert.Equal(69, db.Format.PageSize - offset);
            page.AsSpan(offset, 69).Clear();
            page[offset] = 0x01;
            table.Channel.WritePage(mapPage, page);

            Assert.Empty(table.UsageMap.DataPages());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Reference_usage_map_rejects_a_pointer_to_a_non_bitmap_page()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bad-bitmap-pointer-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var table = db.OpenTable("MSysObjects");
            PageBuffer tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
            int mapPage = tdef.ReadInt24(db.Format.TdefOwnedPagesOffset + 1);
            byte[] page = table.Channel.ReadPage(mapPage).Span.ToArray();
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(
                page.AsSpan(db.Format.DataRowDirectoryOffset, 2)) & 0x1FFF;
            page.AsSpan(offset, 69).Clear();
            page[offset] = 0x01;
            BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(offset + 1, 4), mapPage); // data page, not 0x05
            table.Channel.WritePage(mapPage, page);

            Assert.Throws<InvalidDataException>(() => table.UsageMap.DataPages().ToList());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
