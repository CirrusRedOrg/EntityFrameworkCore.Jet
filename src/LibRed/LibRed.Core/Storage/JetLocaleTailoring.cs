using LibRed.Catalog;

namespace LibRed.Storage;

/// <summary>
/// The weights one tailoring entry contributes: the primary byte(s), and the secondary (diacritic) weight of
/// the first of them. A language letter typically takes a <b>two-byte primary</b> — a value from a gap in the
/// General letter table plus a sub-position ordering the letters that share that gap — and the default
/// secondary, because it is a letter in its own right rather than an accented one. Some entries carry a real
/// secondary as well (Croatian <c>dž</c>, Danish <c>aa</c>), and an expansion instead contributes several
/// ordinary primaries (German Phone Book's <c>ä</c> = <c>a</c>+<c>e</c>).
/// </summary>
internal readonly record struct TailoredWeight(byte[] Primaries, byte Secondary);

/// <summary>
/// A locale's overrides on top of the General weights, keyed by the character <i>or character sequence</i>
/// they replace. A sort order other than General is General plus a small tailoring — no order measured
/// departs in more than 47 of 193 sampled characters (see
/// <c>docs/format/page-03-04-index-btree.md</c> §10.4).
/// </summary>
/// <param name="Entries">Uppercase keys, except where a locale disagrees with invariant casing.</param>
/// <param name="DoublesDigraphs">Whether a doubled digraph is written by doubling only its first letter, so
/// <c>ggy</c> weighs as <c>gy</c>+<c>gy</c> rather than <c>g</c>+<c>gy</c>. Hungarian alone does this;
/// Czech, Croatian, Spanish and Danish all take the plain greedy match (<c>cch</c> = <c>c</c>+<c>ch</c>).</param>
internal sealed class LocaleTailoring
{
    public LocaleTailoring(
        IReadOnlyDictionary<string, TailoredWeight> entries,
        bool doublesDigraphs = false,
        bool reverseDiacritics = false)
    {
        Entries = entries;
        DoublesDigraphs = doublesDigraphs;
        ReverseDiacritics = reverseDiacritics;
        MaxLength = entries.Count == 0 ? 0 : entries.Keys.Max(k => k.Length);
    }

    public IReadOnlyDictionary<string, TailoredWeight> Entries { get; }

    public bool DoublesDigraphs { get; }

    /// <summary>
    /// Whether the diacritic section is written from the END of the string backwards.
    /// </summary>
    /// <remarks>
    /// French sorts accents from the end of the word, so <c>coté</c> comes before <c>côte</c> where General
    /// puts them the other way round. [MS-UCODEREF] calls it <c>IsReverseDW</c> and states the whole rule:
    /// trailing diacritics are dropped from the LEFT rather than the right, and what remains is written right
    /// to left. Measured against ACE, byte for byte — <c>côté</c> is <c>[02 12 02 0E]</c>, trimmed to
    /// <c>[12 02 0E]</c> and stored as <c>0E 02 12</c>.
    /// <para>
    /// This is the whole of French: not one tailored letter, just a reversed section. It read as
    /// "unclassified" for a long time because a word with ONE accent encodes identically either way, and the
    /// sample set that measured every locale against General had no two-accent word in it.
    /// </para>
    /// </remarks>
    public bool ReverseDiacritics { get; }

    /// <summary>Longest key in <see cref="Entries"/>, bounding how far a match may look ahead. Derived in the
    /// constructor, so it cannot fall out of step with the entries it describes — as it would if this were a
    /// record whose <c>with</c> copy kept a stale value while the dictionary grew.</summary>
    public int MaxLength { get; }

