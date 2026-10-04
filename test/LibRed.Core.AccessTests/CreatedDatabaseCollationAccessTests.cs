using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// LibRed synthesises a new .accdb page by page rather than copying a packaged empty file, so the collating
// order is a parameter of creation rather than a property of a template. This asserts that for EVERY order
// LibRed claims to encode — not just the two General ones it was demonstrated with.
//
// The claim is worth a test rather than an inference because creation and collation are entangled: the
// system-table indexes are built on the way, with that order's keys, so a database cannot be created in an
// order whose keys cannot be encoded. That circularity is why measuring French needed DAO to author the file
// first, and it means "creates the file" and "gets the order right" are not separable properties.
//
// The bar is not that the file opens. It is that ACE will CREATE AN INDEX in it and write keys that match
// LibRed's own — two engines agreeing on a shared index, which is the only check that catches a wrong key,
// since a disagreement does not error, it just makes seeks miss rows.
[Collection(AceCollection.Name)]
public class CreatedDatabaseCollationAccessTests(ITestOutputHelper output)
{
    // These exact collation values were read from the former 40 locale fixtures.
    // Keep broad character coverage here without multiplying it across all creation cases.
    private static readonly HashSet<Collation> FullSampleCollations =
    [
        new(CollatingOrder.General, 0, 0), // Northwind
        new(CollatingOrder.SpanishModern, 0, 0), // SpanishModern
        new(CollatingOrder.German, 0, 1), // GermanPhoneBook
        new(CollatingOrder.Polish, 0, 0), // Polish
        new(CollatingOrder.Romanian, 0, 0), // RomanianLegacy
        new(CollatingOrder.Turkish, 0, 0), // Turkish
        new(CollatingOrder.Georgian, 0, 1), // GeorgianModern
        new(CollatingOrder.Indic, 1, 0), // Indic
        new(CollatingOrder.Spanish, 0, 0), // SpanishTraditional
        new(CollatingOrder.Czech, 0, 0), // Czech
        new(CollatingOrder.Croatian, 0, 0), // CroatianLegacy
        new(CollatingOrder.Norwegian, 0, 0), // NorwegianDanish
        new(CollatingOrder.Hungarian, 0, 0), // Hungarian
        new(CollatingOrder.Estonian, 0, 0), // Estonian
        new(CollatingOrder.Icelandic, 0, 0), // Icelandic
        new(CollatingOrder.Latvian, 0, 0), // Latvian
        new(CollatingOrder.Lithuanian, 0, 0), // Lithuanian
        new(CollatingOrder.Slovenian, 0, 0), // Slovenian
        new(CollatingOrder.SwedishFinnish, 0, 0), // SwedishFinnish
        new(CollatingOrder.Slovak, 0, 0), // Slovak
        new(CollatingOrder.Vietnamese, 0, 0), // Vietnamese
        new(CollatingOrder.Hungarian, 0, 1), // HungarianTechnical
        new(CollatingOrder.Ukrainian, 0, 0), // Ukrainian
        new(CollatingOrder.Macedonian, 0, 0), // Macedonian
        new(CollatingOrder.Croatian, 1, 0), // Croatian
        new(CollatingOrder.Bosnian, 1, 0), // Bosnian
        new(CollatingOrder.Serbian, 1, 0), // Serbian
        new(CollatingOrder.Thai, 0, 0), // Thai
        new(CollatingOrder.ChineseSimplified, 1, 0), // ChinesePronunciation
        new(CollatingOrder.ChineseSimplified, 0, 0), // ChinesePronunciationLegacy
        new(CollatingOrder.ChineseSimplified, 1, 2), // ChineseStrokeCount
        new(CollatingOrder.ChineseSimplified, 0, 2), // ChineseStrokeCountLegacy
        new(CollatingOrder.ChineseTraditional, 1, 3), // ChineseTradBopomofo
        new(CollatingOrder.ChineseTraditional, 0, 3), // ChineseTradBopomofoLegacy
        new(CollatingOrder.ChineseTraditional, 1, 0), // ChineseTradStrokeCount
        new(CollatingOrder.ChineseTraditional, 0, 0), // ChineseTradStrokeCountLegacy
        new(CollatingOrder.Japanese, 1, 0), // Japanese
        new(CollatingOrder.Japanese, 0, 0), // JapaneseLegacy
        new(CollatingOrder.Japanese, 1, 4), // JapaneseRadicalStrokeCount
        new(CollatingOrder.Korean, 0, 0), // Korean
    ];

