namespace LibRed.Crypto;

/// <summary>
/// Encrypts/decrypts database pages in place. Page 0 (the database definition page) is never page-encrypted
/// and must be a no-op for both directions. Implemented per encryption scheme (legacy Jet 3/4 RC4, ACE Agile,
/// Office Standard/CryptoAPI). <see cref="EncryptPage"/> is the exact inverse of <see cref="DecryptPage"/>, so
/// for a stream cipher (RC4) the two are the same operation.
/// </summary>
public interface IPageCodec
{
    /// <summary>Decrypts <paramref name="page"/> (a full page) in place. No-op for page 0.</summary>
    void DecryptPage(int pageNumber, Span<byte> page);

    /// <summary>Encrypts <paramref name="page"/> (a full page) in place — the inverse of
    /// <see cref="DecryptPage"/>, used when writing a page back to disk. No-op for page 0.</summary>
    void EncryptPage(int pageNumber, Span<byte> page);
}
