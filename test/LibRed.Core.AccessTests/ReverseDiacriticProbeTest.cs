using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: whether the French order simply REVERSES the diacritic section.
//
// French is refused: its departure from General was recorded as "unclassified, secondary-section tailoring",
// which is a description of the symptom rather than a rule. [MS-UCODEREF]'s GetWindowsSortKey names the
// mechanism — an IsReverseDW flag that removes trailing diacritics from the LEFT and stores the section
// right-to-left instead of left-to-right. That is the well-known French rule, where accents are weighed from
// the end of the word so that cote < coté < côte < côté.
//
// If ACE agrees, French costs one flag rather than a table of overrides, and so do the other orders that
// carry it. Opt-in via LIBRED_REVERSE_DW=1.
public class ReverseDiacriticProbeTest(ITestOutputHelper output)
{
    // The measured French keys, as ACE stored them in an index. French tailors no letter at all: it is
    // General with the diacritic section reversed, so these also pin the reversal's trimming rule — the run
    // of default weights comes off the LEFT, which is the end that becomes trailing once reversed.
    [Theory]
    [InlineData("cote", "7F4D646D510100")]              // no accent: identical to General
    [InlineData("coté", "7F4D646D51010E00")]            // [02 02 02 0E] -> trim left -> [0E]
    [InlineData("côte", "7F4D646D510102021200")]        // [02 12 02 02] -> [12 02 02] -> 02 02 12
    [InlineData("côté", "7F4D646D51010E021200")]        // [02 12 02 0E] -> [12 02 0E] -> 0E 02 12
    [InlineData("péche", "7F66514D5751010202020E00")]
    [InlineData("pêcher", "7F66514D57516901020202021200")]
    public void French_reverses_the_diacritic_section(string value, string expected)
    {
        var column = new ColumnDef
        {
            Name = "t", Type = JetDataType.Text, Index = 0,
            Collation = new Collation(CollatingOrder.French, 0),
        };
        Assert.Equal(expected, Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [value])));
    }

    // The point of the order, and the thing a single-accent sample set could never show: French orders by the
    // LAST accent, so côte and coté swap relative to General.
    [Fact]
    public void French_orders_by_the_last_accent()
    {
        var french = new ColumnDef
        {
            Name = "t", Type = JetDataType.Text, Index = 0,
            Collation = new Collation(CollatingOrder.French, 0),
        };
        var general = new ColumnDef
        {
            Name = "t", Type = JetDataType.Text, Index = 0, Collation = Collation.GeneralLegacy,
        };

        Assert.Equal(["cote", "côte", "coté", "côté"], Sorted(french));
        Assert.Equal(["cote", "coté", "côte", "côté"], Sorted(general));

        static string[] Sorted(ColumnDef column) =>
            [.. new[] { "côté", "coté", "côte", "cote" }
                .OrderBy(v => IndexKeyEncoder.Encode([(column, true)], [v]), Comparer<byte[]>.Create(Compare))];

        static int Compare(byte[] a, byte[] b)
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }
    }

    /// <summary>
    /// LibRed's French keys against ACE's, over a set far wider than the rule that produced them.
    /// </summary>
    /// <remarks>
    /// The reversal is not a tailored letter or two — it changes the diacritic section of EVERY accented
    /// string, so its risk surface is the whole alphabet rather than a handful of entries. Nine words are the
    /// sample size that hid the inline position bug; this sweeps all of Latin-1 and Latin Extended-A, each
    /// letter doubled and tripled so several accents land in one string, plus real words.
    /// </remarks>
    [Fact]
    public void French_matches_ACE_across_the_latin_range()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_REVERSE_DW") == "1",
            "set LIBRED_REVERSE_DW=1 — this probe needs ACE");

        var samples = new List<string>();
        for (int c = 0x20; c <= 0x17F; c++)
        {
            if (c is >= 0x7F and <= 0xA0) continue;
            string one = ((char)c).ToString();
            samples.Add(one);
            samples.Add("a" + one);
            samples.Add(one + "a" + one);          // two accents in one string: what the rule actually moves
            samples.Add(one + one + "a");
        }
        samples.AddRange([
            "cote", "coté", "côte", "côté", "élève", "élevé", "levée", "pêcher", "pécher",
            "sécurité", "après-midi", "l'été", "Noël", "maïs", "aïeul", "çà", "über", "naïve",
        ]);

        string path = TemporaryDatabase.CreatePath("dw-conf-");
        try
        {
            if (!CreateWithDao(path, "0x040C")) { _ = 0; return; }   // DAO unavailable: nothing to compare
            Dictionary<string, string> ace = AceKeys(path, [.. samples.Distinct()]);

            var column = new ColumnDef
            {
                Name = "t", Type = JetDataType.Text, Index = 0,
                Collation = new Collation(CollatingOrder.French, 0),
            };

            int matched = 0, refused = 0;
            var differences = new List<string>();
            foreach (string text in samples.Distinct())
            {
                if (!ace.TryGetValue(text, out string? stored)) continue;
                string? ours = null;
                try { ours = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text])); }
                catch (NotSupportedException) { refused++; continue; }
                if (ours == stored) { matched++; continue; }
                if (differences.Count < 12)
                    differences.Add($"  {Describe(text),-24} ACE {stored,-28} ours {ours}");
            }

            output.WriteLine($"{matched} match, {differences.Count} differ, {refused} refused");
            foreach (string line in differences) output.WriteLine(line);
            Assert.Empty(differences);
            Assert.Equal(0, refused);
        }
        finally { TemporaryDatabase.Delete(path); }

        static string Describe(string s) =>
            s.All(c => c is >= ' ' and <= '~') ? $"\"{s}\"" : string.Concat(s.Select(c => $"U+{(int)c:X4}"));
    }

    /// <summary>
    /// Whether LibRed can now CREATE a French database, and whether ACE agrees with what it wrote.
    /// </summary>
    /// <remarks>
    /// It could not before: CreateEmpty builds the system-table indexes, so a database cannot be created in
    /// an order whose keys are refused — which is why measuring French needed DAO to author the file first.
    /// Implementing the order lifts that by itself, and this checks the consequence rather than assuming it.
    /// <para>
    /// The real test is not that the file opens but that ACE will INDEX into it: ACE writing its own keys
    /// beside LibRed's is what would expose a disagreement, and a wrong key is otherwise silent.
    /// </para>
    /// </remarks>
    [Fact]
    public void LibRed_can_create_a_french_database_that_ACE_indexes()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_REVERSE_DW") == "1",
            "set LIBRED_REVERSE_DW=1 — this probe needs ACE");

        string path = TemporaryDatabase.CreatePath("french-created-");
        try
        {
            var french = new Collation(CollatingOrder.French, 0);
            DatabaseCreator.CreateEmpty(path, collation: french);

            using (var db = JetDatabase.Open(path))
                Assert.Equal(french, db.Collation);

            // ACE creates the table and index in LibRed's file, and writes the keys itself.
            string[] samples = ["cote", "coté", "côte", "côté", "élève", "élevé", "pêcher", "pécher"];
            Dictionary<string, string> ace = AceKeys(path, samples);
            Assert.NotEmpty(ace);

            var column = new ColumnDef
            {
                Name = "K", Type = JetDataType.Text, Index = 0, Collation = french,
            };
            foreach (string text in samples)
            {
                Assert.True(ace.ContainsKey(text), $"ACE did not store '{text}'");
                Assert.Equal(ace[text], Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text])));
            }

            output.WriteLine($"ACE indexed {ace.Count} values into a LibRed-created French database, " +
                             "every key identical to LibRed's own");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Probe_french_diacritic_order()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_REVERSE_DW") == "1",
            "set LIBRED_REVERSE_DW=1 — this probe needs ACE");

        // The textbook set: same letters, accents in different places, so only the diacritic section moves.
        string[] samples = ["cote", "coté", "côte", "côté", "peche", "péche", "pêche", "pécher", "pêcher"];

        // French has to be authored by DAO, not by LibRed: CreateEmpty builds the system-table indexes, and
        // LibRed refuses French keys — so it cannot create the very database needed to learn French. DAO can
        // set the LANGID, which is all the order needs.
        foreach ((string name, string? langId) in ((string, string?)[])
                 [("General v0", null), ("French v0", "0x040C")])
        {
            string path = TemporaryDatabase.CreatePath("dw-");
            try
            {
                if (langId is null) DatabaseCreator.CreateEmpty(path, collation: Collation.GeneralLegacy);
                else if (!CreateWithDao(path, langId)) { output.WriteLine("DAO unavailable."); continue; }

                Dictionary<string, string> ace = AceKeys(path, samples);

                output.WriteLine($"--- {name}");
                foreach (string text in samples)
                    output.WriteLine($"  {text,-8} {ace.GetValueOrDefault(text, "(not stored)")}");

                // The point of the order: what sorts before what.
                var sorted = samples
                    .Where(ace.ContainsKey)
                    .OrderBy(t => Convert.FromHexString(ace[t]), Comparer<byte[]>.Create(Compare))
                    .ToList();
                output.WriteLine($"  order: {string.Join(" < ", sorted)}");
            }
            finally { TemporaryDatabase.Delete(path); }
        }

        static int Compare(byte[] a, byte[] b)
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }
    }

    /// <summary>Authors a database in one locale via DAO, which can set the LANGID even where LibRed cannot
    /// yet encode that order's keys.</summary>
    private static bool CreateWithDao(string path, string langId)
    {
        object? engine = null;
        foreach (int n in (int[])[120, 36])
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { engine = Activator.CreateInstance(type); break; } catch (Exception) { }
        }
        if (engine is null) return false;

        File.Delete(path);   // DAO creates the file itself and refuses an existing one
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object database = Invoke(workspace, "CreateDatabase", path, $";LANGID={langId};CP=1252;COUNTRY=0", 128)!;
        Invoke(database, "Close");
        return true;
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member,
            System.Reflection.BindingFlags.InvokeMethod, null, target, args);

    private static Dictionary<string, string> AceKeys(string path, string[] samples)
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

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
