using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

// GENERATOR: builds LibRed.Engine's embedded LIKE folding table by asking ACE.
//
// ACE's LIKE ignores case through a table of its own — narrower than any runtime's: it leaves the micro sign,
// long s, final sigma, the Greek symbol variants, the titlecase digraphs and every letter Unicode added later
// unfolded — and spells four letters out: ß as ss, æ as ae, œ as oe, þ as th. The runtime's casing differs from
// that and between platforms, so the table is ACE's, measured: every candidate pair (LikeCandidates) asked in one
// database, since the answer is the same in every collation.
//
// Opt-in via LIBRED_GENERATE_LIKE=1: it asks ACE 26,000 pairs and rewrites a checked-in resource.
[Collection(AceCollection.Name)]
public class LikeFoldTableGeneratorTest(ITestOutputHelper output)
{
    private const string ResourcePath = "src/LibRed/LibRed.Engine/Resources/LikeFold.bin";

    [Fact]
    public void Generate_the_like_fold_resource()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_GENERATE_LIKE") == "1",
            "set LIBRED_GENERATE_LIKE=1 — this asks ACE 26,000 pairs and rewrites a resource");

        var pairs = LikeCandidates.Pairs;
        HashSet<int> matched = LikeCandidates.AceMatches(Collation.GeneralLegacy);

        // Case pairs: one character each side. Each must be matched both ways and pair with nothing else, or a
        // simple map cannot describe it.
        var partner = new Dictionary<char, char>();
        foreach (int i in matched.Where(i => pairs[i].Value.Length == 1 && pairs[i].Pattern.Length == 1))
        {
            (char a, char b) = (pairs[i].Value[0], pairs[i].Pattern[0]);
            Assert.True(matched.Contains(Array.IndexOf(pairs, (pairs[i].Pattern, pairs[i].Value))), $"{a}~{b} one way only");
            Assert.True(!partner.TryGetValue(a, out char existing) || existing == b, $"{a} folds with {existing} and {b}");
            partner[a] = b;
        }

        // Each pair folds to one member — the one the other upper-cases to, else the capital. Decided here, at
        // generation, so nothing at run time consults the runtime's casing.
        var folded = new SortedDictionary<char, char>();
        foreach ((char a, char b) in partner)
        {
            if (a > b) continue;
            char to = char.ToUpperInvariant(a) == b ? b
                : char.ToUpperInvariant(b) == a ? a
                : char.IsUpper(b) ? b : a;
            char from = to == a ? b : a;
            folded[from] = to;
        }
        char Fold(char c) => folded.TryGetValue(c, out char to) ? to : c;

        // Expansions: one character against a longer spelling, recorded as that spelling folded.
        var expansions = new SortedDictionary<char, string>();
        foreach (int i in matched.Where(i => pairs[i].Value.Length != pairs[i].Pattern.Length))
        {
            (string one, string many) = pairs[i].Value.Length == 1 ? (pairs[i].Value, pairs[i].Pattern) : (pairs[i].Pattern, pairs[i].Value);
            Assert.Equal(1, one.Length);
            string spelling = string.Concat(many.Select(Fold));
            Assert.True(!expansions.TryGetValue(one[0], out string? had) || had == spelling, $"{one} spelt {had} and {spelling}");
            expansions[one[0]] = spelling;
        }

        // The layout of the sort-key tables: the counts, then each section deflated on its own.
        //   case pairs: (character, what it folds to), a UTF-16 unit each
        //   expansions: (character, letter count, the letters folded)
        var cases = new MemoryStream();
        using (var writer = new BinaryWriter(cases, System.Text.Encoding.UTF8, leaveOpen: true))
            foreach ((char from, char to) in folded) { writer.Write((ushort)from); writer.Write((ushort)to); }
        var spelt = new MemoryStream();
        using (var writer = new BinaryWriter(spelt, System.Text.Encoding.UTF8, leaveOpen: true))
            foreach ((char c, string letters) in expansions)
            {
                writer.Write((ushort)c);
                writer.Write((byte)letters.Length);
                foreach (char letter in letters) writer.Write((ushort)letter);
            }

        var blob = new MemoryStream();
        using (var writer = new BinaryWriter(blob, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(folded.Count);
            writer.Write(expansions.Count);
            WriteDeflated(writer, cases.ToArray());
            WriteDeflated(writer, spelt.ToArray());
        }

        string path = Path.Combine(RepositoryRoot(), ResourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, blob.ToArray());
        output.WriteLine($"wrote {path} ({blob.Length:N0} bytes): {folded.Count} case pairs, {expansions.Count} expansions, " +
                         $"from {matched.Count} matches");

        // Read it straight back, as the sort-key generators do: a structurally valid but empty file would
        // otherwise ship silently.
        (var reloadedFold, var reloadedExpansions) = Parse(blob.ToArray());
        Assert.Equal(folded, reloadedFold);
        Assert.Equal(expansions, reloadedExpansions);

        // What one run found; a different count means ACE changed, or the candidates did.
        Assert.Equal(973, folded.Count);
        Assert.Equal(7, expansions.Count);
    }

    private static void WriteDeflated(BinaryWriter writer, byte[] data)
    {
        var compressed = new MemoryStream();
        using (var deflate = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(data);
        writer.Write((int)compressed.Length);
        writer.Write(compressed.ToArray());
    }

    private static (SortedDictionary<char, char> Fold, SortedDictionary<char, string> Expansions) Parse(byte[] blob)
    {
        var reader = new BinaryReader(new MemoryStream(blob));
        int caseCount = reader.ReadInt32(), expansionCount = reader.ReadInt32();
        var cases = new BinaryReader(new MemoryStream(Inflate(reader)));
        var spelt = new BinaryReader(new MemoryStream(Inflate(reader)));

        var fold = new SortedDictionary<char, char>();
        for (int i = 0; i < caseCount; i++) fold[(char)cases.ReadUInt16()] = (char)cases.ReadUInt16();
        var expansions = new SortedDictionary<char, string>();
        for (int i = 0; i < expansionCount; i++)
        {
            char c = (char)spelt.ReadUInt16();
            int length = spelt.ReadByte();
            expansions[c] = new string([.. Enumerable.Range(0, length).Select(_ => (char)spelt.ReadUInt16())]);
        }
        return (fold, expansions);

        static byte[] Inflate(BinaryReader reader)
        {
            byte[] compressed = reader.ReadBytes(reader.ReadInt32());
            var output = new MemoryStream();
            using (var inflate = new System.IO.Compression.ZLibStream(new MemoryStream(compressed), System.IO.Compression.CompressionMode.Decompress))
                inflate.CopyTo(output);
            return output.ToArray();
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EFCore.Jet.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("EFCore.Jet.sln not found above the test output.");
    }
}
