using System.Text;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: whether the version-1 locale orders are General v1 plus a few overrides.
//
// Bosnian, Croatian and Serbian ship only at sort-order version 1 and are refused, on the grounds that the
// v1 encoder has no tailoring hook — its primaries are two-byte NLS values, a different shape from the
// one-byte table JetLocaleTailoring targets. That is a statement about the ENCODER, not about the orders:
// nothing measured says a v1 locale tailors differently in kind from a v0 one.
//
// If these are General v1 with a handful of letters moved, the hook is a small piece of work and three
// locales follow. This measures the departures rather than assuming either way.
//
// Opt-in via LIBRED_V1_TAILORING=1.
public class V1TailoringProbeTest(ITestOutputHelper output)
{
    /// <summary>
    /// Each tailored letter in BOTH cases, per locale, because they are not folded on disk.
    /// </summary>
    /// <remarks>
    /// All three orders report the same NUMBER of departures, and the entries generated from them come out
    /// byte-identical — but conformance still disagrees on <c>U+016D</c>. Equal counts are not equal content:
    /// the generator keys entries by their uppercase form, so a letter whose two cases behave differently
    /// collapses into one entry and the last one written wins. This asks about each case separately.
    /// </remarks>
    [Theory]
    [InlineData("Croatian")]
    [InlineData("Bosnian")]
    [InlineData("Serbian")]
    public void Probe_v1_case_pairs(string fixture)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_V1_TAILORING") == "1",
            "set LIBRED_V1_TAILORING=1 — this probe needs ACE");

        string source = TestDatabases.Data($"{fixture}.accdb");
        Assert.SkipWhen(!File.Exists(source), $"{fixture}.accdb is not present");

        // The retuned letters and the letters of the alphabet, upper and lower.
        int[] points =
        [
            0x0106, 0x010C, 0x0110, 0x0160, 0x017D,                          // Ć Č Đ Š Ž
            0x0102, 0x010E, 0x0114, 0x011A, 0x011E, 0x012C, 0x013D,          // breve/caron retunes
            0x0147, 0x014E, 0x0158, 0x0164, 0x016C, 0x0179, 0x017B,
        ];
        var samples = new List<string>();
        foreach (int p in points)
        {
            samples.Add(((char)p).ToString());
            samples.Add(((char)p).ToString().ToLowerInvariant());
        }
        samples.AddRange(["DŽ", "dž", "Dž", "LJ", "lj", "Lj", "NJ", "nj", "Nj"]);

        Dictionary<string, string> ace = AceKeys(source, [.. samples.Distinct()]);
        output.WriteLine($"--- {fixture}");
        foreach (int p in points)
        {
            string upper = ((char)p).ToString(), lower = upper.ToLowerInvariant();
            string u = ace.GetValueOrDefault(upper, "-"), l = ace.GetValueOrDefault(lower, "-");
            output.WriteLine($"  U+{p:X4} {u,-24} U+{(int)lower[0]:X4} {l,-24} {(u == l ? "same" : "DIFFER")}");
        }
        foreach (string d in (string[])["DŽ", "dž", "Dž", "LJ", "lj", "Lj", "NJ", "nj", "Nj"])
            output.WriteLine($"  {d,-4} {ace.GetValueOrDefault(d, "-")}");
    }

    [Theory]
    [InlineData("Croatian")]
    [InlineData("Bosnian")]
    [InlineData("Serbian")]
    public void Probe_v1_locale_departures_from_general(string fixture)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_V1_TAILORING") == "1",
            "set LIBRED_V1_TAILORING=1 — this probe needs ACE");

        string source = TestDatabases.Data($"{fixture}.accdb");
        Assert.SkipWhen(!File.Exists(source), $"{fixture}.accdb is not present");

        using (var db = JetDatabase.Open(source))
            output.WriteLine($"{fixture}: {db.Collation.Order} version {db.Collation.Version}" +
                             (db.Collation.SortId == 0 ? "" : $" sort id {db.Collation.SortId}"));

        // The whole range the conformance test guards, not a hand-picked point list. Sampling points is how
        // the first version of this table came out incomplete: the caron retune reaches further into Latin
        // Extended-B than any list of "letters I expect to matter" would have included.
        var samples = new List<string>();
        for (int c = 0x20; c <= 0x24F; c++)
        {
            if (c is >= 0x7F and <= 0xA0) continue;
            samples.Add(((char)c).ToString());
        }
        foreach ((int first, int last) in ((int, int)[])
                 [(0x02B0, 0x02FF), (0x0370, 0x052F), (0x1E00, 0x1EFF), (0x2100, 0x218F)])
            for (int c = first; c <= last; c++)
                if (!char.IsControl((char)c) && !char.IsSurrogate((char)c))
                    samples.Add(((char)c).ToString());
        samples.AddRange([
            "dz", "dž", "DŽ", "lj", "LJ", "nj", "NJ", "ch", "cc", "ss",
            "džem", "ljubav", "njega", "čaj", "ćevapi", "šećer", "žito", "đak",
        ]);

        Dictionary<string, string> ace = AceKeys(source, [.. samples.Distinct()]);
        var general = new ColumnDef
        {
            Name = "K", Type = JetDataType.Text, Index = 0, Collation = Collation.General,
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
            departures.Add($"  {Describe(text),-22} locale {stored,-30} general {asGeneral}");
        }

        output.WriteLine($"{same} identical to General v1, {departures.Count} departures, {refused} refused");
        foreach (string line in departures.Take(40)) output.WriteLine(line);
        output.WriteLine(departures.Count == 0
            ? "IDENTICAL — inert, like the five DAO-only orders"
            : $"{departures.Count} entries would express this order as General v1 plus overrides");

        // Ready-to-paste tailoring entries, keyed uppercase as the table expects. Generated rather than
        // transcribed: hand-copying hex is exactly the work that introduces a wrong byte nobody notices,
        // because a wrong index key does not fail — it silently disagrees with ACE.
        var letters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var accented = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string text in samples.Distinct())
        {
            if (!ace.TryGetValue(text, out string? stored)) continue;
            string? asGeneral = null;
            try { asGeneral = Convert.ToHexString(IndexKeyEncoder.Encode([(general, true)], [text])); }
            catch (NotSupportedException) { continue; }
            if (asGeneral == stored) continue;

            byte[] key = Convert.FromHexString(stored);
            int end = Array.IndexOf(key, (byte)0x01, 1);
            if (end < 0) continue;
            byte[] primaries = key[1..end];
            byte[] secondaries = key[(end + 1)..^1];
            if (secondaries.Length > 1) continue;               // an expansion: more than one weight

            string upper = text.ToUpperInvariant();
            string bytes = string.Join(", ", primaries.Select(b => $"0x{b:X2}"));
            if (secondaries.Length == 0 || secondaries[0] == 0x02) letters[upper] = $"(\"{upper}\", [{bytes}]),";
            else accented[upper] = $"(\"{upper}\", [{bytes}], 0x{secondaries[0]:X2}),";
        }

        output.WriteLine("");
        output.WriteLine($"=== {fixture}: {letters.Count} letters, {accented.Count} accented");
        foreach (string line in letters.Values) output.WriteLine($"    {line}");
        output.WriteLine("    --- accented ---");
        foreach (string line in accented.Values) output.WriteLine($"    {line}");
    }

    private static string Describe(string s) =>
        s.All(c => c is >= ' ' and <= '~') ? $"\"{s}\"" : string.Concat(s.Select(c => $"U+{(int)c:X4}"));

    private static Dictionary<string, string> AceKeys(string source, string[] samples)
    {
        string path = TemporaryDatabase.CopyPath(source, "v1tail-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Probe (K TEXT(50), V LONG)");
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
