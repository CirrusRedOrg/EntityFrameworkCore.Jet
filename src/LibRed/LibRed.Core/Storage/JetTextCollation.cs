namespace LibRed.Storage;

/// <summary>
/// Jet's "General" text collation: maps characters to the order-preserving primary weight bytes
/// used in index keys. Verified byte-for-byte against the ACE engine over printable ASCII.
/// Case is folded (lowercase weighs the same as uppercase), trailing spaces are ignored, and most
/// characters weigh one byte (a handful — <c>^ _ ` { | } ~</c> — weigh two, sharing the 0x2B page).
/// </summary>
/// <remarks>
/// Apostrophe and hyphen are "ignorable": they add no primary weight but append a trailing inline
/// group recording their position (verified against ACE). Non-ASCII characters are still reported
/// unencodable.
/// </remarks>
internal static class JetTextCollation
{
    private const byte EndPrimary = 0x01;
    private const byte EndKey = 0x00;
    private const byte InlineStart = 0x80;
    private const byte InlineMid = 0x06;
    private const byte ApostropheCode = 0x80;
    private const byte HyphenCode = 0x82;

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

        // Apostrophe/hyphen carry no primary weight; they record (position, code) for the inline
        // section, where position is the count of non-ignorable characters before them.
        var inline = new List<(int Position, byte Code)>();
        int primaryChars = 0;

        foreach (char c in s)
        {
            char u = char.ToUpperInvariant(c);
            if (u == '\'') { inline.Add((primaryChars, ApostropheCode)); continue; }
            if (u == '-') { inline.Add((primaryChars, HyphenCode)); continue; }

            if (u is >= 'A' and <= 'Z')
                output.Add(Letters[u - 'A']);
            else if (u is >= '0' and <= '9')
                output.Add((byte)(0x36 + 2 * (u - '0')));
            else if (Symbols.TryGetValue(u, out byte[]? weights))
                output.AddRange(weights);
            else
                return false; // non-ASCII — not handled yet

            primaryChars++;
        }

        output.Add(EndPrimary);
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
    }
}
