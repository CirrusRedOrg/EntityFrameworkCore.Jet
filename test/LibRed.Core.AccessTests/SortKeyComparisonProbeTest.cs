using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE (no assertions about desired behaviour): how do .NET's sort keys relate to the text index keys ACE
// actually writes?
//
// LibRed encodes text index keys from hand-built weight tables (JetTextCollation), verified value by value
// against ACE. The open question is whether a platform API could produce them instead — which matters most
// for the General (v1) order, whose keys LibRed reads but cannot yet encode (Collation.IsIndexKeyEncodable).
//
// Four encodings per string, so the shapes can be compared directly:
//   1. ACE      - the bytes ACE stored in its own index (ground truth).
//   2. LibRed   - IndexKeyEncoder over the same value.
//   3. ICU/NLS  - CompareInfo.GetSortKey, which is what .NET gives you. Since .NET 5 this is ICU on every
//                 platform unless System.Globalization.UseNls is set, so it is NOT the Win32 sort key.
//   4. LCMapStringEx(LCMAP_SORTKEY) - the Win32 NLS API Jet itself used, called directly so the comparison
//                 does not depend on which globalization backend .NET happens to be using.
public class SortKeyComparisonProbeTest(ITestOutputHelper output)
{
    private const uint LcmapSortkey = 0x00000400;
    private const uint NormIgnoreCase = 0x00000001;

