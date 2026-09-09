using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: what the Thai order actually does, which is the one tailoring device never implemented.
//
// Thai writes five vowels BEFORE the consonant they are pronounced after — เ แ โ ใ ไ. Collation follows
// speech rather than writing, so the pair has to be swapped before weighing: เก is written vowel-consonant
// and sorts as if consonant-vowel. Neither a per-character map nor a contraction can express that; it is a
// REORDERING, and no other order here needs one.
//
// Thai was recorded as having a single departure from General over the old 193-sample set. French was
// recorded the same way and turned out to be a whole reversal rule the samples could not see, so the number
// is not evidence of a small tailoring. This measures Thai text rather than Thai characters.
//
// Opt-in via LIBRED_THAI=1.
public class ThaiReorderProbeTest(ITestOutputHelper output)
{
    [Fact]
    public void Probe_thai_against_general()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_THAI") == "1",
            "set LIBRED_THAI=1 — this probe needs ACE");

        string source = TestDatabases.Data("Thai.accdb");
        Assert.SkipWhen(!File.Exists(source), "Thai.accdb is not present");

        using (var db = JetDatabase.Open(source))
            output.WriteLine($"Thai.accdb: {db.Collation.Order} version {db.Collation.Version}" +
                             (db.Collation.SortId == 0 ? "" : $" sort id {db.Collation.SortId}"));

        var samples = new List<string>();

        // Every Thai character on its own: consonants, vowels, tone marks and digits.
        for (int c = 0x0E01; c <= 0x0E5B; c++)
            if (!char.IsControl((char)c)) samples.Add(((char)c).ToString());

        // The reordering itself. Each leading vowel against each of a few consonants, both as written
        // (vowel first) and in spoken order (consonant first) — if the order reorders, the two collide.
        foreach (char vowel in "เแโใไ")
            foreach (char consonant in "กขคงจดตนบปมยรลวสห")
            {
                samples.Add($"{vowel}{consonant}");
                samples.Add($"{consonant}{vowel}");
            }

        // Real words, where the reordering has to survive alongside tone marks and following vowels.
        samples.AddRange([
            "เก", "กเ", "เกา", "เกิน", "เก้า", "แก", "แก้", "โก", "โกรธ", "ใกล้", "ไก่", "ไทย",
            "กา", "ก่า", "ก้า", "กิน", "กีบ", "เดิน", "เด็ก", "แดง", "โต", "ใหม่", "ไหม",
            "ประเทศไทย", "ภาษาไทย", "สวัสดี", "ขอบคุณ", "เรียน", "เขียน", "เที่ยว",
            // A leading vowel with nothing after it, and doubled, where there is nothing to swap with.
            "เ", "เเ", "เ ", " เ", "เก เก",
        ]);

        Dictionary<string, string> ace = AceKeys(source, [.. samples.Distinct()]);
        var general = new ColumnDef
        {
            Name = "K", Type = JetDataType.Text, Index = 0, Collation = Collation.GeneralLegacy,
        };

        int same = 0, refused = 0;
        var departures = new List<string>();
        foreach (string text in samples.Distinct())
        {
            if (!ace.TryGetValue(text, out string? stored)) continue;
            string? asGeneral = null;
            try { asGeneral = Convert.ToHexString(IndexKeyEncoder.Encode([(general, true)], [text])); }
            catch (NotSupportedException) { refused++; continue; }
            if (asGeneral == stored) { same++; continue; }
            departures.Add($"  {Describe(text),-30} Thai {stored,-34} General {asGeneral}");
        }

        output.WriteLine($"{same} identical to General v0, {departures.Count} departures, " +
                         $"{refused} General cannot encode");
        foreach (string line in departures.Take(45)) output.WriteLine(line);

        // The question the whole thing turns on: does a written pair collide with its spoken order?
        foreach ((string written, string spoken) in ((string, string)[])
                 [("เก", "กเ"), ("แก", "กแ"), ("โก", "กโ"), ("ใก", "กใ"), ("ไก", "กไ")])
        {
            string w = ace.GetValueOrDefault(written, "-"), s = ace.GetValueOrDefault(spoken, "-");
            output.WriteLine($"  {Describe(written)} = {w}   {Describe(spoken)} = {s}   " +
                             (w == s ? "SAME KEY — reordered" : "different"));
        }
    }

    private static string Describe(string s) =>
        s.All(c => c is >= ' ' and <= '~') ? $"\"{s}\"" : string.Concat(s.Select(c => $"U+{(int)c:X4}"));

    private static Dictionary<string, string> AceKeys(string source, string[] samples)
    {
        string path = TemporaryDatabase.CopyPath(source, "thai-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Probe (K TEXT(60), V LONG)");
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
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
