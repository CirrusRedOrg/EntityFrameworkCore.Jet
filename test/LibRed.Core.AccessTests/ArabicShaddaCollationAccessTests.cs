using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// U+0651 ARABIC SHADDA is the gemination mark: it says the consonant before it is doubled, and ACE weighs it
// as exactly that — a second copy of whatever precedes it. It is NOT an ignorable, though it sits in the
// middle of the harakat run (U+064B..U+0650, U+0652) that are.
//
// This was wrong for as long as the v0 table existed, because the table was built by sweeping characters ONE
// AT A TIME, and a shadda on its own has nothing to double: it weighs FF FF, which is what got recorded as
// its primary and then emitted in every word. The spec called that value "anomalous". It is not anomalous,
// it is the empty case. The collation survey found it because a survey has to use words.
//
// So the samples here are all CONTEXT: the same mark after a letter, after another mark, after itself, and
// with nothing at all in front of it. A per-character test cannot fail on this bug, which is the whole point.
public class ArabicShaddaCollationAccessTests
{
    private const char Shadda = (char)0x0651;
    private const char Fatha = (char)0x064E;
    private const char Damma = (char)0x064F;
    private const char Meem = (char)0x0645;
    private const char Alef = (char)0x0627;
    private const char Hah = (char)0x062D;
    private const char Dal = (char)0x062F;

    // Written as named characters rather than literals: Arabic renders right to left and a combining mark
    // draws on its base whichever side it is stored, so which of these two orders is which cannot be read
    // off an Arabic string in a source file. Getting that backwards is a silently passing test.
    public static TheoryData<string, string> Samples => new()
    {
        { "shadda alone — nothing to double", new string([Shadda]) },
        { "after a letter", new string([Meem, Shadda]) },
        { "after a different letter", new string([Alef, Shadda]) },
        { "leading, no base", new string([Shadda, Meem]) },
        { "after a mark", new string([Meem, Fatha, Shadda]) },
        { "before a mark", new string([Meem, Shadda, Fatha]) },
        { "between two letters", new string([Meem, Shadda, Meem]) },
        { "last", new string([Meem, Meem, Shadda]) },
        { "doubled, no base", new string([Shadda, Shadda]) },
        { "doubled, after a letter", new string([Meem, Shadda, Shadda]) },
        { "muhammad", new string([Meem, Damma, Hah, Fatha, Meem, Fatha, Shadda, Dal]) },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Libred_encodes_a_shadda_as_ace_does(string description, string value)
    {
        _ = description;   // names the case in the test output

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "shadda-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Shadda (K TEXT(100))");
                Exec(connection, "CREATE INDEX IX_Shadda ON Shadda (K)");
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO Shadda (K) VALUES (?)";
                insert.Parameters.AddWithValue("k", value);
                insert.ExecuteNonQuery();
            }

            using var database = JetDatabase.Open(path);
            var table = database.OpenTable("Shadda");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Shadda");
            ColumnDef column = table.Definition.FindColumn("K")!;

            // ACE's own key, off the index page it wrote.
            (byte[] stored, _) = new IndexCursor(table.Channel, index.RootPage).RawEntries().Single();

            Assert.Equal(
                Convert.ToHexString(stored),
                Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [value])));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
