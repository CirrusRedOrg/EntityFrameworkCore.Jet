namespace LibRed.Storage;

/// <summary>
/// Jet's "General" text collation — specifically the **version-0 "General legacy"** order (locale
/// 1033, version 0) used by Access 2000–2007 and by any newer engine writing an ACE-2007-format
/// file. It maps characters to the order-preserving primary weight bytes used in index keys,
/// verified byte-for-byte against the ACE engine over printable ASCII. Case is folded (lowercase
/// weighs the same as uppercase), trailing spaces are ignored, and most characters weigh one byte
/// (a handful — <c>^ _ ` { | } ~</c> — weigh two, sharing the 0x2B page).
/// </summary>
/// <remarks>
/// Apostrophe and hyphen are "ignorable": they add no primary weight but append a trailing inline
/// group recording their position (verified against ACE). Accented Latin-1 letters sort with their
/// base letter's primary weight and record the accent in a secondary section (see below); a handful of
/// other characters are still reported unencodable.
/// <para>
/// The sort-order version is the column descriptor's <c>0x0D</c> field (spec §3.4). Access 2010+
/// introduced a different default "General" order (1033, version 1) with other key bytes; this class
/// does <b>not</b> implement it. A version-1 column/index therefore needs a separate weight table —
/// see the spec §10.4 note.
/// </para>
/// </remarks>
internal static class JetTextCollation
{
    private const byte EndPrimary = 0x01;
    private const byte EndKey = 0x00;
    private const byte InlineStart = 0x80;
    private const byte InlineMid = 0x06;
    private const byte ApostropheCode = 0x80;
    private const byte HyphenCode = 0x82;
    private const byte SoftHyphenCode = 0x83;

    /// <summary>
    /// The word-sort <b>ignorables</b> and their inline codes. These add no primary weight at all; each
    /// appends a <c>80 &lt;pos&gt; 06 &lt;code&gt;</c> record to the trailing section instead, which is what
    /// keeps <c>coop</c> and <c>co-op</c> together. Every dash, the Arabic harakat and the fullwidth
    /// apostrophe and hyphen are treated the same way — the fullwidth pair share their ASCII counterparts'
    /// codes exactly.
    /// </summary>
    /// <remarks>Written as code points rather than literals: several of these are invisible or are the very
    /// characters an editor normalises, and a wrong one here is a silently wrong key.</remarks>
    private static readonly Dictionary<char, byte> Ignorables = new()
    {
        [(char)0x0027] = ApostropheCode,   // '
        [(char)0xFF07] = ApostropheCode,   // fullwidth '
        [(char)0x002D] = HyphenCode,       // -
        [(char)0xFF0D] = HyphenCode,       // fullwidth -
        [(char)0x00AD] = SoftHyphenCode,   // soft hyphen
        [(char)0x2010] = 0x84,             // hyphen
        [(char)0x2011] = 0x85,             // non-breaking hyphen
        [(char)0x2027] = 0x86,             // hyphenation point
        [(char)0x2043] = 0x87,             // hyphen bullet
        [(char)0x2012] = 0x88,             // figure dash
        [(char)0x2013] = 0x89,             // en dash
        [(char)0x2014] = 0x8B,             // em dash
        [(char)0x2015] = 0x8C,             // horizontal bar
        [(char)0x064B] = 0xA0,             // Arabic fathatan
        [(char)0x064C] = 0xA1,             // dammatan
        [(char)0x064D] = 0xA2,             // kasratan
        [(char)0x064E] = 0xA3,             // fatha
        [(char)0x064F] = 0xA4,             // damma
        [(char)0x0650] = 0xA5,             // kasra
        [(char)0x0652] = 0xA6,             // sukun
    };
    private const byte DefaultSecondary = 0x02; // a character with no accent

    // Secondary (diacritic) weight per Unicode combining mark — depends only on the accent, not the base
    // letter (verified against ACE: acute weighs 0x0E on a/e/i/o/u/y alike, etc.).
    private static readonly Dictionary<char, byte> DiacriticWeights = new()
    {
        ['́'] = 0x0E, // acute
        ['̀'] = 0x0F, // grave
        ['̇'] = 0x10, // dot above
        ['̂'] = 0x12, // circumflex
        ['̈'] = 0x13, // diaeresis / umlaut
        ['̌'] = 0x14, // caron / háček
        ['̆'] = 0x15, // breve
        ['̄'] = 0x17, // macron
        ['̃'] = 0x19, // tilde
        ['̊'] = 0x1A, // ring above
        ['̨'] = 0x1B, // ogonek
        ['̧'] = 0x1C, // cedilla
        ['̋'] = 0x1D, // double acute
    };

