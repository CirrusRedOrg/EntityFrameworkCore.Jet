using System.Globalization;
using System.Reflection;
using System.Text;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: the whole DAO-reachable collating-order surface, measured rather than assumed.
//
// DAO's CreateDatabase/CompactDatabase take a RAW LANGID in the connect string (";LANGID=0x0409;CP=1252;
// COUNTRY=0") and Jet stores it verbatim as the database's collating order, so the reachable set is far wider
// than Access's "New database sort order" dropdown. LibRed refuses to write ANY table in an order it cannot
// encode index keys for — creating a table writes an MSysObjects row whose Name column is indexed — so an
// unsupported order makes the database unusable, not merely differently sorted.
//
// Two passes:
//   Survey_dao_acceptance          - creates a database per candidate LANGID and records what lands on disk.
//   Survey_keys_batch_NN           - for the in-scope orders in that batch, has ACE encode a wide sample set
//                                    inside a database carrying the order and diffs the stored index keys
//                                    against General v0 (measured from ACE in the same run) and General v1
//                                    (LibRed's own v1 encoder).
//
// METHOD NOTES, because this exact measurement has been got wrong before:
//   * Every key batch carries TWO POSITIVE CONTROLS - Czech (0x0405) and Turkish (0x041F) - which are known
//     to depart from General. If a control reports zero departures the harness is dead and every
//     "identical to General" result in that run is worthless.
//   * The sample set is script-targeted: a batch's samples always include the blocks of the scripts its
//     locales are written in, so a Greek order is tested with Greek characters and a Devanagari order with
//     Devanagari. A sweep that cannot distinguish the orders it tests proves nothing.
//   * Words carrying TWO marks are included per script. French tailors no letter at all - it reverses the
//     diacritic section - so a one-accent word encodes identically to General and only a two-accent word
//     can reveal it.
//
// Results are written to %TEMP%\libred-collation-survey (override with LIBRED_SURVEY_OUT) as well as to the
// test output, because the departure lists are long.
//
// Run:  dotnet test test\LibRed.Core.Tests\LibRed.Core.Tests.csproj
//           --filter "FullyQualifiedName~CollationSurveyProbeTests.Survey_dao_acceptance"
//       dotnet test test\LibRed.Core.Tests\LibRed.Core.Tests.csproj
//           --filter "FullyQualifiedName~CollationSurveyProbeTests.Survey_keys_batch_04"
public class CollationSurveyProbeTests(ITestOutputHelper output)
{
    private const int UseJet = 2;
    private const int Ace12 = 128;

    private const int CzechControl = 0x0405;
    private const int TurkishControl = 0x041F;
    private const int GeneralLangId = 0x0409;

    // ---------------------------------------------------------------------------------------------------
    // The candidate LANGIDs.
    //
    // Batch -1 means "not measured for keys": either a CJK order (deliberately out of scope - their weight
    // tables are a different order of problem) or an order LibRed already encodes. Everything else is
    // grouped into a batch by SCRIPT, so the sample set a batch builds stays small while still covering
    // every script its locales use.
    // ---------------------------------------------------------------------------------------------------
    private sealed record Candidate(int LangId, string Name, string Script, int Batch);