    /// <summary>
    /// Matches the longest entry starting at <paramref name="start"/>, or the doubled-digraph form when the
    /// locale uses one. Lookup is by the <b>original</b> text before the uppercased text: storing the
    /// uppercase form is what folds case, and matching the original first is what lets a locale disagree
    /// with invariant casing — which Turkish does, where <c>I</c> is the dotless letter and <c>i</c> is not
    /// its lowercase.
    /// </summary>
    /// <param name="repeat">True when the match is a doubled digraph and must be emitted twice.</param>
    public bool TryMatch(
        ReadOnlySpan<char> text, int start, out TailoredWeight weight, out int consumed, out bool repeat)
    {
        // Doubling is tested first, so it does not depend on whether the locale also tailors the single
        // letter: "ggy" is g followed by the digraph gy, and weighs as that digraph twice.
        if (DoublesDigraphs && start + 2 < text.Length &&
            char.ToUpperInvariant(text[start]) == char.ToUpperInvariant(text[start + 1]) &&
            TryLongest(text, start + 1, out weight, out consumed) && consumed >= 2)
        {
            consumed += 1;
            repeat = true;
            return true;
        }

        repeat = false;
        return TryLongest(text, start, out weight, out consumed);
    }

    /// <summary>
    /// The entry for one character, without the contraction matcher.
    /// </summary>
    /// <remarks>
    /// For the components of an expansion, which take the locale's letters but must not re-enter the
    /// multi-character match: expanding a ligature could otherwise trip a digraph entry that the original
    /// text never contained.
    /// </remarks>
    public bool TryMatchSingle(char character, out TailoredWeight weight) =>
        Entries.TryGetValue(character.ToString(), out weight) ||
        Entries.TryGetValue(character.ToString().ToUpperInvariant(), out weight);

    private bool TryLongest(ReadOnlySpan<char> text, int start, out TailoredWeight weight, out int consumed)
    {
        for (int length = Math.Min(MaxLength, text.Length - start); length >= 1; length--)
        {
            ReadOnlySpan<char> candidate = text.Slice(start, length);
            if (Entries.TryGetValue(candidate.ToString(), out weight) ||
                Entries.TryGetValue(candidate.ToString().ToUpperInvariant(), out weight))
            {
                consumed = length;
                return true;
            }
        }
        weight = default;
        consumed = 0;
        return false;
    }
}

/// <summary>
/// The locale tailorings LibRed can encode. Only orders expressible with the primitives here are listed;
/// one needing <b>reordering</b> (Thai folds a leading vowel with the consonant it precedes in writing) is
/// still unsupported, as are Ukrainian and Macedonian — single-character tailorings, but of Cyrillic
/// characters the General v0 table does not cover at all.
/// </summary>
/// <remarks>
/// Every weight here was measured from ACE: an indexed text column built by ACE inside a database carrying
/// the order, with the stored index keys read back (<c>ContractionProbeTest</c>,
/// <c>LocaleFixtureCollationProbeTest</c>) and then asserted byte-for-byte against this encoder over the
/// whole of printable ASCII, Latin-1 and Latin Extended-A (<c>LocaleCollationAccessTests</c>).
/// </remarks>
internal static class JetLocaleTailoring
{
    private const byte DefaultSecondary = 0x02;

    /// <summary>The tailoring for a collation, or null when it has none — either because it is General
    /// itself, or because LibRed cannot express it. An <b>empty</b> tailoring is meaningful and not the same
    /// as null: it records that the order was measured to be indistinguishable from General.</summary>
    public static LocaleTailoring? For(Collation collation) => Tailorings.GetValueOrDefault(collation);

