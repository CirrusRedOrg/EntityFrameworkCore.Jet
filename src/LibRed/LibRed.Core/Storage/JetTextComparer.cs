using System.Runtime.InteropServices;

namespace LibRed.Storage;

/// <summary>
/// Text compared in Jet's General sort order — the order ACE's text index keys hold, and the one it compares text in
/// (case folded, accents significant, <c>ß</c> as <c>ss</c>, hyphens and apostrophes weighed only after the letters,
/// trailing spaces ignored). See <see cref="JetTextCollation"/>.
/// </summary>
public static class JetTextComparer
{
    /// <summary>The sign of <paramref name="a"/> against <paramref name="b"/>, or null when either holds a character
    /// the order does not cover.</summary>
    public static int? Compare(string a, string b)
    {
        var left = new List<byte>(a.Length + 4);
        var right = new List<byte>(b.Length + 4);
        if (!JetTextCollation.TryEncode(a, left) || !JetTextCollation.TryEncode(b, right))
            return null;
        return Math.Sign(CollectionsMarshal.AsSpan(left).SequenceCompareTo(CollectionsMarshal.AsSpan(right)));
    }
}