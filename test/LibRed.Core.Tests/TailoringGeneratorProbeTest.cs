using System.Text;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: derive weight tables from ACE rather than transcribing them by hand.
//
// It has ACE encode every character in a range, reads the stored index keys back, and prints the entries
// needed to reproduce them — ready to paste into JetTextCollation or JetLocaleTailoring. Hand transcription
// of hex is exactly the kind of work that introduces a wrong byte nobody notices, because a wrong index key
// is silent: ACE simply writes its own keys into the same index and a seek misses rows.
//
// Three reports:
//   Generate_general_coverage                      — everything General v0 does not yet encode, per block
//   Generate_diacritic_weights_missing_from_general — combining marks whose secondary weight is missing
//   Generate_tailoring_for                          — one locale's overrides against General
public class TailoringGeneratorProbeTest(ITestOutputHelper output)
{
    /// <summary>The blocks worth sweeping for General v0 coverage: everything a Jet/ACE text column is
    /// likely to hold that is not CJK. Each is (name, first, last).</summary>
    private static readonly (string Name, int First, int Last)[] Blocks =
    [
        ("Latin-1 + ASCII", 0x0020, 0x00FF),
        ("Latin Extended-A", 0x0100, 0x017F),
        ("Latin Extended-B", 0x0180, 0x024F),
        ("Spacing modifiers", 0x02B0, 0x02FF),
        ("Greek", 0x0370, 0x03FF),
        ("Cyrillic", 0x0400, 0x04FF),
        ("Cyrillic Supplement", 0x0500, 0x052F),
        ("Hebrew", 0x0590, 0x05FF),
        ("Arabic", 0x0600, 0x06FF),
        ("Latin Extended Additional", 0x1E00, 0x1EFF),
        ("General punctuation", 0x2000, 0x206F),
        ("Currency", 0x20A0, 0x20BF),
        ("Letterlike", 0x2100, 0x214F),
        ("Number forms", 0x2150, 0x218F),
        ("Fullwidth forms", 0xFF01, 0xFF65),
    ];

