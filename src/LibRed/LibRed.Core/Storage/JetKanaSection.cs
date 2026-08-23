namespace LibRed.Storage;

/// <summary>
/// The kana section of an index key, which both sort-order versions build identically.
/// </summary>
/// <remarks>
/// A kana weighs <c>7F &lt;sound&gt;</c> with voicing as an ordinary secondary, and the small/normal
/// distinction lives in a section of its own rather than in either weight. That is measured behaviour for
/// General Legacy (v0), and it holds byte-for-byte for General (v1) as well: ACE encodes <c>U+304C</c> as
/// <c>7F 7F0A 01 03 0101 FF 02 80 FF 80 00</c> under both, the same sound weights and the same section. The
/// two versions disagree about a great deal in the base table, and about kana not at all — so this is shared
/// rather than duplicated, and a fix to it necessarily reaches both.
/// </remarks>
internal static class JetKanaSection
{
    /// <summary>The page byte every kana primary starts with: a kana weighs <c>7F &lt;sound&gt;</c>.</summary>
    public const byte KanaPage = 0x7F;

    /// <summary>Closes the kana section, after the <c>FF</c> that introduces the prolonged-mark flags.
    /// Constant across hiragana, katakana, halfwidth, small and voiced forms in every string measured, so it
    /// is emitted literally; what it denotes is not established.</summary>
    private static ReadOnlySpan<byte> Tail => [0x02, 0x80, 0xFF, 0x80];

    /// <summary>
    /// Appends <c>01 01</c>, the packed small/normal flags, the prolonged-mark flags and the closing constant.
    /// Emitted whenever the string holds any kana at all, even if every one of them is a normal form.
    /// </summary>
    public static void Append(List<byte> output, List<bool> small, List<bool> prolonged)
    {
        output.Add(0x01);
        output.Add(0x01);
        AddFlags(output, small, marked: 0b10, unmarked: 0b11);
        output.Add(0xFF);
        AddFlags(output, prolonged, marked: 0b11, unmarked: 0b01);
        output.AddRange(Tail);
    }

    /// <summary>
    /// Packs one flag per kana, three to a byte, <b>most significant first</b>, under a <c>10</c> marker in
    /// the top two bits: <c>11</c> normal, <c>10</c> small, <c>00</c> padding. So one small kana is
    /// <c>A0</c>, "normal small" is <c>B8</c>, and four kana take two bytes, the second repeating the marker.
    /// Verified against ACE over all 30 combinations up to four kana.
    /// <para>
    /// Nothing is emitted at all when no flag is set, which is why a lone normal kana closes straight into
    /// the tail.
    /// </para>
    /// </summary>
    private static void AddFlags(List<byte> output, List<bool> flags, int marked, int unmarked)
    {
        int last = flags.LastIndexOf(true);
        for (int start = 0; start <= last; start += 3)
        {
            int packed = 0x80;
            for (int slot = 0; slot < 3; slot++)
            {
                int index = start + slot;
                int code = index > last ? 0b00 : flags[index] ? marked : unmarked;
                packed |= code << (4 - 2 * slot);
            }
            output.Add((byte)packed);
        }
    }
}
