namespace LibRed.Formats;

/// <summary>The flags word of an EncryptionInfo and of its EncryptionHeader
/// (<see cref="LibRed.Crypto.OfficeStandardEncryption.FlagsOffset"/>, <see cref="LibRed.Crypto.OfficeStandardEncryption.HeaderFlagsOffset"/>), as
/// MS-OFFCRYPTO §2.3.1 names its bits.</summary>
[Flags]
internal enum EncryptionInfoFlags : uint
{
    None = 0,
    CryptoApi = 0x04,
    DocProps = 0x08,
    External = 0x10,
    Aes = 0x20,

    /// <summary>The value an Agile EncryptionInfo carries in this word, which MS-OFFCRYPTO reserves.</summary>
    Agile = 0x40,
}