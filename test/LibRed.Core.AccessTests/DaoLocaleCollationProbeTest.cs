using System.Reflection;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: the five collating orders DAO names but Access's "New Database Sort Order" list does not offer —
// Arabic, Greek, Hebrew, Dutch and Cyrillic.
//
// CollatingOrder mirrors DAO's CollatingOrderEnum, which is a Jet-3.5-era list. Access's own list is a
// different set: it has no Arabic/Greek/Hebrew/Dutch/Cyrillic entry, but adds Bosnian, Croatian, Serbian,
// Macedonian, Ukrainian, Estonian, Latvian, Lithuanian, Slovak, Romanian, Georgian Modern, Vietnamese, Indic,
// French, German Phone Book, Hungarian Technical and the CJK variants. So the two disagree in both
// directions, and it is worth knowing which of DAO's names still do anything.
//
// For each: does DAO accept the locale, what LCID lands on disk, will ACE open the result at all, and do the
// index keys actually differ from General v0? DAO can only author version 0 (DaoDatabaseCreationProbeTest),
// so any difference here is a locale difference, not a sort-order-version one.
public class DaoLocaleCollationProbeTest(ITestOutputHelper output)
{
    private const int UseJet = 2;
    private const int Ace12 = 128;
    private const int Jet4 = 64;

    // (label, DAO locale string, the CollatingOrder the DAO enum claims for it). The last four are the
    // positive controls: Access *does* list Spanish/Czech/Polish/Turkish, and Spanish Traditional's keys are
    // already known to differ from General (SpanishCollationProbeTest). If those come back identical too,
    // the finding is about DAO's locale argument being inert, not about the five orders being unimplemented.
    private static readonly (string Label, string Locale, int ExpectedLcid)[] Locales =
    [
        ("General (control)", ";LANGID=0x0409;CP=1252;COUNTRY=0", 1033),
        ("Arabic",            ";LANGID=0x0401;CP=1256;COUNTRY=0", 1025),
        ("Greek",             ";LANGID=0x0408;CP=1253;COUNTRY=0", 1032),
        ("Hebrew",            ";LANGID=0x040D;CP=1255;COUNTRY=0", 1037),
        ("Dutch",             ";LANGID=0x0413;CP=1252;COUNTRY=0", 1043),
        ("Cyrillic",          ";LANGID=0x0419;CP=1251;COUNTRY=0", 1049),
        ("Spanish [control]", ";LANGID=0x040A;CP=1252;COUNTRY=0", 1034),
        ("Czech [control]",   ";LANGID=0x0405;CP=1250;COUNTRY=0", 1029),
        ("Polish [control]",  ";LANGID=0x0415;CP=1250;COUNTRY=0", 1045),
        ("Turkish [control]", ";LANGID=0x041F;CP=1254;COUNTRY=0", 1055),
    ];

    // Latin controls; the digraphs Spanish/Czech treat as letters; the one Dutch is supposed to; then the
    // letters Polish and Turkish add, and one pair per non-Latin script.
    private static readonly string[] Samples =
    [
        "a", "c", "z", "e", "é",
        "ch", "ll", "ñ", "č", "ř", "ž",
        "ij", "ijsbeer", "y", "yak",
        "ł", "ą", "ż", "ı", "i", "İ",
        // Per script, the letters whose ordering the tailoring would actually move — not just the first two
        // letters of the alphabet, which sort the same in every order and so prove nothing.
        "α", "β", "Α", "ά", "σ", "ς", "ω", "ώ",         // Greek: tonos, and final vs medial sigma
        "а", "б", "А", "е", "ё", "и", "й", "ь", "ъ",    // Cyrillic: yo after ye, short i, the signs
        "א", "ב", "כ", "ך", "מ", "ם", "צ", "ץ",         // Hebrew: medial vs final forms
        "ا", "ب", "أ", "إ", "آ", "ة", "ه", "ى", "ي",    // Arabic: hamza forms, ta marbuta, alef maqsura
        "ĳ", "IJ",                                       // Dutch: the IJ ligature as well as the pair
        // Words carrying TWO marks, per script. Everything above is a single character or a short pair, and
        // a whole class of rule cannot be seen that way: French tailors no letter at all — it reverses the
        // diacritic section — so a word with ONE accent encodes identically to General and only a word with
        // two reveals it. These five orders were called inert on a set that could not have detected such a
        // rule, which is not the same as their being inert.
        "έάν", "άέν", "ελληνικά", "Ελληνικά", "ώρα", "ωρά",     // Greek: two tonos, in either order
        "ёлка", "мёд", "йогурт", "майор",                        // Cyrillic: yo and short i inside words
        "שָׁלוֹם", "בְּרֵאשִׁית",                                     // Hebrew: several niqqud in one word
        "مُحَمَّد", "كِتَاب", "بِسْمِ",                                  // Arabic: several harakat in one word
        "ijsvrij", "yoghurt", "bijzonder", "byzantijns",         // Dutch: ij against y inside words
        // The same shape that exposed French, in each script that has accents to reverse.
        "άα", "αά", "άαά", "éà", "àé", "éàé",
    ];

