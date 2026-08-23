using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: is General v0 the NT4-era NLS order, renumbered into one byte?
//
// v1 was identified outright: its primaries ARE the Windows NLS (Script Member, Alphabetic Weight) pair
// copied verbatim, so scoring measured ACE keys against every published table found Server 2008 at 25/25.
// v0 cannot be identified that way, because its primaries are a Jet-specific compaction into a SINGLE byte —
// which is why its table had to be measured character by character instead.
//
// But if that compaction is order-preserving, v0 stops being 1,500 measured facts and becomes one rule:
// "the NT4-era NLS order, renumbered into one byte with gaps left for language letters". Jet 3.5 shipped with
// Access 97 and Jet 4 with Access 2000, so NT 4.0–Server 2003 is the contemporary table.
//
// The test: sort every character by the NT4 table's primary (SM, AW) and check LibRed's v0 primary bytes come
// out non-decreasing. Set LIBRED_NT4_TABLE to "Windows NT 4.0 through Windows Server 2003 Sorting Weight
// Table.txt" (linked from [MS-UCODEREF]) to run it.
public class SortOrderProvenanceProbeTest(ITestOutputHelper output)
{
    [Fact]
    public void Probe_whether_v0_preserves_the_nt4_primary_order()
    {
        string? path = Environment.GetEnvironmentVariable("LIBRED_NT4_TABLE");
        Assert.SkipWhen(path is null || !File.Exists(path), "LIBRED_NT4_TABLE is not set to the NT4–2003 table");

        Dictionary<char, int> nt4 = ParseTable(path!);
        output.WriteLine($"NT4–2003 table: {nt4.Count} weighted code points");

        // Every character LibRed encodes in the blocks the v0 tables cover.
        var rows = new List<(char Character, int Nt4, byte[] V0)>();
        int ignorable = 0, ignorableAndUnweighted = 0, unencodable = 0;
        foreach (char c in Characters())
        {
            if (!TryPrimaries(c, out byte[]? primaries)) { unencodable++; continue; }
            bool weighted = nt4.TryGetValue(c, out int primary) && primary != 0;
            if (primaries.Length == 0)
            {
                // v0 stores nothing for it. Does the NT4 table agree it has no primary?
                ignorable++;
                if (!weighted) ignorableAndUnweighted++;
                continue;
            }
            if (weighted) rows.Add((c, primary, primaries));
        }

        output.WriteLine($"{rows.Count} characters weighted by both; {unencodable} LibRed does not encode; " +
                         $"{ignorable} ignorable in v0, of which {ignorableAndUnweighted} " +
                         $"({(ignorable == 0 ? 0 : 100.0 * ignorableAndUnweighted / ignorable):F0}%) " +
                         $"are also unweighted in the NT4 table");

        // Sorted by the NT4 primary, is the v0 primary sequence non-decreasing?
        rows.Sort((a, b) => a.Nt4 != b.Nt4 ? a.Nt4.CompareTo(b.Nt4) : Compare(a.V0, b.V0));
        var violations = new List<string>();
        int ties = 0, tiesAgreeing = 0;
        for (int i = 1; i < rows.Count; i++)
        {
            (char previous, int previousNt4, byte[] previousV0) = rows[i - 1];
            (char current, int currentNt4, byte[] currentV0) = rows[i];
            if (previousNt4 == currentNt4)
            {
                ties++;
                if (Compare(previousV0, currentV0) == 0) tiesAgreeing++;
                continue;
            }
            if (Compare(previousV0, currentV0) > 0)
                violations.Add($"{Describe(previous)} NT4 {previousNt4:X4} v0 {Convert.ToHexString(previousV0)}  " +
                               $"> {Describe(current)} NT4 {currentNt4:X4} v0 {Convert.ToHexString(currentV0)}");
        }

        // Per block, so a script whose order Jet renumbered independently shows up as such rather than
        // dragging down a single global figure.
        var perBlock = new SortedDictionary<string, (int Ordered, int Kept)>();
        for (int i = 1; i < rows.Count; i++)
        {
            if (rows[i - 1].Nt4 == rows[i].Nt4) continue;
            string block = BlockOf(rows[i].Character);
            (int blockOrdered, int blockKept) = perBlock.GetValueOrDefault(block);
            bool kept = Compare(rows[i - 1].V0, rows[i].V0) <= 0;
            perBlock[block] = (blockOrdered + 1, blockKept + (kept ? 1 : 0));
        }

        int ordered = rows.Count - 1 - ties;
        output.WriteLine("");
        output.WriteLine("agreement by block:");
        foreach ((string block, (int blockOrdered, int blockKept)) in perBlock)
            output.WriteLine($"    {block,-28} {blockKept,4}/{blockOrdered,-4} " +
                             $"{(blockOrdered == 0 ? 0 : 100.0 * blockKept / blockOrdered):F1}%");
        output.WriteLine("");
        output.WriteLine($"strictly-ordered NT4 pairs: {ordered}, of which {ordered - violations.Count} " +
                         $"({(ordered == 0 ? 0 : 100.0 * (ordered - violations.Count) / ordered):F2}%) " +
                         "keep their order in v0");
        output.WriteLine($"NT4 ties: {ties}, of which {tiesAgreeing} also tie in v0");
        output.WriteLine("");
        output.WriteLine($"order violations ({violations.Count}):");
        foreach (string line in violations.Take(40)) output.WriteLine($"    {line}");
    }

