namespace LibRed.Crypto;

/// <summary>
/// Decrypts database pages in place as they are read. Page 0 (the database definition page) is never
/// page-encrypted and must be a no-op. Implemented per encryption scheme (ACE Agile today; Jet 3/4 RC4
/// is a future addition).
/// </summary>
public interface IPageCodec
{
    /// <summary>Decrypts <paramref name="page"/> (a full page) in place. No-op for page 0.</summary>
    void DecryptPage(int pageNumber, Span<byte> page);
}
