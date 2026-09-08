namespace LibRed.Storage;

/// <summary>
/// The two bytes ACE puts at the end of an index entry too long to store whole.
/// </summary>
/// <remarks>
/// An entry of at most 510 bytes is stored as built. Past that ACE keeps the first 508 bytes and replaces the
/// rest with this value, computed over the bytes it dropped — which is why two long values that share a
/// 508-byte prefix still sort apart instead of colliding.
/// <para>
/// Recovered by measurement, not documentation. Three tails differing in one byte showed the function is
/// affine over GF(2) (<c>L(0xA3) ^ L(0x13) = L(0xB0)</c> exactly), and it proved shift-invariant across 173
/// observations, so a byte at distance d from the end contributes <c>S^(d-1)</c> of itself. Sweeping all
/// 65,536 polynomials in the usual framings found nothing, because the usual framing is wrong: the standard
/// reflected update is <c>crc = (crc >> 8) ^ T[(crc ^ b) &amp; 0xFF]</c>, passing the byte THROUGH the table,
/// while ACE computes <c>crc = (crc >> 8) ^ T[crc &amp; 0xFF] ^ b</c> and injects it raw. The step operator
/// was then solved directly by Gaussian elimination over the measured contributions, and predicts all 657 of
/// them. There is no initial value and no final XOR.
/// </para>
/// <para>
/// <b>Not verified where the dropped bytes contain a word-sort record.</b> Those cannot be checked even in
/// principle: the record sits in the part ACE discarded, so what it contained is unobservable, and if ACE
/// recomputes its position when truncating then the input differs from anything reconstructable here. The
/// caller refuses those rather than guess — see <see cref="IndexKeyEncoder"/>.
/// </para>
/// </remarks>
internal static class JetIndexKeyChecksum
{
    /// <summary>Where the kept bytes end and the checksum begins.</summary>
    public const int KeptBytes = 508;

    /// <summary>
    /// The step's action on each bit. The upper eight are a plain right shift by eight, which makes the
    /// operator the familiar <c>(x >> 8) ^ T(x &amp; 0xFF)</c> of a table-driven CRC; the lower eight are the
    /// table itself, measured from ACE.
    /// </summary>
    private static ReadOnlySpan<ushort> StepBits =>
    [
        0x0580, 0x0F80, 0x1B80, 0x3380, 0x6380, 0xC380, 0x8381, 0x0383,
        0x0001, 0x0002, 0x0004, 0x0008, 0x0010, 0x0020, 0x0040, 0x0080,
    ];

    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (int value = 0; value < 256; value++)
        {
            ushort result = 0;
            for (int bit = 0; bit < 8; bit++) if ((value & (1 << bit)) != 0) result ^= StepBits[bit];
            table[value] = result;
        }
        return table;
    }

    /// <summary>
    /// The checksum over the bytes ACE dropped — everything from <see cref="KeptBytes"/> on.
    /// </summary>
    /// <remarks>
    /// The last byte does not go through a full step. Every byte before it is folded in the usual way, and
    /// then the final one is XORed into the <b>high</b> half — it never gets its own shift or table lookup.
    /// <para>This was first written as "the terminator is excluded", which is the same thing whenever that
    /// byte is <c>0x00</c>: XOR-ing zero changes nothing. A key ending in text always ends in its <c>0x00</c>
    /// terminator, and every measurement behind the original rule used one, so the two readings could not be
    /// told apart. They diverge the moment the key's last column is numeric — <c>(TEXT, TEXT, LONG)</c> put
    /// a data byte there and the keys parted company from ACE's, silently. Re-measured over LONG, CURRENCY
    /// and DOUBLE tails across 24 keys: this form matches ACE on every one, and still matches on the all-text
    /// keys the old form was derived from. See <c>docs/design/index-key-checksum.md</c>.</para>
    /// </remarks>
    public static ushort Compute(ReadOnlySpan<byte> discarded)
    {
        ushort crc = 0;
        foreach (byte b in discarded[..^1]) crc = (ushort)((crc >> 8) ^ Table[crc & 0xFF] ^ b);
        return (ushort)(crc ^ (discarded[^1] << 8));
    }
}
