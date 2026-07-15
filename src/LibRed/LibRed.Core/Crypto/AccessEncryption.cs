namespace LibRed.Crypto;

/// <summary>
/// The encryption scheme to apply when setting or changing an Access database password
/// (see <see cref="DatabaseEncryption"/>). Not every scheme is valid for every file format — Office
/// Standard/CryptoAPI and Agile apply to <c>.accdb</c> (ACE); legacy Jet applies to <c>.mdb</c> — and
/// <see cref="DatabaseEncryption.SetPassword"/> rejects mismatches.
/// </summary>
public enum AccessEncryption
{
    /// <summary>No encryption. Not valid for set/change — use <see cref="DatabaseEncryption.RemovePassword"/>.</summary>
    None = 0,

    /// <summary>Legacy Jet 3/4 page encoding (RC4 keyed by the <c>0x3E</c> database key). <c>.mdb</c> only.
    /// <b>Not yet implemented for set.</b></summary>
    LegacyJet,

    /// <summary>Office "Standard"/CryptoAPI RC4-40 (binary <c>EncryptionInfo</c> descriptor). <c>.accdb</c> only.</summary>
    OfficeStandardRc4,

    /// <summary>Office "Standard"/CryptoAPI AES-256 (binary <c>EncryptionInfo</c> descriptor). <c>.accdb</c> only.</summary>
    OfficeStandardAes,

    /// <summary>ACE Agile encryption (AES-256-CBC / SHA-512, XML descriptor). <c>.accdb</c> only.
    /// <b>Not yet implemented for set.</b></summary>
    Agile,
}
