using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// The control characters, DEL and U+FEFF, keyed as ACE keys them in both General orders — alone and between
/// two letters. They sit outside General Legacy's measured table, so LibRed's v0 encoder carries them as rules
/// (NUL and U+FEFF ignorable, tab to carriage return two-byte primaries, the rest word-sort records); this is
/// what holds those rules to ACE's own keys.
/// </summary>
[Collection(AceCollection.Name)]
public class ControlCharacterKeyAccessTests
{
    private static readonly int[] CodePoints =
        [.. Enumerable.Range(0x00, 0x20), 0x7F, .. Enumerable.Range(0x80, 0x20), 0xFEFF];

    [Fact]
    public void General_legacy_v0_keys_them_as_ace_does() => AssertKeysMatchAce(Collation.GeneralLegacy);

    [Fact]
    public void General_v1_keys_them_as_ace_does() => AssertKeysMatchAce(Collation.General);

    private static void AssertKeysMatchAce(Collation collation)
    {
        string[] samples = [.. CodePoints.SelectMany(c => (string[])[((char)c).ToString(), "a" + (char)c + "b"])];
        Dictionary<string, string> ace = AceKeys(collation, samples);

        // U+FEFF alone does not come back as the value it was stored under, so it alone goes unmatched.
        Assert.True(ace.Count >= samples.Length - 1, $"ACE returned {ace.Count} of {samples.Length} keys");

        var column = new ColumnDef { Name = "T", Type = JetDataType.Text, Index = 0, Collation = collation };
        var wrong = ace
            .Where(k => Convert.ToHexString(IndexKeyCodec.Encode([(column, true)], [k.Key])) != k.Value)
            .Select(k => $"{string.Join("+", k.Key.Select(ch => $"U+{(int)ch:X4}"))}: ACE {k.Value}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    private static Dictionary<string, string> AceKeys(Collation collation, string[] samples)
    {
        string path = TemporaryDatabase.CreatePath("control-keys-", ".accdb");
        try
        {
            JetDatabase.Create(path, collation: collation);
            using (OleDbConnection ace = AceTestDatabase.Open(path))
            {
                Exec(ace, "CREATE TABLE K (Id LONG, T TEXT(10))");
                Exec(ace, "CREATE INDEX IX_K ON K (T)");
                for (int i = 0; i < samples.Length; i++)
                {
                    using OleDbCommand insert = ace.CreateCommand();
                    insert.CommandText = "INSERT INTO K (Id, T) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("i", i);
                    insert.Parameters.AddWithValue("t", samples[i]);
                    insert.ExecuteNonQuery();
                }
            }

            using var database = JetDatabase.Open(path);
            var table = database.OpenTable("K");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_K");
            ColumnDef column = table.Definition.FindColumn("T")!;
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);
            var keys = new Dictionary<string, string>();
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
                if (rows.TryGetValue(rowId, out object?[]? values) && values[column.Index] is string text
                    && samples.Contains(text))
                    keys[text] = Convert.ToHexString(stored);
            return keys;
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}