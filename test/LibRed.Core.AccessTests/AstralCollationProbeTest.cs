using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: what ACE does above U+FFFF, and how much text an index key can actually hold.
//
// The full-BMP sweeps establish that both sort orders reproduce ACE byte-for-byte for all 63,422 characters
// ACE stores below U+10000. Nothing above that has ever been measured. A surrogate pair reaches the encoder
// as two chars, and the embedded v1 weight table holds entries for surrogate code points, so an astral
// character currently ENCODES rather than being refused — whether the result is what ACE stores is exactly
// what this measures.
//
// Both probes need ACE and are opt-in via LIBRED_ASTRAL=1.
public class AstralCollationProbeTest(ITestOutputHelper output)
{
    /// <summary>
    /// A spread across all sixteen astral planes: the plane's first code points, a few interior ones, and its
    /// last non-noncharacter. Reconnaissance before any full sweep — a million code points is sixteen times
    /// the BMP, so it is worth knowing whether ACE stores them at all before paying for that.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Probe_astral_reconnaissance(byte version)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_ASTRAL") == "1",
            "set LIBRED_ASTRAL=1 — this probe needs ACE");

        (string source, string? created, ColumnDef column) = Fixture(version);
        try
        {
            var samples = new List<string>();
            for (int plane = 1; plane <= 16; plane++)
                foreach (int offset in (int[])[0x0000, 0x0001, 0x0040, 0x0300, 0x1000, 0x4000, 0xF000, 0xFFFD])
                    samples.Add(char.ConvertFromUtf32(plane * 0x10000 + offset));

            // Characters that actually mean something up there, rather than only round numbers.
            foreach (int codePoint in (int[])
                     [0x10000, 0x103A0, 0x10400, 0x1D400, 0x1D160, 0x1F600, 0x20000, 0x2A700, 0xE0001, 0xE0100])
                samples.Add(char.ConvertFromUtf32(codePoint));

            Dictionary<string, string> ace = AceKeys(source, "astral", [.. samples]);

            int stored = 0, refused = 0, matched = 0, differ = 0;
            var examples = new List<string>();
            foreach (string text in samples.Distinct())
            {
                if (!ace.TryGetValue(text, out string? key)) { refused++; continue; }
                stored++;

                string? ours = null;
                try { ours = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text])); }
                catch (NotSupportedException) { }

                if (ours == key) { matched++; continue; }
                differ++;
                if (examples.Count < 12)
                    examples.Add($"    U+{char.ConvertToUtf32(text, 0):X5}  ACE {key,-24} ours {ours ?? "(refused)"}");
            }

            output.WriteLine($"General v{version}: {samples.Distinct().Count()} sampled — " +
                             $"{stored} ACE stored, {refused} ACE would not store, {matched} match, {differ} differ");
            foreach (string line in examples) output.WriteLine(line);
        }
        finally { if (created is not null) TemporaryDatabase.Delete(created); }
    }

    /// <summary>
    /// Every code point in one astral plane, for the planes that carry assigned characters. Run after the
    /// reconnaissance above says it is worth it.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    public void Probe_astral_plane(byte version, int plane)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_ASTRAL_FULL") == "1",
            "set LIBRED_ASTRAL_FULL=1 — this inserts 65,536 rows through ACE per plane and takes minutes");

        (string source, string? created, ColumnDef column) = Fixture(version);
        try
        {
            int stored = 0, refused = 0, matched = 0, differ = 0;
            var shapes = new SortedDictionary<string, int>();
            var examples = new List<string>();

            for (int chunk = 0; chunk < 0x10000; chunk += 0x1000)
            {
                var samples = new List<string>();
                for (int offset = chunk; offset < chunk + 0x1000; offset++)
                {
                    int codePoint = plane * 0x10000 + offset;
                    if ((codePoint & 0xFFFE) == 0xFFFE) continue;   // noncharacters
                    samples.Add(char.ConvertFromUtf32(codePoint));
                }

                Dictionary<string, string> ace = AceKeys(source, "aplane", [.. samples]);
                foreach (string text in samples)
                {
                    if (!ace.TryGetValue(text, out string? key)) { refused++; continue; }
                    stored++;

                    string? ours = null;
                    try { ours = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text])); }
                    catch (NotSupportedException) { }

                    if (ours == key) { matched++; continue; }
                    differ++;
                    shapes[key.Length <= 12 ? key : $"{key[..8]}… ({key.Length / 2} bytes)"] =
                        shapes.GetValueOrDefault(key.Length <= 12 ? key : $"{key[..8]}… ({key.Length / 2} bytes)") + 1;
                    if (examples.Count < 10)
                        examples.Add($"    U+{char.ConvertToUtf32(text, 0):X5}  ACE {key,-24} ours {ours ?? "(refused)"}");
                }
            }

            output.WriteLine($"General v{version}, plane {plane}: {stored} ACE stored, {refused} refused, " +
                             $"{matched} match, {differ} differ");
            foreach ((string shape, int count) in shapes.OrderByDescending(e => e.Value).Take(8))
                output.WriteLine($"  {count,6}  ACE key {shape}");
            foreach (string line in examples) output.WriteLine(line);
        }
        finally { if (created is not null) TemporaryDatabase.Delete(created); }
    }

    /// <summary>
    /// How much text an index key can actually carry, measured rather than assumed.
    /// </summary>
    /// <remarks>
    /// The nominal limit is the column width — a Jet/ACE TEXT column holds 255 characters. The index key is
    /// the real constraint, and it is not the same number: v1 spends TWO primary bytes per character where v0
    /// spends one, and accents add a secondary byte each on top. So the question is where ACE stops, and
    /// whether it stops by rejecting the row or by silently truncating the key — truncation is the dangerous
    /// answer, because two different long strings would then collide in the index.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Probe_maximum_indexable_length(byte version)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_ASTRAL") == "1",
            "set LIBRED_ASTRAL=1 — this probe needs ACE");

        (string source, string? created, ColumnDef column) = Fixture(version);
        try
        {
            foreach ((string name, string unit) in ((string, string)[])
                     [("plain 'a'", "a"), ("accented 'á'", "á"), ("CJK", "一")])
            {
                // One row per length, each padded to a distinct value so nothing dedupes.
                var samples = new List<string>();
                for (int length = 1; length <= 255; length++) samples.Add(string.Concat(Enumerable.Repeat(unit, length)));

                Dictionary<string, string> ace = AceKeys(source, "maxlen", [.. samples]);

                int longestStored = 0, longestMatching = 0, previousKeyBytes = 0, firstTruncated = 0;
                foreach (string text in samples)
                {
                    if (!ace.TryGetValue(text, out string? key)) continue;
                    longestStored = text.Length;

                    int keyBytes = key.Length / 2;
                    if (keyBytes == previousKeyBytes && firstTruncated == 0) firstTruncated = text.Length;
                    previousKeyBytes = keyBytes;

                    string? ours = null;
                    try { ours = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text])); }
                    catch (NotSupportedException) { }
                    if (ours == key) longestMatching = text.Length;
                }

                output.WriteLine(
                    $"v{version} {name,-14} longest ACE indexed {longestStored,4} chars " +
                    $"({previousKeyBytes,3} key bytes); LibRed matches to {longestMatching,4}; " +
                    $"key stopped growing at {(firstTruncated == 0 ? "never" : firstTruncated.ToString())}");
            }
        }
        finally { if (created is not null) TemporaryDatabase.Delete(created); }
    }

    /// <summary>
    /// The exact truncation boundary, and whether two longer strings end up with the SAME key.
    /// </summary>
    /// <remarks>
    /// Length alone cannot tell truncation from a key that simply stopped growing. What matters is whether
    /// distinct values collide, because a collision is silent: the index holds one entry where it should hold
    /// two, and a seek returns the wrong rows rather than an error.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Probe_key_truncation_boundary(byte version)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_ASTRAL") == "1",
            "set LIBRED_ASTRAL=1 — this probe needs ACE");

        (string source, string? created, ColumnDef column) = Fixture(version);
        try
        {
            foreach ((string name, string unit) in ((string, string)[])
                     [("plain 'a'", "a"), ("accented 'á'", "á"), ("CJK", "一")])
            {
                // Distinct values of each length: a run of the unit followed by a marker, so two lengths can
                // only produce the same key if the tail was actually discarded.
                var samples = new List<string>();
                for (int length = 1; length <= 255; length++)
                    samples.Add(string.Concat(Enumerable.Repeat(unit, length - 1)) + "z");

                Dictionary<string, string> ace = AceKeys(source, "trunc", [.. samples]);

                int maxBytes = 0, lastGrowing = 0, firstCollision = 0, libredMatches = 0;
                string previous = "";
                foreach (string text in samples)
                {
                    if (!ace.TryGetValue(text, out string? key)) continue;
                    int bytes = key.Length / 2;
                    if (bytes > maxBytes) { maxBytes = bytes; lastGrowing = text.Length; }
                    if (key == previous && firstCollision == 0) firstCollision = text.Length;
                    previous = key;

                    string? ours = null;
                    try { ours = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text])); }
                    catch (NotSupportedException) { }
                    if (ours == key) libredMatches = text.Length;
                }

                output.WriteLine(
                    $"v{version} {name,-14} max key {maxBytes,3} bytes, reached at {lastGrowing,4} chars; " +
                    $"first identical-key collision at {(firstCollision == 0 ? "none" : firstCollision.ToString()),4}; " +
                    $"LibRed matches to {libredMatches,4}");
            }
        }
        finally { if (created is not null) TemporaryDatabase.Delete(created); }
    }

    /// <summary>What ACE actually stores either side of the 510-byte boundary, byte for byte.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Probe_key_at_the_boundary(byte version)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_ASTRAL") == "1",
            "set LIBRED_ASTRAL=1 — this probe needs ACE");

        (string source, string? created, ColumnDef column) = Fixture(version);
        try
        {
            foreach ((string name, string unit, int around) in ((string, string, int)[])
                     [("plain 'a'", "a", 254), ("accented 'á'", "á", 170), ("CJK", "一", 128)])
            {
                var samples = new List<string>();
                for (int length = around - 2; length <= around + 2; length++)
                    samples.Add(string.Concat(Enumerable.Repeat(unit, length - 1)) + "z");

                Dictionary<string, string> ace = AceKeys(source, "bound", [.. samples]);
                output.WriteLine($"--- v{version} {name}");
                foreach (string text in samples)
                {
                    if (!ace.TryGetValue(text, out string? key)) { output.WriteLine($"  {text.Length,4}  (not stored)"); continue; }
                    string? ours = null;
                    try { ours = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text])); }
                    catch (NotSupportedException) { }

                    // Only the tails differ, so show those rather than 500 identical bytes.
                    output.WriteLine($"  {text.Length,4}  ACE {key.Length / 2,3}B …{key[^24..]}   " +
                                     (ours is null ? "ours (refused)"
                                      : ours == key ? "ours same"
                                      : $"ours {ours.Length / 2,3}B …{ours[^24..]}"));
                }
            }
        }
        finally { if (created is not null) TemporaryDatabase.Delete(created); }
    }

    /// <summary>
    /// Whether a key of exactly 510 bytes is stored intact, or already hashed.
    /// </summary>
    /// <remarks>
    /// Uniform strings step over 510 — 253 'a' is 509 bytes and 254 is 511 — so the boundary itself is never
    /// exercised by them, and "≤ 510 is safe" would be an assumption. Putting the only accent on the FIRST
    /// character makes the secondary section exactly one byte long, which lands the key on any length wanted:
    /// 1 (start) + 2n (primaries) + 1 (delimiter) + 1 (secondary) + 1 (terminator).
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Probe_exact_key_length_boundary(byte version)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_ASTRAL") == "1",
            "set LIBRED_ASTRAL=1 — this probe needs ACE");

        (string source, string? created, ColumnDef column) = Fixture(version);
        try
        {
            var samples = new List<string>();
            for (int n = 250; n <= 255; n++) samples.Add("á" + new string('a', n - 1));

            Dictionary<string, string> ace = AceKeys(source, "exact", [.. samples]);
            foreach (string text in samples)
            {
                string? ours = null;
                try { ours = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text])); }
                catch (NotSupportedException) { }

                string key = ace.GetValueOrDefault(text, "");
                output.WriteLine(
                    $"v{version} {text.Length,4} chars: ACE {(key.Length == 0 ? "(not stored)" : $"{key.Length / 2,3}B ending {key[^6..]}"),-22} " +
                    $"ours {(ours is null ? "(refused)" : $"{ours.Length / 2,3}B ending {ours[^6..]}")}  " +
                    (ours == key ? "SAME" : "differ"));
            }
        }
        finally { if (created is not null) TemporaryDatabase.Delete(created); }
    }

    /// <summary>
    /// Whether the 510-byte cap is on ONE text column's key or on the whole index entry.
    /// </summary>
    /// <remarks>
    /// Everything measured so far used a single-column index, so "510 per text column" would be an
    /// extrapolation. It matters: a two-column index of two 400-byte keys is under the cap per column and far
    /// over it per entry, and guessing wrong leaves exactly the silent-corruption case this is meant to close.
    /// Two 200-character columns are ~400 bytes each under v1 — comfortably under per column, 800 combined.
    /// </remarks>
    [Fact]
    public void Probe_multi_column_key_limit()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_ASTRAL") == "1",
            "set LIBRED_ASTRAL=1 — this probe needs ACE");

        string path = TemporaryDatabase.CreatePath("general-v1-multi-");
        DatabaseCreator.CreateEmpty(path, collation: Collation.General);
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Two (A TEXT(255), B TEXT(255), V LONG)");
                Exec(connection, "CREATE INDEX IX_Two ON Two (A, B)");
                foreach (int length in (int[])[10, 100, 200, 255])
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Two (A, B, V) VALUES (?, ?, ?)";
                    insert.Parameters.AddWithValue("a", new string('a', length));
                    insert.Parameters.AddWithValue("b", new string('b', length));
                    insert.Parameters.AddWithValue("v", length);
                    try { insert.ExecuteNonQuery(); }
                    catch (Exception error) { output.WriteLine($"  {length,4}+{length,-4} ACE refused: {error.Message.Trim()}"); }
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("Two");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Two");
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);
            int valueColumn = table.Definition.FindColumn("V")!.Index;

            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
                if (rows.TryGetValue(rowId, out object?[]? values))
                    output.WriteLine($"  {values[valueColumn],4} chars each: entry {stored.Length,4} bytes, " +
                                     $"ends {Convert.ToHexString(stored)[^6..]}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static (string Source, string? Created, ColumnDef Column) Fixture(byte version)
    {
        bool v1 = version == Collation.GeneralVersion;
        string? created = null;
        if (v1)
        {
            created = TemporaryDatabase.CreatePath("general-v1-astral-");
            DatabaseCreator.CreateEmpty(created, collation: Collation.General);
        }

        return (created ?? TestDatabases.NorthwindAccdb, created, new ColumnDef
        {
            Name = "K", Type = JetDataType.Text, Index = 0,
            Collation = v1 ? Collation.General : Collation.GeneralLegacy,
        });
    }

    private static Dictionary<string, string> AceKeys(string source, string tag, string[] samples)
    {
        string path = TemporaryDatabase.CopyPath(source, tag);
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Probe (K TEXT(255), V LONG)");
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