    private static readonly Candidate[] Candidates =
    [
        // --- Already encodable (measured in pass A only, to confirm what DAO writes) ---
        new(0x0409, "English United States (General)", "latin", -1),
        new(0x0405, "Czech", "latin", -1),
        new(0x040A, "Spanish Traditional", "latin", -1),
        new(0x0C0A, "Spanish Modern", "latin", -1),
        new(0x040C, "French", "latin", -1),
        new(0x040E, "Hungarian", "latin", -1),
        new(0x040F, "Icelandic", "latin", -1),
        new(0x0414, "Norwegian Bokmal", "latin", -1),
        new(0x0415, "Polish", "latin", -1),
        new(0x0418, "Romanian", "latin", -1),
        new(0x041A, "Croatian", "latin", -1),
        new(0x041B, "Slovak", "latin", -1),
        new(0x041D, "Swedish", "latin", -1),
        new(0x041E, "Thai", "thai", -1),
        new(0x041F, "Turkish", "latin", -1),
        new(0x0422, "Ukrainian", "cyrillic", -1),
        new(0x0424, "Slovenian", "latin", -1),
        new(0x0425, "Estonian", "latin", -1),
        new(0x0426, "Latvian", "latin", -1),
        new(0x0427, "Lithuanian", "latin", -1),
        new(0x042A, "Vietnamese", "latin", -1),
        new(0x042F, "Macedonian", "cyrillic", -1),

        // --- CJK: deliberately out of scope ---
        new(0x0404, "Chinese Traditional", "cjk", -1),
        new(0x0804, "Chinese Simplified", "cjk", -1),
        new(0x0411, "Japanese", "cjk", -1),
        new(0x0412, "Korean", "cjk", -1),

        // --- Batch 0: Latin, western Europe ---
        new(0x0403, "Catalan", "latin", 0),
        new(0x0406, "Danish", "latin", 0),
        new(0x0407, "German", "latin", 0),
        new(0x040B, "Finnish", "latin", 0),
        new(0x0410, "Italian", "latin", 0),
        new(0x0413, "Dutch", "latin", 0),
        new(0x0416, "Portuguese Brazil", "latin", 0),
        new(0x0417, "Romansh", "latin", 0),
        new(0x041C, "Albanian", "latin", 0),
        new(0x042D, "Basque", "latin", 0),

        // --- Batch 1: Latin, sublanguage variants and the ex-Yugoslav Latin orders ---
        new(0x0809, "English United Kingdom", "latin", 1),
        new(0x0C09, "English Australia", "latin", 1),
        new(0x0807, "German Switzerland", "latin", 1),
        new(0x0C07, "German Austria", "latin", 1),
        new(0x080C, "French Belgium", "latin", 1),
        new(0x0C0C, "French Canada", "latin", 1),
        new(0x0810, "Italian Switzerland", "latin", 1),
        new(0x0813, "Dutch Belgium", "latin", 1),
        new(0x0814, "Norwegian Nynorsk", "latin", 1),
        new(0x0816, "Portuguese Portugal", "latin", 1),
        new(0x081D, "Swedish Finland", "latin", 1),
        new(0x080A, "Spanish Mexico", "latin", 1),

        // --- Batch 2: Latin, central/northern/other Europe ---
        new(0x042E, "Upper Sorbian", "latin", 2),
        new(0x0436, "Afrikaans", "latin", 2),
        new(0x0438, "Faroese", "latin", 2),
        new(0x043A, "Maltese", "latin", 2),
        new(0x043B, "Sami Northern", "latin", 2),
        new(0x0452, "Welsh", "latin", 2),
        new(0x0456, "Galician", "latin", 2),
        new(0x0462, "Frisian", "latin", 2),
        new(0x046E, "Luxembourgish", "latin", 2),
        new(0x046F, "Greenlandic", "latin", 2),
        new(0x047E, "Breton", "latin", 2),
        new(0x0483, "Corsican", "latin", 2),
        new(0x0484, "Alsatian", "latin", 2),
        new(0x0482, "Occitan", "latin", 2),
        new(0x0491, "Scottish Gaelic", "latin", 2),

        // --- Batch 3: Latin, Turkic / African / Asian / American ---
        new(0x0421, "Indonesian", "latin", 3),
        new(0x042C, "Azerbaijani Latin", "latin", 3),
        new(0x0432, "Tswana", "latin", 3),
        new(0x0434, "Xhosa", "latin", 3),
        new(0x0435, "Zulu", "latin", 3),
        new(0x043E, "Malay", "latin", 3),
        new(0x0441, "Swahili", "latin", 3),
        new(0x0442, "Turkmen", "latin", 3),
        new(0x0443, "Uzbek Latin", "latin", 3),
        new(0x045F, "Tamazight Latin", "latin", 3),
        new(0x0464, "Filipino", "latin", 3),
        new(0x0468, "Hausa", "latin", 3),
        new(0x046A, "Yoruba", "latin", 3),
        new(0x046B, "Quechua", "latin", 3),
        new(0x046C, "Sesotho sa Leboa", "latin", 3),
        new(0x0470, "Igbo", "latin", 3),
        new(0x047A, "Mapudungun", "latin", 3),
        new(0x047C, "Mohawk", "latin", 3),
        new(0x0481, "Maori", "latin", 3),
        new(0x0486, "K'iche", "latin", 3),
        new(0x0487, "Kinyarwanda", "latin", 3),
        new(0x0488, "Wolof", "latin", 3),

        // --- Batch 4: Cyrillic ---
        new(0x0402, "Bulgarian", "cyrillic", 4),
        new(0x0419, "Russian", "cyrillic", 4),
        new(0x0423, "Belarusian", "cyrillic", 4),
        new(0x0428, "Tajik", "cyrillic", 4),
        new(0x043F, "Kazakh", "cyrillic", 4),
        new(0x0440, "Kyrgyz", "cyrillic", 4),
        new(0x0444, "Tatar", "cyrillic", 4),
        new(0x0450, "Mongolian Cyrillic", "cyrillic", 4),
        new(0x046D, "Bashkir", "cyrillic", 4),
        new(0x0485, "Sakha", "cyrillic", 4),
        new(0x082C, "Azerbaijani Cyrillic", "cyrillic", 4),
        new(0x0843, "Uzbek Cyrillic", "cyrillic", 4),

        // --- Batch 5: the ex-Yugoslav orders DAO reaches, both scripts ---
        new(0x081A, "Serbian Latin", "latin", 5),
        new(0x0C1A, "Serbian Cyrillic", "cyrillic", 5),
        new(0x101A, "Croatian Bosnia-Herzegovina", "latin", 5),
        new(0x141A, "Bosnian Latin", "latin", 5),
        new(0x201A, "Bosnian Cyrillic", "cyrillic", 5),
        new(0x0437, "Georgian", "georgian", 5),
        new(0x042B, "Armenian", "armenian", 5),
        new(0x0408, "Greek", "greek", 5),

        // --- Batch 6: Arabic script and the Semitic/RTL orders ---
        new(0x0401, "Arabic Saudi Arabia", "arabic", 6),
        new(0x0801, "Arabic Iraq", "arabic", 6),
        new(0x0429, "Persian", "arabic", 6),
        new(0x0420, "Urdu Pakistan", "arabic", 6),
        new(0x0820, "Urdu India", "arabic", 6),
        new(0x0463, "Pashto", "arabic", 6),
        new(0x048C, "Dari", "arabic", 6),
        new(0x0480, "Uighur", "arabic", 6),
        new(0x0492, "Central Kurdish", "arabic", 6),
        new(0x040D, "Hebrew", "hebrew", 6),
        new(0x043D, "Yiddish", "hebrew", 6),
        new(0x045A, "Syriac", "syriac", 6),
        new(0x0465, "Divehi", "thaana", 6),

        // --- Batch 7: Devanagari and the eastern Indic scripts ---
        new(0x0439, "Hindi", "devanagari", 7),
        new(0x044E, "Marathi", "devanagari", 7),
        new(0x044F, "Sanskrit", "devanagari", 7),
        new(0x0461, "Nepali", "devanagari", 7),
        new(0x0457, "Konkani", "devanagari", 7),
        new(0x0445, "Bengali", "bengali", 7),
        new(0x044D, "Assamese", "bengali", 7),
        new(0x0446, "Punjabi", "gurmukhi", 7),
        new(0x0447, "Gujarati", "gujarati", 7),
        new(0x0448, "Odia", "oriya", 7),

        // --- Batch 8: the southern Indic scripts ---
        new(0x0449, "Tamil", "tamil", 8),
        new(0x044A, "Telugu", "telugu", 8),
        new(0x044B, "Kannada", "kannada", 8),
        new(0x044C, "Malayalam", "malayalam", 8),
        new(0x045B, "Sinhala", "sinhala", 8),

        // --- Batch 9: the remaining scripts ---
        new(0x0453, "Khmer", "khmer", 9),
        new(0x0454, "Lao", "lao", 9),
        new(0x0451, "Tibetan", "tibetan", 9),
        new(0x0850, "Mongolian Traditional", "mongolian", 9),
        new(0x045E, "Amharic", "ethiopic", 9),
        new(0x0473, "Tigrinya", "ethiopic", 9),
        new(0x045C, "Cherokee", "cherokee", 9),
        new(0x045D, "Inuktitut Syllabics", "syllabics", 9),
        new(0x085D, "Inuktitut Latin", "latin", 9),
        new(0x0478, "Yi", "yi", 9),

        // --- Batch 10: Irish, alone, because its acceptance depends on process state ---
        //
        // Pass A creates 0x043C happily (lcid 1084 lands on disk) and repeats that on a re-run. The SAME
        // CreateFor call inside a key batch is refused, also repeatably, with a different message from every
        // other refusal on this machine — "Selected collating sequence not supported by the operating
        // system" rather than Jet's own "Incorrect collating sequence". The two passes differ only in what
        // else the process has done first, so Irish gets a batch to itself to say whether being measured
        // early is enough.
        new(0x043C, "Irish", "latin", 10),
    ];

