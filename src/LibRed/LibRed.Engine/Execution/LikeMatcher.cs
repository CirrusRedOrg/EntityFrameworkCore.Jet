namespace LibRed.Engine.Execution;

/// <summary>
/// The <c>LIKE</c> match with ANSI-92 wildcards, the set EF Core emits over both OLE DB and ODBC (verified vs ACE).
/// </summary>
/// <remarks>
/// <para><b>The wildcard set belongs to the connection, not to the database file</b> — measured over ACE
/// across nine combinations. OLE DB is ANSI-92 for ad-hoc SQL and for saved queries alike; ODBC is too once
/// <c>ExtendedAnsiSQL=1</c> is set, which <c>JetConnection</c> sets on every ODBC connection it makes. Only a
/// bare ODBC connection — one this repo never opens — reads a saved query's pattern as ANSI-89. The
/// <c>MSysDb</c> property <c>ANSI Query Mode</c> changes none of it: it is an Access application setting the
/// engine does not consult, so there is one wildcard set here, not two. Access's own saved queries are full
/// of <c>*</c> patterns, which makes the opposite look true; it isn't, and this is the expensive way to find
/// out. See <c>system-catalog.md</c>.</para>
/// <list type="bullet">
/// <item><c>%</c> matches any run and <c>_</c> any one character. <c>*</c>, <c>?</c> and <c>#</c> are plain
/// characters.</item>
/// <item><c>[abc]</c> matches one of the characters listed, <c>[!abc]</c> one character not listed, and <c>a-c</c>
/// inside the brackets a range. A <c>-</c> at either end is itself listed, <c>[!]</c> is the character <c>!</c>, and
/// <c>[]</c> matches nothing at all, so <c>[]]</c> is a plain <c>]</c> and <c>[[]]</c> the text <c>[]</c>. The
/// Access documentation gives <c>^</c> as the ANSI-92 negation, but ACE does not treat it so: <c>[^ae]</c> lists
/// <c>^</c>, <c>a</c> and <c>e</c>, and <c>!</c> negates as it does in ANSI-89.</item>
/// <item>Case is ignored and accents are not. <c>ß</c> counts as <c>ss</c> and <c>æ</c> as <c>ae</c>, in the pattern,
/// in a bracket list and in the value, so <c>'aßb' LIKE 'a[s]sb'</c> is True; <c>_</c> still takes the whole
/// character.</item>
/// <item>A bracket that is never closed, or a range written backwards, is an invalid pattern. It is only reported
/// when the match reaches it with a character left to test: <c>'' LIKE '['</c> and <c>'z' LIKE 'x[z-a]'</c> are
/// both False.</item>
/// <item>There is no <c>ESCAPE</c> clause; ACE rejects it as a syntax error. A wildcard is matched literally by
/// bracketing it.</item>
/// </list>
/// <para>Documentation: https://support.microsoft.com/en-us/office/like-operator-b2f7ef03-9085-4ffb-9829-eef18358e931,
/// https://support.microsoft.com/en-us/office/access-wildcard-character-reference-af00c501-7972-40ee-8889-e18abaad12d1
/// and https://support.microsoft.com/en-us/office/use-wildcards-in-queries-and-parameters-in-access-ec057a45-78b1-4d16-8c20-242cde582e0b.</para>
/// </remarks>
internal static class LikeMatcher
{
    /// <summary>The two characters a character counts as (ß as ss, æ and Æ as ae), or null for itself.</summary>
    private static string? Expansion(char c) => c switch
    {
        'ß' => "ss",
        'æ' => "ae",
        'Æ' => "AE",
        _ => null,
    };