    // Atomic accented letters that have no Unicode canonical decomposition: base letter + secondary weight.
    // Verified against ACE.
    private static readonly Dictionary<char, (char Base, byte Secondary)> AtomicAccents = new()
    {
        ['Ø'] = ('O', 0x21),
        ['Ð'] = ('D', 0x68),
        // A stroke through the letter is its own diacritic weight — 0x1E on D and H, 0x1F on L.
        ['Đ'] = ('D', 0x1E),
        ['Ħ'] = ('H', 0x1E),
        ['Ł'] = ('L', 0x1F),
        ['Ŀ'] = ('L', 0x11),   // L with middle dot
        ['ĸ'] = ('K', 0x03),   // kra; has no uppercase, so it is matched as itself
        ['ŉ'] = ('N', 0x48),   // n preceded by apostrophe; likewise has no uppercase
        // Ordinal indicators: the base letter's primary with a distinguishing secondary, so they sort beside
        // 'a'/'o' rather than with the symbols. Harvested from ACE (7F 4A 01 03 00 / 7F 64 01 03 00).
        ['ª'] = ('A', 0x03),
        ['º'] = ('O', 0x03),
    };

    // Letters that are NOT an A–Z fold: they carry a primary of their own. Looked up by the original
    // character, because invariant uppercasing would send them to the base letter and lose the distinction.
    private static readonly Dictionary<char, TailoredWeight> ExtraLetters = new()
    {
        // U+017F LATIN SMALL LETTER LONG S. ACE gives it its own two-byte primary in the S–T gap rather
        // than folding it onto 's' — verified against ACE in every v0 order, General included.
        ['ſ'] = new([0x6C, 0x06], DefaultSecondary),
        // U+0131 DOTLESS I: the letter i's primary with a secondary of its own. It has to be matched on the
        // original character, because invariant uppercasing turns it into a plain 'I'.
        ['ı'] = new([0x59], 0x03),
        // U+014A ENG, in the N–O gap.
        ['Ŋ'] = new([0x63, 0x05], DefaultSecondary),
        // U+0166 T WITH STROKE: a two-byte primary that also carries the stroke as a secondary.
        ['Ŧ'] = new([0x6E, 0x06], 0x1E),
        // U+00A0 NO-BREAK SPACE: a two-byte primary rather than the ordinary space's 0x07.
        [' '] = new([0x08, 0x02], DefaultSecondary),
    };

    // Letters that sort as a multi-letter expansion (each expanded letter weighs its normal primary, no
    // accent). Verified against ACE: ß = SS, Þ/þ = TH, Æ = AE.
    private static readonly Dictionary<char, string> Expansions = new()
    {
        ['Æ'] = "AE",
        ['ß'] = "SS",
        ['Þ'] = "TH",
        ['Ĳ'] = "IJ",
        ['Œ'] = "OE",
    };

    // Primary weight for 'A'..'Z' (general collation; mostly +2 with a few +1 steps).
    private static readonly byte[] Letters =
    [
        0x4A, 0x4C, 0x4D, 0x4F, 0x51, 0x53, 0x55, 0x57, 0x59, 0x5B, 0x5C, 0x5E, 0x60,
        0x62, 0x64, 0x66, 0x68, 0x69, 0x6B, 0x6D, 0x6F, 0x71, 0x73, 0x75, 0x76, 0x78,
    ];

