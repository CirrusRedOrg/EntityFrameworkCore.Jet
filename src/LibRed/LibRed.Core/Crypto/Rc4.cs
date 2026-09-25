namespace LibRed.Crypto;

/// <summary>
/// RC4, shared by the two schemes that use it: legacy Jet's own page encoding
/// (<see cref="JetLegacyEncryption"/>) and the Office-Standard RC4 variant
/// (<see cref="OfficeStandardEncryption"/>). One cipher, one implementation — they had a copy each.
/// </summary>
internal static class Rc4Cipher
{
    /// <summary>Standard RC4: KSA then PRGA, XOR'ing the keystream over <paramref name="data"/> in place.
    /// Symmetric, so encrypting and decrypting are the same call.</summary>
    public static void Apply(ReadOnlySpan<byte> key, Span<byte> data)
    {
        Span<byte> s = stackalloc byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }
        for (int n = 0, a = 0, b = 0; n < data.Length; n++)
        {
            a = (a + 1) & 0xFF;
            b = (b + s[a]) & 0xFF;
            (s[a], s[b]) = (s[b], s[a]);
            data[n] ^= s[(s[a] + s[b]) & 0xFF];
        }
    }
}
