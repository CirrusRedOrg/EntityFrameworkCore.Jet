using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: exactly how a contraction is encoded, before implementing one.
//
// A contraction is several characters weighing as one letter. Ten sort orders need it and nothing else, so
// it is the single primitive that unlocks the most. But the summary diff left one thing unreconciled:
// Hungarian "ny" is a clean single primary (63 06, no trailing y) while "gy" looked like 56 03 76 - a
// two-byte primary AND a trailing y. Either the digraph set is not uniform, or that reading was wrong.
//
// So: for each order, the component letters on their own, every digraph, the doubled forms (Hungarian writes
// a doubled digraph by doubling only its first letter - "ggy" is "gy"+"gy", not "g"+"gy"), and real words.
// Printed in full, no capping, so the structure is visible rather than inferred.
public class ContractionProbeTest(ITestOutputHelper output)
{
    private static readonly (string Fixture, string[] Samples)[] Cases =
    [
        ("Hungarian", [
            "c", "s", "z", "d", "g", "y", "n", "t", "l",
            "cs", "dz", "dzs", "gy", "ly", "ny", "sz", "ty", "zs",
            "ccs", "ddz", "ggy", "lly", "nny", "ssz", "tty", "zzs",
            "cukor", "csak", "gyar", "nagy", "meggy", "asszony", "gy", "gz", "gyy",
        ]),
        ("Czech", [
            "c", "h", "s", "z", "r", "ch", "cch", "chch", "hc",
            "cukr", "chata", "hodina", "chch",
        ]),
        ("CroatianLegacy", [
            "d", "z", "l", "n", "j", "dz", "dž", "lj", "nj", "ddž", "llj", "nnj", "dzz",
            "ljubav", "njegov", "džem",
        ]),
        ("SpanishTraditional", [
            "c", "h", "l", "ch", "ll", "cch", "lll", "chh", "llll",
            "chico", "llama", "coche", "calle",
        ]),
        // Vietnamese turned out to be a digraph order too — "Ångström" showed "ng" and "tr" each weighing as
        // one letter, which a single-character sweep could never have revealed.
        ("Vietnamese", [
            "c", "g", "h", "i", "k", "n", "p", "q", "t", "u", "r",
            "ch", "gh", "gi", "kh", "ng", "ngh", "nh", "ph", "qu", "th", "tr",
            "nng", "ngg", "ngstr", "nghi", "nghe", "nga", "nhe", "quy", "tre", "thu",
        ]),
        ("NorwegianDanish", [
            "a", "aa", "aaa", "å", "aab", "ab", "baa", "Aa", "AA",
            // Where does the secondary land relative to a TWO-BYTE primary? "å" and "æ" are two-byte
            // primaries with a default secondary; "ö" is a two-byte primary carrying 0x13. Vary how many of
            // each precede the accented one, and the index the accent lands on tells us what the section
            // counts: characters, primary weights, or primary bytes.
            "ö", "bö", "öb", "bbö", "aö", "åö", "ååö", "æö", "aaö", "bäb", "Ångström", "ånö", "nåö",
        ]),
    ];