    private static readonly Dictionary<char, byte[]> Symbols = new()
    {
        [' '] = [0x07],
        ['!'] = [0x09], ['"'] = [0x0A], ['#'] = [0x0C], ['$'] = [0x0E], ['%'] = [0x10],
        ['&'] = [0x12], ['('] = [0x14], [')'] = [0x16], ['*'] = [0x18], [','] = [0x1A],
        ['.'] = [0x1C], ['/'] = [0x1E], [':'] = [0x20], [';'] = [0x22], ['?'] = [0x24],
        ['@'] = [0x26], ['['] = [0x27], ['\\'] = [0x29], [']'] = [0x2A], ['+'] = [0x2C],
        ['<'] = [0x2E], ['='] = [0x30], ['>'] = [0x32],
        ['^'] = [0x2B, 0x02], ['_'] = [0x2B, 0x03], ['`'] = [0x2B, 0x07],
        ['{'] = [0x2B, 0x09], ['|'] = [0x2B, 0x0B], ['}'] = [0x2B, 0x0D], ['~'] = [0x2B, 0x0F],

        // Latin-1 punctuation and symbols, harvested from ACE's own index keys (see
        // SortKeyComparisonProbeTest). Each group mirrors the order of the corresponding Win32 NLS primaries
        // in ACE's compacted one-byte-per-group numbering:
        //   0x2B  continues the ^_`{|}~ group          NLS 0x0751..0x0757
        ['¡'] = [0x2B, 0x10], ['¦'] = [0x2B, 0x11], ['¨'] = [0x2B, 0x12], ['¯'] = [0x2B, 0x13],
        ['´'] = [0x2B, 0x14], ['¸'] = [0x2B, 0x15], ['¿'] = [0x2B, 0x16],
        //   0x33  mathematical                          NLS 0x0817..0x081D (both skip the same slots)
        ['±'] = [0x33, 0x04], ['«'] = [0x33, 0x05], ['»'] = [0x33, 0x07],
        ['×'] = [0x33, 0x09], ['÷'] = [0x33, 0x0A],
        //   0x34  currency then symbols — ACE runs two NLS groups (0x0797.. and 0x0A06..) into one
        ['¢'] = [0x34, 0xA6], ['£'] = [0x34, 0xA7], ['¤'] = [0x34, 0xA8], ['¥'] = [0x34, 0xA9],
        ['§'] = [0x34, 0xAA], ['©'] = [0x34, 0xAB], ['¬'] = [0x34, 0xAC], ['®'] = [0x34, 0xAD],
        ['°'] = [0x34, 0xAE], ['µ'] = [0x34, 0xAF], ['¶'] = [0x34, 0xB0], ['·'] = [0x34, 0xB1],
        //   0x37  fractions                             NLS 0x0D0D/0x0D11/0x0D15 (step 4 in both)
        ['¼'] = [0x37, 0x12], ['½'] = [0x37, 0x16], ['¾'] = [0x37, 0x1A],
        // Superscript digits take the *same* primary as their base digit and no distinguishing secondary, so
        // ACE sorts (and compares) '¹' equal to '1'. Verified: both encode to 7F 38 01 00.
        ['¹'] = [0x38], ['²'] = [0x3A], ['³'] = [0x3C],
    };

    /// <summary>
    /// Ligature characters, which ACE weighs as their decomposition rather than as anything of their own —
    /// <c>Ǆ</c> encodes exactly as the string <c>DŽ</c>, and <c>Ǣ</c> exactly as <c>ĀĒ</c>, so its macron
    /// lands on both letters and the key carries two secondary slots. Keyed by the UPPERCASE form, which is
    /// what case folding leaves; the title-case and lower-case forms encode identically.
    /// </summary>
    /// <remarks>Written as code points rather than literals: these are exactly the characters an editor or a
    /// tool is liable to normalise into something else, and a wrong one here is a silently wrong key.</remarks>
    private static readonly Dictionary<char, string> Ligatures = BuildLigatures();

    private static Dictionary<char, string> BuildLigatures()
    {
        var ligatures = new Dictionary<char, string>();
        void Add(int ligature, params int[] components)
        {
            string decomposition = new([.. components.Select(component => (char)component)]);
            // The upper/title/lower trio all fold to the same key, so all three map to the same components.
            for (int form = ligature; form < ligature + 3; form++) ligatures[(char)form] = decomposition;
        }

        Add(0x01C4, 0x0044, 0x017D);   // Ǆ ǅ ǆ  = D Ž
        Add(0x01C7, 0x004C, 0x004A);   // Ǉ ǈ ǉ  = L J
        Add(0x01CA, 0x004E, 0x004A);   // Ǌ ǋ ǌ  = N J
        Add(0x01F1, 0x0044, 0x005A);   // Ǳ ǲ ǳ  = D Z
        ligatures[(char)0x01E2] = new([(char)0x0100, (char)0x0112]);   // Ǣ = Ā Ē (macron on both)
        ligatures[(char)0x01FC] = new([(char)0x00C1, (char)0x00C9]);   // Ǽ = Á É (acute on both)
        return ligatures;
    }

