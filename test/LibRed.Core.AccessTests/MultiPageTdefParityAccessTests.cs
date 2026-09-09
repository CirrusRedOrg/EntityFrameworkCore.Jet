using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;
using Xunit;

namespace LibRed.Core.Tests;

// A table definition too big for one page must match ACE's, and split where ACE splits.
//
// TdefByteParityAccessTests compares the whole definition but every shape in it fits a single page. Beyond
// that the definition spills onto continuation pages, and the splitting is LibRed's own: TdefBuilder emits
// one oversized buffer and TableCreator.WriteDefinition cuts it up.
//
// Stitching alone would not catch a wrong split — the same content can be divided differently and still
// reassemble — so the definition page's own next-page link and free space are compared as well.
public class MultiPageTdefParityAccessTests : TempDatabaseTest
{
    [Theory]
    [InlineData(60, 1)]
    [InlineData(100, 1)]
    [InlineData(150, 2)]
    [InlineData(200, 2)]
    public void A_definition_spanning_pages_matches_ace(int columns, int expectedPages)
    {
        string acePath = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "mptdef-ace-");
        string libredPath = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "mptdef-libred-");
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(acePath))
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE W ("
                    + string.Join(", ", Enumerable.Range(0, columns).Select(i => $"C{i} LONG")) + ")";
                command.ExecuteNonQuery();
            }

            using (var database = JetDatabase.Open(libredPath, readOnly: false))
                database.CreateTable("W", [.. Enumerable.Range(0, columns)
                    .Select(i => new ColumnSpec($"C{i}", JetDataType.Int32, 4, IsFixedLength: true))]);

            (byte[] ace, int acePages) = Definition(acePath);
            (byte[] libred, int libredPages) = Definition(libredPath);

            Assert.Equal(expectedPages, acePages);
            Assert.Equal(acePages, libredPages);
            Assert.Equal(Convert.ToHexString(ace), Convert.ToHexString(libred));
            Assert.Equal(FirstPageChain(acePath), FirstPageChain(libredPath));
        }
        finally
        {
            TemporaryDatabase.Delete(acePath);
            TemporaryDatabase.Delete(libredPath);
        }
    }

    /// <summary>The definition page's next-page link and remaining free space — where the split falls.</summary>
    private static string FirstPageChain(string path)
    {
        using var channel = PageChannel.Open(path, readOnly: true);
        byte[] page = channel.ReadPage(new JetCatalog(channel).FindTable("W")!.DefinitionPage).Span.ToArray();
        return $"next={BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(4, 4))} "
            + $"free={BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(2, 2))}";
    }

    /// <summary>The stitched definition and how many pages it spans.</summary>
    private static (byte[] Bytes, int Pages) Definition(string path)
    {
        using var channel = PageChannel.Open(path, readOnly: true);
        TableDef table = new JetCatalog(channel).FindTable("W")!;
        (PageBuffer buffer, IReadOnlyList<int> continuation) = TdefChainReader.Read(channel, table.DefinitionPage);
        return (buffer.Slice(0, buffer.ReadInt32(channel.Format.TdefLengthOffset)).ToArray(),
            continuation.Count + 1);
    }
}
