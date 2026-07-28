using System.Buffers.Binary;

namespace LibRed.Crypto;

/// <summary>
/// The legacy Jet 3/4 engine-level page encryption (pre-ACE). Every page except page 0 is RC4-encrypted with a
/// per-page key of <c>LE32(pageNumber XOR databaseKey)</c>, where <c>databaseKey</c> is the 4-byte value at
/// page-0 <c>0x3E</c>. Unlike ACE Agile encryption there is no password or key-derivation step — the database
/// key in the header is the whole secret. This is what protects the account/password data in a workgroup file
/// (<c>System.mdw</c>, which always carries a nonzero database key) and is also used by password-protected
/// <c>.mdb</c> files.
/// </summary>
/// <remarks>
/// Verified against a real <c>System.mdw</c> (databaseKey <c>0xABBB315C</c>): with this key every page decrypts
/// to a valid page-type byte (page 1 → <c>0x01</c> data, page 2/3 → <c>0x02</c> TDEF, index pages → <c>0x04</c>),
/// and the XOR (not ADD) page-number mixing is the one that yields valid types on pages where the two differ.
/// Same per-page key derivation as <see cref="AgileEncryption"/> (<c>LE32(pageNumber) XOR encodingKey</c>), just
/// feeding RC4 directly rather than an AES IV.
/// </remarks>
public sealed class JetLegacyEncryption : IPageCodec
{
    private readonly int _databaseKey;

    public JetLegacyEncryption(int databaseKey) => _databaseKey = databaseKey;

    /// <summary>Returns a codec when <paramref name="databaseKey"/> is nonzero (an encrypted Jet 3/4 file), else null.</summary>
    public static JetLegacyEncryption? TryCreate(int databaseKey) => databaseKey == 0 ? null : new JetLegacyEncryption(databaseKey);

    public void DecryptPage(int pageNumber, Span<byte> page)
    {
        if (pageNumber == 0)
            return; // header page is never encrypted

        Span<byte> key = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(key, pageNumber ^ _databaseKey);
        Rc4(key, page);
    }

    // RC4 is a symmetric XOR keystream, so encryption is the identical operation.
    public void EncryptPage(int pageNumber, Span<byte> page) => DecryptPage(pageNumber, page);

    /// <summary>Standard RC4: KSA then PRGA, XOR'ing the keystream over <paramref name="data"/> in place.</summary>
    private static void Rc4(ReadOnlySpan<byte> key, Span<byte> data)
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
