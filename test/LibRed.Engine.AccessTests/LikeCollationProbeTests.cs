using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

// PROBE: what ACE's LIKE treats as the same character, and whether the database's collation changes any of it.
//
// Report asks a handful of telling pairs in General v0, General v1 and Croatian v1 alongside LibRed. The screen
// asks every candidate pair (LikeCandidates) in every collation LibRed can create — 405 of them. What it found:
// every collation matches exactly the same pairs, Turkish, Azerbaijani and Lithuanian included, so LIKE is
// independent of the collation and one table describes it (LikeFoldTableGeneratorTest).
[Collection(AceCollection.Name)]
public class LikeCollationProbeTests(ITestOutputHelper output)
{
    private static readonly (string Value, string Pattern)[] Cases =
    [
        // case beyond ASCII
        ("é", "É"), ("σ", "Σ"), ("ς", "Σ"), ("ς", "σ"), ("ı", "I"), ("i", "İ"), ("ǆ", "Ǆ"), ("ǅ", "ǆ"), ("ÿ", "Ÿ"),
        ("µ", "Μ"), ("ж", "Ж"),
        // width and kana
        ("Ａ", "A"), ("ａ", "A"), ("ｶ", "カ"), ("カ", "か"),
        // accents
        ("é", "e"), ("e", "é"), ("é", "_"), ("é", "[e]"),
        // expansions
        ("ß", "ss"), ("ss", "ß"), ("ß", "SS"), ("ẞ", "ss"), ("æ", "ae"), ("œ", "oe"), ("Œ", "OE"), ("ĳ", "ij"),
        ("þ", "th"), ("Þ", "TH"), ("ﬁ", "fi"), ("ǆ", "dž"), ("ǉ", "lj"),
        // ignorables
        ("co-op", "coop"), ("coop", "co-op"), ("O'Brien", "OBrien"), ("a­b", "ab"), ("a\u0001b", "ab"),
        // trailing spaces
        ("a ", "a"), ("a", "a "), ("a ", "a_"),
        // superscripts and fractions
        ("²", "2"), ("½", "1/2"),
        // ranges
        ("é", "[a-z]"), ("É", "[A-Z]"), ("ß", "[a-z]"), ("ñ", "[m-o]"), ("Z", "[a-z]"), ("_", "[A-z]"), ("^", "[A-z]"),
        ("1", "[a-z]"), ("²", "[0-9]"), ("č", "[c-d]"), ("č", "[a-c]"), ("ž", "[a-z]"), ("ω", "[α-ψ]"),
        // a tailored letter
        ("lj", "_"), ("lj", "__"), ("ča", "[c]a"), ("dž", "_"),
    ];

    [Fact]
    public void Report()
    {
        var v0 = Ace(Collation.GeneralLegacy);
        var v1 = Ace(Collation.General);
        var hr = Ace(new Collation(CollatingOrder.Croatian, Collation.GeneralVersion));
        var libred = LibRed(Collation.GeneralLegacy);

        output.WriteLine($"{"value",-10} {"pattern",-10} v0 v1 hr | LibRed(v0)");
        for (int i = 0; i < Cases.Length; i++)
        {
            string flag = v0[i] != libred[i] ? "   <-- differs" : "";
            output.WriteLine($"{Show(Cases[i].Value),-10} {Show(Cases[i].Pattern),-10} {v0[i],-2} {v1[i],-2} {hr[i],-2} | {libred[i]}{flag}");
        }
    }

