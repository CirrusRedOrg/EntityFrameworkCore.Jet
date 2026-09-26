using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using LibRed.Catalog;

namespace LibRed.Storage;

/// <summary>
/// Text compared as ACE compares it: in one collation, by the keys its index encoder writes for that collation
/// (case folded, accents significant, hyphens and apostrophes weighed only after the letters, trailing spaces
/// ignored, and each locale's own letters where it has them — see <see cref="JetTextCollation"/>).
/// </summary>
/// <remarks>
/// <para>A query compares text in the <b>database's</b> collation — page 0's, never a column's (page-02b §3.4,
/// verified against ACE) — so an engine uses the one comparer <see cref="For"/> gives for
/// <see cref="JetDatabase.Collation"/>.</para>
/// <para>Built on LibRed's own weight tables alone, which cover the whole Basic Multilingual Plane, so the answer
/// is the same on every platform: nothing here consults the runtime's culture data, whose collation differs
/// between ICU and Windows NLS and between their versions. An order the index encoder refuses is refused here
/// too, at the first comparison, rather than answered in some other order.</para>
/// </remarks>
public sealed class JetTextComparer : IEqualityComparer<string>
{
    private static readonly ConcurrentDictionary<Collation, JetTextComparer> Comparers = new();

    /// <summary>The comparer for <paramref name="collation"/>.</summary>
    public static JetTextComparer For(Collation collation) => Comparers.GetOrAdd(collation, c => new JetTextComparer(c));

    private readonly LocaleTailoring? _tailoring;
    private readonly bool _version1;

    private JetTextComparer(Collation collation)
    {
        Collation = collation;
        _tailoring = collation.IsIndexKeyEncodable ? JetLocaleTailoring.For(collation) : null;
        _version1 = collation.Version == Collation.GeneralVersion;
    }

    /// <summary>The collation this compares in.</summary>
    public Collation Collation { get; }

    /// <summary>Whether this is a General order with nothing tailored — no letter of its own and no reversed
    /// accents — so plain ASCII compares as it does in General (see <c>PlainTextCollationTests</c>). A locale
    /// that tailors nothing counts: its keys are General's.</summary>
    public bool IsUntailoredGeneral =>
        Collation.IsIndexKeyEncodable && _tailoring is null or { Entries.Count: 0, ReverseDiacritics: false };

    // Both sides are encoded into this thread's buffers: a comparison is made per row, or per pair in a sort,
    // and fresh lists each time were most of what a text GROUP BY allocated.
    [ThreadStatic] private static List<byte>? t_left;
    [ThreadStatic] private static List<byte>? t_right;

    /// <summary>The key <paramref name="text"/> sorts by. Two keys compare, byte for byte, as
    /// <see cref="Compare(string, string)"/> compares their texts, so a sort can build each value's key once.</summary>
    public byte[] Key(string text)
    {
        List<byte> key = t_left ??= [];
        Encode(text, key);
        return [.. key];
    }

    /// <summary>The sign of <paramref name="a"/> against <paramref name="b"/>.</summary>
    public int Compare(string a, string b)
    {
        List<byte> left = t_left ??= [], right = t_right ??= [];
        Encode(a, left);
        Encode(b, right);
        return Math.Sign(CollectionsMarshal.AsSpan(left).SequenceCompareTo(CollectionsMarshal.AsSpan(right)));
    }

    /// <summary>The sign of <paramref name="a"/> against the text whose <see cref="Key"/> is
    /// <paramref name="bKey"/> — for a side compared with many values, whose key is made once.</summary>
    public int Compare(string a, byte[] bKey)
    {
        List<byte> left = t_left ??= [];
        Encode(a, left);
        return Math.Sign(CollectionsMarshal.AsSpan(left).SequenceCompareTo(bKey));
    }

    /// <summary>Whether the two texts are one value in this collation.</summary>
    /// <remarks>Texts identical once trailing spaces are dropped are equal in any order, so they are answered
    /// without encoding either.</remarks>
    public bool Equals(string? x, string? y)
    {
        if (x is null || y is null) return x is null && y is null;
        return x.AsSpan().TrimEnd(' ').SequenceEqual(y.AsSpan().TrimEnd(' ')) || Compare(x, y) == 0;
    }

    /// <summary>A hash that agrees with <see cref="Equals(string?, string?)"/>: taken over the key.</summary>
    public int GetHashCode(string text)
    {
        List<byte> key = t_left ??= [];
        Encode(text, key);
        var hash = new HashCode();
        hash.AddBytes(CollectionsMarshal.AsSpan(key));
        return hash.ToHashCode();
    }

    private void Encode(string text, List<byte> output)
    {
        if (!Collation.IsIndexKeyEncodable)
            throw new NotSupportedException(
                $"Text is compared in the database's collation, {Collation.Order} version {Collation.Version}" +
                (Collation.SortId == 0 ? "" : $" sort id {Collation.SortId}") + ", which is not implemented yet.");

        output.Clear();
        bool encoded = _version1
            ? JetTextCollationV1.TryEncode(text, output, _tailoring, out _)
            : JetTextCollation.TryEncode(text, output, _tailoring, out _);

        // Both tables cover the whole BMP, and each order handles surrogates (CollationBmpCoverageTests), so
        // this cannot be reached by any string; refusing is still the answer if it ever is.
        if (!encoded)
            throw new NotSupportedException(
                $"'{text}' holds a character the {Collation.Order} v{Collation.Version} order has no weight for.");
    }
}