    private static readonly string[] SmokeSamples =
    [
        "apple", "café", "coté", "côte", "Ångström", "co-op", "O'Brien",
        "ñ", "č", "ž", "lj", "dž", "ch", "ll", "ı", "İ", "å", "ø", "ß",
        "เก", "ไทย", "Ω", "б", "א",
        "一", "漢字", "人々", "カタカナ", "がくせい", "한국", "韓國", @"C:\",
    ];

    /// <summary>Every collation LibRed claims to encode, found by asking rather than by keeping a list that
    /// could fall out of step with <c>JetLocaleTailoring</c>.</summary>
    public static TheoryData<int, byte, byte> EncodableCollations()
    {
        var data = new TheoryData<int, byte, byte>();
        foreach (CollatingOrder order in Enum.GetValues<CollatingOrder>())
            foreach (byte version in (byte[])[0, 1])
                foreach (byte sortId in (byte[])[0, 1, 2, 3, 4])   // the CJK orders reach sort id 4
                    if (new Collation(order, version, sortId).IsIndexKeyEncodable)
                        data.Add((int)order, version, sortId);
        return data;
    }

    [Theory]
    [MemberData(nameof(EncodableCollations))]
    public void LibRed_creates_a_database_that_ACE_indexes(int order, byte version, byte sortId)
    {
        var collation = new Collation((CollatingOrder)order, version, sortId);
        string path = TemporaryDatabase.CreatePath($"created-{order}-{version}-{sortId}-");
        try
        {
            JetDatabase.Create(path, collation: collation);

            // The order has to survive the round trip, or everything below is measuring the wrong thing.
            using (var db = JetDatabase.Open(path))
                Assert.Equal(collation, db.Collation);

            string[] samples = FullSampleCollations.Contains(collation) ? Samples() : SmokeSamples;

            Dictionary<string, string> ace = AceKeys(path, samples);
            Assert.NotEmpty(ace);

            var column = new ColumnDef
            {
                Name = "K", Type = JetDataType.Text, Index = 0, Collation = collation,
            };

            var mismatches = new List<string>();
            foreach (string text in samples)
            {
                if (!ace.TryGetValue(text, out string? stored)) continue;   // ACE refused the value
                string ours = Convert.ToHexString(IndexKeyCodec.Encode([(column, true)], [text]));
                if (ours != stored) mismatches.Add($"  {Describe(text),-16} ACE {stored,-30} LibRed {ours}");
            }

            output.WriteLine($"{collation.Order} v{version}" + (sortId == 0 ? "" : $" sort id {sortId}") +
                             $": ACE indexed {ace.Count} of {samples.Length} values into a LibRed-created " +
                             $"database, {mismatches.Count} disagreeing");
            foreach (string line in mismatches.Take(40)) output.WriteLine(line);
            Assert.Empty(mismatches);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Printable ASCII, all of Latin-1 and Latin Extended-A, and a few words — so a tailoring is
    /// tested well beyond the handful of characters it actually overrides.</summary>
    private static string[] Samples()
    {
        var samples = new List<string>(SmokeSamples);
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
        foreach ((int first, int last) in blocks)
            for (int c = first; c <= last; c++)
                if (!char.IsControl((char)c) && !char.IsSurrogate((char)c))
                    samples.Add(((char)c).ToString());

        // The CJK blocks, where the CJK orders put their weights: the symbols, Bopomofo, jamo and enclosed forms
        // whole, and a stride through the large ones — every 7th ideograph, every 11th syllable — which crosses
        // every page of their tables while keeping the suite to seconds. The whole BMP is what the generator
        // measures; this keeps what it wrote honest.
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
        return [.. samples.Distinct()];
    }

    private static Dictionary<string, string> AceKeys(string path, string[] samples)
    {
        using (var connection = AceTestDatabase.Open(path))
        {
            Exec(connection, "CREATE TABLE Probe (K TEXT(100), V LONG)");
            Exec(connection, "CREATE INDEX IX_Probe ON Probe (K)");
            for (int i = 0; i < samples.Length; i++)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO Probe (K, V) VALUES (?, ?)";
                insert.Parameters.AddWithValue("k", samples[i]);
                insert.Parameters.AddWithValue("v", i);
                try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
            }
        }

        using var db = JetDatabase.Open(path);
        var table = db.OpenTable("Probe");
        IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Probe");
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