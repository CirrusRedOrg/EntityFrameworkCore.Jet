using System.Data.OleDb;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// ACE builds every row an UPDATE rewrites in one buffer per table, zeroed when the statement starts and never
/// cleared, and only the rows it updates pass through it: a byte the new row does not write and its old record did
/// not reach holds the last updated record that reached it. That shows in the padding of a null-bitmap byte an UPDATE
/// adds (after a BIT column takes the table past eight), and in the fixed slots a row predating fixed ADD COLUMNs
/// grows into. The text is compressed, one byte per character, so a stale byte names the row and position it came
/// from. ACE and LibRed run the same statements and must leave every row byte for byte the same.
/// </summary>
[Collection(AceCollection.Name)]
public class UpdateRowBufferAccessTests
{
    private static readonly string[] Texts =
    [
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ",  // Id 1, long
        "x",                           // Id 2, short
        "abcdefghijklmnopqrstuvwxyz",  // Id 3, long
        "yy",                          // Id 4, short
        "0123456789",                  // Id 5
    ];

    public static TheoryData<string, string> Updates => new()
    {
        { "U1", "UPDATE U1 SET Y = TRUE WHERE Id IN (2, 4)" },   // only the short rows: nothing longer reaches the buffer
        { "U2", "UPDATE U2 SET Y = TRUE WHERE Id = 4" },
        { "U3", "UPDATE U3 SET Y = TRUE" },                      // every row: each short one takes a longer one's text
        { "U4", "UPDATE U4 SET Y = TRUE WHERE Id = 2" },
        { "U5", "UPDATE U5 SET C1 = 9 WHERE Id IN (2, 4)" },
    };

    [Theory]
    [MemberData(nameof(Updates))]
    public void A_null_bitmap_byte_an_update_adds_holds_the_buffer(string table, string update) =>
        AssertSameRows(table,
        [
            $"CREATE TABLE {table} (Id LONG, T TEXT(60) WITH COMPRESSION, C1 LONG, C2 LONG, C3 LONG, C4 LONG, C5 LONG, C6 LONG)",
            .. Texts.Select((text, i) => $"INSERT INTO {table} VALUES ({i + 1}, '{text}', 1, 2, 3, 4, 5, 6)"),
            $"ALTER TABLE {table} ADD COLUMN Y BIT",
            update,
        ]);

    [Fact]
    public void Fixed_slots_past_an_old_record_hold_the_buffer() =>
        AssertSameRows("W",
        [
            "CREATE TABLE W (Id LONG, T TEXT(60) WITH COMPRESSION)",
            .. Texts.Select((text, i) => $"INSERT INTO W VALUES ({i + 1}, '{text}')"),
            "ALTER TABLE W ADD COLUMN W1 DOUBLE",
            "ALTER TABLE W ADD COLUMN W2 DOUBLE",
            "ALTER TABLE W ADD COLUMN W3 DOUBLE",
            "UPDATE W SET Id = Id + 10",
        ]);

    private static void AssertSameRows(string table, string[] statements)
    {
        string ace = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-upd-");
        string libred = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "libred-upd-");
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
            Assert.Equal(Rows(ace, table), Rows(libred, table));
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    // Each row's raw record and values, without its row id: the two files need not place the table on the same page.
    private static string[] Rows(string path, string table) =>
    [
        .. ColumnRetypeParityAccessTests.Describe(path, table).Split('\n')
            .Where(l => l.Contains('|')).Select(l => l[(l.IndexOf(' ', StringComparison.Ordinal) + 1)..]),
    ];
}
