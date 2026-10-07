using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Under an <b>untailored</b> General order — either version — two texts of printable ASCII without a hyphen or
/// apostrophe are one value exactly when they are equal ignoring case. <c>InStr</c> leans on that to search such
/// text directly instead of trying every substring against the collation, and only under those orders: a tailored
/// one gives plain ASCII letters weights of their own (Czech <c>ch</c>, Danish <c>aa</c>), so nothing here holds
/// for it. Held over every string of up to two characters and a random sweep of longer ones.
/// </summary>
public class PlainTextCollationTests
{
    private static readonly char[] Plain =
        [.. Enumerable.Range(0x20, 0x7F - 0x20).Select(c => (char)c).Where(c => c is not ('-' or '\''))];

    public static TheoryData<byte> Versions => [0, Collation.GeneralVersion];

    [Theory]
    [MemberData(nameof(Versions))]
    public void Plain_ascii_is_equal_exactly_when_equal_ignoring_case(byte version)
    {
        JetTextComparer comparer = JetTextComparer.For(new Collation(CollatingOrder.General, version));
        Assert.True(comparer.IsUntailoredGeneral);

        var random = new Random(20260927);
        IEnumerable<string> texts = Plain.Select(c => c.ToString())
            .Concat(Plain.SelectMany(a => Plain.Select(b => $"{a}{b}")))
            .Concat(Enumerable.Range(0, 100_000).Select(_ =>
                new string([.. Enumerable.Range(0, random.Next(1, 8)).Select(_ => Plain[random.Next(Plain.Length)])])));

        var byKey = new Dictionary<string, string>();
        var byFolded = new Dictionary<string, string>();
        foreach (string text in texts.Distinct())
        {
            string key = Convert.ToHexString(comparer.Key(text)), folded = text.TrimEnd(' ').ToUpperInvariant();
            if (byKey.TryGetValue(key, out string? sameKey))
                Assert.True(sameKey.TrimEnd(' ').ToUpperInvariant() == folded, $"'{sameKey}' and '{text}' share a key.");
            else
                byKey[key] = text;
            if (byFolded.TryGetValue(folded, out string? sameText))
                Assert.True(Convert.ToHexString(comparer.Key(sameText)) == key, $"'{sameText}' and '{text}' key apart.");
            else
                byFolded[folded] = text;
        }
    }

    [Fact]
    public void A_tailored_order_is_not_untailored_general()
        => Assert.False(JetTextComparer.For(new Collation(CollatingOrder.Czech, 0)).IsUntailoredGeneral);
}
