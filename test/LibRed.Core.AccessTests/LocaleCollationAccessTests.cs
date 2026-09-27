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
[Collection(AceCollection.Name)]
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
        // Version-1 orders. The same order under three LCIDs — each measures identically against General v1 —
        // and the first tailorings the v1 encoder carries, so they also cover the two-byte-primary path.
        "Croatian", "Bosnian", "Serbian",
        // Thai, whose contraction is built as a rule rather than a table of entries.
        "Thai",
        // The Chinese, Japanese and Korean orders — tables of ideograph weights, and Korean's script reordering.
        "ChinesePronunciation", "ChinesePronunciationLegacy", "ChineseStrokeCount", "ChineseStrokeCountLegacy",
        "ChineseTradBopomofo", "ChineseTradBopomofoLegacy", "ChineseTradStrokeCount", "ChineseTradStrokeCountLegacy",
        "Japanese", "JapaneseLegacy", "JapaneseRadicalStrokeCount", "Korean",
    ];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Encodes_the_same_index_keys_as_ace(string fixture)
    {
        string source = TestDatabases.Data($"{fixture}.accdb");
        Assert.SkipWhen(!File.Exists(source), $"{fixture}.accdb is not present");

        // Both versions are asserted over the whole measured range. The extended blocks used to be skipped
        // for version-1 fixtures, on the grounds that the v1 encoder was "a separate table with its own
        // coverage" — but v1 now reproduces ACE across the entire BMP and shares the kana section, so the
        // narrowing only hid ground. It was worth removing: the one thing these three locales got wrong was
        // a case asymmetry INSIDE the narrow range, which is a poor argument for testing less.
        string[] samples = Samples(extendedBlocks: true);
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
            (0x0E01, 0x0E5B),                     // Thai: consonants, vowels, tone marks and digits
            (0x1E00, 0x1EFF), (0x2000, 0x206F), (0x20A0, 0x20BF), (0x2100, 0x218F), (0xFF01, 0xFF65),
            (0x3040, 0x30FF), (0xFF66, 0xFF9F),   // kana: hiragana, katakana, halfwidth katakana
        ];
        if (extendedBlocks)
            foreach ((int first, int last) in blocks)
                for (int c = first; c <= last; c++)
                    if (!char.IsControl((char)c) && !char.IsSurrogate((char)c))
                        samples.Add(((char)c).ToString());

        // The CJK blocks, where the CJK orders put their weights: the symbols, Bopomofo, jamo and enclosed forms
        // whole, and a stride through the large ones — every 7th ideograph, every 11th syllable — which crosses
        // every page of their tables while keeping the suite to seconds. The whole BMP is what the generator
        // measures; this keeps what it wrote honest.
        if (extendedBlocks)
        {
            (int First, int Last, int Stride)[] cjk =
            [
                (0x1100, 0x11FF, 1), (0x3000, 0x303F, 1), (0x3100, 0x312F, 1), (0x3130, 0x318F, 1),
                (0x3190, 0x31BF, 1), (0x3200, 0x33FF, 1), (0x3400, 0x4DBF, 7), (0x4E00, 0x9FFF, 7),
                (0xAC00, 0xD7A3, 11), (0xF900, 0xFAFF, 1),
            ];
            foreach ((int first, int last, int stride) in cjk)
                for (int c = first; c <= last; c += stride)
                    samples.Add(((char)c).ToString());
        }

        samples.AddRange([
            "apple", "Apple", "APPLE", "cafe", "café", "Ångström", "O'Brien", "Anne-Marie", "co-op", "coop",
            "Łódź", "Kraków", "İstanbul", "Isparta", "ırmak", "Ğğ", "München", "Grüße", "Bär", "Baer",
            "România", "Timișoara", "Iași", "señor", "senor", "mañana",
            // Digraphs, the strings that must NOT contract, and the doubled forms.
            "ch", "cch", "chh", "ll", "lll", "llll", "cs", "dz", "dzs", "gy", "ly", "ny", "sz", "ty", "zs",
            "ccs", "ddz", "ggy", "lly", "nny", "ssz", "tty", "zzs", "gyy", "hc", "dzz",
            "lj", "nj", "dž", "ddž", "llj", "nnj", "aa", "aaa", "aab", "baa", "Aa", "AA",
            // Thai: each leading vowel before a consonant, the reverse order (which must NOT contract), the
            // vowel with nothing to attach to, and words where the contraction meets tone marks.
            "เก", "แก", "โก", "ใก", "ไก", "กเ", "กแ", "กโ", "กใ", "กไ", "เ", "เเ", "เ ", " เ",
            "เกา", "เก้า", "ไก่", "ไทย", "แดง", "โกรธ", "ใหม่", "ประเทศไทย", "ภาษาไทย", "สวัสดี", "เรียน",
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
            // Iteration marks: a copy of what came before with the mark's own secondary — after a kana (joining
            // the kana section as a repeat), a voiced or small one, an ideograph, a Latin letter, an accented one,
            // an inline record, a combining accent; chained, after the long vowel mark, before it, and with
            // nothing to copy (at the start, after an expansion, after a mark that found nothing).
            "かゝ", "かゞ", "がゝ", "がゞ", "ぱゞ", "ゃゝ", "カヽ", "カヾ", "かヽ", "かゝゝ", "かーゝ", "かゝー",
            "ｶヽﾞ", "ｶﾞゝ", "か〱", "か〲", "か々", "か々ゝ", "かー々", "ヴゞ", "㋐ゝ",
            "人々", "人々々", "人々ー", "人ゝ", "人ー", "人ｰ", "人〱", "人〻", "a々", "é々", "é々", "aー",
            "'々", "人-々", "½々", "\t々", "ß々", "æ々", "々", "々々", "ゝゝ", "ー々", "々人", "ꀀꀕ", "ꀀꀕꀕ",
            "々が", "ーが", "ゝが", "ゞé", "〱é", "ｰが", "ß々é", "々々é", "ー々é",
            // A Han character counts twice in version 1's inline section, a one-byte primary once.
            "人-", "人人-", "人-a", "人'", "ະ-", "aະ-", "가-", "ᄀ-",
            // Words through the CJK tables, beside Latin and kana: a tailored weight next to General's, and the
            // context rules running over tailored weights (Korean moves the kana a long vowel mark copies).
            "中文", "中国", "中國", "漢字", "日本語", "時々", "한글", "대한민국", "韓國", "ㄅㄆㄇ", "\\",
            "―", "a中", "中a", "a한", "한a", "カー", "カタカナ", "中-文", "한-글", "가",
            "東京タワー", "ソウル", "北京",
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
