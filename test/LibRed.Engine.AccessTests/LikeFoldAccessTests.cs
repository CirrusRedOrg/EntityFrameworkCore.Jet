using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// LibRed's LIKE matches exactly the pairs ACE's does, over every candidate — each BMP character against its upper
/// and lower case, the Turkish i forms, and each character against the letters it decomposes to (LikeCandidates).
/// One database is enough: ACE's answer is the same in every collation.
/// </summary>
[Collection(AceCollection.Name)]
public class LikeFoldAccessTests
{
    [Fact]
    public void Every_candidate_pair_matches_as_ace_matches_it()
    {
        var pairs = LikeCandidates.Pairs;
        HashSet<int> ace = LikeCandidates.AceMatches(Collation.GeneralLegacy);
        HashSet<int> libred = LikeCandidates.LibRedMatches(Collation.GeneralLegacy);

        string Show(int i) => $"'{pairs[i].Value}' LIKE '{pairs[i].Pattern}'";
        var missed = ace.Except(libred).Take(20).Select(Show).ToList();
        var extra = libred.Except(ace).Take(20).Select(Show).ToList();
        Assert.True(missed.Count == 0 && extra.Count == 0,
            $"ACE only: {string.Join(", ", missed)}; LibRed only: {string.Join(", ", extra)}");
        Assert.True(ace.Count > 1_900, $"ACE matched only {ace.Count} — the instrument is not measuring");
    }
}