    public static bool IsMatch(string value, string pattern)
    {
        string text = Fold(value);
        int[] characterEnd = CharacterEnds(value, text.Length);

        // reach[p] is whether the pattern so far can consume exactly the first p characters of the text.
        var reach = new bool[text.Length + 1];
        reach[0] = true;
        int i = 0;
        while (i < pattern.Length)
        {
            int first = Array.IndexOf(reach, true);
            if (first < 0)
                return false;

            if (pattern[i] == '%')
            {
                Array.Fill(reach, true, first, reach.Length - first);
                i++;
                continue;
            }

            var next = new bool[reach.Length];
            if (pattern[i] == '_')
            {
                // One character of the value, however many it folds to (verified vs ACE: 'ß' LIKE '_' is True).
                for (int p = first; p < text.Length; p++)
                    next[characterEnd[p]] |= reach[p];
                i++;
            }
            else if (pattern[i] == '[')
            {
                int close = pattern.IndexOf(']', i + 1);
                if (close < 0)
                    return Invalid(first, text);
                if (close == i + 1)
                {
                    i += 2; // [] matches nothing
                    continue;
                }
                if (BracketList.Parse(pattern.AsSpan(i + 1, close - i - 1)) is not { } list)
                    return Invalid(first, text);
                for (int p = first; p < text.Length; p++)
                {
                    if (reach[p])
                        list.Match(text, p, next);
                }
                i = close + 1;
            }
            else
            {
                int end = pattern.IndexOfAny(['%', '_', '['], i);
                if (end < 0)
                    end = pattern.Length;
                string run = Fold(pattern[i..end]);
                for (int p = first; p + run.Length <= text.Length; p++)
                {
                    if (reach[p] && text.AsSpan(p).StartsWith(run, StringComparison.Ordinal))
                        next[p + run.Length] = true;
                }
                i = end;
            }
            reach = next;
        }
        return reach[text.Length];
    }

    /// <summary>For each position of the folded value, where the value's character it belongs to ends.</summary>
    private static int[] CharacterEnds(string value, int foldedLength)
    {
        var ends = new int[foldedLength];
        int p = 0;
        foreach (char c in value)
        {
            int end = p + (Expansion(c)?.Length ?? 1);
            for (; p < end; p++)
                ends[p] = end;
        }
        return ends;
    }

    private static bool Invalid(int first, string text) =>
        first < text.Length ? throw new ArgumentException("Invalid pattern string.") : false;

    /// <summary>Text as the match compares it: expansions spelt out, then upper case. Upper-casing keeps the length,
    /// so positions in the folded text line up with one another.</summary>
    private static string Fold(string text)
    {
        var folded = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (Expansion(c) is { } expansion)
                folded.Append(expansion);
            else
                folded.Append(c);
        }
        return folded.ToString().ToUpperInvariant();
    }

    private sealed class BracketList(bool negated, List<string> members, List<(char Low, char High)> ranges)
    {
        /// <summary>The list between the brackets, or null when a range is written backwards.</summary>
        public static BracketList? Parse(ReadOnlySpan<char> body)
        {
            bool negated = body.Length > 1 && body[0] == '!';
            if (negated)
                body = body[1..];

            var members = new List<string>();
            var ranges = new List<(char, char)>();
            for (int i = 0; i < body.Length; i++)
            {
                if (i + 2 < body.Length && body[i + 1] == '-')
                {
                    if (body[i] > body[i + 2])
                        return null;
                    ranges.Add((char.ToUpperInvariant(body[i]), char.ToUpperInvariant(body[i + 2])));
                    i += 2;
                }
                else
                {
                    members.Add(Fold(body[i].ToString()));
                }
            }
            return new BracketList(negated, members, ranges);
        }

        /// <summary>Marks in <paramref name="next"/> each position the list can advance <paramref name="p"/> to. A
        /// member that expands matches two characters, where anything else matches one.</summary>
        public void Match(string text, int p, bool[] next)
        {
            bool any = false;
            foreach (string member in members)
            {
                if (text.AsSpan(p).StartsWith(member, StringComparison.Ordinal))
                {
                    any = true;
                    next[p + member.Length] |= !negated;
                }
            }
            foreach ((char low, char high) in ranges)
            {
                if (text[p] >= low && text[p] <= high)
                {
                    any = true;
                    next[p + 1] |= !negated;
                }
            }
            if (negated && !any)
                next[p + 1] = true;
        }
    }
}