    // PROBE: the sixteen characters General v0 refuses — the DŽ/LJ/NJ/DZ ligatures and AE-with-accent.
    //
    // Each is known to be more than one primary weight, which is why a single table entry cannot express it.
    // What is not established is the shape: how ACE splits them, and where an accent lands. Measured against
    // the components on their own, and against strings that put an accented letter AFTER the ligature, since
    // the secondary section's length is what reveals how many weights were emitted.
    [Fact]
    public void Probe_how_the_refused_ligatures_encode()
    {
        int[] ligatures =
        [
            0x01C4, 0x01C5, 0x01C6,   // DŽ Dž dž
            0x01C7, 0x01C8, 0x01C9,   // LJ Lj lj
            0x01CA, 0x01CB, 0x01CC,   // NJ Nj nj
            0x01F1, 0x01F2, 0x01F3,   // DZ Dz dz
            0x01E2, 0x01E3,           // Ǣ ǣ  (AE with macron)
            0x01FC, 0x01FD,           // Ǽ ǽ  (AE with acute)
        ];

        var samples = new List<string>();
        foreach (int c in ligatures) samples.Add(((char)c).ToString());
        // The components, so the split can be read off rather than guessed.
        samples.AddRange(["D", "Z", "Ž", "L", "J", "N", "A", "E", "Æ", "DZ", "DŽ", "LJ", "NJ", "AE"]);
        // An accented letter AFTER the ligature: the secondary section then runs to that letter, and its
        // length says how many weights the ligature contributed.
        foreach (int c in ligatures) samples.Add((char)c + "é");
        samples.AddRange(["DŽé", "AEé", "DZé", "LJé"]);

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ligature-");
        try
        {
            Dictionary<string, string> keys = AceKeys(path, [.. samples]);
            foreach (string sample in samples)
                output.WriteLine($"   {Describe(sample),-22} {keys.GetValueOrDefault(sample) ?? "(refused)"}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Describe(string s) =>
        s.All(c => c is >= ' ' and <= '~') ? $"\"{s}\"" : string.Concat(s.Select(c => $"U+{(int)c:X4}"));

    // PROBE: the inline code for each remaining word-sort ignorable.
    //
    // An ignorable adds no primary weight; it appends a record to the trailing inline section instead —
    // 80 <pos> 06 <code>, with the section introduced once by 01 01 01. LibRed knows three of them
    // (apostrophe 0x80, hyphen 0x82, soft hyphen 0x83); ACE treats fourteen more the same way. Measured
    // alone, then inside a word, so the position arithmetic is confirmed rather than assumed.
    // PROBE: the inline records that are NOT the simple 7F 01 01 01 01 80 07 06 <code> 00 shape.
    //
    // Generating the v0 resource found 213 characters whose key carries an inline word-sort record, but only
    // 40 in the shape a lone ignorable produces. The other 173 are something else, and guessing what would
    // be exactly the way to plant a wrong key.
    // PROBE: how kana encode.
    //
    // The one mechanism General v0 still refuses. A kana key is shaped unlike anything else in the format —
    // a doubled start flag, then a section introduced by 01 01 rather than the 01 01 01 an inline record
    // uses:
    //
    //     U+3042 あ    7F 7F 02 01 01 01 FF 02 80 FF 80 00
    //     U+3041 ぁ    7F 7F 02 01 01 01 A0 FF 02 80 FF 80 00
    //
    // Hiragana, katakana and halfwidth katakana share a key, so what separates them must live in that
    // trailing section. This measures the axes one at a time — vowel, consonant row, small form, voicing,
    // script, width, the prolonged mark — and then in pairs, since only a two-character string shows how the
    // section is positioned.
    [Fact]
    public void Probe_how_kana_encode()
    {
        (string Label, int[] CodePoints)[] groups =
        [
            ("hiragana vowels",   [0x3042, 0x3044, 0x3046, 0x3048, 0x304A]),
            ("small vowels",      [0x3041, 0x3043, 0x3045, 0x3047, 0x3049]),
            ("ka row",            [0x304B, 0x304D, 0x304F, 0x3051, 0x3053]),
            ("ga row (voiced)",   [0x304C, 0x304E, 0x3050, 0x3052, 0x3054]),
            ("ha/ba/pa",          [0x306F, 0x3070, 0x3071]),
            ("katakana",          [0x30A2, 0x30A4, 0x30AB, 0x30AC]),
            ("halfwidth",         [0xFF71, 0xFF72, 0xFF76]),
            ("marks",             [0x3063, 0x3083, 0x3093, 0x30FC, 0xFF70, 0x309B, 0x309C]),
        ];

        var samples = new List<string>();
        foreach ((_, int[] codePoints) in groups)
            foreach (int c in codePoints) samples.Add(((char)c).ToString());
        // Pairs: kana with kana, kana with Latin, and the same sound in different scripts.
        samples.AddRange([
            "あい", "ああ", "あア", "アあ", "あｱ",
            "あA", "Aあ", "あぁ", "かが", "あé",
        ]);

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "kana-");
        try
        {
            Dictionary<string, string> keys = AceKeys(path, [.. samples]);
            foreach ((string label, int[] codePoints) in groups)
            {
                output.WriteLine("");
                output.WriteLine($"  {label}:");
                foreach (int c in codePoints)
                {
                    string text = ((char)c).ToString();
                    output.WriteLine($"     U+{c:X4}  {keys.GetValueOrDefault(text) ?? "(refused)"}");
                }
            }
            output.WriteLine("");
            output.WriteLine("  pairs:");
            foreach (string sample in samples.Where(s => s.Length > 1))
                output.WriteLine($"     {Describe(sample),-30} {keys.GetValueOrDefault(sample) ?? "(refused)"}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // PROBE: the two things Probe_how_kana_encode left open.
    //
    // The shape is now known: 7F <primaries> 01 <secondaries> 01 01 <kana section> 00, where a kana takes the
    // two-byte primary 7F <sound> and voicing is an ordinary secondary (03 dakuten, 04 handakuten). What is
    // not known is (a) how a small kana's record is positioned — A0 alone, B8 when one kana precedes it, a
    // step of 0x18 that could be per character or 0x0C per primary byte — and (b) whether the kana section
    // and the word-sort inline section can coexist, and in what order.
    [Fact]
    public void Probe_kana_positions_and_sections()
    {
        string[] samples =
        [
            "ぁ",                       // ぁ alone                        → A0
            "あぁ",                 // あぁ, one kana ahead (2 primary bytes)
            "Aぁ",                      // Aぁ, one LATIN letter ahead (1 primary byte) — the discriminator
            "ぁぁ",                 // ぁぁ, two records
            "ああぁ",           // ああぁ, two kana ahead
            "ぁああ",           // ぁああ, record first
            "あいう",           // あいう — is the trailer constant for three kana?
            "あ-", "-あ", "あ'",// kana with a word-sort ignorable, both orders
            "ーあ", "あー", // the prolonged mark, which alone is NOT kana
        ];

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "kanapos-");
        try
        {
            Dictionary<string, string> keys = AceKeys(path, samples);
            foreach (string sample in samples)
                output.WriteLine($"   {Describe(sample),-34} {keys.GetValueOrDefault(sample) ?? "(refused)"}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // PROBE: does an inline record's position count primary WEIGHTS or primary BYTES?
    //
    // The spec says bytes, and LibRed implements that with primaries.Count. Every case tested so far had
    // one-byte weights, where the two agree — but "あ-" puts the hyphen at 0x0B = 0x07 + 4x1, and あ is a
    // TWO-byte primary. If that generalises to any two-byte primary then the rule is weights, and LibRed is
    // wrong for symbols, Greek, Cyrillic and everything else on the 0x79 page.
    [Fact]
    public void Probe_whether_inline_position_counts_weights_or_bytes()
    {
        // £ © ½ are two-byte symbol primaries; ß expands to two ONE-byte weights, so it is the control that
        // cannot tell the two rules apart.
        string[] samples =
        [
            "-", "A-", "AB-",
            "£-", "©-", "½-", "£A-", "A£-",
            "ß-", "Aß-",
            "Ω-", "б-",
        ];

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "inlinepos-");
        try
        {
            Dictionary<string, string> keys = AceKeys(path, samples);
            foreach (string sample in samples)
                output.WriteLine($"   {Describe(sample),-24} {keys.GetValueOrDefault(sample) ?? "(refused)"}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // PROBE: how the small-kana record packs.
    //
    // It is not one record per small kana — ぁぁ produces a single byte A8, not two records. Observed so
    // far: {0}=A0, {1}=B8, {0,1}=A8, {2}=BE, and all-normal emits no byte at all. Sweeping every
    // small/normal combination up to four kana should show the packing.
    [Fact]
    public void Probe_small_kana_packing()
    {
        var samples = new List<string>();
        for (int length = 1; length <= 4; length++)
            for (int bits = 0; bits < 1 << length; bits++)
            {
                var text = new char[length];
                for (int i = 0; i < length; i++)
                    text[i] = (char)((bits & (1 << i)) != 0 ? 0x3041 : 0x3042);   // ぁ small : あ normal
                samples.Add(new string(text));
            }

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "kanapack-");
        try
        {
            Dictionary<string, string> keys = AceKeys(path, [.. samples]);
            foreach (string sample in samples)
            {
                string flags = string.Concat(sample.Select(c => c == (char)0x3041 ? 's' : 'n'));
                string key = keys.GetValueOrDefault(sample) ?? "(refused)";
                // Print just the section between the 01 01 marker and the constant FF 02 80 FF 80 trailer.
                int marker = key.IndexOf("010101", StringComparison.Ordinal);
                string section = marker < 0 ? "?" : key[(marker + 6)..].Replace("FF0280FF8000", "");
                output.WriteLine($"   {flags,-6} section {section,-8} {key}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // PROBE: how the prolonged sound mark records its position.
    //
    // ー takes the PREVIOUS kana's primary — exactly what the character means, lengthen the preceding vowel —
    // and inserts a record into the kana section: あー is 7F 7F02 7F02 … FF 9C 02 80 FF 80 00, where a plain
    // あ has FF 02 80 FF 80. One sample cannot say whether 9C encodes position, so vary it.
    [Fact]
    public void Probe_prolonged_mark_records()
    {
        string[] samples =
        [
            "ー", "ーあ",                       // alone, and with nothing to lengthen
            "あー", "あいー", "あーい",   // one mark at each position
            "ああー", "あああー",
            "あーー", "あーあー",         // two marks
            "ぁー", "がー",                   // after a small kana, and after a voiced one
            "ｱｰ", "アー",                     // halfwidth and katakana
        ];

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "prolonged-");
        try
        {
            Dictionary<string, string> keys = AceKeys(path, samples);
            foreach (string sample in samples)
                output.WriteLine($"   {Describe(sample),-34} {keys.GetValueOrDefault(sample) ?? "(refused)"}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // PROBE: the presentation forms, v1's last cluster of size.
    //
    // 276 of them differ, and one guess has already been wrong — that ACE prefers a direct NLS weight over
    // an expansion, which changed nothing. Three examples were not enough to see the rule, so this dumps
    // many, with the components alongside so the relationship is visible rather than inferred.
    [Fact]
    public void Probe_presentation_form_keys()
    {
        var samples = new List<string>();
        for (int c = 0xFB00; c <= 0xFB4F; c++) samples.Add(((char)c).ToString());   // alphabetic forms
        // Arabic Presentation Forms-A, the contextual isolated/initial/medial/final variants. This is where
        // the bulk of the remaining differences live; the first pass sampled either side of it and missed it.
        for (int c = 0xFB50; c <= 0xFB90; c++) samples.Add(((char)c).ToString());
        for (int c = 0xFD50; c <= 0xFD60; c++) samples.Add(((char)c).ToString());
        for (int c = 0xFE70; c <= 0xFEFC; c++) samples.Add(((char)c).ToString());   // Arabic forms-B
        // The Hebrew letters and points the FB1x forms are built from, so a composed key can be read against
        // its parts rather than guessed at.
        samples.AddRange(["ו", "י", "א", "ִ", "ַ", "ּ", "ְ",
                          "ا", "ب", "َ", "ُ", "ِ", "ّ"]);

        string path = TemporaryDatabase.CreatePath("presentation-v1-");
        DatabaseCreator.CreateEmpty(path, collation: Collation.General);
        try
        {
            Dictionary<string, string> keys = AceKeys(path, [.. samples]);
            var column = new ColumnDef
            {
                Name = "K", Type = JetDataType.Text, Index = 0, Collation = Collation.General,
            };
            foreach (string sample in samples)
            {
                if (!keys.TryGetValue(sample, out string? ace)) continue;
                string ours;
                try { ours = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [sample])); }
                catch (NotSupportedException) { ours = "(refused)"; }
                if (ours == ace) continue;
                output.WriteLine($"   {Describe(sample),-10} ACE {ace,-26} ours {ours}");
            }
            output.WriteLine("");
            output.WriteLine("   components:");
            foreach (string sample in samples.Where(s => s[0] is >= (char)0x05B0 and <= (char)0x0651))
                output.WriteLine($"   {Describe(sample),-10} ACE {keys.GetValueOrDefault(sample)}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Probe_unusual_inline_records()
    {
        var samples = new List<string>();
        foreach ((int first, int last) in new[] { (0x2000, 0x2FFF), (0x3000, 0x3FFF), (0xFB00, 0xFFFF) })
            for (int c = first; c <= last; c++)
                if (!char.IsControl((char)c) && !char.IsSurrogate((char)c))
                    samples.Add(((char)c).ToString());

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "inline-");
        try
        {
            Dictionary<string, string> keys = AceKeys(path, [.. samples]);
            var odd = keys
                .Where(k => k.Value.Contains("010101") && k.Value != "7F0100")
                .Where(k => !(k.Value.Length == 20 && k.Value[10..16] == "800706"))
                .OrderBy(k => k.Key)
                .ToList();
            output.WriteLine($"{odd.Count} inline records of an unexpected shape:");
            foreach ((string text, string key) in odd.Take(30))
                output.WriteLine($"   {Describe(text),-14} {key}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Probe_word_sort_ignorable_codes()
    {
        int[] ignorables =
        [
            0x0027, 0x002D, 0x00AD,                                  // the three LibRed already knows
            0x064B, 0x064C, 0x064D, 0x064E, 0x064F, 0x0650, 0x0652,  // Arabic harakat
            0x2010, 0x2011, 0x2012, 0x2013, 0x2014, 0x2015,          // hyphens and dashes
            0x2027, 0x2043,                                          // hyphenation point, hyphen bullet
            0xFF07, 0xFF0D,                                          // fullwidth apostrophe and hyphen
        ];

        var samples = new List<string>();
        foreach (int c in ignorables) samples.Add(((char)c).ToString());
        foreach (int c in ignorables) samples.Add("AB" + (char)c + "CD");   // position = 0x07 + 4x2 = 0x0F
        samples.Add("AB");

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ignorable-");
        try
        {
            Dictionary<string, string> keys = AceKeys(path, [.. samples]);
            foreach (string sample in samples)
                output.WriteLine($"   {Describe(sample),-26} {keys.GetValueOrDefault(sample) ?? "(refused)"}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Probe_how_contractions_encode()
    {
        foreach ((string fixture, string[] samples) in Cases)
        {
            string source = TestDatabases.Data($"{fixture}.accdb");
            if (!File.Exists(source)) { output.WriteLine($"{fixture}: missing"); continue; }

            string path = TemporaryDatabase.CopyPath(source, $"contraction-{fixture.ToLowerInvariant()}-");
            try
            {
                Dictionary<string, string> keys = AceKeys(path, samples);
                using var db = JetDatabase.Open(path);
                output.WriteLine("");
                output.WriteLine($"{fixture} — {db.Collation.Order} v{db.Collation.Version}:");
                foreach (string sample in samples.Distinct())
                    output.WriteLine($"   {sample,-10} {keys.GetValueOrDefault(sample) ?? "(refused)"}");
            }
            finally { TemporaryDatabase.Delete(path); }
        }
    }

    private static Dictionary<string, string> AceKeys(string path, string[] samples)
    {
        using (var connection = AceTestDatabase.Open(path))
        {
            Exec(connection, "CREATE TABLE Contr (K TEXT(60), V LONG)");
            Exec(connection, "CREATE INDEX IX_Contr ON Contr (K)");
            int i = 0;
            foreach (string sample in samples.Distinct())
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO Contr (K, V) VALUES (?, ?)";
                insert.Parameters.AddWithValue("k", sample);
                insert.Parameters.AddWithValue("v", i++);
                try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
            }
        }

        using var db = JetDatabase.Open(path);
        var table = db.OpenTable("Contr");
        IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Contr");
        ColumnDef keyColumn = table.Definition.FindColumn("K")!;
        var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

        var keys = new Dictionary<string, string>();
        foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            if (rows.TryGetValue(rowId, out object?[]? values) && values[keyColumn.Index] is string text)
                keys[text] = Convert.ToHexString(stored);
        return keys;
    }

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