    [DllImport("kernel32.dll", EntryPoint = "LCMapStringEx", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(
        string localeName, uint mapFlags, string src, int srcLen,
        byte[]? dest, int destLen, IntPtr versionInformation, IntPtr reserved, IntPtr sortHandle);

    private static readonly string[] Samples =
    [
        "apple", "Apple", "APPLE", "banana",
        "cafe", "café", "CAFÉ",
        "O'Brien", "OBrien", "Anne-Marie", "AnneMarie",
        "a b", "ab", "a1", "Ä", "ä", "z", "",
    ];

    [Fact]
    public void Probe_sort_keys_against_ace_index_keys()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "sortkey-probe-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE SortProbe (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_SortProbe ON SortProbe (K)");
                for (int i = 0; i < Samples.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO SortProbe (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", Samples[i]);
                    insert.Parameters.AddWithValue("v", i);
                    insert.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("SortProbe");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_SortProbe");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            ColumnDef valueColumn = table.Definition.FindColumn("V")!;

            output.WriteLine($"collation: {keyColumn.Collation.Order} v{keyColumn.Collation.Version}   " +
                             $"(.NET globalization backend: {(UsingIcu() ? "ICU" : "NLS")})");
            output.WriteLine("");

            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            {
                if (!rows.TryGetValue(rowId, out object?[]? values)) continue;
                string value = (string?)values[keyColumn.Index] ?? "";

                var aligned = new object?[table.Definition.Columns.Count];
                aligned[keyColumn.Index] = values[keyColumn.Index];
                byte[] libred = IndexKeyEncoder.Encode(index.Columns, aligned);

                output.WriteLine($"\"{value}\"  (row V={values[valueColumn.Index]})");
                output.WriteLine($"    ACE     {Hex(stored)}");
                output.WriteLine($"    LibRed  {Hex(libred)}{(Hex(libred) == Hex(stored) ? "   == ACE" : "   != ACE")}");
                output.WriteLine($"    GetSort {Hex(SortKey(value))}");
                output.WriteLine($"    LCMap   {Hex(WinSortKey(value))}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The single-character mapping, across a wide set: ACE's primary weight (LibRed's table, verified equal to
    // ACE above) against the Win32 NLS primary for the same character. If ACE's table is an order-preserving
    // compaction of the NLS weights, sorting by one must sort by the other.
    [Fact]
    public void Probe_primary_weight_mapping_against_win32()
    {
        var mapping = new List<(char Ch, int Ace, int Nls)>();
        foreach (char ch in "abcdefghijklmnopqrstuvwxyz0123456789")
        {
            byte[] ace = JetTextPrimary(ch);
            byte[] nls = WinSortKey(ch.ToString());
            if (ace.Length != 1 || nls.Length < 2) continue;
            mapping.Add((ch, ace[0], (nls[0] << 8) | nls[1]));
        }

        output.WriteLine("char   ACE   NLS");
        foreach ((char ch, int ace, int nls) in mapping)
            output.WriteLine($"  {ch}    {ace:X2}   {nls:X4}");

        var byAce = mapping.OrderBy(m => m.Ace).Select(m => m.Ch).ToArray();
        var byNls = mapping.OrderBy(m => m.Nls).Select(m => m.Ch).ToArray();
        output.WriteLine("");
        output.WriteLine($"order by ACE weight: {new string(byAce)}");
        output.WriteLine($"order by NLS weight: {new string(byNls)}");
        output.WriteLine(byAce.SequenceEqual(byNls)
            ? "=> the two orders agree: ACE's table is an order-preserving compaction of the NLS weights."
            : "=> the orders DIVERGE: ACE is not simply a compaction of NLS.");
    }

    /// <summary>The primary-weight bytes ACE stores for a single character (the section between the 0x7F start
    /// flag and the 0x01 end-of-primary marker).</summary>
    private static byte[] JetTextPrimary(char ch)
    {
        var column = new ColumnDef { Name = "t", Type = JetDataType.Text, Index = 0, Collation = Collation.GeneralLegacy };
        byte[] key = IndexKeyEncoder.Encode([(column, true)], [ch.ToString()]);
        int end = Array.IndexOf(key, (byte)0x01, 1);
        return end < 0 ? [] : key[1..end];
    }

    // Which sort-order version do the committed fixtures actually use? Encoding v1 (General) keys is blocked
    // on having a database that contains them to check against.
    [Fact]
    public void Probe_fixture_collation_versions()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Data"), "*.accdb")
                     .Concat(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Data"), "*.mdb")))
        {
            try
            {
                using var db = JetDatabase.Open(file);
                var collations = db.Catalog.Tables
                    .SelectMany(t => t.Columns)
                    .Where(c => c.Type is JetDataType.Text or JetDataType.Memo)
                    .Select(c => $"{c.Collation.Order} v{c.Collation.Version}")
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray();
                output.WriteLine($"{Path.GetFileName(file),-28} {string.Join(", ", collations)}");
            }
            catch (Exception ex) { output.WriteLine($"{Path.GetFileName(file),-28} <{ex.GetType().Name}>"); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NlsVersionInfoEx
    {
        public uint dwNLSVersionInfoSize;
        public uint dwNLSVersion;
        public uint dwDefinedVersion;
        public uint dwEffectiveId;
        public Guid guidCustomVersion;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetNLSVersionEx", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNLSVersionEx(int function, string localeName, ref NlsVersionInfoEx version);

    // ESE records the NLS sort version with each Unicode index so it can tell when Windows changed the weights
    // underneath it. Jet Red pins a table instead (General-Legacy vs General). Either way the sort version is
    // the thing that identifies a weight table, so: what does this machine report?
    [Fact]
    public void Probe_windows_nls_sort_version()
    {
        foreach (string locale in new[] { "en-US", "en-GB", "de-DE", "" })
        {
            var version = new NlsVersionInfoEx { dwNLSVersionInfoSize = (uint)Marshal.SizeOf<NlsVersionInfoEx>() };
            const int compareString = 0x00000001;
            bool ok = GetNLSVersionEx(compareString, locale.Length == 0 ? "" : locale, ref version);
            output.WriteLine(ok
                ? $"{(locale.Length == 0 ? "(invariant)" : locale),-12} sortVersion=0x{version.dwNLSVersion:X8}  " +
                  $"definedVersion=0x{version.dwDefinedVersion:X8}  effectiveId=0x{version.dwEffectiveId:X8}"
                : $"{locale,-12} GetNLSVersionEx failed ({Marshal.GetLastWin32Error()})");
        }
    }

    // Where does ACE's frozen v0 table disagree with the NLS table this Windows ships (0x00060502)? Those
    // characters are where NLS changed after the legacy table was frozen — the candidate list for what the
    // v1 "General" order actually alters, obtainable without a v1 database to compare against.
    // Caveat: a disagreement can also be a gap in LibRed's hand-built table, so anything here needs ACE as
    // the oracle before it means anything.
    [Fact]
    public void Probe_where_v0_diverges_from_modern_nls()
    {
        var comparable = new List<(char Ch, int Ace, int Nls)>();
        var unsupported = new List<char>();
        for (char ch = ' '; ch <= 'ÿ'; ch++)
        {
            if (char.IsControl(ch)) continue;
            byte[] nls = WinSortKey(ch.ToString());
            if (nls.Length < 2) continue;
            byte[] ace;
            try { ace = JetTextPrimary(ch); }
            catch (Exception) { unsupported.Add(ch); continue; }
            if (ace.Length != 1) continue;                       // multi-weight or ignorable: not comparable here
            comparable.Add((ch, ace[0], (nls[0] << 8) | nls[1]));
        }

        var byAce = comparable.OrderBy(m => m.Ace).ThenBy(m => m.Ch).Select(m => m.Ch).ToArray();
        var byNls = comparable.OrderBy(m => m.Nls).ThenBy(m => m.Ch).Select(m => m.Ch).ToArray();

        output.WriteLine($"comparable single-weight characters: {comparable.Count}");
        output.WriteLine($"not encodable by LibRed: {unsupported.Count}" +
                         (unsupported.Count > 0 ? $" -> {new string(unsupported.ToArray())}" : ""));
        output.WriteLine("");

        var divergences = byAce.Zip(byNls).Select((pair, i) => (i, pair.First, pair.Second))
            .Where(x => x.First != x.Second).ToArray();
        if (divergences.Length == 0)
        {
            output.WriteLine("ACE v0 and NLS 0x00060502 agree on the order of every comparable character.");
            return;
        }

        output.WriteLine($"order diverges at {divergences.Length} position(s):");
        output.WriteLine($"  by ACE: {new string(byAce)}");
        output.WriteLine($"  by NLS: {new string(byNls)}");
        foreach ((int i, char a, char n) in divergences.Take(40))
            output.WriteLine($"  position {i,3}: ACE has '{a}' (U+{(int)a:X4}), NLS has '{n}' (U+{(int)n:X4})");
    }

    // The Latin-1 punctuation/symbol block that JetTextCollation has no weights for, so an index insert
    // throws. ACE is the oracle: give it each character in an indexed column and read back the key it stores.
    [Fact]
    public void Probe_ace_weights_for_characters_libred_cannot_encode()
    {
        const string missing = "¡¢£¤¥¦§¨©ª«¬­®¯" +
                               "°±²³´µ¶·¸¹º»¼½¾¿" +
                               "×÷";

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "missing-weights-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE W (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_W ON W (K)");
                for (int i = 0; i < missing.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO W (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", missing[i].ToString());
                    insert.Parameters.AddWithValue("v", i);
                    insert.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("W");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_W");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

            output.WriteLine("char  U+      ACE key            NLS primary");
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            {
                if (!rows.TryGetValue(rowId, out object?[]? values)) continue;
                string value = (string?)values[keyColumn.Index] ?? "";
                if (value.Length != 1) continue;
                byte[] nls = WinSortKey(value);
                output.WriteLine($" {value}    {(int)value[0]:X4}   {Hex(stored),-18} {(nls.Length >= 2 ? $"{nls[0]:X2}{nls[1]:X2}" : "-")}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private const uint LcmapHalfwidth = 0x00400000;
    private const uint LcmapFullwidth = 0x00800000;

    // Does ACE normalise width before building the key? If it applies LCMAP_HALFWIDTH first, a full-width
    // character must produce the same index key as its half-width counterpart. Each pair is inserted through
    // ACE and its stored keys compared; the Win32 mapping is shown alongside so the two can be told apart.
    [Fact]
    public void Probe_whether_ace_folds_width_like_lcmap_halfwidth()
    {
        (string Wide, string Narrow, string What)[] pairs =
        [
            ("Ａ", "A", "fullwidth A"),
            ("１", "1", "fullwidth 1"),
            ("＄", "$", "fullwidth $"),
            ("　", " ", "ideographic space"),
            ("ｱ", "ア", "halfwidth katakana A vs fullwidth"),
            ("ﬁ", "fi", "ligature fi"),
        ];

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "halfwidth-");
        try
        {
            var inserted = new List<(string Text, string What, int Id)>();
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE HW (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_HW ON HW (K)");
                int id = 0;
                foreach ((string wide, string narrow, string what) in pairs)
                    foreach (string text in new[] { wide, narrow })
                    {
                        using var insert = connection.CreateCommand();
                        insert.CommandText = "INSERT INTO HW (K, V) VALUES (?, ?)";
                        insert.Parameters.AddWithValue("k", text);
                        insert.Parameters.AddWithValue("v", id);
                        try { insert.ExecuteNonQuery(); inserted.Add((text, what, id)); }
                        catch (Exception ex) { output.WriteLine($"  insert of {what} '{Describe(text)}' rejected: {ex.Message.Trim()}"); }
                        id++;
                    }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("HW");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_HW");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

            var keys = new Dictionary<string, string>();
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
                if (rows.TryGetValue(rowId, out object?[]? values))
                    keys[(string?)values[keyColumn.Index] ?? ""] = Convert.ToHexString(stored);

            foreach ((string wide, string narrow, string what) in pairs)
            {
                keys.TryGetValue(wide, out string? wideKey);
                keys.TryGetValue(narrow, out string? narrowKey);
                output.WriteLine($"{what}:");
                output.WriteLine($"    ACE  {Describe(wide),-12} {wideKey ?? "(not stored)"}");
                output.WriteLine($"    ACE  {Describe(narrow),-12} {narrowKey ?? "(not stored)"}");
                output.WriteLine($"    -> {(wideKey is not null && wideKey == narrowKey ? "SAME key: ACE folded the width" : "different keys: no width folding")}");
                output.WriteLine($"    LCMap plain      {Hex(WinSortKey(wide))}");
                output.WriteLine($"    LCMap +HALFWIDTH {Hex(WinMap(wide, LcmapHalfwidth))} (mapped form of the input)");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>LCMapStringEx as a character mapping (no sort key) — returns the transformed string's bytes.</summary>
    private static byte[] WinMap(string value, uint flags)
    {
        int size = LCMapStringEx("en-US", flags, value, value.Length, null, 0, 0, 0, 0);
        if (size <= 0) return [];
        var buffer = new byte[size * 2];
        int written = LCMapStringEx("en-US", flags, value, value.Length, buffer, size, 0, 0, 0);
        return written <= 0 ? [] : buffer[..(written * 2)];
    }

    private static string Describe(string s) => string.Concat(s.Select(c => $"U+{(int)c:X4}"));

    /// <summary>.NET's own sort key, case-insensitive to match Jet's index semantics.</summary>
    private static byte[] SortKey(string value) =>
        CultureInfo.GetCultureInfo("en-US").CompareInfo.GetSortKey(value, CompareOptions.IgnoreCase).KeyData;

    /// <summary>The Win32 NLS sort key — the API Jet used to build these keys.</summary>
    private static byte[] WinSortKey(string value)
    {
        if (value.Length == 0) return [];
        int size = LCMapStringEx("en-US", LcmapSortkey | NormIgnoreCase, value, value.Length, null, 0, 0, 0, 0);
        if (size <= 0) return [];
        var buffer = new byte[size];
        int written = LCMapStringEx("en-US", LcmapSortkey | NormIgnoreCase, value, value.Length, buffer, size, 0, 0, 0);
        return written <= 0 ? [] : buffer[..written];
    }

    /// <summary>Whether .NET is using ICU rather than Win32 NLS — decided by comparing the two directly.</summary>
    private static bool UsingIcu() => !SortKey("a").AsSpan().SequenceEqual(WinSortKey("a"));

    private static string Hex(byte[] bytes) => bytes.Length == 0 ? "(empty)" : Convert.ToHexString(bytes);

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