    // ---------------------------------------------------------------------------------------------------
    // Pass A: what DAO accepts, and what lands on disk.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Survey_dao_acceptance()
    {
        object? engine = CreateDbEngine(out string progId);
        Assert.SkipWhen(engine is null, "DAO is not available in this process.");
        object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;

        var report = new StringBuilder();
        (int LangId, string Name)[] candidates = [.. AcceptanceCandidates()];
        Write(report, $"DAO engine: {progId}");
        Write(report, $"{candidates.Length} candidate LANGIDs: the curated table plus every culture Windows " +
                      "defines an LCID for");
        Write(report, $"{"LANGID",-8} {"name",-40} {"how",-9} {"lcid",-6} {"ver",-4} {"sortid",-7} " +
                      $"{"order",-28} encodable");

        var unencodable = new List<string>();
        foreach ((int langId, string name) in candidates)
        {
            string path = TemporaryDatabase.CreatePath("survey-dao-", ".accdb");
            try
            {
                string how = CreateFor(engine!, workspace, langId, path);
                if (how.StartsWith("rejected", StringComparison.Ordinal) ||
                    how.StartsWith("failed", StringComparison.Ordinal))
                {
                    Write(report, $"0x{langId:X4}   {name,-40} {how}");
                    continue;
                }

                using var db = JetDatabase.Open(path);
                Collation collation = db.Collation;
                Write(report,
                    $"0x{langId:X4}   {name,-40} {how,-9} {db.DefaultCollationLcid,-6} " +
                    $"{collation.Version,-4} {collation.SortId,-7} {collation.Order,-28} " +
                    $"{collation.IsIndexKeyEncodable}");
                if (!collation.IsIndexKeyEncodable)
                    unencodable.Add($"0x{langId:X4}   {name,-40} on disk {collation.Order} " +
                                    $"v{collation.Version} sortId {collation.SortId}");
            }
            catch (Exception ex)
            {
                Write(report, $"0x{langId:X4}   {name,-40} {ex.GetType().Name}: {ex.Message.Trim()}");
            }
            finally { TemporaryDatabase.Delete(path); }
        }

        // The only thing pass B still has to measure. Everything else either fails to create or lands on an
        // order LibRed already encodes — and an order that is already encodable has already been checked
        // against ACE by CreatedDatabaseCollationAccessTests, which builds a file in it and has ACE index it.
        Write(report, "");
        Write(report, $"=== {unencodable.Count} ORDERS CREATED THAT LIBRED CANNOT YET ENCODE ===");
        foreach (string line in unencodable) Write(report, line);

        Save("dao-acceptance.txt", report);
    }

    /// <summary>
    /// Every LANGID worth asking DAO for: the curated table above, plus the LANGID of every culture Windows
    /// defines an LCID for. The curated table alone is not the reachable set — DAO takes a <b>raw</b> LANGID,
    /// so the sublanguages are reachable too, and there are far more of those than base languages (Spanish
    /// has about twenty, Arabic and English about fifteen each).
    /// </summary>
    /// <remarks>
    /// Worth sweeping rather than predicting, because the sublanguages do NOT simply inherit their base
    /// language's order: Spanish (Mexico) <c>0x080A</c> measured as Spanish <b>Traditional</b> 1034, while the
    /// base order Access offers is Spanish <b>Modern</b> 3082. A survey that assumed inheritance would have
    /// recorded that one backwards.
    /// </remarks>
    private static IEnumerable<(int LangId, string Name)> AcceptanceCandidates()
    {
        var names = Candidates.ToDictionary(c => c.LangId, c => c.Name);
        var all = new SortedSet<int>(names.Keys);

        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            // 0x1000 is LOCALE_CUSTOM_UNSPECIFIED — a culture .NET knows but Windows has no LCID for, so
            // there is no LANGID to ask DAO for. The invariant culture is LCID 0x007F.
            if (culture.LCID is 0x1000 or 0x007F or 0) continue;
            int langId = culture.LCID & 0xFFFF;
            all.Add(langId);
            names.TryAdd(langId, $"{culture.Name} {culture.EnglishName}");
        }

