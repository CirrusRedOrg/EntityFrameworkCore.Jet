using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: every Access-authored sort-order fixture at once — what lands on disk, and how the keys differ.
//
// Two questions this set can answer that Spanish alone could not:
//
//   1. Is the collation field a full 32-bit LCID? Several entries in Access's list are Windows *alternate
//      sort orders*, which live in the high word: German Phone Book is 0x00010407, Hungarian Technical is
//      0x0001040E. We read the LCID as 16 bits from page-0 0x6E, and the byte at 0x70 (the page-0 analogue
//      of a column descriptor's 0x0D) is flagged in Collation.cs as "0 in every file seen — keep an eye on
//      it". A sort ID would sit exactly there, so this dumps the raw de-masked bytes rather than the parse.
//
//   2. Does the v1 scheme generalise beyond General? Romanian and Croatian are the only Latin-script orders
//      Access offers in "- Legacy" / current pairs, so they are the cheapest test of whether a non-General
//      order ever reaches sort-order version 1 and 2-byte NLS primaries.
public class LocaleFixtureCollationProbeTest(ITestOutputHelper output)
{
    /// <summary>Everything in Data\ that is a fixture for something else. Anything else is treated as a
    /// sort-order fixture, so adding one is a matter of dropping the file in — same rule as the csproj glob.
    /// </summary>
    private static readonly HashSet<string> NotSortOrderFixtures = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ace16Types", "BigTable", "BuiltInDataTypes", "Database4", "Decimals", "EncryptedTest",
        "EverythingIsBytes", "Northwind", "WideTable",
    };

    private static readonly string[] Samples =
    [
        // Base Latin, so a wholesale reweighting (a version-1 order) is visible as everything moving.
        "a", "c", "d", "e", "g", "h", "i", "j", "l", "n", "o", "r", "s", "t", "u", "v", "w", "y", "z",
        // Multi-character letters — the contractions.
        "ch", "ll", "dz", "dž", "lj", "nj", "cs", "gy", "ny", "sz", "zs", "ty", "ggy", "ccs", "dzs",
        // Latin diacritics, grouped by base letter.
        "á", "à", "â", "ä", "ã", "å", "ā", "ă", "ą", "æ", "aa", "ae",
        "ç", "ć", "č", "ď", "đ",
        "é", "è", "ê", "ë", "ē", "ė", "ę", "ě",
        "ğ", "ģ", "í", "î", "ï", "ī", "į", "ı", "ķ",
        "ł", "ĺ", "ľ", "ļ", "ń", "ň", "ņ", "ñ",
        "ó", "ò", "ô", "ö", "õ", "ø", "ō", "ơ", "œ", "oe",
        "ŕ", "ř", "ś", "š", "ş", "ß", "ť", "ţ", "ș", "ț",
        "ú", "ù", "û", "ü", "ū", "ů", "ų", "ư", "ue", "ý", "ÿ",
        "ź", "ż", "ž", "þ", "ð", "ő", "ű",
        // Cyrillic, including the letters only some of these locales add.
        "а", "б", "в", "г", "ґ", "д", "е", "ё", "є", "ж", "з", "и", "і", "ї", "й",
        "ъ", "ы", "ь", "э", "ю", "я", "ђ", "ј", "љ", "њ", "ћ", "џ", "ѓ", "ќ", "ѕ",
        // Greek, Hebrew, Arabic — the characters a tailoring would actually move.
        "α", "β", "ά", "σ", "ς", "ω", "ώ",
        "א", "ב", "כ", "ך", "מ", "ם",
        "ا", "ب", "أ", "إ", "آ", "ة", "ى",
        // Thai: leading vowels are written before the consonant they follow phonetically, so a Thai order
        // has to reorder them — the one tailoring here that is not a reweighting.
        "ก", "ข", "ค", "ง", "เ", "แ", "โ", "ใ", "ไ", "เก", "กเ", "ะ", "า", "ิ",
        // Vietnamese (tone marks stack on top of the letter) and Devanagari for the Indic order.
        "ắ", "ầ", "ế", "ộ", "ớ", "ự",
        "अ", "आ", "इ", "क", "ख", "ग",
        "ა", "ბ", "გ",
    ];

    [Fact]
    public void Probe_every_sort_order_fixture()
    {
        string[] fixtures = Directory
            .EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Data"), "*.accdb")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && !NotSortOrderFixtures.Contains(n))
            .Select(n => n!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        output.WriteLine("page-0 sort order — raw de-masked bytes at 0x6E..0x71, then how we parse them:");
        output.WriteLine($"  {"fixture",-20} {"raw",-10} {"langid",-8} {"sortid",-8} {"ver",-5} {"full LCID",-12} order");
        Report("General", TestDatabases.NorthwindAccdb);
        foreach (string name in fixtures) Report(name, TestDatabases.Data($"{name}.accdb"));

        // Two baselines. A version-1 order compared against General *v0* trivially differs everywhere,
        // because the key shape changes; what matters is how far it departs from General *v1*, which is the
        // table we already implement. LibRed can author the v1 baseline itself.
        Dictionary<string, string> generalV0 = KeysFor(TestDatabases.NorthwindAccdb, "general-v0");
        string v1Path = TemporaryDatabase.CreatePath("general-v1-");
        DatabaseCreator.CreateEmpty(v1Path, collation: Collation.General);
        Dictionary<string, string> generalV1 = KeysFor(v1Path, "general-v1", copy: false);
        TemporaryDatabase.Delete(v1Path);

        var differences = new Dictionary<string, List<string>>();
        var baselines = new Dictionary<string, string>();
        foreach (string name in fixtures)
        {
            string path = TestDatabases.Data($"{name}.accdb");
            using (var db = JetDatabase.Open(path))
                baselines[name] = db.DefaultCollationVersion == Collation.GeneralVersion ? "v1" : "v0";
            Dictionary<string, string> baseline = baselines[name] == "v1" ? generalV1 : generalV0;
            Dictionary<string, string> theirs = KeysFor(path, name);
            differences[name] = Samples
                .Where(s => baseline.GetValueOrDefault(s) != theirs.GetValueOrDefault(s))
                .Select(s => $"{Describe(s),-16} General {baseline.GetValueOrDefault(s) ?? "(none)",-26} " +
                             $"{theirs.GetValueOrDefault(s) ?? "(none)"}")
                .ToList();
        }

        output.WriteLine("");
        output.WriteLine($"departure from the General order of the SAME version, over {Samples.Length} samples:");
        foreach ((string name, List<string> diff) in differences.OrderByDescending(d => d.Value.Count))
            output.WriteLine($"  {name,-20} {baselines[name]}  {diff.Count,4} differ" +
                             (diff.Count == 0 ? "   <-- indistinguishable from General" : ""));

        foreach ((string name, List<string> diff) in differences)
        {
            if (diff.Count == 0) continue;
            output.WriteLine("");
            output.WriteLine($"  {name} — {diff.Count} of {Samples.Length}:");
            foreach (string line in diff.Take(DetailLimit)) output.WriteLine($"     {line}");
            if (diff.Count > DetailLimit) output.WriteLine($"     … and {diff.Count - DetailLimit} more");
        }
    }

    /// <summary>Per-fixture cap on printed detail — a version-1 order moves nearly every sample, and the
    /// count in the summary is the interesting part for those.</summary>
    private const int DetailLimit = 30;

    /// <summary>Dumps the sort-order field straight out of page 0, de-masked but otherwise unparsed, so a
    /// value in the byte we do not model is visible rather than silently dropped.</summary>
    private void Report(string label, string path)
    {
        if (!File.Exists(path)) { output.WriteLine($"  {label,-20} missing"); return; }

        byte[] header = new byte[0x80];
        using (var stream = File.OpenRead(path)) stream.ReadExactly(header);
        ReadOnlySpan<byte> mask = JetFormatBase.PageZeroHeaderMask;
        int start = JetFormatBase.PageZeroHeaderMaskStart;
        byte[] field = new byte[4];
        for (int i = 0; i < 4; i++)
            field[i] = (byte)(header[JetFormatBase.CollationSortOrderOffset + i] ^ mask[JetFormatBase.CollationSortOrderOffset + i - start]);

        using var db = JetDatabase.Open(path);
        Collation c = db.Collation;
        output.WriteLine($"  {label,-20} {Convert.ToHexString(field),-10} {(int)c.Order,-8} " +
                         $"0x{c.SortId:X2}     {c.Version,-5} 0x{c.Lcid:X8}   {c.Order}" +
                         $"{(Convert.ToHexString(field) == $"{c.Lcid & 0xFF:X2}{(c.Lcid >> 8) & 0xFF:X2}{c.SortId:X2}{c.Version:X2}" ? "" : "  <-- field not fully accounted for")}");
    }

    /// <summary>Has ACE build and populate an indexed text column in a copy, then reads the stored keys back
    /// with LibRed, mapped by the value that produced them.</summary>
    private static Dictionary<string, string> KeysFor(string source, string label, bool copy = true)
    {
        var keys = new Dictionary<string, string>();
        string path = copy ? TemporaryDatabase.CopyPath(source, $"locale-{label.ToLowerInvariant()}-") : source;
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE CollProbe (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_CollProbe ON CollProbe (K)");
                for (int i = 0; i < Samples.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO CollProbe (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", Samples[i]);
                    insert.Parameters.AddWithValue("v", i);
                    try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("CollProbe");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_CollProbe");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
                if (rows.TryGetValue(rowId, out object?[]? values))
                    keys[(string?)values[keyColumn.Index] ?? ""] = Convert.ToHexString(stored);
            return keys;
        }
        finally { if (copy) TemporaryDatabase.Delete(path); }
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
