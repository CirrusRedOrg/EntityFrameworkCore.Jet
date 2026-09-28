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
/// to a valid page type (page 1 → <c>0x0101</c> data, page 2/3 → <c>0x0102</c> TDEF, index pages → <c>0x0104</c>),
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
        Rc4Cipher.Apply(key, page);
    }

    // RC4 is a symmetric XOR keystream, so encryption is the identical operation.
    public void EncryptPage(int pageNumber, Span<byte> page) => DecryptPage(pageNumber, page);

}