    private static readonly Dictionary<Collation, LocaleTailoring> Tailorings = new()
    {
        // --- Orders measured to be indistinguishable from General; recorded on disk, but no tailoring. ---
        [new Collation(CollatingOrder.Georgian, 0, SortId: 1)] = Table([]),
        [new Collation(CollatingOrder.Indic, Collation.GeneralVersion)] = Table([]),

        // --- Thai: the five leading vowels contract with the consonant they precede. Built as a rule. ---
        [new Collation(CollatingOrder.Thai, 0)] = Thai(),

        // --- Bosnian, Croatian and Serbian at version 1: the same order under three LCIDs. ---
        // Each measures 289 values identical to General v1 and the same 47 departures, byte for byte, so one
        // table serves all three. These are the FIRST version-1 tailorings: their primaries are two-byte
        // (SM, AW) pairs rather than v0's single byte, which is the only thing that made the v1 encoder look
        // as though it could not tailor at all.
        //
        // The letters land where the Croatian alphabet puts them — L 0E48, LJ 0E4A, M 0E51; D 0E1A, DŽ 0E1D,
        // Đ 0E1E — so the three digraphs are contractions, exactly as in the v0 orders.
        [new Collation(CollatingOrder.Croatian, Collation.GeneralVersion)] = BosnianCroatianSerbian(),
        [new Collation(CollatingOrder.Bosnian, Collation.GeneralVersion)] = BosnianCroatianSerbian(),
        [new Collation(CollatingOrder.Serbian, Collation.GeneralVersion)] = BosnianCroatianSerbian(),

        // --- French: not one tailored letter — General with the diacritic section REVERSED. ---
        // Accents are weighed from the end of the word, so coté sorts before côte where General has it the
        // other way round. [MS-UCODEREF] names this IsReverseDW; verified against ACE byte for byte.
        [new Collation(CollatingOrder.French, 0)] = Table([], reverseDiacritics: true),

        // --- Spanish Modern: General plus ñ as a letter of its own, between n (0x62) and o (0x64). ---
        [new Collation(CollatingOrder.SpanishModern, 0)] = Table([
            ("Ñ", [0x63, 0x04])]),

        // --- Spanish Traditional: Modern plus the two digraphs the 1994 reform dropped. ---
        [new Collation(CollatingOrder.Spanish, 0)] = Table([
            ("CH", [0x4E, 0x04]), ("LL", [0x5F, 0x04]), ("Ñ", [0x63, 0x04])]),

        // --- German Phone Book: the umlauts expand to the vowel plus e, exactly as ß expands to SS. ---
        [new Collation(CollatingOrder.German, 0, SortId: 1)] = Table([
            ("Ä", [0x4A, 0x51]),      // a + e
            ("Ö", [0x64, 0x51]),      // o + e
            ("Ü", [0x6F, 0x51])]),    // u + e

        // --- Polish: nine letters, each into the gap after its base. ---
        [new Collation(CollatingOrder.Polish, 0)] = Table([
            ("Ą", [0x4B, 0x03]), ("Ć", [0x4E, 0x02]), ("Ę", [0x52, 0x02]),
            ("Ł", [0x5F, 0x05]), ("Ń", [0x63, 0x03]), ("Ó", [0x65, 0x02]),
            ("Ś", [0x6C, 0x07]), ("Ź", [0x79, 0x03]), ("Ż", [0x79, 0x04])]),

        // --- Romanian Legacy: only the cedilla forms move; â and the comma-below forms keep General's. ---
        [new Collation(CollatingOrder.Romanian, 0)] = Table([
            ("Ă", [0x4B, 0x07]), ("Î", [0x5A, 0x03]),
            ("Ş", [0x6C, 0x08]), ("Ţ", [0x6E, 0x06])]),

        // --- Turkish: six letters, plus the dotted/dotless i, where the locale disagrees with invariant
        //     casing. Uppercase I is the DOTLESS letter and sorts before i; dotted İ folds onto plain i with
        //     no secondary at all, where General gives it 0x10. Both cases are listed explicitly so the
        //     original-text lookup wins before invariant uppercasing can conflate them. ---
        [new Collation(CollatingOrder.Turkish, 0)] = Table([
            ("Ç", [0x4E, 0x03]), ("Ğ", [0x56, 0x02]), ("Ö", [0x65, 0x02]),
            ("Ş", [0x6C, 0x07]), ("Ü", [0x70, 0x03]),
            ("ı", [0x58, 0x06]), ("I", [0x58, 0x06]),
            ("i", [0x59]), ("İ", [0x59]),
            // The IJ ligature expands, and follows the locale's casing: uppercase onto the dotless I,
            // lowercase onto the dotted i.
            ("Ĳ", [0x58, 0x06, 0x5B]), ("ĳ", [0x59, 0x5B])]),

        // --- Czech: ch is a letter between h and i, not after c; and the diaeresis is retuned from the
        //     General 0x13 to 0x05, which is a change to the accent rather than to any letter. ---
        [new Collation(CollatingOrder.Czech, 0)] = Table(
            [("CH", [0x58, 0x03]), ("Č", [0x4E, 0x03]), ("Ř", [0x6A, 0x04]), ("Š", [0x6C, 0x07]),
             ("Ž", [0x79, 0x05])],
            [("Ä", [0x4A], 0x05), ("Ë", [0x51], 0x05), ("Ï", [0x59], 0x05), ("Ö", [0x64], 0x05),
             ("Ü", [0x6F], 0x05), ("Ÿ", [0x76], 0x05), ("Ċ", [0x4D], 0x04), ("Ė", [0x51], 0x04),
             ("Ġ", [0x55], 0x04), ("İ", [0x59], 0x04), ("Ŀ", [0x5E], 0x04), ("Ż", [0x78], 0x04)]),

        // --- Slovak: Czech's ch and letters, with ä and ô of its own. ---
        [new Collation(CollatingOrder.Slovak, 0)] = Table(
            [("CH", [0x58, 0x03]), ("Ä", [0x4B, 0x02]), ("Ô", [0x65, 0x02]), ("Č", [0x4E, 0x03]),
             ("Ř", [0x6A, 0x04]), ("Š", [0x6C, 0x07]), ("Ž", [0x79, 0x05])],
            [("Ë", [0x51], 0x05), ("Ï", [0x59], 0x05), ("Ö", [0x64], 0x05), ("Ü", [0x6F], 0x05),
             ("Ċ", [0x4D], 0x04), ("Ė", [0x51], 0x04), ("Ġ", [0x55], 0x04), ("İ", [0x59], 0x04),
             ("Ŀ", [0x5E], 0x04), ("Ÿ", [0x76], 0x05), ("Ż", [0x78], 0x04)]),

        // --- Croatian Legacy: three digraphs, and dž carries a real secondary of its own. The ligature
        //     characters Ǆ/Ǉ/Ǌ ARE encodable here, unlike in General: Croatian makes each digraph a single
        //     letter, so the ligature is one weight rather than two. ---
        [new Collation(CollatingOrder.Croatian, 0)] = Table(
            [("LJ", [0x5F, 0x03]), ("NJ", [0x63, 0x04]), ("Ć", [0x4E, 0x03]), ("Č", [0x4E, 0x02]),
             ("Đ", [0x50, 0x05]), ("Š", [0x6C, 0x07]), ("Ž", [0x79, 0x05]),
             ("Ǉ", [0x5F, 0x03]), ("Ǌ", [0x63, 0x04])],
            [("DŽ", [0x50, 0x04], 0x04), ("Ǆ", [0x50, 0x04], 0x04),
             ("Ă", [0x4A], 0x05), ("Ď", [0x4F], 0x04), ("Ĕ", [0x51], 0x05), ("Ě", [0x51], 0x04),
             ("Ğ", [0x55], 0x05), ("Ĭ", [0x59], 0x05), ("Ľ", [0x5E], 0x04), ("Ň", [0x62], 0x04),
             ("Ŏ", [0x64], 0x05), ("Ř", [0x69], 0x04), ("Ť", [0x6D], 0x04), ("Ŭ", [0x6F], 0x05),
             // Latin Extended-B caron letters, which Croatian retunes away from General's 0x14.
             ("Ǎ", [0x4A], 0x04), ("Ǐ", [0x59], 0x04), ("Ǒ", [0x64], 0x04), ("Ǔ", [0x6F], 0x04),
             ("Ǧ", [0x55], 0x04), ("Ǩ", [0x5C], 0x04), ("Ǯ", [0x79, 0x02], 0x04), ("ǰ", [0x5B], 0x04)]),

        // --- Slovenian: the same letters as Croatian, at different sub-positions. ---
        [new Collation(CollatingOrder.Slovenian, 0)] = Table(
            [("Ć", [0x4E, 0x10]), ("Č", [0x4E, 0x0F]), ("Đ", [0x50, 0x07]), ("Ś", [0x6C, 0x08]),
             ("Š", [0x6C, 0x07]), ("Ź", [0x79, 0x05]), ("Ž", [0x79, 0x04])]),

        // --- Norwegian/Danish: æ ø å after z; "aa" weighs as å with a secondary marking the spelling, and
        //     ä/ö are æ/ø with an umlaut while ü rides on y. ---
        [new Collation(CollatingOrder.Norwegian, 0)] = Table(
            [("Æ", [0x79, 0x04]), ("Ø", [0x79, 0x06]), ("Å", [0x79, 0x09])],
            [("AA", [0x79, 0x09], 0x03),
             ("Ä", [0x79, 0x04], 0x13), ("Ö", [0x79, 0x06], 0x13), ("Ü", [0x76], 0x7B),
             ("Ő", [0x79, 0x06], 0x1B), ("Ű", [0x76], 0x1B),
             ("Ǣ", [0x79, 0x04], 0x03)]),   // Æ with a macron rides on the locale's own Æ

        // --- Swedish/Finnish: å ä ö after z, w is a variant of v, and ü rides on y. ---
        [new Collation(CollatingOrder.SwedishFinnish, 0)] = Table(
            [("Ä", [0x79, 0x07]), ("Å", [0x79, 0x05]), ("Ö", [0x79, 0x08])],
            [("W", [0x71], 0x03), ("Ŵ", [0x71], 0x12), ("Ø", [0x79, 0x08], 0x1E),
             ("Ü", [0x76], 0x7B), ("Ő", [0x79, 0x08], 0x1B), ("Ű", [0x76], 0x1B),
             // Wynn follows w onto v's primary. Keyed lowercase deliberately: its uppercase U+01F7 is
             // ignorable in General, so folding to it would lose the weight.
             ("ƿ", [0x71], 0x7B)]),

        // --- Icelandic: the accented vowels are letters, and þ æ ö close the alphabet after z. ---
        [new Collation(CollatingOrder.Icelandic, 0)] = Table(
            [("Á", [0x4B, 0x03]), ("Æ", [0x79, 0x04]), ("É", [0x52, 0x02]), ("Í", [0x5A, 0x02]),
             ("Ð", [0x50, 0x02]), ("Ó", [0x65, 0x02]), ("Ö", [0x79, 0x05]), ("Ú", [0x70, 0x02]),
             ("Ý", [0x77, 0x02]), ("Þ", [0x79, 0x03])],
            [("Ø", [0x79, 0x05], 0x1E), ("Ǣ", [0x79, 0x04], 0x16)]),

        // --- Estonian: the most radical of these. It rewrites the base alphabet rather than extending it —
        //     z moves between s and t, v moves down, and õ and ö take over the bare one-byte primaries
        //     General uses for v and w. ---
        [new Collation(CollatingOrder.Estonian, 0)] = Table(
            [("V", [0x70, 0x03]), ("Z", [0x6C, 0x07]), ("Ä", [0x72, 0x02]), ("Õ", [0x71]),
             ("Ö", [0x73]), ("Ü", [0x74, 0x02]), ("Š", [0x6C, 0x06]), ("Ž", [0x6C, 0x08])],
            [("W", [0x70, 0x03], 0x03), ("Ź", [0x6C, 0x07], 0x0E), ("Ż", [0x6C, 0x07], 0x10),
             // 0x6C is Estonian's š/z/ž slot, so the long s cannot live there as it does in General; it
             // falls back onto s with a secondary. Lowercase-only, so it is matched as itself.
             ("ſ", [0x6B], 0x03)]),

        // --- Latvian: seven letters, and the widest sub-positions seen (ķ at 0x12, ņ at 0x0C). ---
        [new Collation(CollatingOrder.Latvian, 0)] = Table(
            [("Č", [0x4E, 0x02]), ("Ģ", [0x56, 0x02]), ("Ķ", [0x5D, 0x12]), ("Ļ", [0x5F, 0x02]),
             ("Ņ", [0x63, 0x0C]), ("Š", [0x6C, 0x07]), ("Ž", [0x79, 0x03])]),

        // --- Lithuanian: y follows i rather than closing the alphabet, and the ogonek letters stay
        //     secondaries but at 0x0F instead of General's 0x1B. ---
        [new Collation(CollatingOrder.Lithuanian, 0)] = Table(
            // Fullwidth Ｙ follows the tailoring too — a locale can retailor a fullwidth form, and Estonian
            // proves the converse by leaving fullwidth Ｖ on General's weight, so these are per-locale facts.
            [("Y", [0x5A, 0x02]), ("Ｙ", [0x5A, 0x02])],
            [("Ą", [0x4A], 0x0F), ("Ę", [0x51], 0x0F), ("Į", [0x59], 0x0F), ("Ų", [0x6F], 0x0F)]),

        // --- Vietnamese: nine digraphs, and p and r shift to make room. Note "gh" and "ngh" are NOT letters
        //     — they fall out of greedy matching as g+h and ng+h, which is what ACE stores. ---
        [new Collation(CollatingOrder.Vietnamese, 0)] = Table(
            [("CH", [0x4E, 0x04]), ("GI", [0x56, 0x02]), ("KH", [0x5D, 0x02]), ("NG", [0x63, 0x02]),
             ("NH", [0x63, 0x03]), ("PH", [0x67, 0x03]), ("QU", [0x69]), ("TH", [0x6E, 0x02]),
             ("TR", [0x6E, 0x03]),
             ("P", [0x67, 0x02]), ("R", [0x6A, 0x02]), ("Â", [0x4B, 0x02]),
             ("Ê", [0x52, 0x02]), ("Ô", [0x65, 0x02]), ("Ă", [0x4B, 0x03]), ("Đ", [0x50, 0x02]),
             ("Ơ", [0x66]), ("Ư", [0x70, 0x02])]),   // the horned vowels, in Latin Extended-B

        // --- Ukrainian and Macedonian: Cyrillic orders, and the smallest tailorings of the lot. They were
        //     blocked until General v0 carried the Cyrillic block at all; now they are one and two entries.
        [new Collation(CollatingOrder.Ukrainian, 0)] = Table([
            ("Ь", [0x79, 0x5D])]),
        [new Collation(CollatingOrder.Macedonian, 0)] = Table([
            ("Ѓ", [0x79, 0x34]), ("Ќ", [0x79, 0x4C])]),

        // --- Hungarian: the full digraph set, the only order that doubles them, plus ö and ü as letters. ---
        [new Collation(CollatingOrder.Hungarian, 0)] = Table(
            [("CS", [0x4E, 0x05]), ("DZ", [0x50, 0x03]), ("DZS", [0x50, 0x05]), ("GY", [0x56, 0x03]),
             ("LY", [0x5F, 0x05]), ("NY", [0x63, 0x06]), ("SZ", [0x6C, 0x08]), ("TY", [0x6E, 0x06]),
             ("ZS", [0x79, 0x09]), ("Ö", [0x65, 0x02]), ("Ü", [0x70, 0x03])],
            [("Ő", [0x65, 0x02], 0x1B), ("Ű", [0x70, 0x03], 0x1B)],
            doublesDigraphs: true),

        // --- Hungarian Technical: NOT a digraph order at all, despite the name suggesting a variant of the
        //     one above. It tailors 46 individual letters — plain g becomes 0x56 03, so "gy" is that g
        //     followed by an ordinary y rather than a contraction. ---
        [new Collation(CollatingOrder.Hungarian, 0, SortId: 1)] = Table(
            [("F", [0x56, 0x02]), ("G", [0x56, 0x03]), ("P", [0x67, 0x04]), ("V", [0x73]),
             ("W", [0x74, 0x02]), ("Á", [0x4B, 0x02]), ("Â", [0x4B, 0x03]), ("Ä", [0x4B, 0x04]),
             ("Ç", [0x4E, 0x02]), ("É", [0x52, 0x02]), ("Ë", [0x53]), ("Í", [0x5A, 0x02]),
             ("Î", [0x5A, 0x03]), ("Ó", [0x65, 0x02]), ("Ô", [0x66]), ("Ö", [0x67, 0x02]),
             ("Ú", [0x70, 0x02]), ("Ü", [0x70, 0x03]), ("Ý", [0x77, 0x02]), ("ß", [0x6C, 0x05]),
             ("Ă", [0x4B, 0x05]), ("Ą", [0x4B, 0x06]), ("Ć", [0x4E, 0x03]), ("Č", [0x4E, 0x04]),
             ("Ď", [0x50, 0x02]), ("Đ", [0x50, 0x03]), ("Ę", [0x54, 0x02]), ("Ě", [0x55]),
             ("Ĺ", [0x5F, 0x02]), ("Ľ", [0x5F, 0x03]), ("Ł", [0x5F, 0x04]), ("Ń", [0x63, 0x02]),
             ("Ň", [0x63, 0x03]), ("Ő", [0x67, 0x03]), ("Ŕ", [0x6A, 0x02]), ("Ř", [0x6A, 0x03]),
             ("Ś", [0x6C, 0x02]), ("Ş", [0x6C, 0x03]), ("Š", [0x6C, 0x04]), ("Ţ", [0x6E, 0x02]),
             ("Ť", [0x6E, 0x03]), ("Ů", [0x71]), ("Ű", [0x72, 0x02]), ("Ź", [0x79, 0x02]),
             ("Ż", [0x79, 0x03]), ("Ž", [0x79, 0x04])]),
    };

