using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// <see cref="IndexWriter.CompareWithTrailer"/> compares <c>key ++ trailer</c> without building that array —
/// the leaf insert scans every entry on a page for every row inserted, so materialising a concatenation per
/// comparison was the write path's largest allocator. These pin it against the naive construction it replaced.
/// </summary>
/// <remarks>
/// The case worth caring about is one key being a <b>prefix</b> of another. The byte comparison walks the
/// shared prefix and only then falls back to length, so it runs on into the trailer bytes — which means
/// "compare keys, then break ties on the trailer" is a DIFFERENT relation, and using it would misplace entries
/// rather than throw. Several pairs below differ only there.
/// </remarks>
public class IndexKeyTrailerComparisonTests
{
    private static readonly byte[][] Keys =
    [
        [],
        [0x00],
        [0x01],
        [0x7F],
        [0x7F, 0x00],
        [0x7F, 0x01],
        [0x7F, 0x01, 0x00],
        [0x7F, 0x01, 0x00, 0x00],
        [0x7F, 0x01, 0x00, 0x00, 0x00],   // a key whose tail looks like a trailer
        [0x7F, 0xFF],
        [0xFF],
        [0xFF, 0xFF, 0xFF, 0xFF],
    ];

    private static readonly int[] Trailers = [0, 1, 0x0100, 0x7F000001, unchecked((int)0xFFFFFFFF), 0x00FFFFFF];

    /// <summary>The construction the production code used to do, kept here as the oracle.</summary>
    private static int Naive(byte[] key, int trailer, byte[] other)
    {
        var full = new byte[key.Length + 4];
        key.CopyTo(full, 0);
        full[key.Length] = (byte)(trailer >> 24);
        full[key.Length + 1] = (byte)(trailer >> 16);
        full[key.Length + 2] = (byte)(trailer >> 8);
        full[key.Length + 3] = (byte)trailer;

        int n = Math.Min(full.Length, other.Length);
        for (int i = 0; i < n; i++)
            if (full[i] != other[i]) return full[i] - other[i];
        return full.Length - other.Length;
    }

    [Fact]
    public void Agrees_with_the_materialised_comparison_over_every_pair()
    {
        // Every (key, trailer) against every other (key, trailer) rendered as a full entry — 5,184 pairs,
        // including the prefix-vs-longer-key cases that a keys-then-trailer comparison gets wrong.
        int checkedPairs = 0;
        foreach (byte[] key in Keys)
        foreach (int trailer in Trailers)
        foreach (byte[] otherKey in Keys)
        foreach (int otherTrailer in Trailers)
        {
            byte[] other = new byte[otherKey.Length + 4];
            otherKey.CopyTo(other, 0);
            other[otherKey.Length] = (byte)(otherTrailer >> 24);
            other[otherKey.Length + 1] = (byte)(otherTrailer >> 16);
            other[otherKey.Length + 2] = (byte)(otherTrailer >> 8);
            other[otherKey.Length + 3] = (byte)otherTrailer;

            int expected = Naive(key, trailer, other);
            int actual = IndexWriter.CompareWithTrailer(key, trailer, other);
            Assert.True(
                Math.Sign(expected) == Math.Sign(actual),
                $"key=[{Convert.ToHexString(key)}] trailer=0x{trailer:X8} vs [{Convert.ToHexString(other)}]: "
                + $"expected sign {Math.Sign(expected)}, got {Math.Sign(actual)}");
            checkedPairs++;
        }

        Assert.Equal(Keys.Length * Trailers.Length * Keys.Length * Trailers.Length, checkedPairs);
    }

    [Fact]
    public void A_prefix_key_is_ordered_by_the_trailer_bytes_that_follow_it()
    {
        // [0x7F] ++ trailer 0xFF000000 against the longer key [0x7F, 0x01]: the fifth byte decides, and it is
        // the trailer's first byte on one side and the key's second byte on the other. Comparing keys first
        // would call [0x7F] smaller unconditionally.
        byte[] longer = [0x7F, 0x01, 0x00, 0x00, 0x00, 0x00];   // key [0x7F,0x01] ++ trailer 0
        Assert.True(IndexWriter.CompareWithTrailer([0x7F], unchecked((int)0xFF000000), longer) > 0);
        Assert.True(IndexWriter.CompareWithTrailer([0x7F], 0x00000000, longer) < 0);
    }

    [Fact]
    public void Equal_key_and_trailer_compare_equal()
        => Assert.Equal(0, IndexWriter.CompareWithTrailer([0x7F, 0x2A], 0x01020304, [0x7F, 0x2A, 0x01, 0x02, 0x03, 0x04]));
}