    // RECONNAISSANCE: how much of the BMP does ACE actually weigh, and how much of it do we already have?
    //
    // The block list above is a guess at "what a Jet text column plausibly holds", so "complete coverage" so
    // far means complete for that guess — it leaves out Devanagari, Thai, Georgian, the presentation forms
    // (which contain ﬁ), Greek Extended, CJK, Hangul and more. This sweeps all 65,536 code points in chunks
    // and counts what falls where, which is what decides whether the compact per-block strings can scale or
    // whether this needs a generated binary resource like the v1 table.
    //
    // Slow by nature: every character is a real INSERT through ACE. Run it deliberately, not in a suite.
    [Fact]
    public void Probe_full_bmp_coverage()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_FULL_BMP") == "1",
            "set LIBRED_FULL_BMP=1 — this inserts ~63,000 rows through ACE and takes minutes");

        int totalCorrect = 0, totalToAdd = 0, totalIgnorable = 0, totalRefused = 0;
        output.WriteLine($"  {"range",-14} {"correct",8} {"to add",8} {"ignorable",10} {"ACE refused",12}");
        for (int chunk = 0x0000; chunk <= 0xF000; chunk += 0x1000)
        {
            string[] characters = Range(chunk, chunk + 0x0FFF);
            if (characters.Length == 0) continue;
            Dictionary<string, string> ace = AceKeys(TestDatabases.NorthwindAccdb, "bmp", characters);

            int correct = 0, toAdd = 0, ignorable = 0, refused = 0;
            foreach (string text in characters)
            {
                if (!ace.TryGetValue(text, out string? key)) { refused++; continue; }
                if (Matches(text, key)) { correct++; continue; }
                if (key == "7F0100") { ignorable++; continue; }
                toAdd++;
            }

            totalCorrect += correct; totalToAdd += toAdd; totalIgnorable += ignorable; totalRefused += refused;
            output.WriteLine($"  U+{chunk:X4}..U+{chunk + 0xFFF:X4} {correct,8} {toAdd,8} {ignorable,10} {refused,12}");
        }

        output.WriteLine("");
        output.WriteLine($"  BMP total: {totalCorrect} already correct, {totalToAdd} to add, " +
                         $"{totalIgnorable} ignorable-but-unhandled, {totalRefused} ACE would not store");
    }

    // Sweeps every block against General v0 and classifies what ACE stores, so the base table can be filled
    // in from measurement rather than a character at a time. Entries are grouped by the mechanism each needs.
    [Fact]
    public void Generate_general_coverage()
    {
        foreach ((string name, int first, int last) in Blocks)
        {
            string[] characters = Range(first, last);
            Dictionary<string, string> ace = AceKeys(TestDatabases.NorthwindAccdb, "cov", characters);

            var own = new List<string>();
            var atomic = new List<string>();
            var expansion = new List<string>();
            var marks = new SortedDictionary<char, byte>();
            int correct = 0, refused = 0, ignorable = 0;

            foreach (string text in characters)
            {
                if (!ace.TryGetValue(text, out string? key)) { refused++; continue; }
                if (Matches(text, key)) { correct++; continue; }
                if (key.Contains("010101")) { ignorable++; continue; }   // an inline (word-sort) record

                (byte[] primaries, byte secondary) = Decode(key);
                string nfd = text.Normalize(NormalizationForm.FormD);
                string bytes = string.Join(", ", primaries.Select(b => $"0x{b:X2}"));

                if (nfd.Length == 2 && char.IsLetter(nfd[0]) && primaries.Length == 1)
                    marks.TryAdd(nfd[1], secondary);
                else if (primaries.Length == 1 && secondary != 0x02 && LetterFor(primaries[0]) is char b)
                    atomic.Add($"['{Escape(text[0])}'] = ('{b}', 0x{secondary:X2}),");
                else if (secondary == 0x02 && primaries.Length > 1 && primaries.All(p => LetterFor(p) is not null))
                    expansion.Add($"['{Escape(text[0])}'] = \"{new string([.. primaries.Select(p => LetterFor(p)!.Value)])}\",");
                else
                    own.Add($"['{Escape(text[0])}'] = new([{bytes}], 0x{secondary:X2}),".PadRight(48) +
                            $"// {key}");
            }

            output.WriteLine("");
            output.WriteLine($"=== {name}: {correct} correct, {own.Count + atomic.Count + expansion.Count} to add, " +
                             $"{marks.Count} new marks, {ignorable} ignorable, {refused} refused by ACE");
            foreach ((char mark, byte weight) in marks) output.WriteLine($"    mark ['\\u{(int)mark:X4}'] = 0x{weight:X2},");
            foreach (string line in atomic.Take(30)) output.WriteLine($"    atomic {line}");
            foreach (string line in expansion.Take(30)) output.WriteLine($"    expand {line}");
            foreach (string line in own.Take(400)) output.WriteLine($"    {line}");
        }
    }

    // Emits each block as ONE compact string, in the format JetTextCollationBlocks parses: a start code
    // point, then one token per consecutive character —
    //     -           ignorable: ACE stores no weight at all for it (key 7F 01 00)
    //     ?           no data: ACE refused the value, so the encoder must refuse it too
    //     <hex>       primary bytes, default secondary
    //     <hex>,<ss>  primary bytes and a secondary of its own
    // 1500-odd entries as a dictionary literal would be unreadable and unreviewable; as runs of short tokens
    // it stays diffable, and it is regenerated from ACE rather than hand-maintained.
    [Fact]
    public void Generate_block_tables()
    {
        foreach ((string name, int first, int last) in Blocks)
        {
            string[] characters = Range(first, last);
            Dictionary<string, string> ace = AceKeys(TestDatabases.NorthwindAccdb, "blk", characters);

            var tokens = new List<string>();
            for (int c = first; c <= last; c++)
            {
                string text = ((char)c).ToString();
                if (char.IsControl((char)c) || char.IsSurrogate((char)c) ||
                    !ace.TryGetValue(text, out string? key)) { tokens.Add("?"); continue; }
                if (key == "7F0100") { tokens.Add("-"); continue; }
                if (key.Contains("010101")) { tokens.Add("?"); continue; }    // inline record; handled apart
                (byte[] primaries, byte secondary) = Decode(key);
                // A combining mark contributes a secondary weight and NO primary at all (key 7F 01 ss 00) —
                // distinct from being ignorable, which contributes nothing whatsoever.
                if (primaries.Length == 0 && secondary == 0x02) { tokens.Add("-"); continue; }
                string hex = Convert.ToHexString(primaries);
                tokens.Add(secondary == 0x02 ? hex : $"{hex},{secondary:X2}");
            }

            // Trim trailing no-data so a block does not carry a tail of question marks.
            while (tokens.Count > 0 && tokens[^1] == "?") tokens.RemoveAt(tokens.Count - 1);

            output.WriteLine("");
            output.WriteLine($"// {name} — U+{first:X4}..U+{first + tokens.Count - 1:X4}");
            output.WriteLine($"\"{first:X4}|\" +");
            for (int i = 0; i < tokens.Count; i += 16)
                output.WriteLine($"    \"{string.Join(" ", tokens.Skip(i).Take(16))} \" +");
        }
    }

    [Fact]
    public void Generate_diacritic_weights_missing_from_general()
    {
        string[] characters = Range(0x0020, 0x017F);
        Dictionary<string, string> ace = AceKeys(TestDatabases.NorthwindAccdb, "general", characters);
        var derived = new SortedDictionary<char, (byte Weight, string From)>();
        var unexplained = new List<string>();

        foreach ((string text, string key) in ace)
        {
            if (Matches(text, key)) continue;
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
    [InlineData("Ukrainian")]
    [InlineData("Macedonian")]
    [InlineData("French")]
    [InlineData("Thai")]
    public void Generate_tailoring_for(string fixture)
    {
        string source = TestDatabases.Data($"{fixture}.accdb");
        Assert.SkipWhen(!File.Exists(source), $"{fixture}.accdb is not present");

        // Every block, not just Latin: the question is how far a locale departs from General across the
        // whole range LibRed can encode, which is what decides whether the extended blocks can be shared.
        string[] characters = [.. Blocks.SelectMany(b => Range(b.First, b.Last))];
        Dictionary<string, string> locale = AceKeys(source, fixture, characters);
        Dictionary<string, string> general = AceKeys(TestDatabases.NorthwindAccdb, "general", characters);

        var letters = new List<string>();
        var accented = new List<string>();
        var extended = new List<string>();
        foreach (string text in characters)
        {
            if (!locale.TryGetValue(text, out string? key)) continue;
            if (general.TryGetValue(text, out string? baseline) && baseline == key) continue;
            // Only the uppercase form is needed; the lowercase folds onto it.
            if (text != text.ToUpperInvariant()) continue;

            (byte[] primaries, byte secondary) = Decode(key);
            string bytes = string.Join(", ", primaries.Select(b => $"0x{b:X2}"));
            string entry = secondary == 0x02
                ? $"(\"{text}\", [{bytes}])"
                : $"(\"{text}\", [{bytes}], 0x{secondary:X2})";

            // Beyond Latin Extended-A the General block tables already carry a weight, so only the entries
            // where the locale DIFFERS need adding — those are what stop a locale sharing those blocks.
            if (text[0] > (char)0x017F) extended.Add($"{entry}   // U+{(int)text[0]:X4}");
            else if (secondary == 0x02) letters.Add(entry);
            else accented.Add(entry);
        }

        output.WriteLine($"// --- {fixture} ---");
        output.WriteLine($"letters ({letters.Count}):");
        foreach (string line in Wrap(letters)) output.WriteLine($"    {line}");
        output.WriteLine($"accented ({accented.Count}):");
        foreach (string line in Wrap(accented)) output.WriteLine($"    {line}");
        output.WriteLine($"EXTENDED ({extended.Count}) — beyond Latin Extended-A, i.e. what a locale needs on " +
                         "top of the General block tables:");
        foreach (string line in extended) output.WriteLine($"    {line}");
    }

    /// <summary>The printable characters of a code-point range. Controls are skipped: ACE treats them as
    /// ignorables with an inline record rather than as letters, so they are a separate topic.</summary>
    private static string[] Range(int first, int last)
    {
        var characters = new List<string>();
        for (int c = first; c <= last; c++)
            if (!char.IsControl((char)c) && !char.IsSurrogate((char)c))
                characters.Add(((char)c).ToString());
        return [.. characters];
    }

    /// <summary>Whether LibRed already encodes <paramref name="text"/> exactly as ACE did.</summary>
    private static bool Matches(string text, string expected)
    {
        var column = new ColumnDef
        {
            Name = "K", Type = JetDataType.Text, Index = 0, Collation = Collation.GeneralLegacy,
        };
        try { return Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text])) == expected; }
        catch (NotSupportedException) { return false; }
    }

    /// <summary>The A–Z letter a primary weight belongs to, if any — used to recognise an atomic accent or
    /// an expansion without hard-coding the letter table twice.</summary>
    private static char? LetterFor(byte primary)
    {
        byte[] letters =
        [
            0x4A, 0x4C, 0x4D, 0x4F, 0x51, 0x53, 0x55, 0x57, 0x59, 0x5B, 0x5C, 0x5E, 0x60,
            0x62, 0x64, 0x66, 0x68, 0x69, 0x6B, 0x6D, 0x6F, 0x71, 0x73, 0x75, 0x76, 0x78,
        ];
        int index = Array.IndexOf(letters, primary);
        return index < 0 ? null : (char)('A' + index);
    }

    private static string Escape(char c) =>
        c is >= ' ' and <= '~' && c != '\'' && c != '\\' ? c.ToString() : $"\\u{(int)c:X4}";

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
