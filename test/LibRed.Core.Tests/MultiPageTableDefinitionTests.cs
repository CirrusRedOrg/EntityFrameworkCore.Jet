using LibRed;
using LibRed.Pages;
using System.Buffers.Binary;
using Xunit;

namespace LibRed.Core.Tests;

public class MultiPageTableDefinitionTests
{
    [Fact]
    public void Parses_definition_that_spans_multiple_pages()
    {
        using var db = JetDatabase.Open(TestDatabases.WideTableAccdb);

        var def = db.Catalog.FindTable("WideTable");
        Assert.NotNull(def);

        var tdef = db.ReadTableDefinition(def!.DefinitionPage);

        // The definition continues onto another page.
        Assert.NotEqual(0, tdef.NextDefinitionPage);
        Assert.Equal(200, tdef.ColumnCount);
        Assert.Equal(200, tdef.Columns.Count);

        // Column names continue seamlessly across the page boundary (C000..C199).
        Assert.Equal(
            Enumerable.Range(0, 200).Select(i => $"C{i:D3}"),
            tdef.Columns.Select(c => c.Name));
    }

    [Fact]
    public void Reads_rows_from_a_wide_table()
    {
        using var db = JetDatabase.Open(TestDatabases.WideTableAccdb);

        var table = db.OpenTable("WideTable");
        int Idx(string n) => table.Definition.Columns.First(c => c.Name == n).Index;

        var row = Assert.Single(table.Rows());
        Assert.Equal(1000, row[Idx("C000")]);
        Assert.Equal(1100, row[Idx("C100")]);
        Assert.Equal(1199, row[Idx("C199")]);
        Assert.Null(row[Idx("C001")]); // not inserted
    }

    [Theory]
    [InlineData("wrong-type")]
    [InlineData("outside-file")]
    [InlineData("declared-single-page")]
    [InlineData("cycle")]
    [InlineData("missing-continuation")]
    [InlineData("oversized-length")]
    [InlineData("wrong-root-header")]
    public void Rejects_an_invalid_continuation_chain_before_assembly(string corruption)
    {
        string path = Path.Combine(Path.GetTempPath(), $"bad-tdef-chain-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.WideTableAccdb, path);
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var table = db.OpenTable("WideTable");
            var channel = table.Channel;
            int firstPage = table.Definition.DefinitionPage;
            byte[] first = channel.ReadPage(firstPage).Span.ToArray();
            int continuation = BinaryPrimitives.ReadInt32LittleEndian(
                first.AsSpan(db.Format.TdefNextPageOffset, 4));
            Assert.True(continuation > 0);

            switch (corruption)
            {
                case "wrong-type":
                    byte[] wrongType = channel.ReadPage(continuation).Span.ToArray();
                    wrongType[0] = (byte)PageType.DataPage;
                    channel.WritePage(continuation, wrongType);
                    break;
                case "outside-file":
                    BinaryPrimitives.WriteInt32LittleEndian(
                        first.AsSpan(db.Format.TdefNextPageOffset, 4), channel.PageCount + 1);
                    channel.WritePage(firstPage, first);
                    break;
                case "declared-single-page":
                    BinaryPrimitives.WriteInt32LittleEndian(
                        first.AsSpan(db.Format.TdefLengthOffset, 4), db.Format.PageSize);
                    channel.WritePage(firstPage, first);
                    break;
                case "cycle":
                    byte[] cyclic = channel.ReadPage(continuation).Span.ToArray();
                    BinaryPrimitives.WriteInt32LittleEndian(
                        cyclic.AsSpan(db.Format.TdefNextPageOffset, 4), continuation);
                    channel.WritePage(continuation, cyclic);
                    break;
                case "missing-continuation":
                    BinaryPrimitives.WriteInt32LittleEndian(
                        first.AsSpan(db.Format.TdefNextPageOffset, 4), 0);
                    channel.WritePage(firstPage, first);
                    break;
                case "oversized-length":
                    BinaryPrimitives.WriteInt32LittleEndian(
                        first.AsSpan(db.Format.TdefLengthOffset, 4), 1024 * 1024 + 1);
                    channel.WritePage(firstPage, first);
                    break;
                case "wrong-root-header":
                    first[1] = 0;
                    channel.WritePage(firstPage, first);
                    break;
            }

            Assert.Throws<InvalidDataException>(() => db.ReadTableDefinition(firstPage));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