    /// <summary>Every candidate pair asked of ACE in every collation LibRed can create, one query per database.
    /// Opt-in via LIBRED_SCREEN_LIKE=1: it creates 417 databases and takes the better part of an hour.</summary>
    [Fact]
    public void Screen_every_collation()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_SCREEN_LIKE") == "1",
            "set LIBRED_SCREEN_LIKE=1 — this asks ACE 26,000 pairs in each of 417 collations");

        var pairs = LikeCandidates.Pairs;
        List<Collation> collations = [.. Enum.GetValues<CollatingOrder>().Distinct()
            .Where(o => o != CollatingOrder.Undefined)
            .SelectMany(o => (Collation[])[new(o, 0), new(o, Collation.GeneralVersion), new(o, 0, 1),
                // the CJK orders reach sort id 4, at both versions
                new(o, 0, 2), new(o, Collation.GeneralVersion, 2), new(o, 0, 3), new(o, Collation.GeneralVersion, 3),
                new(o, Collation.GeneralVersion, 4)])
            .Where(c => c.IsIndexKeyEncodable)
            .Distinct()];

        string directory = Path.Combine(AppContext.BaseDirectory, "like-survey");
        Directory.CreateDirectory(directory);
        string reportPath = Path.Combine(directory, "like-by-collation.txt");
        File.WriteAllText(reportPath, "");

        HashSet<int>? baseline = null;
        foreach (Collation collation in collations)
        {
            HashSet<int> matched;
            try { matched = LikeCandidates.AceMatches(collation); }
            catch (Exception e) when (e is OleDbException or InvalidOperationException or NotSupportedException)
            {
                File.AppendAllText(reportPath, $"{collation.Order} v{collation.Version} sort {collation.SortId}: not created ({e.Message}){Environment.NewLine}");
                continue;
            }

            baseline ??= matched;
            var extra = matched.Except(baseline).ToList();
            var missing = baseline.Except(matched).ToList();
            string line = $"{collation.Order,-28} v{collation.Version} sort {collation.SortId}: {matched.Count} match" +
                (extra.Count + missing.Count == 0 ? "" : $"   DIFFERS +{extra.Count} -{missing.Count}: " +
                    string.Join(" ", extra.Take(12).Select(i => "+" + Pair(pairs[i])).Concat(missing.Take(12).Select(i => "-" + Pair(pairs[i])))));
            // Written as it goes, so a long run shows its progress and a failure keeps what it had.
            File.AppendAllText(reportPath, line + Environment.NewLine);
            output.WriteLine(line);
        }
    }

    private static string Pair((string Value, string Pattern) p) => $"{Show(p.Value)}~{Show(p.Pattern)}";

    private static string Show(string s) =>
        string.Concat(s.Select(c => c < 0x20 || c == 0xAD ? $"\\u{(int)c:X4}" : c.ToString()));

    private static string Sql((string Value, string Pattern) c) =>
        $"SELECT TOP 1 IIF('{c.Value.Replace("'", "''", StringComparison.Ordinal)}' LIKE " +
        $"'{c.Pattern.Replace("'", "''", StringComparison.Ordinal)}', 'T', 'F') FROM One";

    private static string[] Ace(Collation collation)
    {
        string path = TemporaryDatabase.CreatePath("like-probe-", ".accdb");
        try
        {
            DatabaseCreator.CreateEmpty(path, collation: collation);
            using OleDbConnection ace = AceTestDatabase.Open(path);
            Exec(ace, "CREATE TABLE One (Id LONG)");
            Exec(ace, "INSERT INTO One (Id) VALUES (1)");
            return [.. Cases.Select(c =>
            {
                try
                {
                    using OleDbCommand command = ace.CreateCommand();
                    command.CommandText = Sql(c);
                    return command.ExecuteScalar()?.ToString() ?? "N";
                }
                catch (OleDbException) { return "E"; }
            })];
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string[] LibRed(Collation collation)
    {
        string path = TemporaryDatabase.CreatePath("like-probe-libred-", ".accdb");
        try
        {
            DatabaseCreator.CreateEmpty(path, collation: collation);
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery("CREATE TABLE One (Id LONG)");
            engine.ExecuteNonQuery("INSERT INTO One (Id) VALUES (1)");
            return [.. Cases.Select(c =>
            {
                try { return engine.ExecuteQuery(Sql(c)).Rows.Single()[0]?.ToString() ?? "N"; }
                catch (ArgumentException) { return "E"; }
            })];
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
