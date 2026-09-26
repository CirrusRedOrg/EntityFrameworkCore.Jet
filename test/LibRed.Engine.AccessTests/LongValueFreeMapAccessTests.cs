using System.Data.OleDb;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A long-value page leaves its column's free-pages map once it cannot hold a 256-byte value and its slot — 257
/// bytes free or fewer — the same through ACE and LibRed (docs/format/long-values.md). Two values fill the page to
/// just either side of the line: a 2000-byte one, then one that leaves <c>remaining</c> bytes.
/// </summary>
[Collection(AceCollection.Name)]
public class LongValueFreeMapAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(258)]
    [InlineData(259)]
    public void A_page_leaves_the_free_map_where_ace_takes_it_out(int remaining)
    {
        string[] statements =
        [
            "CREATE TABLE L (Id LONG, M LONGBINARY)",
            $"INSERT INTO L VALUES (1, 0x{new string('A', 4000)})",
            $"INSERT INTO L VALUES (2, 0x{new string('B', 2 * (2078 - remaining))})",
        ];
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string ace = TemporaryDatabase.CopyPath(northwind, "lvfree-ace-"), libred = TemporaryDatabase.CopyPath(northwind, "lvfree-lib-");
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(ace))
                foreach (string sql in statements)
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            using (var db = JetDatabase.Open(libred, readOnly: false))
            {
                var engine = new QueryEngine(db);
                foreach (string sql in statements) engine.ExecuteNonQuery(sql);
            }

            string expected = Maps(ace), actual = Maps(libred);
            output.WriteLine($"ACE {expected}, LibRed {actual}");
            Assert.Equal(expected, actual);
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>The column's long-value page, its free space, and whether its free-pages map still names it.</summary>
    private static string Maps(string path)
    {
        using var db = JetDatabase.Open(path, readOnly: true);
        Table table = db.OpenTable("L");
        var tdef = new TableDefinitionPage();
        tdef.Read(table.Channel.ReadPage(table.Definition.DefinitionPage), table.Channel.Format);
        int id = table.Definition.RequireColumn("M").ColumnId;
        var maps = new UsageMap(table.Channel, table.Definition);
        int page = maps.PagesInMap(tdef.LongValueOwnedMaps[id].Row, tdef.LongValueOwnedMaps[id].Page).Single();
        bool free = maps.PagesInMap(tdef.LongValueFreeMaps[id].Row, tdef.LongValueFreeMaps[id].Page).Contains(page);
        int space = BitConverter.ToUInt16(table.Channel.ReadPage(page).Span[2..4]);
        return $"{space} bytes free, {(free ? "in the free map" : "out of it")}";
    }
}
