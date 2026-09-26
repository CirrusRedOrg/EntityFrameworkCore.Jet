using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Every code point of the Basic Multilingual Plane has a key in both General orders — alone, between letters,
/// and as a lone surrogate half. A text comparison is built on these keys and on nothing else, so a character
/// without one would leave it with no answer the collation gives.
/// </summary>
public class CollationBmpCoverageTests
{
    private static List<int> Unencodable(Func<string, List<byte>, bool> encode)
    {
        var missing = new List<int>();
        for (int c = 0; c <= 0xFFFF; c++)
            if (!encode(((char)c).ToString(), []) || !encode("a" + (char)c + "b", []))
                missing.Add(c);
        return missing;
    }

    private static string Describe(List<int> missing) =>
        $"{missing.Count} unencodable: {string.Join(" ", missing.Select(c => $"U+{c:X4}"))}";

    [Fact]
    public void General_legacy_v0_encodes_the_whole_bmp()
    {
        List<int> missing = Unencodable((s, o) => JetTextCollation.TryEncode(s, o));
        Assert.True(missing.Count == 0, Describe(missing));
    }

    [Fact]
    public void General_v1_encodes_the_whole_bmp()
    {
        List<int> missing = Unencodable((s, o) => JetTextCollationV1.TryEncode(s, o));
        Assert.True(missing.Count == 0, Describe(missing));
    }
}
