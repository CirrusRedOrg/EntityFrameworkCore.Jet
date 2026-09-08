using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Several small long values share LVAL pages (Access packs many per page as separate rows) instead of
/// each taking a whole page. This inserts many medium memos and checks they land on far fewer pages than
/// values, that a full page leaves the free map while the current one stays free, and that both LibRed
/// and Access read every value back.
/// </summary>
public class LongValuePackingAccessTests
{
    private const int N = 20; // each ~310 bytes → LVAL, but ~10 fit per page
    private static readonly string[] Values =
        Enumerable.Range(0, N).Select(i => $"row{i}:" + new string((char)('A' + i % 26), 150)).ToArray();

    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static List<int> MapPages(PageChannel ch, (int Row, int Page) ptr)
    {
        var holder = new DataPage();
        holder.Read(ch.ReadPage(ptr.Page), ch.Format);
        byte[] map = holder.GetRow(ptr.Row).ToArray();
        int start = BinaryPrimitives.ReadInt32LittleEndian(map.AsSpan(1, 4));
        var pages = new List<int>();
        for (int i = 5; i < map.Length; i++)
            for (int bit = 0; bit < 8; bit++)
                if ((map[i] & (1 << bit)) != 0) pages.Add(start + (i - 5) * 8 + bit);
        return pages;
    }

    [Fact]
    public void Small_long_values_share_lval_pages_and_round_trip()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "lval-pack-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Big",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("M", JetDataType.Memo, 0, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                var t = db.OpenTable("Big");
                for (int i = 0; i < N; i++) t.Insert([i + 1, Values[i]]);
            }

            using (var ch = PageChannel.Open(path, readOnly: true))
            {
                var t = new JetCatalog(ch).FindTable("Big")!;
                var def = new TableDefinitionPage();
                def.Read(ch, t.DefinitionPage);
                int m = t.Columns.First(c => c.Name == "M").ColumnId;
                var owned = MapPages(ch, def.LongValueOwnedMaps[m]);
                var free = MapPages(ch, def.LongValueFreeMaps[m]);

                Assert.True(owned.Count < N / 3, $"{N} values packed onto {owned.Count} pages"); // ~2, not 20
                Assert.Single(free);                        // only the current append page is free
                Assert.Contains(free[0], owned);            // and it is an owned page
                Assert.Equal(owned.Max(), free[0]);         // the last-allocated page is the free one
            }

            using (var db = JetDatabase.Open(path))
            {
                var t = db.OpenTable("Big");
                int id = t.Definition.Columns.First(c => c.Name == "Id").Index;
                int m = t.Definition.Columns.First(c => c.Name == "M").Index;
                foreach (object?[] row in t.Rows())
                    Assert.Equal(Values[(int)row[id]! - 1], (string)row[m]!);
            }

            using var conn = OpenOleDb(path);
            for (int i = 1; i <= N; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT M FROM Big WHERE Id = {i}";
                Assert.Equal(Values[i - 1], (string)cmd.ExecuteScalar()!);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