        foreach (int langId in all) yield return (langId, names[langId]);
    }

    private static readonly Dictionary<int, CultureInfo> CultureByLangId = BuildCultureIndex();

    private static Dictionary<int, CultureInfo> BuildCultureIndex()
    {
        var index = new Dictionary<int, CultureInfo>();
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
            if (culture.LCID is not (0x1000 or 0x007F or 0))
                index.TryAdd(culture.LCID & 0xFFFF, culture);
        return index;
    }

    /// <summary>The writing system a LANGID's locale actually uses, so a batch can be given the Unicode
    /// blocks its orders need. The curated table's hand-checked answer wins; otherwise it is derived from the
    /// culture, script tag first — <c>sr-Cyrl</c> and <c>sr-Latn</c> are the same language and must not share
    /// a sample set.</summary>
    private static string ScriptFor(int langId)
    {
        if (Candidates.FirstOrDefault(c => c.LangId == langId) is { } curated) return curated.Script;
        if (!CultureByLangId.TryGetValue(langId, out CultureInfo? culture)) return "latin";

        string name = culture.Name;
        if (name.Contains("-Cyrl", StringComparison.Ordinal)) return "cyrillic";
        if (name.Contains("-Latn", StringComparison.Ordinal)) return "latin";
        if (name.Contains("-Arab", StringComparison.Ordinal)) return "arabic";
        if (name.Contains("-Deva", StringComparison.Ordinal)) return "devanagari";
        if (name.Contains("-Mong", StringComparison.Ordinal)) return "mongolian";
        if (name.Contains("-Tfng", StringComparison.Ordinal)) return "tifinagh";
        if (name.Contains("-Hans", StringComparison.Ordinal) ||
            name.Contains("-Hant", StringComparison.Ordinal)) return "cjk";

        return culture.TwoLetterISOLanguageName switch
        {
            "ar" or "fa" or "ps" or "ur" or "ug" or "ku" or "ckb" or "prs" or "sd" => "arabic",
            "he" or "yi" => "hebrew",
            "ru" or "bg" or "be" or "uk" or "mk" or "kk" or "ky" or "tg" or "tt" or "ba" or "sah"
                or "mn" or "cv" or "os" => "cyrillic",
            "el" => "greek",
            "hy" => "armenian",
            "ka" => "georgian",
            "hi" or "mr" or "sa" or "ne" or "kok" or "ks" or "brx" or "mai" => "devanagari",
            "bn" or "as" or "mni" => "bengali",
            "pa" => "gurmukhi",
            "gu" => "gujarati",
            "or" => "oriya",
            "ta" => "tamil",
            "te" => "telugu",
            "kn" => "kannada",
            "ml" => "malayalam",
            "si" => "sinhala",
            "th" => "thai",
            "lo" => "lao",
            "km" => "khmer",
            "bo" or "dz" => "tibetan",
            "my" => "myanmar",
            "am" or "ti" => "ethiopic",
            "chr" => "cherokee",
            "iu" or "cr" => "syllabics",
            "ii" => "yi",
            "syr" => "syriac",
            "dv" => "thaana",
            "tzm" => "tifinagh",
            "zh" or "ja" or "ko" => "cjk",
            _ => "latin",
        };
    }

    /// <summary>
    /// Everything pass B still has to measure: every LANGID DAO will take whose order LibRed cannot already
    /// encode, minus CJK. Derived rather than hand-listed, because the reachable set is 400-odd LANGIDs and a
    /// typed table of that size goes stale the moment a locale is implemented.
    /// </summary>
    /// <remarks>
    /// Grouped by script so a batch's sample set stays small — a batch always carries the Latin baseline plus
    /// the full Unicode block of every script its members are written in — then chunked so no single batch
    /// creates a hundred databases. Batch numbers are therefore positional and WILL shift as orders get
    /// implemented and drop out of this list; the report each batch writes names its own members.
    /// </remarks>
    private static Candidate[] KeyCandidates()
    {
        var pending = new List<Candidate>();
        foreach ((int langId, string name) in AcceptanceCandidates())
        {
            if (new Collation((CollatingOrder)langId, 0).IsIndexKeyEncodable) continue;
            string script = ScriptFor(langId);
            if (script == "cjk") continue;
            pending.Add(new Candidate(langId, name, script, Batch: -1));
        }

        var batched = new List<Candidate>();
        int batch = 0;
        foreach (IGrouping<string, Candidate> group in pending.GroupBy(c => c.Script).OrderBy(g => g.Key))
            foreach (Candidate[] chunk in group.Chunk(28))
            {
                batched.AddRange(chunk.Select(c => c with { Batch = batch }));
                batch++;
            }
        return [.. batched];
    }

    // ---------------------------------------------------------------------------------------------------
    // Pass B: the index keys, batch by batch.
    // ---------------------------------------------------------------------------------------------------

    [Fact] public void Survey_keys_batch_00() => SurveyBatch(0);
    [Fact] public void Survey_keys_batch_01() => SurveyBatch(1);
    [Fact] public void Survey_keys_batch_02() => SurveyBatch(2);
    [Fact] public void Survey_keys_batch_03() => SurveyBatch(3);
    [Fact] public void Survey_keys_batch_04() => SurveyBatch(4);
    [Fact] public void Survey_keys_batch_05() => SurveyBatch(5);
    [Fact] public void Survey_keys_batch_06() => SurveyBatch(6);
    [Fact] public void Survey_keys_batch_07() => SurveyBatch(7);
    [Fact] public void Survey_keys_batch_08() => SurveyBatch(8);
    [Fact] public void Survey_keys_batch_09() => SurveyBatch(9);
    [Fact] public void Survey_keys_batch_10() => SurveyBatch(10);
    [Fact] public void Survey_keys_batch_11() => SurveyBatch(11);
    [Fact] public void Survey_keys_batch_12() => SurveyBatch(12);
    [Fact] public void Survey_keys_batch_13() => SurveyBatch(13);
    [Fact] public void Survey_keys_batch_14() => SurveyBatch(14);
    [Fact] public void Survey_keys_batch_15() => SurveyBatch(15);
    [Fact] public void Survey_keys_batch_16() => SurveyBatch(16);
    [Fact] public void Survey_keys_batch_17() => SurveyBatch(17);
    [Fact] public void Survey_keys_batch_18() => SurveyBatch(18);
    [Fact] public void Survey_keys_batch_19() => SurveyBatch(19);
    [Fact] public void Survey_keys_batch_20() => SurveyBatch(20);
    [Fact] public void Survey_keys_batch_21() => SurveyBatch(21);
    [Fact] public void Survey_keys_batch_22() => SurveyBatch(22);
    [Fact] public void Survey_keys_batch_23() => SurveyBatch(23);
    [Fact] public void Survey_keys_batch_24() => SurveyBatch(24);
    [Fact] public void Survey_keys_batch_25() => SurveyBatch(25);
    [Fact] public void Survey_keys_batch_26() => SurveyBatch(26);
    [Fact] public void Survey_keys_batch_27() => SurveyBatch(27);
    [Fact] public void Survey_keys_batch_28() => SurveyBatch(28);
    [Fact] public void Survey_keys_batch_29() => SurveyBatch(29);
    [Fact] public void Survey_keys_batch_30() => SurveyBatch(30);
    [Fact] public void Survey_keys_batch_31() => SurveyBatch(31);
    [Fact] public void Survey_keys_batch_32() => SurveyBatch(32);
    [Fact] public void Survey_keys_batch_33() => SurveyBatch(33);
    [Fact] public void Survey_keys_batch_34() => SurveyBatch(34);
    [Fact] public void Survey_keys_batch_35() => SurveyBatch(35);

    // ---------------------------------------------------------------------------------------------------
    // Pass C: turn the measurements into code.
    //
    // 218 enum members and 42 dictionary entries is more than anyone should retype off a report. This reads
    // the batch reports back and writes the fragment, so a wrong LCID cannot come from a transcription slip —
    // and re-running it after a re-measurement produces the new fragment rather than a diff to apply by hand.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Emit_implementation_fragment()
    {
        string directory = Environment.GetEnvironmentVariable("LIBRED_SURVEY_OUT")
            ?? Path.Combine(AppContext.BaseDirectory, "collation-survey");
        string[] reports = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "batch-*.txt") : [];
        Assert.SkipWhen(reports.Length == 0, $"no batch reports in {directory} - run the key batches first");

        var identical = new List<(int LangId, string Id, string Name)>();
        var aliases = new List<(int LangId, string Id, string Name, string Matches)>();
        var clashes = new List<string>();
        var used = new HashSet<string>(Enum.GetNames<CollatingOrder>(), StringComparer.Ordinal);

        foreach (string line in reports.SelectMany(File.ReadAllLines))
        {
            if (!line.StartsWith("SUMMARY 0x", StringComparison.Ordinal)) continue;
            int langId = Convert.ToInt32(line.Substring(10, 4), 16);
            string identifier = Identifier(langId);
            if (!used.Add(identifier)) clashes.Add($"{identifier} (0x{langId:X4})");

            string name = CultureByLangId.TryGetValue(langId, out CultureInfo? c) ? c.EnglishName : "?";
            if (line.Contains("IDENTICAL TO GENERAL v0", StringComparison.Ordinal))
                identical.Add((langId, identifier, name));
            else
                aliases.Add((langId, identifier, name,
                    line[(line.IndexOf("matches implemented: ", StringComparison.Ordinal) + 21)..]));
        }

        var report = new StringBuilder();
        Write(report, $"// {identical.Count} orders measured identical to General v0, {aliases.Count} " +
                      "measured equal to an existing tailoring.");
        Write(report, clashes.Count == 0
            ? "// No identifier clashes."
            : $"// !! IDENTIFIER CLASHES, fix by hand: {string.Join(", ", clashes)}");

        Write(report, "");
        Write(report, "// ---- CollatingOrder members ----");
        foreach ((int langId, string id, string name) in identical.Concat(
                     aliases.Select(a => (a.LangId, a.Id, a.Name))).OrderBy(e => e.LangId))
            Write(report, $"    {id} = {langId},{new string(' ', Math.Max(1, 34 - id.Length - $"{langId}".Length))}// 0x{langId:X4} {name}");

        Write(report, "");
        Write(report, "// ---- JetLocaleTailoring.GeneralV0 ----");
        foreach (IGrouping<string, (int LangId, string Id, string Name)> group in
                 identical.GroupBy(e => ScriptFor(e.LangId)).OrderBy(g => g.Key))
        {
            Write(report, $"        // {group.Key}");
            foreach ((_, string id, _) in group.OrderBy(e => e.LangId))
                Write(report, $"        CollatingOrder.{id},");
        }

        Write(report, "");
        Write(report, "// ---- JetLocaleTailoring.Tailorings (aliases) ----");
        foreach ((int langId, string id, _, string matches) in aliases.OrderBy(a => a.LangId))
            Write(report, $"        [new Collation(CollatingOrder.{id}, 0)] = ???,   // {matches}");

        Save("implementation.txt", report);
    }

    /// <summary>A C# identifier for a LANGID, from the culture's English name. A <b>neutral</b> culture is
    /// suffixed, because its language almost always has a region-specific member already and they are
    /// different orders — Bengali is 1093 while the neutral <c>bn</c> is 69. Neutrality is asked of the
    /// culture rather than derived from the sublanguage bits: <c>sr-Cyrl</c> (0x6C1A) is region-neutral but
    /// its sublanguage field is 0x1B, so a bitmask misses it and it collides with Serbian Cyrillic 3098.
    /// </summary>
    private const char MaxAscii = (char)0x7F;

    private static string Identifier(int langId)
    {
        if (!CultureByLangId.TryGetValue(langId, out CultureInfo? culture)) return $"Order{langId}";

        // Fold to ASCII first. C# would accept Māori, Kʼicheʼ and Bokmål verbatim — U+02BC is a letter, so
        // even the apostrophe survives IsLetterOrDigit — but nobody can type them, and an identifier nobody
        // can type is worse than a slightly anglicised one.
        string folded = culture.EnglishName.Normalize(NormalizationForm.FormD);
        var text = new StringBuilder();
        bool upper = true;
        foreach (char c in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            if (c > MaxAscii) { upper = true; continue; }
            if (char.IsLetterOrDigit(c)) { text.Append(upper ? char.ToUpperInvariant(c) : c); upper = false; }
            else upper = true;
        }
        if (text.Length > 0 && char.IsDigit(text[0])) text.Insert(0, 'N');
        if (culture.IsNeutralCulture) text.Append("Neutral");
        return text.ToString();
    }

    // A batch number past the end is an empty batch and skips, so the only way to notice a batch that never
    // ran is to count. This fails if KeyCandidates produces more batches than there are methods above.
    [Fact]
    public void Every_batch_has_a_test_method()
    {
        Candidate[] candidates = KeyCandidates();
        Assert.SkipWhen(candidates.Length == 0, "nothing left to measure");
        int batches = candidates.Max(c => c.Batch) + 1;
        int methods = typeof(CollationSurveyProbeTests).GetMethods()
            .Count(m => m.Name.StartsWith("Survey_keys_batch_", StringComparison.Ordinal));
        Assert.True(batches <= methods,
            $"{candidates.Length} candidates need {batches} batches but only {methods} methods exist - " +
            $"add Survey_keys_batch_{methods:00} and up, or orders go unmeasured in silence.");
    }

    private void SurveyBatch(int batch)
    {
        object? engine = CreateDbEngine(out string progId);
        Assert.SkipWhen(engine is null, "DAO is not available in this process.");
        object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;

        Candidate[] members = [.. KeyCandidates().Where(c => c.Batch == batch)];
        Assert.SkipWhen(members.Length == 0, $"batch {batch} is empty");

        string[] samples = Samples(members.Select(m => m.Script));
        var report = new StringBuilder();
        Write(report, $"batch {batch:00} - DAO engine {progId}, {samples.Length} samples, " +
                      $"scripts: {string.Join(" ", members.Select(m => m.Script).Distinct().Order())}");

        // The General v0 baseline, measured from ACE in this same run rather than assumed.
        Dictionary<string, string>? general = AceKeysFor(engine!, workspace, GeneralLangId, samples, report);
        if (general is null) { Write(report, "!! the General baseline could not be built - batch aborted"); Save($"batch-{batch:00}.txt", report); return; }

        // Harness self-check: LibRed's own General v0 encoder against the ACE baseline just measured. The
        // baseline argument is what Compare labels "GEN", so LibRed's keys go there and ACE's in the other
        // slot — the reverse of the per-candidate calls below, where the baseline really is General.
        Compare("SELF-CHECK LibRed v0 encoder (GEN column) vs ACE General v0 (ACE column)",
            LibRedKeys(samples, Collation.GeneralLegacy), general, report, printAll: false);

        Dictionary<string, string> libredV1 = LibRedKeys(samples, Collation.General);

        // Every order LibRed can already encode, run through LibRed's own encoder over this sample set.
        var encodable = Implemented.ToDictionary(
            e => e.Name, e => LibRedKeys(samples, e.Collation));

        // The positive controls, in this batch's own sample set.
        foreach (int control in new[] { CzechControl, TurkishControl })
        {
            Dictionary<string, string>? keys = AceKeysFor(engine!, workspace, control, samples, report);
            if (keys is null) continue;
            Write(report, "");
            Write(report, $"=== POSITIVE CONTROL 0x{control:X4} ===");
            int departures = Compare($"control 0x{control:X4} vs ACE General v0", general, keys, report, printAll: false);
            if (departures == 0)
                Write(report, $"!!!! CONTROL 0x{control:X4} SHOWS ZERO DEPARTURES - THE HARNESS IS BROKEN. " +
                              "Every 'identical to General' result in this batch is worthless.");
        }

        foreach (Candidate candidate in members)
        {
            Write(report, "");
            Write(report, $"=== 0x{candidate.LangId:X4} {candidate.Name} ({candidate.Script}) ===");
            Dictionary<string, string>? keys = AceKeysFor(engine!, workspace, candidate.LangId, samples, report);
            if (keys is null) continue;

            int vs0 = Compare("vs ACE General v0", general, keys, report, printAll: true);
            int vs1 = Compare("vs LibRed General v1", libredV1, keys, report, printAll: false, printNone: true);

            // Which order LibRed ALREADY encodes, if any, produces these exact bytes? An order that matches
            // an implemented tailoring costs one dictionary entry, not a weight table — so this is the single
            // most useful number in the sweep. Ranked by departure count so the near-misses are visible too.
            Write(report, "  nearest implemented orders:");
            foreach ((string name, int departures) in encodable
                         .Select(e => (e.Key, Departures(e.Value, keys)))
                         .OrderBy(e => e.Item2).Take(4))
                Write(report, $"    {name,-34} {(departures == 0 ? "EXACT MATCH" : $"{departures} departures")}");

            string exact = string.Join(", ", encodable.Where(e => Departures(e.Value, keys) == 0).Select(e => e.Key));
            Write(report, $"SUMMARY 0x{candidate.LangId:X4} {candidate.Name}: " +
                          $"{(vs0 == 0 ? "IDENTICAL TO GENERAL v0" : $"{vs0} departures from General v0")}; " +
                          $"{(vs1 == 0 ? "IDENTICAL TO GENERAL v1" : $"{vs1} departures from General v1")}; " +
                          $"matches implemented: {(exact.Length == 0 ? "(none)" : exact)}");
        }

        Save($"batch-{batch:00}.txt", report);
    }

    /// <summary>
    /// The collations LibRed can already encode — the General pair plus every key of
    /// <c>JetLocaleTailoring.Tailorings</c>. Mirrored here rather than read from the (internal, private)
    /// dictionary, so if that dictionary grows this list must be updated with it.
    /// </summary>
    private static readonly (string Name, Collation Collation)[] Implemented =
    [
        ("General v0 (1033)", Collation.GeneralLegacy),
        ("General v1 (1033)", Collation.General),
        ("Georgian Modern (1079 sortId 1)", new Collation(CollatingOrder.Georgian, 0, SortId: 1)),
        ("Indic v1 (1081)", new Collation(CollatingOrder.Indic, Collation.GeneralVersion)),
        ("Thai (1054)", new Collation(CollatingOrder.Thai, 0)),
        ("Croatian v1 (1050)", new Collation(CollatingOrder.Croatian, Collation.GeneralVersion)),
        ("Bosnian v1 (5146)", new Collation(CollatingOrder.Bosnian, Collation.GeneralVersion)),
        ("Serbian v1 (2074)", new Collation(CollatingOrder.Serbian, Collation.GeneralVersion)),
        ("French (1036)", new Collation(CollatingOrder.French, 0)),
        ("Spanish Modern (3082)", new Collation(CollatingOrder.SpanishModern, 0)),
        ("Spanish Traditional (1034)", new Collation(CollatingOrder.Spanish, 0)),
        ("German Phone Book (1031 sortId 1)", new Collation(CollatingOrder.German, 0, SortId: 1)),
        ("Polish (1045)", new Collation(CollatingOrder.Polish, 0)),
        ("Romanian Legacy (1048)", new Collation(CollatingOrder.Romanian, 0)),
        ("Turkish (1055)", new Collation(CollatingOrder.Turkish, 0)),
        ("Czech (1029)", new Collation(CollatingOrder.Czech, 0)),
        ("Slovak (1051)", new Collation(CollatingOrder.Slovak, 0)),
        ("Croatian Legacy (1050)", new Collation(CollatingOrder.Croatian, 0)),
        ("Slovenian (1060)", new Collation(CollatingOrder.Slovenian, 0)),
        ("Norwegian/Danish (1044)", new Collation(CollatingOrder.Norwegian, 0)),
        ("Swedish/Finnish (1053)", new Collation(CollatingOrder.SwedishFinnish, 0)),
        ("Icelandic (1039)", new Collation(CollatingOrder.Icelandic, 0)),
        ("Estonian (1061)", new Collation(CollatingOrder.Estonian, 0)),
        ("Latvian (1062)", new Collation(CollatingOrder.Latvian, 0)),
        ("Lithuanian (1063)", new Collation(CollatingOrder.Lithuanian, 0)),
        ("Vietnamese (1066)", new Collation(CollatingOrder.Vietnamese, 0)),
        ("Ukrainian (1058)", new Collation(CollatingOrder.Ukrainian, 0)),
        ("Macedonian (1071)", new Collation(CollatingOrder.Macedonian, 0)),
        ("Hungarian (1038)", new Collation(CollatingOrder.Hungarian, 0)),
        ("Hungarian Technical (1038 sortId 1)", new Collation(CollatingOrder.Hungarian, 0, SortId: 1)),
    ];

    private static int Departures(Dictionary<string, string> baseline, Dictionary<string, string> other)
    {
        int departures = 0;
        foreach ((string value, string expected) in baseline)
            if (other.TryGetValue(value, out string? actual) && actual != expected)
                departures++;
        return departures;
    }

    /// <summary>Compares two key maps over the values both hold, reporting the departures.</summary>
    private int Compare(string label, Dictionary<string, string> baseline, Dictionary<string, string> other,
        StringBuilder report, bool printAll, bool printNone = false)
    {
        var departures = new List<string>();
        int shared = 0;
        foreach ((string value, string expected) in baseline)
        {
            if (!other.TryGetValue(value, out string? actual)) continue;
            shared++;
            if (actual != expected)
                departures.Add($"  DEP {Describe(value),-24} GEN {expected,-34} ACE {actual}");
        }

        Write(report, $"{label}: {departures.Count} departures over {shared} shared values");
        if (printNone) return departures.Count;
        foreach (string line in printAll ? departures : departures.Take(12))
            Write(report, line);
        if (!printAll && departures.Count > 12) Write(report, $"  ... {departures.Count - 12} more");
        return departures.Count;
    }

    /// <summary>LibRed's own encoder over the sample set, for the collation given.</summary>
    private static Dictionary<string, string> LibRedKeys(string[] samples, Collation collation)
    {
        var keys = new Dictionary<string, string>();
        foreach (string sample in samples)
        {
            var column = new ColumnDef { Name = "K", Type = JetDataType.Text, Index = 0, Collation = collation };
            try { keys[sample] = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [sample])); }
            catch (NotSupportedException) { /* not encodable - simply absent */ }
        }
        return keys;
    }

    /// <summary>Creates a database in the locale, has ACE build and populate an indexed text column, then
    /// reads the stored keys back with LibRed. Null means the order could not be measured at all.</summary>
    private Dictionary<string, string>? AceKeysFor(object engine, object workspace, int langId,
        string[] samples, StringBuilder report)
    {
        string path = TemporaryDatabase.CreatePath("survey-keys-", ".accdb");
        try
        {
            string how = CreateFor(engine, workspace, langId, path);
            if (how.StartsWith("rejected", StringComparison.Ordinal) ||
                how.StartsWith("failed", StringComparison.Ordinal))
            {
                Write(report, $"  0x{langId:X4}: {how}");
                return null;
            }

            Collation collation;
            using (var db = JetDatabase.Open(path)) collation = db.Collation;
            Write(report, $"  0x{langId:X4}: created by {how}; on disk {collation.Order} v{collation.Version} " +
                          $"sortId {collation.SortId} (lcid {collation.Lcid}); " +
                          $"LibRed encodable = {collation.IsIndexKeyEncodable}");

            try
            {
                using var connection = AceTestDatabase.Open(path);
                Exec(connection, "CREATE TABLE CollSurvey (K TEXT(100), V LONG)");
                Exec(connection, "CREATE INDEX IX_CollSurvey ON CollSurvey (K)");
                for (int i = 0; i < samples.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO CollSurvey (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", samples[i]);
                    insert.Parameters.AddWithValue("v", i);
                    try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
                }
            }
            catch (Exception ex)
            {
                Write(report, $"  0x{langId:X4}: ACE refused the file: {ex.GetType().Name}: {ex.Message.Trim()}");
                return null;
            }

            using var opened = JetDatabase.Open(path);
            var table = opened.OpenTable("CollSurvey");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_CollSurvey");
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

    /// <summary>Creates an ACCDB in the given LANGID, trying CreateDatabase and then CompactDatabase — the
    /// two entry points do not necessarily accept the same set.</summary>
    private static string CreateFor(object engine, object workspace, int langId, string path)
    {
        string locale = $";LANGID=0x{langId:X4};CP=1252;COUNTRY=0";
        try
        {
            object database = Invoke(workspace, "CreateDatabase", path, locale, Ace12)!;
            Invoke(database, "Close");
            return "Create";
        }
        catch (Exception createFailure)
        {
            TemporaryDatabase.Delete(path);
            // CompactDatabase takes the destination locale as its own argument, and the sweep that first
            // found this surface used that entry point rather than CreateDatabase.
            string source = TemporaryDatabase.CreatePath("survey-src-", ".accdb");
            try
            {
                object seed = Invoke(workspace, "CreateDatabase", source,
                    ";LANGID=0x0409;CP=1252;COUNTRY=0", Ace12)!;
                Invoke(seed, "Close");
                Invoke(engine, "CompactDatabase", source, path, locale);
                return "Compact";
            }
            catch (Exception compactFailure)
            {
                string a = (createFailure as TargetInvocationException)?.InnerException?.Message
                    ?? createFailure.Message;
                string b = (compactFailure as TargetInvocationException)?.InnerException?.Message
                    ?? compactFailure.Message;
                return $"rejected by DAO: create='{a.Trim()}' compact='{b.Trim()}'";
            }
            finally { TemporaryDatabase.Delete(source); }
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // Samples.
    // ---------------------------------------------------------------------------------------------------

    /// <summary>Printable ASCII, Latin-1, Latin Extended-A, the parts of Latin Extended-B and Latin Extended
    /// Additional that carry tailored letters, punctuation, a word list — plus the full block of every script
    /// this batch's locales are actually written in.</summary>
    private static string[] Samples(IEnumerable<string> scripts)
    {
        var samples = new List<string>();
        for (char c = ' '; c <= '~'; c++) samples.Add(c.ToString());
        AddBlock(samples, 0x00A0, 0x017F);          // Latin-1 supplement and Latin Extended-A, in full
        foreach (int c in LatinExtendedB) samples.Add(((char)c).ToString());
        AddBlock(samples, 0x1EA0, 0x1EF9);          // the precomposed Vietnamese vowels
        AddBlock(samples, 0x2010, 0x2027);          // dashes and quotes, where the word-sort ignorables live
        samples.AddRange(BaseWords);

        foreach (string script in scripts.Distinct().Order())
        {
            foreach ((int first, int last) in Blocks(script)) AddBlock(samples, first, last);
            samples.AddRange(ScriptWords(script));
        }

        return [.. samples.Distinct()];
    }

    private static void AddBlock(List<string> samples, int first, int last)
    {
        for (int c = first; c <= last; c++)
            if (!char.IsControl((char)c) && !char.IsSurrogate((char)c))
                samples.Add(((char)c).ToString());
    }

    private static (int First, int Last)[] Blocks(string script) => script switch
    {
        "greek" => [(0x0370, 0x03FF), (0x1F00, 0x1FFE)],
        "cyrillic" => [(0x0400, 0x052F)],
        "armenian" => [(0x0530, 0x058A)],
        "hebrew" => [(0x0590, 0x05F4)],
        "arabic" => [(0x0600, 0x06FF), (0x0750, 0x077F)],
        "syriac" => [(0x0700, 0x074F)],
        "thaana" => [(0x0780, 0x07B1)],
        "devanagari" => [(0x0900, 0x097F)],
        "bengali" => [(0x0980, 0x09FF)],
        "gurmukhi" => [(0x0A00, 0x0A7F)],
        "gujarati" => [(0x0A80, 0x0AFF)],
        "oriya" => [(0x0B00, 0x0B7F)],
        "tamil" => [(0x0B80, 0x0BFF)],
        "telugu" => [(0x0C00, 0x0C7F)],
        "kannada" => [(0x0C80, 0x0CFF)],
        "malayalam" => [(0x0D00, 0x0D7F)],
        "sinhala" => [(0x0D80, 0x0DFF)],
        "thai" => [(0x0E01, 0x0E5B)],
        "lao" => [(0x0E80, 0x0EFF)],
        "tibetan" => [(0x0F00, 0x0FBF)],
        "georgian" => [(0x10A0, 0x10FF)],
        "ethiopic" => [(0x1200, 0x12FF)],
        "cherokee" => [(0x13A0, 0x13F4)],
        "syllabics" => [(0x1400, 0x14FF)],
        "khmer" => [(0x1780, 0x17FF)],
        "mongolian" => [(0x1800, 0x18AA)],
        "myanmar" => [(0x1000, 0x109F)],
        "tifinagh" => [(0x2D30, 0x2D7F)],
        "yi" => [(0xA000, 0xA0FF)],
        _ => [],
    };

    /// <summary>The Latin Extended-B characters that locale tailorings actually reach: horned vowels,
    /// the digraph ligatures, the caron letters, comma-below, and the ligature/expansion cases.</summary>
    private static readonly int[] LatinExtendedB =
    [
        0x0180, 0x0181, 0x0187, 0x0188, 0x018F, 0x0192, 0x0197, 0x019A, 0x019D, 0x019F,
        0x01A0, 0x01A1, 0x01AF, 0x01B0, 0x01B5, 0x01B6, 0x01B7, 0x01B8,
        0x01C4, 0x01C5, 0x01C6, 0x01C7, 0x01C8, 0x01C9, 0x01CA, 0x01CB, 0x01CC,
        0x01CD, 0x01CE, 0x01CF, 0x01D0, 0x01D1, 0x01D2, 0x01D3, 0x01D4, 0x01D5, 0x01D6,
        0x01D7, 0x01D8, 0x01D9, 0x01DA, 0x01DB, 0x01DC, 0x01DD,
        0x01E2, 0x01E3, 0x01E6, 0x01E7, 0x01E8, 0x01E9, 0x01EA, 0x01EB,
        0x01F0, 0x01F1, 0x01F2, 0x01F3, 0x01F7, 0x01F8, 0x01F9,
        0x01FA, 0x01FB, 0x01FC, 0x01FD, 0x01FE, 0x01FF,
        0x0218, 0x0219, 0x021A, 0x021B, 0x0237, 0x0250, 0x0259, 0x0292,
    ];

    /// <summary>Words and pairs, for the rules a single character cannot expose: contractions, expansions,
    /// and the two-accent shape that is the only way a reversed diacritic section shows itself.</summary>
    private static readonly string[] BaseWords =
    [
        "apple", "Apple", "APPLE", "cafe", "café", "Angstrom", "Ångström", "O'Brien", "Anne-Marie",
        "co-op", "coop", "coté", "côte", "côté", "cote", "éà", "àé", "éàé", "aé", "éa", "éaé",
        "Łódź", "Kraków", "İstanbul", "Isparta", "ırmak", "Ğğ", "München", "Grüße", "Bär", "Baer",
        "România", "Timișoara", "Iași", "señor", "senor", "mañana",
        "ch", "cch", "chh", "ll", "lll", "llll", "cs", "dz", "dzs", "gy", "ly", "ny", "sz", "ty", "zs",
        "ccs", "ddz", "ggy", "lly", "nny", "ssz", "tty", "zzs", "gyy", "hc", "dzz",
        "lj", "nj", "dž", "ddž", "llj", "nnj", "aa", "aaa", "aab", "baa", "Aa", "AA",
        "ij", "IJ", "ijsbeer", "ijsvrij", "yoghurt", "bijzonder", "byzantijns",
        "aa", "å", "aaa", "Aarhus", "Ålborg", "vw", "wv", "Waage", "Vaage",
        "gh", "ngh", "ng", "nh", "ph", "qu", "th", "tr", "kh", "gi",
        "chico", "llama", "coche", "calle", "chata", "hodina", "cukr",
        "ljubav", "njegov", "džem", "meggy", "asszony", "nagy", "cukor", "csak",
        // An ignorable after a two-byte primary, which distinguishes counting weights from counting bytes.
        "£-", "©-", "½-", "£A-", "A£-", "£'", "€-B",
    ];

    private static string[] ScriptWords(string script) => script switch
    {
        "greek" =>
        [
            "έάν", "άέν", "ελληνικά", "Ελληνικά", "ώρα", "ωρά", "άα", "αά", "άαά",
            "σ", "ς", "σοφός", "ΣΟΦΟΣ", "Ωμέγα", "αϊ", "άι", "ΐ", "ΰ",
        ],
        "cyrillic" =>
        [
            "ёлка", "мёд", "йогурт", "майор", "ель", "ел", "съезд", "сезд",
            "ґанок", "ганок", "їжак", "ижак", "ѓеорѓи", "ќерка", "ў", "ъ", "ь",
            "ә", "ө", "ұ", "ү", "һ", "і", "ң", "ғ", "қ", "ҳ", "ҷ", "ҵ",
        ],
        "hebrew" =>
        [
            "שָׁלוֹם", "שלום", "בְּרֵאשִׁית", "בראשית", "אב", "כך", "מם", "צץ", "נן", "פף",
            "אָ", "אַ", "אָא", "אאָ", "שׁ", "שׂ",
        ],
        "arabic" =>
        [
            "مُحَمَّد", "محمد", "كِتَاب", "كتاب", "بِسْمِ", "بسم", "أب", "إب", "آب", "اب",
            "ة", "ه", "ى", "ي", "لا", "لآ", "لأ", "لإ", "پژگچ", "ک", "ی", "ڵ", "ۆ",
            // U+0651 SHADDA in every position, because the self-check departs on exactly one word that
            // carries it. Alone it weighs the anomalous FF FF that the spec records; the survey shows ACE
            // giving it 06A3 inside a word, so the isolated measurement the table was built from is not the
            // whole rule. These say whether what matters is having a base letter, having another mark
            // before it, or the shadda's position in the string.
            "مّ", "ّم", "مَّ", "مَّ",
            "مّم", "مَمّ", "ّّ", "مّّ",
            "اّ", "مُحَمَّد",
            // These render identically to each other in an editor - Arabic runs right to left and a
            // combining mark draws on its base either way - so read the pair on the next two lines as the
            // comments describe them, not as they look. The report does not depend on it: every departure
            // it prints names the sequence by code point.
            "ّ",                        // shadda alone - the isolated case the v0 table was built from
            "مّ",                  // meem + shadda
            "ّم",                  // shadda leading, no base letter
            "مَّ",            // meem, fatha, shadda   - a mark BEFORE the shadda
            "مَّ",            // meem, shadda, fatha   - a mark AFTER the shadda
            "مّم",            // shadda between two letters
            "ممّ",            // shadda last
            "ّّ",                  // two shaddas, no base
            "مّّ",            // meem + two shaddas
            "اّ",                  // alef + shadda - a different base letter
            "مُحَمَّد",   // the departing word itself
        ],
        "devanagari" =>
        [
            "क", "का", "कि", "की", "क्ष", "त्र", "ज्ञ", "कं", "कः", "क़", "ड़", "ढ़",
            "अ", "आ", "इ", "ई", "हिन्दी", "संस्कृत", "मराठी", "नेपाली",
        ],
        "bengali" => ["ক", "কা", "কি", "ক্ষ", "বাংলা", "অসমীয়া", "ড়", "ঢ়", "য়"],
        "gurmukhi" => ["ਕ", "ਕਾ", "ਪੰਜਾਬੀ", "ਖ਼", "ਗ਼", "ਜ਼", "ੜ", "ਫ਼"],
        "gujarati" => ["ક", "કા", "ગુજરાતી", "ક્ષ", "જ્ઞ"],
        "oriya" => ["କ", "କା", "ଓଡ଼ିଆ", "କ୍ଷ"],
        "tamil" => ["க", "கா", "தமிழ்", "க்ஷ", "ஸ்ரீ", "ஜ", "ஷ"],
        "telugu" => ["క", "కా", "తెలుగు", "క్ష", "ఱ"],
        "kannada" => ["ಕ", "ಕಾ", "ಕನ್ನಡ", "ಕ್ಷ", "ೞ"],
        "malayalam" => ["ക", "കാ", "മലയാളം", "ക്ഷ", "ൺ", "ൻ", "ർ", "ൽ", "ൾ"],
        "sinhala" => ["ක", "කා", "සිංහල", "ඤ්ජ", "ං", "ඃ"],
        "thai" => ["เก", "แก", "โก", "ใก", "ไก", "กเ", "เกา", "เก้า", "ไก่", "ไทย", "แดง", "ภาษาไทย"],
        "lao" => ["ເກ", "ແກ", "ໂກ", "ໃກ", "ໄກ", "ລາວ", "ພາສາລາວ"],
        "tibetan" => ["ཀ", "ཀི", "བོད", "ཀྱ", "ཞ"],
        "georgian" => ["ა", "ბ", "ქართული", "ჭ", "ჯ", "ჰ", "ჱ", "ჲ", "ჳ", "ჴ", "ჵ"],
        "armenian" => ["ա", "բ", "հայերեն", "և", "ու", "ՈՒ", "ը", "ֆ"],
        "ethiopic" => ["ሀ", "ሁ", "ሂ", "አማርኛ", "ትግርኛ", "ሠ", "ሰ", "ጸ", "ፀ"],
        "cherokee" => ["Ꭰ", "Ꭱ", "ᏣᎳᎩ"],
        "syllabics" => ["ᐃ", "ᐄ", "ᐃᓄᒃᑎᑐᑦ"],
        "khmer" => ["ក", "កា", "ខ្មែរ", "ញ", "ឣ"],
        "mongolian" => ["ᠠ", "ᠮᠣᠩᠭᠣᠯ"],
        "myanmar" => ["က", "ကာ", "မြန်မာ", "ကြ", "ဿ"],
        "tifinagh" => ["ⴰ", "ⴱ", "ⵜⴰⵎⴰⵣⵉⵖⵜ", "ⵯ"],
        "syriac" => ["ܐ", "ܒ", "ܣܘܪܝܝܐ"],
        "thaana" => ["ހ", "ށ", "ދިވެހި"],
        "yi" => ["ꀀ", "ꀁ"],
        _ => [],
    };

    // ---------------------------------------------------------------------------------------------------
    // Plumbing.
    // ---------------------------------------------------------------------------------------------------

    private void Write(StringBuilder report, string line)
    {
        report.AppendLine(line);
        output.WriteLine(line);
    }

    private void Save(string fileName, StringBuilder report)
    {
        // Under the test assembly's own directory, which is inside the repo and already gitignored. NOT the
        // system temp folder: reading a path outside the working directory needs approval, so a report
        // written there cannot be read back by an unattended run — which is exactly how the first attempt at
        // this survey hung after pass A.
        string directory = Environment.GetEnvironmentVariable("LIBRED_SURVEY_OUT")
            ?? Path.Combine(AppContext.BaseDirectory, "collation-survey");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, report.ToString());
        output.WriteLine($"[written] {path}");
    }

    private static string Describe(string s) =>
        s.All(c => c is >= ' ' and <= '~') ? $"\"{s}\"" : string.Concat(s.Select(c => $"U+{(int)c:X4} ")).Trim();

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
