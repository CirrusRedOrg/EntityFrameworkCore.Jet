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
    /// <b>Not a set-scheme here</b> — the legacy Jet database <i>password</i> (obfuscation only) is set via
    /// <see cref="DatabaseEncryption.SetJetPassword"/>; RC4 page encoding of a <c>.mdb</c> is not yet a create path.</summary>
    LegacyJet,

    /// <summary>Office "Standard"/CryptoAPI RC4-40 (binary <c>EncryptionInfo</c> descriptor). <c>.accdb</c> only.</summary>
    OfficeStandardRc4,

    /// <summary>Office "Standard"/CryptoAPI AES-256 (binary <c>EncryptionInfo</c> descriptor). <c>.accdb</c> only.</summary>
    OfficeStandardAes,

    /// <summary>ACE Agile encryption (AES-256-CBC / SHA-512, spinCount 100000, XML descriptor). <c>.accdb</c> only.</summary>
    Agile,
}

/// <summary>
/// The hashing algorithm for Office "Standard"/CryptoAPI RC4 encryption, selectable when creating a password with
/// <see cref="DatabaseEncryption.SetPasswordRc4"/>. Mirrors the CryptoAPI <c>AlgIDHash</c> values Access accepts.
/// MD2/MD4 are intentionally absent (no managed implementation).
/// </summary>
public enum StandardHash
{
    /// <summary>MD5 (<c>0x8003</c>). Base-provider compatible.</summary>
    Md5,
    /// <summary>SHA-1 (<c>0x8004</c>). Base-provider compatible; the Access 2007 default hash.</summary>
    Sha1,
    /// <summary>SHA-256 (<c>0x800c</c>). Requires the Enhanced provider / EncryptionEnhancer add-in.</summary>
    Sha256,
    /// <summary>SHA-384 (<c>0x800d</c>). Requires the Enhanced provider / EncryptionEnhancer add-in.</summary>
    Sha384,
    /// <summary>SHA-512 (<c>0x800e</c>). Requires the Enhanced provider / EncryptionEnhancer add-in.</summary>
    Sha512,
}
