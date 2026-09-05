using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// The Latin-1 punctuation/symbol block: ACE writes an index key for every one of these, and LibRed used to
// refuse them outright ("collation weight is not implemented yet"), so a text column holding "£100" or "Café ©"
// could not be indexed. ACE is the oracle here — it builds the index, LibRed re-encodes the same value and must
// reproduce the stored bytes exactly.
public class Latin1SymbolCollationAccessTests
{
    // Every printable Latin-1 character outside A–Z/a–z/0–9 and the ASCII punctuation LibRed already knew,
    // plus the soft hyphen (which carries no primary weight at all).
    private const string Latin1Symbols =
        "¡¢£¤¥¦§¨©ª«¬­®¯" +
        "°±²³´µ¶·¸¹º»¼½¾¿" +
        "×÷";

    [Fact]
    public void Libred_reproduces_ace_index_keys_for_every_latin1_symbol()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "latin1-collation-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE L1 (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_L1 ON L1 (K)");
                for (int i = 0; i < Latin1Symbols.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO L1 (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", Latin1Symbols[i].ToString());
                    insert.Parameters.AddWithValue("v", i);
                    insert.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("L1");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_L1");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

            int checkedKeys = 0;
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            {
                if (!rows.TryGetValue(rowId, out object?[]? values)) continue;
                string value = (string?)values[keyColumn.Index] ?? "";

                var aligned = new object?[table.Definition.Columns.Count];
                aligned[keyColumn.Index] = values[keyColumn.Index];

                Assert.Equal(
                    (value, Convert.ToHexString(stored)),
                    (value, Convert.ToHexString(IndexKeyEncoder.Encode(index.Columns, aligned))));
                checkedKeys++;
            }

            Assert.Equal(Latin1Symbols.Length, checkedKeys);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Superscript digits take their base digit's primary weight and no distinguishing secondary, and ACE's key
    // format stops after the secondary section — so these collate *equal*, which matters for a unique index.
    [Theory]
    [InlineData('¹', '1')]
    [InlineData('²', '2')]
    [InlineData('³', '3')]
    public void A_superscript_digit_encodes_identically_to_its_base_digit(char superscript, char digit)
    {
        var column = new ColumnDef
        {
            Name = "t", Type = JetDataType.Text, Index = 0, Collation = Collation.GeneralLegacy,
        };
        Assert.Equal(
            Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [digit.ToString()])),
            Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [superscript.ToString()])));
    }

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