    /// <summary>
    /// Thai — the five leading vowels contract with the consonant they precede.
    /// </summary>
    /// <remarks>
    /// Thai writes <c>เ แ โ ใ ไ</c> BEFORE the consonant they are pronounced after, and collation follows
    /// speech. This was long recorded as needing <i>reordering</i> — a device nothing else here uses, and the
    /// reason Thai stayed unimplemented. Measurement says otherwise: it is an ordinary CONTRACTION, the same
    /// device Croatian's <c>lj</c> uses.
    /// <para>
    /// ACE gives the pair a SINGLE weight at the consonant's own primary plus a vowel offset — <c>เก</c> is
    /// <c>7C99</c> where <c>ก</c> alone is <c>7C98</c>, <c>แก</c> is <c>7C9A</c>, and so on to <c>ไ</c> at
    /// +5. Every consonant sits on a six-wide block: itself, then a slot for each leading vowel. And it is
    /// genuinely a contraction rather than a swap, because the reverse order does not collide with it —
    /// <c>กเ</c> stays two weights, <c>7C98 7C93</c>.
    /// </para>
    /// <para>
    /// Built as the rule rather than as 220 transcribed entries, with each consonant's primary read from the
    /// measured v0 table, so there is no hand-copied hex to get wrong.
    /// </para>
    /// </remarks>
    private static LocaleTailoring Thai()
    {
        const char firstConsonant = 'ก', lastConsonant = 'ฮ', firstLeadingVowel = 'เ';
        var table = new Dictionary<string, TailoredWeight>(StringComparer.Ordinal);

        for (char consonant = firstConsonant; consonant <= lastConsonant; consonant++)
        {
            if (!JetTextCollationTableV0.TryGet(consonant, out TailoredWeight? weight) || weight is null) continue;
            for (int offset = 1; offset <= 5; offset++)
            {
                byte[] primaries = [.. weight.Value.Primaries];
                primaries[^1] = (byte)(primaries[^1] + offset);
                table[$"{(char)(firstLeadingVowel + offset - 1)}{consonant}"] =
                    new TailoredWeight(primaries, weight.Value.Secondary);
            }
        }

        return new LocaleTailoring(table);
    }