    [Fact]
    public void Probe_dao_only_collating_orders()
    {
        object? engine = CreateDbEngine(out string progId);
        if (engine is null) { output.WriteLine("DAO unavailable in this process."); return; }
        output.WriteLine($"DAO engine: {progId}");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", UseJet)!;

        // Jet 4 first: these are Jet-era orders, so an .mdb is the format they were designed for.
        output.WriteLine("");
        output.WriteLine("Jet 4 (.mdb) — creation and the LCID that lands on disk:");
        foreach ((string label, string locale, int expected) in Locales)
            output.WriteLine($"  {label,-18} {Create(workspace, locale, Jet4, ".mdb", expected)}");

        output.WriteLine("");
        output.WriteLine("ACE 12 (.accdb) — creation, then whether ACE will use the file:");
        Dictionary<string, Dictionary<string, string>> keys = [];
        foreach ((string label, string locale, int expected) in Locales)
        {
            string path = TemporaryDatabase.CreatePath("dao-locale-", ".accdb");
            try
            {
                object database = Invoke(workspace, "CreateDatabase", path, locale, Ace12)!;
                Invoke(database, "Close");

                using (var db = JetDatabase.Open(path))
                    output.WriteLine($"  {label,-18} lcid {db.DefaultCollationLcid} " +
                                     $"(expected {expected}){(db.DefaultCollationLcid == expected ? "" : "  <-- MISMATCH")}" +
                                     $", version {db.DefaultCollationVersion}, order {db.Collation.Order}");

                keys[label] = KeysFor(label, path);
            }
            catch (TargetInvocationException ex)
            {
                output.WriteLine($"  {label,-18} rejected by DAO: {ex.InnerException?.Message.Trim()}");
            }
            catch (Exception ex)
            {
                output.WriteLine($"  {label,-18} {ex.GetType().Name}: {ex.Message.Trim()}");
            }
            finally { TemporaryDatabase.Delete(path); }
        }

        if (!keys.TryGetValue("General (control)", out Dictionary<string, string>? general)) return;
        output.WriteLine("");
        output.WriteLine("index keys vs the General control (only differing samples listed):");
        foreach ((string label, Dictionary<string, string> theirs) in keys)
        {
            if (label == "General (control)") continue;
            var different = Samples
                .Where(s => general.GetValueOrDefault(s) != theirs.GetValueOrDefault(s))
                .ToList();
            if (different.Count == 0) { output.WriteLine($"  {label,-18} identical to General for all {Samples.Length} samples"); continue; }
            output.WriteLine($"  {label}:");
            foreach (string s in different)
                output.WriteLine($"     {Describe(s),-14} General {general.GetValueOrDefault(s) ?? "(none)",-26} " +
                                 $"{label} {theirs.GetValueOrDefault(s) ?? "(none)"}");
        }
    }

    private string Create(object workspace, string locale, int type, string extension, int expected)
    {
        string path = TemporaryDatabase.CreatePath("dao-locale-", extension);
        try
        {
            object database = Invoke(workspace, "CreateDatabase", path, locale, type)!;
            Invoke(database, "Close");
            using var db = JetDatabase.Open(path);
            return $"created; lcid {db.DefaultCollationLcid} (expected {expected})" +
                   $"{(db.DefaultCollationLcid == expected ? "" : "  <-- MISMATCH")}, version {db.DefaultCollationVersion}";
        }
        catch (TargetInvocationException ex) { return $"rejected by DAO: {ex.InnerException?.Message.Trim()}"; }
        catch (Exception ex) { return $"{ex.GetType().Name}: {ex.Message.Trim()}"; }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Has ACE build and populate an indexed text column, then reads the stored keys back with
    /// LibRed. An empty result means ACE would not work with the database at all — which is itself the
    /// answer for an order ACE no longer lists.</summary>
    private Dictionary<string, string> KeysFor(string label, string path)
    {
        var keys = new Dictionary<string, string>();
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
        }
        catch (Exception ex)
        {
            output.WriteLine($"  {"",-18} ACE refused the file: {ex.GetType().Name}: {ex.Message.Trim()}");
            return keys;
        }

        using var db = JetDatabase.Open(path);
        var table = db.OpenTable("CollProbe");
        IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_CollProbe");
        ColumnDef keyColumn = table.Definition.FindColumn("K")!;
        // Which collation ACE stamped on the column it just created: the database default, or General? That
        // separates "the order is recorded but unimplemented" from "ACE overrode it at column creation".
        output.WriteLine($"  {"",-18} ACE stamped the new column {keyColumn.Collation.Order} " +
                         $"v{keyColumn.Collation.Version}");
        var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);
        foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            if (rows.TryGetValue(rowId, out object?[]? values))
                keys[(string?)values[keyColumn.Index] ?? ""] = Convert.ToHexString(stored);
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

    private static object? CreateDbEngine(out string progId)
    {
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            progId = $"DAO.DBEngine.{n}";
            Type? type = Type.GetTypeFromProgID(progId);
            if (type is null) continue;
            try { return Activator.CreateInstance(type); }
            catch (Exception) { /* registered but not instantiable in this bitness */ }
        }
        progId = "(none)";
        return null;
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
