using System.IO.Compression;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// GENERATOR: builds LibRed.Core's embedded General v0 sorting-weight resource by measuring ACE.
//
// v1's table came from a published Microsoft file, so a PowerShell script could parse it
// (tools/sortkey-table/generate.ps1). No such file exists for v0 — its weights are a Jet-specific
// compaction, so the only source of truth is ACE itself: insert every code point into an indexed text
// column, read the stored index keys back, and record what they say.
//
// Across the BMP that is ~42,600 characters carrying weights and ~18,700 ignorable — far past what the
// hand-written tables in JetTextCollation and the compact per-block strings can hold, hence a resource.
//
// Opt-in via LIBRED_GENERATE_V0=1: it inserts ~63,000 rows through ACE and rewrites a checked-in binary.
public class SortKeyTableV0GeneratorTest(ITestOutputHelper output)
{
    private const string ResourcePath = "src/LibRed/LibRed.Core/Resources/SortKeyTableV0.bin";

    /// <summary>Sentinel primary length meaning <b>ignorable</b>: ACE stores nothing for the character, not
    /// even a secondary slot. Distinct from a zero-length primary, which is a secondary-only combining
    /// mark.</summary>
    private const byte IgnorableLength = 0xFF;

    /// <summary>The constant that closes every kana key. Its meaning is unknown — it never varies across
    /// hiragana, katakana, halfwidth, small or voiced forms — so it is emitted as a literal.</summary>
    private const string KanaTail = "FF0280FF8000";

