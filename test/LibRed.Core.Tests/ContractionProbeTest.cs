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