    /// <summary>
    /// Appends the order-preserving collation key body for <paramref name="value"/> (everything
    /// after the start flag: primary weights, end-of-primary marker, any ignorable-char inline
    /// codes, and the terminator). Trailing spaces are dropped. Returns false if any character is
    /// not yet supported.
    /// </summary>
    /// <param name="tailoring">Per-character overrides for a locale order other than General; null for
    /// General itself. See <see cref="JetLocaleTailoring"/>.</param>
    public static bool TryEncode(string value, List<byte> output, LocaleTailoring? tailoring = null)
    {
        ReadOnlySpan<char> s = value.AsSpan().TrimEnd(' ');

        // Build the primary weight bytes and a parallel secondary weight per byte (0x02 = no accent).
        var primaries = new List<byte>();
        var secondaries = new List<byte>();
        // Apostrophe/hyphen carry no primary weight; they record (position, code) for the inline
        // section, where position is the count of **primary weight bytes** emitted before them (so a
        // multi-byte expansion like ß→SS counts as 2 — verified against ACE).
        var inline = new List<(int Position, byte Code)>();

        // Indexed rather than foreach, because a tailoring entry can consume several characters: a
        // contraction is a digraph weighing as one letter (Czech "ch", Hungarian "gy", Danish "aa").
        for (int position = 0; position < s.Length; position++)
        {
            char c = s[position];
            char u = char.ToUpperInvariant(c);
            if (Ignorables.TryGetValue(c, out byte code))
            {
                inline.Add((primaries.Count, code));
                continue;
            }

            // A locale tailoring overrides everything below it.
            if (tailoring is not null &&
                tailoring.TryMatch(s, position, out TailoredWeight tailored, out int consumed, out bool repeat))
            {
                for (int emit = repeat ? 2 : 1; emit > 0; emit--)
                    AddWeight(tailored.Primaries, tailored.Secondary);
                position += consumed - 1;
            }
            // A ligature character weighs as its decomposition, one component at a time — ACE stores Ǆ
            // exactly as it stores the string "DŽ", and Ǣ exactly as "ĀĒ" (A and E each carrying the macron,
            // hence two secondary slots). The components are weighed INDIVIDUALLY, never re-entering the
            // contraction matcher: expand Ǳ in a Hungarian database and its "dz" digraph would otherwise
            // fire, giving 50 03 where ACE stores 4F 78. And this sits below the tailoring, because some
            // locales do not decompose at all — Icelandic's Ǣ is its own Æ plus a secondary.
            else if (Ligatures.TryGetValue(u, out string? components))
            {
                foreach (char component in components)
                    if (!WeighCharacter(component)) return false;
            }
            else if (!WeighCharacter(c))
                return false; // not handled yet
        }

        output.AddRange(primaries);
        output.Add(EndPrimary);

        // Secondary (diacritic) section: only emitted when a character carries a non-default weight; it lists
        // the secondary weight of every byte from the first up to and including the last accented one.
        int lastAccent = secondaries.FindLastIndex(w => w != DefaultSecondary);
        for (int i = 0; i <= lastAccent; i++)
            output.Add(secondaries[i]);

        // Apostrophe/hyphen inline (tertiary) section.
        if (inline.Count > 0)
        {
            output.Add(0x01);
            output.Add(0x01);
            output.Add(0x01);
            foreach (var (position, code) in inline)
            {
                output.Add(InlineStart);
                output.Add((byte)(0x07 + 4 * position));
                output.Add(InlineMid);
                output.Add(code);
            }
        }
        output.Add(EndKey);
        return true;

        // One character's weights, with no contraction and no tailoring: the path a ligature's components
        // take, and the tail of the ordinary path once a tailoring has declined the character.
        bool WeighCharacter(char character)
        {
            char upper = char.ToUpperInvariant(character);
            // The locale still applies to a single character — only the contraction matcher is bypassed.
            // A ligature's components take the locale's letters: Slovenian's Ǆ is D plus SLOVENIAN's ž.
            if (tailoring is not null &&
                (tailoring.Entries.TryGetValue(character.ToString(), out TailoredWeight tailoredOne) ||
                 tailoring.Entries.TryGetValue(upper.ToString(), out tailoredOne)))
                AddWeight(tailoredOne.Primaries, tailoredOne.Secondary);
            else if (ExtraLetters.TryGetValue(character, out TailoredWeight own) ||
                     ExtraLetters.TryGetValue(upper, out own))
                AddWeight(own.Primaries, own.Secondary);
            else if (upper is >= 'A' and <= 'Z')
                Add(Letters[upper - 'A']);
            else if (upper is >= '0' and <= '9')
                Add((byte)(0x36 + 2 * (upper - '0')));
            // Look the symbol up by the original character as well as the uppercased one: uppercasing is for
            // letters, and it corrupts some symbols — char.ToUpperInvariant('µ') is GREEK CAPITAL LETTER MU,
            // which is not what ACE weighs it as (ACE gives it a symbol weight in the 0x34 group).
            else if (Symbols.TryGetValue(character, out byte[]? weights) || Symbols.TryGetValue(upper, out weights))
                AddWeight(weights, DefaultSecondary);
            // The measured block tables for Greek, Cyrillic, the Latin extensions, punctuation and the rest.
            // They cover no character the hand-verified Latin-1 / Latin Extended-A tables do, so they cannot
            // override anything already proven — but they DO take precedence over the decomposition below,
            // which is guesswork by comparison: a measured weight beats a derived one. A null weight means
            // ACE stores nothing at all for the character, not even a secondary slot.
            //
            // Locales share them. A locale CAN reweigh a character in these blocks, but measuring all 21
            // against General showed the departures are tiny — most add one or two entries across the whole
            // range, Croatian eleven — and every one of them is listed in its tailoring, which is consulted
            // first. `LocaleCollationAccessTests` asserts the whole range for every locale, so a missed
            // departure fails rather than writing a silently wrong key.
            else if (JetTextCollationBlocks.TryGet(character, out TailoredWeight? block))
            {
                if (block is { } weight) AddWeight(weight.Primaries, weight.Secondary);
            }
            else if (!TryAddAccented(upper, Add))
                return false;
            return true;
        }

        void Add(byte primary, byte secondary = DefaultSecondary)
        {
            primaries.Add(primary);
            secondaries.Add(secondary);
        }

        // A primary WEIGHT may be one or two bytes, and the secondary section has one entry per weight —
        // not per byte. Measured against ACE: Norwegian "ö" is 7F 79 06 01 13 00, two primary bytes and a
        // single secondary. (The inline apostrophe/hyphen section counts differently, by primary *bytes* —
        // hence `primaries.Count` there rather than `secondaries.Count`.)
        void AddWeight(ReadOnlySpan<byte> weight, byte secondary)
        {
            foreach (byte b in weight) primaries.Add(b);
            secondaries.Add(secondary);
        }
    }

    /// <summary>Emits the primary+secondary weight(s) for an accented or special Latin-1 letter (uppercased):
    /// a multi-letter expansion (ß=SS, Þ=TH, Æ=AE), an atomic accent (Ø, Ð), or a Unicode canonical
    /// decomposition (base letter + combining mark). Returns false if the character is unknown.</summary>
    private static bool TryAddAccented(char u, Action<byte, byte> add)
    {
        if (Expansions.TryGetValue(u, out string? expansion))
        {
            foreach (char letter in expansion) add(Letters[letter - 'A'], DefaultSecondary);
            return true;
        }
        if (AtomicAccents.TryGetValue(u, out (char Base, byte Secondary) atomic))
        {
            add(Letters[atomic.Base - 'A'], atomic.Secondary);
            return true;
        }

        // Canonical decomposition: a base A–Z letter followed by one combining diacritic we know.
        string nfd = u.ToString().Normalize(System.Text.NormalizationForm.FormD);
        if (nfd.Length == 2 && nfd[0] is >= 'A' and <= 'Z' && DiacriticWeights.TryGetValue(nfd[1], out byte weight))
        {
            add(Letters[nfd[0] - 'A'], weight);
            return true;
        }
        return false;
    }
}
