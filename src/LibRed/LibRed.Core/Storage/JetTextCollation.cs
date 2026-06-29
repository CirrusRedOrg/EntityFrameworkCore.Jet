namespace LibRed.Storage;

/// <summary>
/// Jet's "General" text collation: maps characters to the order-preserving primary weight bytes
/// used in index keys. Verified byte-for-byte against the ACE engine over printable ASCII.
/// Case is folded (lowercase weighs the same as uppercase), trailing spaces are ignored, and most
/// characters weigh one byte (a handful — <c>^ _ ` { | } ~</c> — weigh two, sharing the 0x2B page).
/// </summary>
/// <remarks>
/// Not handled yet: apostrophe and hyphen (Jet sorts them as "ignorable" with a different,
/// multi-byte placeholder form), and any non-ASCII character. Such keys are reported unencodable.
/// </remarks>
internal static class JetTextCollation
{
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
    /// Appends the order-preserving primary weights for <paramref name="value"/> (trailing spaces
    /// dropped) to <paramref name="output"/>. Returns false if any character is not yet supported.
    /// </summary>
    public static bool TryEncodePrimary(string value, List<byte> output)
    {
        ReadOnlySpan<char> s = value.AsSpan().TrimEnd(' ');
        foreach (char c in s)
        {
            char u = char.ToUpperInvariant(c);
            if (u is >= 'A' and <= 'Z')
                output.Add(Letters[u - 'A']);
            else if (u is >= '0' and <= '9')
                output.Add((byte)(0x36 + 2 * (u - '0')));
            else if (Symbols.TryGetValue(u, out byte[]? weights))
                output.AddRange(weights);
            else
                return false; // apostrophe, hyphen, non-ASCII — not handled yet
        }
        return true;
    }
}
