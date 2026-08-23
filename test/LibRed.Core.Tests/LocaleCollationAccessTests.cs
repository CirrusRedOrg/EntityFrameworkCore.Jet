using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Conformance: for every locale sort order LibRed claims to encode, its index keys must be byte-identical to
// the ones ACE writes in a database carrying that order.
//
// This is the check that matters for locales, because the failure mode is silent. A wrong key does not throw
// and does not corrupt anything visibly — ACE simply writes its own keys into the same index and the two
// disagree, so a seek misses rows. The only way to know a tailoring is right is to have ACE encode the same
// values and compare bytes.
//
// The sample set is deliberately much wider than the tailoring: the whole ASCII range, every Latin-1 and
// Latin Extended-A letter, and words. A tailoring is only trustworthy if it is also correct for the
// characters it does *not* mention.
public class LocaleCollationAccessTests(ITestOutputHelper output)
{
    public static TheoryData<string> Fixtures() =>
    [
        // General itself, so a base-table gap is attributed to the base table rather than to a tailoring.
        "Northwind",
        "SpanishModern", "GermanPhoneBook", "Polish", "RomanianLegacy", "Turkish", "GeorgianModern", "Indic",
        // Contraction locales.
        "SpanishTraditional", "Czech", "CroatianLegacy", "NorwegianDanish", "Hungarian",
        // Single-character locales.
        "Estonian", "Icelandic", "Latvian", "Lithuanian", "Slovenian", "SwedishFinnish",
        "Slovak", "Vietnamese", "HungarianTechnical",
        // Cyrillic orders, encodable once General v0 carried the Cyrillic block.
        "Ukrainian", "Macedonian",
    ];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Encodes_the_same_index_keys_as_ace(string fixture)
    {
        string source = TestDatabases.Data($"{fixture}.accdb");
        Assert.SkipWhen(!File.Exists(source), $"{fixture}.accdb is not present");

        // The extended blocks are measured for version-0 orders only; the version-1 encoder is a separate
        // table with its own coverage, so a v1 fixture is asserted over the range it was verified in.
        byte version;
        using (var probe = JetDatabase.Open(source)) version = probe.DefaultCollationVersion;
        string[] samples = Samples(extendedBlocks: version != Collation.GeneralVersion);
        string path = TemporaryDatabase.CopyPath(source, $"conformance-{fixture.ToLowerInvariant()}-");
        try
        {
            Collation collation;
            using (var db = JetDatabase.Open(path)) collation = db.Collation;
            Assert.True(collation.IsIndexKeyEncodable,
                $"{fixture} reports collation {collation}, which LibRed does not claim to encode.");

            Dictionary<string, string> ace = AceKeys(path, samples);
            Assert.NotEmpty(ace);

            var mismatches = new List<string>();
            var unencodable = new List<string>();
            foreach (string sample in samples)
            {
                if (!ace.TryGetValue(sample, out string? expected)) continue;   // ACE refused the value
                string actual;
                try { actual = Convert.ToHexString(Encode(sample, collation)); }
                catch (NotSupportedException) { unencodable.Add(Describe(sample)); continue; }
                if (actual != expected)
                    mismatches.Add($"{Describe(sample),-16} ACE {expected,-28} LibRed {actual}");
            }

            output.WriteLine($"{fixture}: collation {collation}");
            output.WriteLine($"  {ace.Count} values encoded by ACE, {mismatches.Count} mismatched, " +
                             $"{unencodable.Count} that LibRed does not encode at all");
            foreach (string line in mismatches.Take(40)) output.WriteLine($"     {line}");
            if (unencodable.Count > 0)
                output.WriteLine($"  not encodable: {string.Join(" ", unencodable.Take(40))}");

            Assert.Empty(mismatches);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Encodes through the real index-key path, so the test covers the gate and the routing as well
    /// as the weight table.</summary>
    private static byte[] Encode(string value, Collation collation)
    {
        var column = new ColumnDef { Name = "K", Type = JetDataType.Text, Index = 0, Collation = collation };
        return IndexKeyEncoder.Encode([(column, true)], [value]);
    }

    /// <summary>Printable ASCII, all of Latin-1 and Latin Extended-A, and a few words — so a tailoring is
    /// tested well beyond the handful of characters it actually overrides.</summary>
    private static string[] Samples(bool extendedBlocks)
    {
        var samples = new List<string>();
        for (char c = ' '; c <= '~'; c++) samples.Add(c.ToString());
        for (char c = ' '; c <= 'ſ'; c++) samples.Add(c.ToString());
        // Every further block JetTextCollationBlocks covers — Greek, Cyrillic, Hebrew, Arabic, the Latin
        // extensions, punctuation, currency and the fullwidth forms — so the whole measured range stays
        // guarded rather than only the range a tailoring happens to mention.
        (int First, int Last)[] blocks =
        [
            (0x0180, 0x024F), (0x02B0, 0x02FF), (0x0370, 0x052F), (0x0590, 0x06FF),
            (0x1E00, 0x1EFF), (0x2000, 0x206F), (0x20A0, 0x20BF), (0x2100, 0x218F), (0xFF01, 0xFF65),
            (0x3040, 0x30FF), (0xFF66, 0xFF9F),   // kana: hiragana, katakana, halfwidth katakana
        ];
        if (extendedBlocks)
            foreach ((int first, int last) in blocks)
                for (int c = first; c <= last; c++)
                    if (!char.IsControl((char)c) && !char.IsSurrogate((char)c))
                        samples.Add(((char)c).ToString());

        samples.AddRange([
            "apple", "Apple", "APPLE", "cafe", "café", "Ångström", "O'Brien", "Anne-Marie", "co-op", "coop",
            "Łódź", "Kraków", "İstanbul", "Isparta", "ırmak", "Ğğ", "München", "Grüße", "Bär", "Baer",
            "România", "Timișoara", "Iași", "señor", "senor", "mañana",
            // Digraphs, the strings that must NOT contract, and the doubled forms.
            "ch", "cch", "chh", "ll", "lll", "llll", "cs", "dz", "dzs", "gy", "ly", "ny", "sz", "ty", "zs",
            "ccs", "ddz", "ggy", "lly", "nny", "ssz", "tty", "zzs", "gyy", "hc", "dzz",
            "lj", "nj", "dž", "ddž", "llj", "nnj", "aa", "aaa", "aab", "baa", "Aa", "AA",
            // An ignorable AFTER a two-byte primary: the inline position counts weights, not bytes, and only
            // a non-Latin or symbol character ahead of it can tell those two rules apart.
            "£-", "©-", "½-", "£A-", "A£-", "Ω-", "б-", "£'", "Ω'A", "€-B",
            // Kana: voicing, small forms and their packing, mixed scripts, and a kana beside an ignorable —
            // which changes the inline section's introducer.
            "あい", "ぁ", "あぁ", "ぁぁ", "ああぁ", "ぁああ", "あいう",
            "かが", "ぱば", "アイ", "ｱｲ", "あア", "あA", "Aあ", "あé", "あ-", "-あ", "あ'",
            "ニホンゴ", "にほんご", "ﾆﾎﾝｺﾞ", "ちょっと", "キャッシュ",
            // The prolonged mark: alone, with nothing to lengthen, and after every kind of kana.
            "ー", "ーあ", "あー", "あいー", "あーい", "ああー", "あああー", "あーー", "あーあー",
            "ぁー", "がー", "ｱｰ", "アー", "コーヒー", "ｺｰﾋｰ", "サーバー",
            "chico", "llama", "coche", "calle", "chata", "hodina", "cukr",
            "ljubav", "njegov", "džem", "meggy", "asszony", "nagy", "cukor", "csak",
        ]);
        return [.. samples];
    }

    /// <summary>Has ACE build and populate an indexed text column in the database, then reads the stored
    /// index keys back with LibRed, mapped by the value that produced them.</summary>
    private static Dictionary<string, string> AceKeys(string path, string[] samples)
    {
        using (var connection = AceTestDatabase.Open(path))
        {
            Exec(connection, "CREATE TABLE CollConf (K TEXT(100), V LONG)");
            Exec(connection, "CREATE INDEX IX_CollConf ON CollConf (K)");
            for (int i = 0; i < samples.Length; i++)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO CollConf (K, V) VALUES (?, ?)";
                insert.Parameters.AddWithValue("k", samples[i]);
                insert.Parameters.AddWithValue("v", i);
                try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
            }
        }

        using var db = JetDatabase.Open(path);
        var table = db.OpenTable("CollConf");
        IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_CollConf");
        ColumnDef keyColumn = table.Definition.FindColumn("K")!;
        var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

        var keys = new Dictionary<string, string>();
        foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            if (rows.TryGetValue(rowId, out object?[]? values) && values[keyColumn.Index] is string text)
                keys[text] = Convert.ToHexString(stored);
        return keys;
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