    [Fact]
    public void Generate_the_v0_sort_key_resource()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_GENERATE_V0") == "1",
            "set LIBRED_GENERATE_V0=1 — this measures ~63,000 characters through ACE and rewrites a resource");

        var entries = new SortedDictionary<int, (byte[] Primaries, byte Secondary, bool Ignorable)>();
        var inline = new SortedDictionary<int, byte>();
        var kana = new SortedDictionary<int, (byte Sound, byte Secondary, bool Small, byte Vowel)>();
        int inlineSkipped = 0, refused = 0, altered = 0;

        for (int chunk = 0x0000; chunk <= 0xF000; chunk += 0x1000)
        {
            string[] characters = Measurable(chunk, chunk + 0x0FFF);
            if (characters.Length == 0) continue;
            Dictionary<string, string> measured = AceKeys(characters);
            refused += characters.Length - measured.Count;
            foreach ((string text, string key) in measured)
            {
                // ACE did not store the value verbatim — something was trimmed or folded away on insert, so
                // the key cannot be attributed to a code point. (SPACE is excluded up front for exactly this
                // reason; this catches anything else that behaves the same way.)
                if (text.Length != 1) { altered++; continue; }
                // Kana first: their keys contain 01 01 01 too, so the inline test below would claim them.
                // 7F | 7F <sound> | 01 | [secondary] | 01 01 | [A0 if small] | FF 02 80 FF 80 | 00
                if (key.StartsWith("7F7F", StringComparison.Ordinal) && key.EndsWith(KanaTail, StringComparison.Ordinal))
                {
                    byte[] b = Convert.FromHexString(key);
                    int tail = b.Length - KanaTail.Length / 2;
                    int i = 4;
                    byte voicing = 0x02;
                    if (b[i] != 0x01) voicing = b[i++];
                    i += 2;                                  // the 01 01 that introduces the kana section
                    kana[text[0]] = (b[2], voicing, i < tail, 0);   // vowel filled by the second pass
                    continue;
                }
                // A word-sort ignorable records an inline 80 <pos> 06 <code> instead of a weight — no primary
                // and no secondary, so it is kept in its own stream. Measured alone the record is always
                // 7F 01 01 01 01 80 07 06 <code> 00; anything else means the shape is not what we think.
                if (key.Contains("010101"))
                {
                    byte[] bytes = Convert.FromHexString(key);
                    if (bytes.Length == 10 && bytes[5] == 0x80 && bytes[6] == 0x07 && bytes[7] == 0x06)
                        inline[text[0]] = bytes[8];
                    else
                        inlineSkipped++;
                    continue;
                }
                if (key == "7F0100") { entries[text[0]] = ([], 0x02, true); continue; }
                (byte[] primaries, byte secondary) = Decode(key);
                entries[text[0]] = (primaries, secondary, false);
            }
        }

        // Second pass for the prolonged sound mark. ー takes the preceding kana's VOWEL, not its sound —
        // が followed by ー is 7F 0A then 7F 02, "ga" lengthened by "a" — so the vowel has to be measured per
        // kana rather than derived from the sound, which would mean knowing the row structure.
        int measuredVowels = 0;
        string[] lengthened = [.. kana.Keys.Select(c => (char)c + "ー")];
        foreach ((string text, string key) in AceKeys(lengthened))
        {
            byte[] b = Convert.FromHexString(key);
            // 7F | 7F <sound> | 7F <vowel> | 01 …
            if (b.Length <= 5 || b[1] != 0x7F || b[3] != 0x7F) continue;
            (byte sound, byte secondary, bool small, byte _) = kana[text[0]];
            kana[text[0]] = (sound, secondary, small, b[4]);
            measuredVowels++;
        }
        output.WriteLine($"measured the lengthened vowel for {measuredVowels} of {kana.Count} kana");

        output.WriteLine($"measured {entries.Count} weighted code points " +
                         $"({entries.Count(e => e.Value.Ignorable)} ignorable), {inline.Count} word-sort " +
                         $"ignorables and {kana.Count} kana; skipped {inlineSkipped} of unexpected shape, " +
                         $"{altered} not stored verbatim, {refused} refused by ACE");

        byte[] blob = Build(entries, inline, kana);
        string path = Path.Combine(RepositoryRoot(), ResourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, blob);
        output.WriteLine($"wrote {path} ({blob.Length:N0} bytes)");

        // Read it straight back and check every entry survives, because both bugs the v1 generator hit
        // wrote a structurally valid but empty file, and both would have shipped silently.
        (var reloaded, var reloadedInline, var reloadedKana) = Parse(blob);
        Assert.Equal(entries.Count, reloaded.Count);
        foreach ((int codePoint, (byte[] primaries, byte secondary, bool ignorable)) in entries)
        {
            (byte[] gotPrimaries, byte gotSecondary, bool gotIgnorable) = reloaded[codePoint];
            Assert.Equal(ignorable, gotIgnorable);
            Assert.Equal(primaries, gotPrimaries);
            if (!ignorable) Assert.Equal(secondary, gotSecondary);
        }
        Assert.Equal(inline.Count, reloadedInline.Count);
        foreach ((int codePoint, byte code) in inline) Assert.Equal(code, reloadedInline[codePoint]);
        Assert.Equal(kana.Count, reloadedKana.Count);
        foreach ((int codePoint, var entry) in kana) Assert.Equal(entry, reloadedKana[codePoint]);
        output.WriteLine($"round-tripped {reloaded.Count} weights, {reloadedInline.Count} ignorables " +
                         $"and {reloadedKana.Count} kana");
    }

    /// <summary>The code points worth measuring. Controls and surrogates are excluded, and so is the plain
    /// SPACE: measured alone it is trimmed away and would be recorded as ignorable, which it is not — it is
    /// weight <c>0x07</c> inside a string. It is hand-verified in JetTextCollation regardless.</summary>
    private static string[] Measurable(int first, int last)
    {
        var characters = new List<string>();
        for (int c = first; c <= last; c++)
            if (c != ' ' && !char.IsControl((char)c) && !char.IsSurrogate((char)c))
                characters.Add(((char)c).ToString());
        return [.. characters];
    }

    private static byte[] Build(
        SortedDictionary<int, (byte[] Primaries, byte Secondary, bool Ignorable)> entries,
        SortedDictionary<int, byte> inline,
        SortedDictionary<int, (byte Sound, byte Secondary, bool Small, byte Vowel)> kana)
    {
        // Homogeneous streams rather than interleaved records: the v1 table compressed to ~194 KB
        // interleaved and ~16 KB split, because each column is nearly constant on its own.
        var deltas = new List<byte>();
        var lengths = new List<byte>();
        var primaries = new List<byte>();
        var secondaries = new List<byte>();
        int previous = 0;
        foreach ((int codePoint, (byte[] bytes, byte secondary, bool ignorable)) in entries)
        {
            WriteVarInt(deltas, codePoint - previous);
            previous = codePoint;
            lengths.Add(ignorable ? IgnorableLength : (byte)bytes.Length);
            if (!ignorable) primaries.AddRange(bytes);
            secondaries.Add(ignorable ? (byte)0 : secondary);
        }

        var inlineDeltas = new List<byte>();
        var inlineCodes = new List<byte>();
        previous = 0;
        foreach ((int codePoint, byte code) in inline)
        {
            WriteVarInt(inlineDeltas, codePoint - previous);
            previous = codePoint;
            inlineCodes.Add(code);
        }

        var kanaDeltas = new List<byte>();
        var kanaSounds = new List<byte>();
        var kanaSecondaries = new List<byte>();
        var kanaSmall = new List<byte>();
        var kanaVowels = new List<byte>();
        previous = 0;
        foreach ((int codePoint, (byte sound, byte secondary, bool small, byte vowel)) in kana)
        {
            WriteVarInt(kanaDeltas, codePoint - previous);
            previous = codePoint;
            kanaSounds.Add(sound);
            // The small flag gets a stream of its own. Riding it in the secondary's top bit looked safe —
            // voicing is 02, 03 or 04 — but at least one kana carries a secondary of 0xEE, which the flag
            // then destroyed. The stream is all zeros and ones and compresses to nothing anyway.
            kanaSecondaries.Add(secondary);
            kanaSmall.Add(small ? (byte)1 : (byte)0);
            // 0 where the vowel could not be measured — the encoder refuses ー after such a kana.
            kanaVowels.Add(vowel);
        }

        var blob = new MemoryStream();
        var writer = new BinaryWriter(blob);
        writer.Write(entries.Count);
        writer.Write(inline.Count);
        writer.Write(kana.Count);
        foreach (List<byte> stream in new[]
                 { deltas, lengths, primaries, secondaries, inlineDeltas, inlineCodes,
                   kanaDeltas, kanaSounds, kanaSecondaries, kanaSmall, kanaVowels })
        {
            byte[] compressed = Compress([.. stream]);
            writer.Write(compressed.Length);
            writer.Write(compressed, 0, compressed.Length);
        }
        writer.Flush();
        return blob.ToArray();
    }

    private static (Dictionary<int, (byte[] Primaries, byte Secondary, bool Ignorable)> Weights,
                    Dictionary<int, byte> Inline,
                    Dictionary<int, (byte Sound, byte Secondary, bool Small, byte Vowel)> Kana) Parse(byte[] blob)
    {
        var reader = new BinaryReader(new MemoryStream(blob));
        int count = reader.ReadInt32();
        int inlineCount = reader.ReadInt32();
        int kanaCount = reader.ReadInt32();
        var streams = new byte[11][];
        for (int i = 0; i < 11; i++) streams[i] = Decompress(reader.ReadBytes(reader.ReadInt32()));

        var entries = new Dictionary<int, (byte[], byte, bool)>(count);
        int offset = 0, codePoint = 0, primaryOffset = 0;
        for (int i = 0; i < count; i++)
        {
            codePoint += ReadVarInt(streams[0], ref offset);
            byte length = streams[1][i];
            bool ignorable = length == IgnorableLength;
            byte[] bytes = [];
            if (!ignorable)
            {
                bytes = streams[2][primaryOffset..(primaryOffset + length)];
                primaryOffset += length;
            }
            entries[codePoint] = (bytes, streams[3][i], ignorable);
        }

        var inline = new Dictionary<int, byte>(inlineCount);
        offset = 0;
        codePoint = 0;
        for (int i = 0; i < inlineCount; i++)
        {
            codePoint += ReadVarInt(streams[4], ref offset);
            inline[codePoint] = streams[5][i];
        }

        var kana = new Dictionary<int, (byte Sound, byte Secondary, bool Small, byte Vowel)>(kanaCount);
        offset = 0;
        codePoint = 0;
        for (int i = 0; i < kanaCount; i++)
        {
            codePoint += ReadVarInt(streams[6], ref offset);
            kana[codePoint] = (streams[7][i], streams[8][i], streams[9][i] != 0, streams[10][i]);
        }
        return (entries, inline, kana);
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

    private static (byte[] Primaries, byte Secondary) Decode(string hex)
    {
        byte[] key = Convert.FromHexString(hex);
        int end = key.Length - 1;
        int split = end - 1;
        while (split > 0 && key[split] != 0x01) split--;
        return (key[1..split], end - split == 1 ? (byte)0x02 : key[split + 1]);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EFCore.Jet.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("EFCore.Jet.sln not found above the test output.");
    }

    private static Dictionary<string, string> AceKeys(string[] samples)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "v0gen-");
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
