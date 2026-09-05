using System.IO.Compression;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// GENERATOR: the characters where ACE's v1 weights disagree with the published table we embed.
//
// v1's primaries ARE the Windows NLS (Script Member, Alphabetic Weight) pair, and the table was identified as
// Windows Server 2008 by reconstructing measured ACE keys — 25 of 25 against every published version. That
// held for what it was tested on, Latin and symbols. It does not hold everywhere: ACE gives Balinese and
// Canadian syllabics LATIN weights, and differs on the Arabic harakat and several ligature blocks. Those are
// scripts added or reweighted after Server 2008, so ACE's real table is not exactly the file we parse.
//
// Rather than guess at which NLS revision ACE carries, this measures the disagreements and embeds them: for
// every BMP character, encode it through ACE, compare with what JetTextCollationV1 produces, and record the
// weight ACE implies wherever they differ. That is the same answer v0 needed, at 1% of the size.
//
// Opt-in via LIBRED_GENERATE_V1=1: it inserts ~63,000 rows through ACE and rewrites a checked-in binary.
public class SortKeyTableV1OverrideGeneratorTest(ITestOutputHelper output)
{
    private const string ResourcePath = "src/LibRed/LibRed.Core/Resources/SortKeyTableV1Overrides.bin";

    [Fact]
    public void Generate_the_v1_override_resource()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_GENERATE_V1") == "1",
            "set LIBRED_GENERATE_V1=1 — this measures ~63,000 characters through ACE and rewrites a resource");

        // Suppressed for the whole run, before anything can touch the encoder. The resource records where the
        // encoder disagrees with ACE, so measuring an encoder that consults it would find no disagreements and
        // write an empty file. Suppressing from the outset also means the generator never has to be able to
        // READ the resource it is about to replace — it bootstraps from a stale or absent one either way.
        JetTextCollationV1Overrides.Suppressed = true;
        string database = TemporaryDatabase.CreatePath("general-v1-gen-");
        DatabaseCreator.CreateEmpty(database, collation: Collation.General);
        var column = new ColumnDef
        {
            Name = "K", Type = JetDataType.Text, Index = 0, Collation = Collation.General,
        };

        var overrides = new SortedDictionary<int, (byte[] Primaries, byte[] Secondaries)>();
        var ignorable = new SortedSet<int>();
        var leftover = new SortedDictionary<int, string>();
        int agreed = 0;
        try
        {
            for (int chunk = 0x0000; chunk <= 0xF000; chunk += 0x1000)
            {
                string[] characters = Range(chunk, chunk + 0x0FFF);
                if (characters.Length == 0) continue;
                foreach ((string text, string key) in AceKeys(database, characters))
                {
                    if (text.Length != 1) continue;
                    // An empty key — ACE contributes nothing at all for this character. There is no weight to
                    // record, only membership, so these go in a set of ranges rather than the weight table.
                    // Tested before agreement, because membership is a fact about ACE alone: recording it only
                    // where the encoder currently differs would make the set shrink every regeneration.
                    if (key == EmptyKey) { ignorable.Add(text[0]); continue; }

                    string? ours = null;
                    try { ours = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text])); }
                    catch (NotSupportedException) { }
                    if (ours == key) { agreed++; continue; }

                    // Otherwise record the two sections verbatim. A key carrying a third section — kana, or a
                    // word-sort record — is a mechanism the encoder implements rather than data an override
                    // can carry, so report it for triage instead.
                    if (!TryReadSections(key, out byte[] primaries, out byte[] secondaries))
                    {
                        leftover[text[0]] = key;
                        continue;
                    }
                    overrides[text[0]] = (primaries, secondaries);
                }
            }

            (int[] starts, int[] lengths) = ToRanges(ignorable);
            output.WriteLine($"{agreed} characters already agree; {overrides.Count} weights overridden; " +
                             $"{ignorable.Count} ignorable in {starts.Length} ranges; " +
                             $"{leftover.Count} left over");
            foreach (IGrouping<string, KeyValuePair<int, string>> shape in
                     leftover.GroupBy(e => ShapeOf(e.Value)).OrderByDescending(g => g.Count()))
                output.WriteLine($"  {shape.Count(),6}  {shape.Key,-28}  " +
                                 string.Join(" ", shape.Take(4).Select(e => $"U+{e.Key:X4}={e.Value}")));

            byte[] blob = Build(overrides, starts, lengths);
            string path = Path.Combine(RepositoryRoot(), ResourcePath);
            File.WriteAllBytes(path, blob);
            output.WriteLine($"wrote {path} ({blob.Length:N0} bytes)");

            (var reloaded, SortedSet<int> reloadedIgnorable) = Parse(blob);
            Assert.Equal(overrides.Count, reloaded.Count);
            foreach ((int codePoint, (byte[] primaries, byte[] secondaries)) in overrides)
            {
                Assert.Equal(primaries, reloaded[codePoint].Item1);
                Assert.Equal(secondaries, reloaded[codePoint].Item2);
            }
            Assert.Equal(ignorable, reloadedIgnorable);
            output.WriteLine($"round-tripped {reloaded.Count} overrides and {reloadedIgnorable.Count} ignorables");
        }
        finally
        {
            JetTextCollationV1Overrides.Suppressed = false;
            TemporaryDatabase.Delete(database);
        }
    }

    /// <summary>
    /// Splits <c>7F primaries 01 secondaries 00</c> into its two sections, kept as raw bytes.
    /// </summary>
    /// <remarks>
    /// Deliberately not parsed into <c>(ScriptMember, Alphabetic, Diacritic)</c> weights. That reading assumes
    /// every primary is a two-byte pair carrying one secondary, and ACE breaks it in both directions: the
    /// Arabic harakat have a secondary and <i>no primary at all</i> (<c>U+064C</c> is <c>7F 01 56 00</c>),
    /// while the Lao vowel signs take a <i>one-byte</i> primary (<c>U+0EB0</c> is <c>7F 41 01 0A 00</c>).
    /// Raw bytes state what ACE actually stores, and an override is finished bytes rather than a table entry.
    /// <para>
    /// The delimiter is the <b>last</b> <c>01</c>, not the first, because a primary byte can itself be
    /// <c>01</c>. Five characters do exactly that — <c>U+0385</c>, <c>U+1B3B</c>, <c>U+FC25</c> weigh
    /// <c>07 53 01</c>, and <c>U+FC33</c>, <c>U+FCC2</c> weigh <c>29 0B 01</c>. Splitting at the first
    /// <c>01</c> made them look like a key with a third section bolted on, and they were misreported as an
    /// unknown mechanism until measuring them in combination showed the truth: they are ordinary two-weight
    /// expansions, and appending their bytes verbatim reproduces ACE in strings too — <c>AX</c> and
    /// <c>XA</c> and <c>XAX</c> all check out.
    /// </para>
    /// <para>
    /// Returns false for a key carrying a kana section, which combines across a whole string and so cannot be
    /// carried per character. <c>FF</c> identifies one: it introduces the prolonged-mark flags and appears in
    /// the closing constant, and never in a secondary weight.
    /// </para>
    /// </remarks>
    private static bool TryReadSections(string key, out byte[] primaries, out byte[] secondaries)
    {
        primaries = secondaries = [];
        byte[] b = Convert.FromHexString(key);
        if (b.Length < 3 || b[0] != 0x7F || b[^1] != 0x00) return false;

        int end = Array.LastIndexOf(b, (byte)0x01, b.Length - 2);
        if (end < 1) return false;

        byte[] rest = b[(end + 1)..^1];
        if (Array.IndexOf(rest, (byte)0xFF) >= 0) return false;   // a kana section, not plain weights

        primaries = b[1..end];
        secondaries = rest;
        return true;
    }

    private const string EmptyKey = "7F0100";

    /// <summary>Collapses the ignorable code points into runs, which is what makes them cheap to store.</summary>
    private static (int[] Starts, int[] Lengths) ToRanges(SortedSet<int> codePoints)
    {
        var starts = new List<int>();
        var lengths = new List<int>();
        foreach (int codePoint in codePoints)
        {
            if (starts.Count > 0 && starts[^1] + lengths[^1] == codePoint) lengths[^1]++;
            else { starts.Add(codePoint); lengths.Add(1); }
        }
        return ([.. starts], [.. lengths]);
    }

    /// <summary>Names the mechanism a key uses, so leftovers cluster by what they would need rather than by
    /// code point. A kana section and an inline word-sort record are separate features, not stray weights.</summary>
    private static string ShapeOf(string key)
    {
        byte[] b = Convert.FromHexString(key);
        if (b.Length < 3 || b[0] != 0x7F || b[^1] != 0x00) return "not a sort key";
        int end = Array.IndexOf(b, (byte)0x01, 1);
        if (end < 0) return "no secondary section";
        int primaries = (end - 1) / 2;
        int secondaries = b.Length - end - 2;
        if ((end - 1) % 2 != 0) return "odd primary byte count";
        if (key.Contains("010101")) return "inline (word-sort) record";
        return secondaries > primaries
            ? $"{secondaries - primaries} extra secondary byte(s)"
            : "unclassified";
    }

    private static string[] Range(int first, int last)
    {
        var characters = new List<string>();
        for (int c = first; c <= last; c++)
            if (c != ' ' && !char.IsControl((char)c) && !char.IsSurrogate((char)c))
                characters.Add(((char)c).ToString());
        return [.. characters];
    }

    private static byte[] Build(
        SortedDictionary<int, (byte[] Primaries, byte[] Secondaries)> overrides,
        int[] ignorableStarts, int[] ignorableLengths)
    {
        var deltas = new List<byte>();
        var primaryLengths = new List<byte>();
        var secondaryLengths = new List<byte>();
        var primaryBytes = new List<byte>();
        var secondaryBytes = new List<byte>();
        int previous = 0;
        foreach ((int codePoint, (byte[] primaries, byte[] secondaries)) in overrides)
        {
            WriteVarInt(deltas, codePoint - previous);
            previous = codePoint;
            primaryLengths.Add(checked((byte)primaries.Length));
            secondaryLengths.Add(checked((byte)secondaries.Length));
            primaryBytes.AddRange(primaries);
            secondaryBytes.AddRange(secondaries);
        }

        var rangeStarts = new List<byte>();
        var rangeLengths = new List<byte>();
        previous = 0;
        for (int i = 0; i < ignorableStarts.Length; i++)
        {
            WriteVarInt(rangeStarts, ignorableStarts[i] - previous);
            WriteVarInt(rangeLengths, ignorableLengths[i]);
            previous = ignorableStarts[i];
        }

        var blob = new MemoryStream();
        var writer = new BinaryWriter(blob);
        writer.Write(overrides.Count);
        writer.Write(ignorableStarts.Length);
        foreach (List<byte> stream in new[]
                 {
                     deltas, primaryLengths, secondaryLengths, primaryBytes, secondaryBytes,
                     rangeStarts, rangeLengths,
                 })
        {
            byte[] compressed = Compress([.. stream]);
            writer.Write(compressed.Length);
            writer.Write(compressed, 0, compressed.Length);
        }
        writer.Flush();
        return blob.ToArray();
    }

    private static (Dictionary<int, (byte[], byte[])> Overrides, SortedSet<int> Ignorable) Parse(byte[] blob)
    {
        var reader = new BinaryReader(new MemoryStream(blob));
        int count = reader.ReadInt32();
        int rangeCount = reader.ReadInt32();
        var streams = new byte[7][];
        for (int i = 0; i < 7; i++) streams[i] = Decompress(reader.ReadBytes(reader.ReadInt32()));

        var overrides = new Dictionary<int, (byte[], byte[])>(count);
        int offset = 0, codePoint = 0, primary = 0, secondary = 0;
        for (int i = 0; i < count; i++)
        {
            codePoint += ReadVarInt(streams[0], ref offset);
            overrides[codePoint] = (
                streams[3][primary..(primary += streams[1][i])],
                streams[4][secondary..(secondary += streams[2][i])]);
        }

        var ignorable = new SortedSet<int>();
        int startOffset = 0, lengthOffset = 0, start = 0;
        for (int i = 0; i < rangeCount; i++)
        {
            start += ReadVarInt(streams[5], ref startOffset);
            int length = ReadVarInt(streams[6], ref lengthOffset);
            for (int n = 0; n < length; n++) ignorable.Add(start + n);
        }
        return (overrides, ignorable);
    }

    private static void WriteVarInt(List<byte> target, int value)
    {
        while (value >= 0x80) { target.Add((byte)((value & 0x7F) | 0x80)); value >>= 7; }
        target.Add((byte)value);
    }

    private static int ReadVarInt(byte[] source, ref int offset)
    {
        int value = 0, shift = 0;
        while (true)
        {
            byte b = source[offset++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
    }

    private static byte[] Compress(byte[] data)
    {
        var output = new MemoryStream();
        using (var deflate = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        var output = new MemoryStream();
        using (var inflate = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress))
            inflate.CopyTo(output);
        return output.ToArray();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EFCore.Jet.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("EFCore.Jet.sln not found above the test output.");
    }

    private static Dictionary<string, string> AceKeys(string source, string[] samples)
    {
        string path = TemporaryDatabase.CopyPath(source, "v1gen-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Gen (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_Gen ON Gen (K)");
                for (int i = 0; i < samples.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Gen (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", samples[i]);
                    insert.Parameters.AddWithValue("v", i);
                    try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("Gen");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Gen");
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
