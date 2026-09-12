using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A memo value larger than one LVAL page (&gt; 4076 bytes) is written as a <b>chain</b> of LVAL pages,
/// each chunk row beginning with a 4-byte pointer to the next. This checks a large value round-trips
/// through LibRed and that Access reads it back intact.
/// </summary>
public class ChainedLongValueAccessTests
{
    // 20 000 chars = 40 000 bytes → several chained LVAL pages (chunk data is 4072 bytes/page).
    private static readonly string Big =
        string.Concat(Enumerable.Range(0, 20_000).Select(i => (char)('A' + i % 26)));

    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void A_large_memo_chains_across_lval_pages_and_round_trips()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "chained-lval-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Big",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("M", JetDataType.Memo, 0, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                db.OpenTable("Big").Insert([1, Big]);
            }

            // LibRed reads the chain back exactly.
            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Big");
                int m = table.Definition.Columns.First(c => c.Name == "M").Index;
                Assert.Equal(Big, (string)table.Rows().First()[m]!);
            }

            // Access reads the same value through its own long-value chain resolution.
            using var conn = OpenOleDb(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT M FROM Big WHERE Id = 1";
            Assert.Equal(Big, (string)cmd.ExecuteScalar()!);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A chained descriptor's 0x08 and the FIRST chain page's header 0x08 are one value in two places, and
    // ACE enforces that they agree: patch either alone and it refuses the record as "you and another user
    // are attempting to change the same data at the same time". The value itself is free -- ACE stamps
    // GetTickCount(), LibRed writes zero, and both are accepted because both are self-consistent. What a
    // writer must never do is set one without the other, which is what this pins: half a stamp is a file
    // Access cannot read, and nothing else in our tests would notice.
    [Fact]
    public void Libreds_chain_stamp_agrees_with_its_first_chain_page()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "chain-stamp-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Big",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("M", JetDataType.Memo, 0, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                db.OpenTable("Big").Insert([1, Big]);
            }

            using var channel = LibRed.IO.PageChannel.Open(path);
            TableDef definition = new JetCatalog(channel).FindTable("Big")!;
            var decoder = new RowDecoder(definition.Columns, channel.Format);
            int columnId = definition.Columns.First(c => c.Name == "M").ColumnId;

            byte[] descriptor = new UsageMap(channel, definition).DataPages()
                .Select(p => { var page = new DataPage(); page.Read(channel.ReadPage(p), channel.Format); return page; })
                .SelectMany(page => Enumerable.Range(0, page.RowCount)
                    .Where(row => !page.Rows[row].IsDeleted)
                    .SelectMany(row => decoder.LongValueRaw(page.GetRow(row))))
                .Single(d => d.Key == columnId).Value[..12];

            Assert.Equal(0x00, descriptor[3] & 0xC0);   // chained, or the stamp would not apply
            int firstChainPage = descriptor[5] | (descriptor[6] << 8) | (descriptor[7] << 16);

            var header = new byte[channel.PageSize];
            channel.ReadPage(firstChainPage, header);
            Assert.Equal(Convert.ToHexString(descriptor[8..12]), Convert.ToHexString(header[8..12]));
            // And it is a real tag, not two zeroes agreeing by accident — zero would satisfy the check while
            // proving nothing, which is what LibRed used to write.
            Assert.NotEqual("00000000", Convert.ToHexString(descriptor[8..12]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The check has to bite, or writing the stamp is decoration. Breaks the agreement the way a stale
    // pointer would — the chain rewritten under a descriptor that still names its first page — and expects
    // the read to refuse rather than hand back another value's bytes.
    [Fact]
    public void A_chain_whose_stamp_disagrees_is_refused()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "chain-stale-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Big",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("M", JetDataType.Memo, 0, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                db.OpenTable("Big").Insert([1, Big]);
            }

            int firstChainPage;
            using (var channel = LibRed.IO.PageChannel.Open(path, readOnly: false))
            {
                TableDef definition = new JetCatalog(channel).FindTable("Big")!;
                var decoder = new RowDecoder(definition.Columns, channel.Format);
                int columnId = definition.Columns.First(c => c.Name == "M").ColumnId;
                int dataPage = new UsageMap(channel, definition).DataPages().First();
                var parsed = new DataPage();
                parsed.Read(channel.ReadPage(dataPage), channel.Format);
                byte[] descriptor = decoder.LongValueRaw(parsed.GetRow(0)).Single(d => d.Key == columnId).Value;
                firstChainPage = descriptor[5] | (descriptor[6] << 8) | (descriptor[7] << 16);

                // Restamp the chain page alone, as a rewrite by another writer would.
                var page = new byte[channel.PageSize];
                channel.ReadPage(firstChainPage, page);
                BitConverter.GetBytes(0xDEADBEEFu).CopyTo(page, channel.Format.DataChainStampOffset);
                channel.WritePage(firstChainPage, page);
            }

            using var database = JetDatabase.Open(path);
            var table = database.OpenTable("Big");
            int m = table.Definition.Columns.First(c => c.Name == "M").Index;
            var thrown = Assert.Throws<InvalidDataException>(() => table.Rows().Select(r => r[m]).ToList());
            Assert.Contains("not the one this row was written against", thrown.Message);
            Assert.Contains("DEADBEEF", thrown.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
