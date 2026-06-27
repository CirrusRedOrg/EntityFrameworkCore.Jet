using LibRed.Formats;

namespace LibRed.Crypto;

/// <summary>
/// Page-level encryption/decryption. Jet 3/4 use RC4 keyed per page from the
/// database key on page 0; ACE (ACCDB) uses an RC4/AES scheme depending on the
/// configured provider. Applied transparently by the IO layer on each page read.
/// </summary>
public static class JetCrypto
{
    /// <summary>Decrypts a page in place, if the database is encrypted. No-op otherwise.</summary>
    public static void DecryptPage(JetFormatBase format, int pageNumber, Span<byte> page)
    {
        // TODO: derive the per-page key and apply RC4 (Jet) or the ACE cipher.
        _ = format;
        _ = pageNumber;
        _ = page.Length;
    }
}
