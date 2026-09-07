using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

// A corrupt file must fail as a CORRUPT FILE. LibRed's contract for "these bytes are not a valid database"
// is InvalidDataException, and callers that want to tell a damaged file from a bug in the reader catch that.
// An IndexOutOfRange or an EndOfStream escaping from the middle of a parser is not that contract: it reaches
// the caller as an unexpected crash, and .NET makes it worse than it sounds — InvalidDataException derives
// from SystemException while EndOfStreamException derives from IOException, so the two share no base a
// caller could catch instead.
//
// The table's usage-map pointer is a good place to press, because it is four bytes of pure indirection read
// straight out of the TDEF: one byte of row index and three of page number, both used to reach another page
// without ever being compared against anything.
public class CorruptUsageMapPointerTests
{
    [Theory]
    [InlineData(true, "a page number far past the end of the file")]
    [InlineData(false, "a row index past the end of a VALID map page")]
    public void A_corrupt_usage_map_pointer_is_reported_as_a_corrupt_file(bool corruptPage, string what)
    {
        _ = what;   // names the case in the test output

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "corrupt-umap-");
        try
        {
            int definitionPage;
            using (var database = JetDatabase.Open(path))
                definitionPage = database.Catalog.FindTable("Customers")!.DefinitionPage;

            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                byte[] tdef = channel.ReadPage(definitionPage).Span.ToArray();
                int at = channel.Format.TdefOwnedPagesOffset;

                if (corruptPage)
                {
                    tdef[at + 1] = 0xFF;                // holder page, 3 bytes LE — far past the file
                    tdef[at + 2] = 0xFF;
                    tdef[at + 3] = 0xFF;
                }
                else
                {
                    // Leave the holder page alone so the map page really parses, and break only the row
                    // index. Pointing at page 0 instead would fail as a bad page type long before the row
                    // index was ever used, which is not the path under test.
                    tdef[at] = 200;
                }

                channel.WritePage(definitionPage, tdef);
            }

            Assert.Throws<InvalidDataException>(() =>
            {
                using var database = JetDatabase.Open(path);
                _ = database.OpenTable("Customers").Rows().ToList();
            });
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
