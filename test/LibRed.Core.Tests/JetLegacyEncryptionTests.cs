using LibRed.Crypto;
using Xunit;

namespace LibRed.Core.Tests;

public class JetLegacyEncryptionTests
{
    // The published RC4 test vector (key "Key", plaintext "Plaintext") — validates the reference RC4 below,
    // which we then use as an independent oracle for the codec.
    [Fact]
    public void Reference_rc4_matches_published_vector()
    {
        byte[] data = System.Text.Encoding.ASCII.GetBytes("Plaintext");
        Rc4Reference(System.Text.Encoding.ASCII.GetBytes("Key"), data);
        Assert.Equal("BBF316E8D940AF0AD3", Convert.ToHexString(data));
    }

    [Fact]
    public void Page0_is_never_decrypted()
    {
        var codec = new JetLegacyEncryption(unchecked((int)0xABBB315C));
        var page = RandomPage(0xDEADBEEF);
        var copy = (byte[])page.Clone();
        codec.DecryptPage(0, page);
        Assert.Equal(copy, page);
    }

    [Theory]
    [InlineData(unchecked((int)0xABBB315C), 1)]
    [InlineData(unchecked((int)0xABBB315C), 2)]
    [InlineData(0x0000_0001, 63)]
    [InlineData(0x1234_5678, 100)]
    public void DecryptPage_is_rc4_keyed_by_page_xor_dbkey(int dbKey, int pageNumber)
    {
        var page = RandomPage((uint)(dbKey ^ pageNumber));
        var expected = (byte[])page.Clone();
        // Independent oracle: RC4 with key = little-endian(pageNumber XOR dbKey).
        Rc4Reference(BitConverter.GetBytes(pageNumber ^ dbKey), expected);

        new JetLegacyEncryption(dbKey).DecryptPage(pageNumber, page);
        Assert.Equal(expected, page);
    }

    [Fact]
    public void DecryptPage_is_symmetric()
    {
        var codec = new JetLegacyEncryption(0x1234_5678);
        var page = RandomPage(42);
        var original = (byte[])page.Clone();
        codec.DecryptPage(7, page);
        Assert.NotEqual(original, page);   // it did something
        codec.DecryptPage(7, page);        // applying the XOR keystream twice restores the input
        Assert.Equal(original, page);
    }

    [Fact]
    public void TryCreate_returns_null_for_unencrypted() => Assert.Null(JetLegacyEncryption.TryCreate(0));

    private static byte[] RandomPage(uint seed)
    {
        var page = new byte[4096];
        for (int i = 0; i < page.Length; i++) { seed = seed * 1664525 + 1013904223; page[i] = (byte)(seed >> 24); }
        return page;
    }

    private static void Rc4Reference(byte[] key, byte[] data)
    {
        var s = new byte[256];
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
