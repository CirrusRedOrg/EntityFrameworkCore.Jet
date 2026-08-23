using System.Text;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: derive tailoring tables from ACE rather than transcribing them by hand.
//
// For each sort-order fixture it has ACE encode every character in a broad range, reads the stored index
// keys back, and prints the entries needed to reproduce them — ready to paste into JetLocaleTailoring. Hand
// transcription of hex is exactly the kind of work that introduces a wrong byte nobody notices, because a
// wrong index key is silent.
//
// It also reports what GENERAL itself cannot encode. Most of those are Latin Extended-A letters that
// decompose to a base letter plus a combining mark whose secondary weight is simply missing from
// JetTextCollation.DiacriticWeights — so the probe derives that weight too, which fixes the base table for
// every order at once.
public class TailoringGeneratorProbeTest(ITestOutputHelper output)
{
    [Fact]
    public void Generate_diacritic_weights_missing_from_general()
    {
        Dictionary<string, string> ace = AceKeys(TestDatabases.NorthwindAccdb, "general", Characters());
        var derived = new SortedDictionary<char, (byte Weight, string From)>();
        var unexplained = new List<string>();

        foreach ((string text, string key) in ace)
        {
            if (Encodable(text, Collation.GeneralLegacy)) continue;
            string nfd = text.Normalize(NormalizationForm.FormD);
            (byte[] primaries, byte secondary) = Decode(key);
            // A base letter plus one combining mark, weighing as that letter's primary: the difference is
            // wholly in the secondary, so the mark's weight is all that is missing.
            if (nfd.Length == 2 && char.IsLetter(nfd[0]) && primaries.Length == 1)
                derived.TryAdd(nfd[1], (secondary, $"{text} U+{(int)text[0]:X4}"));
            else
                unexplained.Add($"{Describe(text)} -> {key}");
        }

        output.WriteLine("Combining marks whose secondary weight General is missing:");
        foreach ((char mark, (byte weight, string from)) in derived)
            output.WriteLine($"    ['\\u{(int)mark:X4}'] = 0x{weight:X2},   // {from}");
        output.WriteLine("");
        output.WriteLine($"Not explained by base letter + one mark ({unexplained.Count}):");
        foreach (string line in unexplained.Take(40)) output.WriteLine($"    {line}");
    }

    [Theory]
    [InlineData("SpanishModern")]
    [InlineData("SpanishTraditional")]
    [InlineData("GermanPhoneBook")]
    [InlineData("Polish")]
    [InlineData("RomanianLegacy")]
    [InlineData("Turkish")]
    [InlineData("Czech")]
    [InlineData("CroatianLegacy")]
    [InlineData("NorwegianDanish")]
    [InlineData("Hungarian")]
    [InlineData("Estonian")]
    [InlineData("Icelandic")]
    [InlineData("Latvian")]
    [InlineData("Lithuanian")]
    [InlineData("Slovenian")]
    [InlineData("SwedishFinnish")]
    [InlineData("Slovak")]
    [InlineData("Vietnamese")]
    [InlineData("HungarianTechnical")]
    public void Generate_tailoring_for(string fixture)
    {
        string source = TestDatabases.Data($"{fixture}.accdb");
        Assert.SkipWhen(!File.Exists(source), $"{fixture}.accdb is not present");

        string[] characters = Characters();
        Dictionary<string, string> locale = AceKeys(source, fixture, characters);
        Dictionary<string, string> general = AceKeys(TestDatabases.NorthwindAccdb, "general", characters);

        var letters = new List<string>();
        var accented = new List<string>();
        foreach (string text in characters)
        {
            if (!locale.TryGetValue(text, out string? key)) continue;
            if (general.TryGetValue(text, out string? baseline) && baseline == key) continue;
            // Only the uppercase form is needed; the lowercase folds onto it.
            if (text != text.ToUpperInvariant()) continue;

            (byte[] primaries, byte secondary) = Decode(key);
            string bytes = string.Join(", ", primaries.Select(b => $"0x{b:X2}"));
            if (secondary == 0x02) letters.Add($"(\"{text}\", [{bytes}])");
            else accented.Add($"(\"{text}\", [{bytes}], 0x{secondary:X2})");
        }

        output.WriteLine($"// --- {fixture} ---");
        output.WriteLine($"letters ({letters.Count}):");
        foreach (string line in Wrap(letters)) output.WriteLine($"    {line}");
        output.WriteLine($"accented ({accented.Count}):");
        foreach (string line in Wrap(accented)) output.WriteLine($"    {line}");
    }

    /// <summary>Printable ASCII, Latin-1 and Latin Extended-A — the range these orders tailor. The C1
    /// controls are skipped: ACE treats them as ignorables with an inline record rather than as letters,
    /// so they are a separate topic and would otherwise fill the report.</summary>
    private static string[] Characters()
    {
        var characters = new List<string>();
        for (char c = ' '; c <= 'ſ'; c++)
            if (c is < '' or > '')
                characters.Add(c.ToString());
        return [.. characters];
    }

    /// <summary>Splits an index key into its primary bytes and its single secondary weight (the default
    /// <c>0x02</c> when the section is empty). Reads from the end: the key is
    /// <c>7F primaries… 01 secondaries… 00</c>, and no observed secondary weight is <c>0x01</c>.</summary>
    private static (byte[] Primaries, byte Secondary) Decode(string hex)
    {
        byte[] key = Convert.FromHexString(hex);
        int end = key.Length - 1;                       // the 0x00 terminator
        int split = end - 1;
        while (split > 0 && key[split] != 0x01) split--;
        byte[] primaries = key[1..split];
        byte secondary = end - split == 1 ? (byte)0x02 : key[split + 1];
        return (primaries, secondary);
    }

    private static bool Encodable(string text, Collation collation)
    {
        var column = new ColumnDef { Name = "K", Type = JetDataType.Text, Index = 0, Collation = collation };
        try { IndexKeyEncoder.Encode([(column, true)], [text]); return true; }
        catch (NotSupportedException) { return false; }
    }

    private static IEnumerable<string> Wrap(List<string> entries)
    {
        for (int i = 0; i < entries.Count; i += 4)
            yield return string.Join(", ", entries.Skip(i).Take(4)) + ",";
    }

    private static Dictionary<string, string> AceKeys(string source, string label, string[] samples)
    {
        string path = TemporaryDatabase.CopyPath(source, $"gen-{label.ToLowerInvariant()}-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Gen (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_Gen ON Gen (K)");
                for (int i = 0; i < samples.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Gen (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", samples[i]);
                    insert.Parameters.AddWithValue("v", i);
                    try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("Gen");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Gen");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

            var keys = new Dictionary<string, string>();
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
                if (rows.TryGetValue(rowId, out object?[]? values) && values[keyColumn.Index] is string text)
                    keys[text] = Convert.ToHexString(stored);
            return keys;
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Describe(string s) =>
        s.All(c => c is >= ' ' and <= '~') ? $"\"{s}\"" : string.Concat(s.Select(c => $"U+{(int)c:X4}"));

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
