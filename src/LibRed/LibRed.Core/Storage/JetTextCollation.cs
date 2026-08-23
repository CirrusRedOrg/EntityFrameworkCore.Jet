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
    private const byte DefaultSecondary = 0x02; // a character with no accent

    // Secondary (diacritic) weight per Unicode combining mark — depends only on the accent, not the base
    // letter (verified against ACE: acute weighs 0x0E on a/e/i/o/u/y alike, etc.).
    private static readonly Dictionary<char, byte> DiacriticWeights = new()
    {
        ['́'] = 0x0E, // acute
        ['̀'] = 0x0F, // grave
        ['̂'] = 0x12, // circumflex
        ['̈'] = 0x13, // diaeresis / umlaut
        ['̃'] = 0x19, // tilde
        ['̊'] = 0x1A, // ring above
        ['̧'] = 0x1C, // cedilla
    };

    // Atomic accented letters that have no Unicode canonical decomposition: base letter + secondary weight.
    // Verified against ACE.
    private static readonly Dictionary<char, (char Base, byte Secondary)> AtomicAccents = new()
    {
        ['Ø'] = ('O', 0x21),
        ['Ð'] = ('D', 0x68),
        // Ordinal indicators: the base letter's primary with a distinguishing secondary, so they sort beside
        // 'a'/'o' rather than with the symbols. Harvested from ACE (7F 4A 01 03 00 / 7F 64 01 03 00).
        ['ª'] = ('A', 0x03),
        ['º'] = ('O', 0x03),
    };

    // Letters that sort as a multi-letter expansion (each expanded letter weighs its normal primary, no
    // accent). Verified against ACE: ß = SS, Þ/þ = TH, Æ = AE.
    private static readonly Dictionary<char, string> Expansions = new()
    {
        ['Æ'] = "AE",
        ['ß'] = "SS",
        ['Þ'] = "TH",
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
    /// Appends the order-preserving collation key body for <paramref name="value"/> (everything
    /// after the start flag: primary weights, end-of-primary marker, any ignorable-char inline
    /// codes, and the terminator). Trailing spaces are dropped. Returns false if any character is
    /// not yet supported.
    /// </summary>
    public static bool TryEncode(string value, List<byte> output)
    {
        ReadOnlySpan<char> s = value.AsSpan().TrimEnd(' ');

        // Build the primary weight bytes and a parallel secondary weight per byte (0x02 = no accent).
        var primaries = new List<byte>();
        var secondaries = new List<byte>();
        // Apostrophe/hyphen carry no primary weight; they record (position, code) for the inline
        // section, where position is the count of **primary weight bytes** emitted before them (so a
        // multi-byte expansion like ß→SS counts as 2 — verified against ACE).
        var inline = new List<(int Position, byte Code)>();

        foreach (char c in s)
        {
            char u = char.ToUpperInvariant(c);
            if (u == '\'') { inline.Add((primaries.Count, ApostropheCode)); continue; }
            if (u == '-') { inline.Add((primaries.Count, HyphenCode)); continue; }
            if (u == '­') { inline.Add((primaries.Count, SoftHyphenCode)); continue; }   // soft hyphen

            if (u is >= 'A' and <= 'Z')
                Add(Letters[u - 'A']);
            else if (u is >= '0' and <= '9')
                Add((byte)(0x36 + 2 * (u - '0')));
            // Look the symbol up by the original character as well as the uppercased one: uppercasing is for
            // letters, and it corrupts some symbols — char.ToUpperInvariant('µ') is GREEK CAPITAL LETTER MU,
            // which is not what ACE weighs it as (ACE gives it a symbol weight in the 0x34 group).
            else if (Symbols.TryGetValue(c, out byte[]? weights) || Symbols.TryGetValue(u, out weights))
                foreach (byte w in weights) Add(w);
            else if (!TryAddAccented(u, Add))
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

        void Add(byte primary, byte secondary = DefaultSecondary)
        {
            primaries.Add(primary);
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