    /// <summary>
    /// Bosnian, Croatian and Serbian at sort-order version 1 — one table, three LCIDs.
    /// </summary>
    /// <remarks>
    /// Generated from ACE rather than transcribed (<c>V1TailoringProbeTest</c>): hand-copying hex is exactly
    /// the work that introduces a wrong byte nobody notices, because a wrong index key does not fail — it
    /// silently disagrees with ACE.
    /// <para>
    /// Seven letters of their own, three of them digraphs, and thirteen secondary retunes. The retuned
    /// letters are not in the alphabet at all: they are the caron and breve forms, which these orders weigh
    /// <c>04</c> and <c>05</c> where General gives <c>14</c> and <c>15</c>. The same shape as Czech's
    /// diaeresis retune in version 0.
    /// </para>
    /// <para>
    /// <b>U+016D is the exception, and it is ACE's, not a mistake here.</b> Every other letter weighs the
    /// same in both cases — including all three digraphs in all three of their forms, <c>DŽ</c> and
    /// <c>dž</c> and <c>Dž</c>. But <c>Ŭ</c> takes the retuned <c>05</c> while lowercase <c>ŭ</c> keeps
    /// General's <c>15</c>, identically in all three locales. It needs an entry of its own because matching
    /// tries the original text before the uppercased text, so without one the uppercase entry would claim it.
    /// </para>
    /// </remarks>
    private static LocaleTailoring BosnianCroatianSerbian() => Table(
        [("Ć", [0x0E, 0x0C]), ("Č", [0x0E, 0x0B]), ("Đ", [0x0E, 0x1E]),
         ("Š", [0x0E, 0x97]), ("Ž", [0x0E, 0xAD]),
         ("LJ", [0x0E, 0x4A]), ("NJ", [0x0E, 0x73])],
        [("DŽ", [0x0E, 0x1D], 0x04),
         // The caron and breve retunes. Generated from the whole guarded range, not a list of letters that
         // seemed likely: a point list produced these thirteen and missed the eight below it, which are just
         // as much a part of the rule.
         ("Ă", [0x0E, 0x02], 0x05), ("Ď", [0x0E, 0x1A], 0x04), ("Ĕ", [0x0E, 0x21], 0x05),
         ("Ě", [0x0E, 0x21], 0x04), ("Ğ", [0x0E, 0x25], 0x05), ("Ĭ", [0x0E, 0x32], 0x05),
         ("Ľ", [0x0E, 0x48], 0x04), ("Ň", [0x0E, 0x70], 0x04), ("Ŏ", [0x0E, 0x7C], 0x05),
         ("Ř", [0x0E, 0x8A], 0x04), ("Ť", [0x0E, 0x99], 0x04), ("Ŭ", [0x0E, 0x9F], 0x05),
         ("ŭ", [0x0E, 0x9F], 0x15),
         ("Ǎ", [0x0E, 0x02], 0x04), ("Ǐ", [0x0E, 0x32], 0x04), ("Ǒ", [0x0E, 0x7C], 0x04),
         ("Ǔ", [0x0E, 0x9F], 0x04), ("Ǧ", [0x0E, 0x25], 0x04), ("Ǩ", [0x0E, 0x36], 0x04),
         // Ezh with caron carries a primary of its own (0EAA), not the base letter's — the one retune here
         // that moves a character rather than only its accent.
         ("Ǯ", [0x0E, 0xAA], 0x04),
         // J with caron has no uppercase form, so it is keyed as itself.
         ("ǰ", [0x0E, 0x35], 0x04)]);

    /// <summary>Builds a tailoring. <paramref name="letters"/> take the default secondary — they are letters
    /// in their own right; <paramref name="accented"/> carry one of their own, which is how a locale retunes
    /// a diacritic (Czech moves the diaeresis from <c>0x13</c> to <c>0x05</c>) or marks a spelling (Danish
    /// <c>aa</c> is <c>å</c> with secondary <c>0x03</c>).</summary>
    private static LocaleTailoring Table(
        (string Text, byte[] Primaries)[] letters,
        (string Text, byte[] Primaries, byte Secondary)[]? accented = null,
        bool doublesDigraphs = false,
        bool reverseDiacritics = false)
    {
        var table = new Dictionary<string, TailoredWeight>(StringComparer.Ordinal);
        foreach ((string text, byte[] primaries) in letters)
            table[text] = new TailoredWeight(primaries, DefaultSecondary);
        foreach ((string text, byte[] primaries, byte secondary) in accented ?? [])
            table[text] = new TailoredWeight(primaries, secondary);
        return new LocaleTailoring(table, doublesDigraphs, reverseDiacritics);
    }
}