    /// <summary>The DEFAULT SORTKEY block only: the per-locale COMPRESSION tables that follow redefine the
    /// same code points and would silently corrupt the parse. Columns are
    /// <c>codepoint SM AW DW CW</c>; the primary is (SM, AW).</summary>
    private static Dictionary<char, int> ParseTable(string path)
    {
        var weights = new Dictionary<char, int>();
        bool inSortKey = false;
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("SORTKEY")) { inSortKey = true; continue; }
            if (trimmed.StartsWith("ENDSORTKEY")) { inSortKey = false; continue; }
            if (!inSortKey || !line.StartsWith("0x")) continue;

            string[] fields = line.Split(';')[0].Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 5) continue;
            int codePoint = Convert.ToInt32(fields[0], 16);
            if (codePoint > 0xFFFF) continue;
            weights[(char)codePoint] = (int.Parse(fields[1]) << 8) | int.Parse(fields[2]);
        }
        return weights;
    }

    private static IEnumerable<char> Characters()
    {
        (int First, int Last)[] blocks =
        [
            (0x0020, 0x024F), (0x02B0, 0x02FF), (0x0370, 0x052F), (0x0590, 0x06FF),
            (0x1E00, 0x1EFF), (0x2000, 0x206F), (0x20A0, 0x20BF), (0x2100, 0x218F), (0xFF01, 0xFF65),
        ];
        foreach ((int first, int last) in blocks)
            for (int c = first; c <= last; c++)
                if (!char.IsControl((char)c) && !char.IsSurrogate((char)c))
                    yield return (char)c;
    }

    /// <summary>The primary weight bytes LibRed emits for a character under General v0, or false when it
    /// refuses the character. An empty array means the character is ignorable.</summary>
    private static bool TryPrimaries(char c, out byte[] primaries)
    {
        primaries = [];
        var column = new ColumnDef
        {
            Name = "K", Type = JetDataType.Text, Index = 0, Collation = Collation.GeneralLegacy,
        };
        byte[] key;
        try { key = IndexKeyEncoder.Encode([(column, true)], [c.ToString()]); }
        catch (NotSupportedException) { return false; }

        int split = key.Length - 2;
        while (split > 0 && key[split] != 0x01) split--;
        primaries = key[1..split];
        return true;
    }

    private static int Compare(byte[] a, byte[] b) => a.AsSpan().SequenceCompareTo(b);

    private static string BlockOf(char c) => c switch
    {
        <= (char)0x00FF => "Latin-1 + ASCII",
        <= (char)0x017F => "Latin Extended-A",
        <= (char)0x024F => "Latin Extended-B",
        <= (char)0x02FF => "Spacing modifiers",
        <= (char)0x03FF => "Greek",
        <= (char)0x052F => "Cyrillic",
        <= (char)0x05FF => "Hebrew",
        <= (char)0x06FF => "Arabic",
        <= (char)0x1EFF => "Latin Extended Additional",
        <= (char)0x206F => "General punctuation",
        <= (char)0x20BF => "Currency",
        <= (char)0x214F => "Letterlike",
        <= (char)0x218F => "Number forms",
        _ => "Fullwidth forms",
    };

    private static string Describe(char c) =>
        c is >= ' ' and <= '~' ? $"'{c}'" : $"U+{(int)c:X4}";
}
