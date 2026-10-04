using LibRed.Formats;
using LibRed.Pages;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LibRed.Crypto;

/// <summary>
/// Reads a password-encrypted ACCDB using Office <b>Agile Encryption</b> (MS-OFFCRYPTO §2.3.4) — the
/// scheme Access 2010+ applies on "Set Database Password". Page 0's header stays readable (only base-masked);
/// every subsequent page is AES-CBC encrypted with a per-page IV.
/// </summary>
/// <remarks>
/// The <c>EncryptionInfo</c> XML descriptor lives in page 0 (after the masked header). The data key is
/// recovered from the password + descriptor; each page N is then decrypted with
/// <c>IV = Hash(keyDataSalt ‖ LE32(N ⊕ databaseKey))</c>. The <c>⊕ databaseKey</c> (the 4-byte key at
/// page-0 <c>0x3E</c>) is Access's deviation from stock Agile. Verified byte-for-byte against a real
/// password-protected file (decrypted pages match the unencrypted twin).
/// </remarks>
public sealed class AgileEncryption : IPageCodec
{
    /// <summary>An Agile EncryptionInfo opens with version 4.4 and <see cref="EncryptionInfoFlags.Agile"/>, then the XML
    /// (verified against a real file).</summary>
    internal static ReadOnlySpan<byte> AgilePrefix => [0x04, 0x00, 0x04, 0x00, (byte)EncryptionInfoFlags.Agile, 0x00, 0x00, 0x00];


    // The one profile Access writes and this reads: AES-256-CBC (16-byte blocks), SHA-512, 16-byte salts and
    // 100000 spins. Create emits exactly these, and ValidateSupportedProfile refuses anything else.
    private const int SupportedBlockSize = 16;
    private const int SupportedKeyBits = 256;
    private const int SupportedSpinCount = 100000;
    private const int SupportedSaltSize = 16;

    // Block keys that salt the key-derivation for each purpose (MS-OFFCRYPTO §2.3.4.13/§2.3.4.14).
    private static readonly byte[] BlockVerifierHashInput = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static readonly byte[] BlockVerifierHashValue = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static readonly byte[] BlockKeyValue = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];

    private readonly byte[] _secretKey;    // the data-encryption key
    private readonly byte[] _keyDataSalt;
    private readonly int _blockSize;
    private readonly int _databaseKey;     // page-0 0x3E
    private readonly HashAlgorithmName _hash; // the descriptor's hashAlgorithm (also used per-page for the IV)

    private AgileEncryption(byte[] secretKey, byte[] keyDataSalt, int blockSize, int databaseKey, HashAlgorithmName hash)
    {
        _secretKey = secretKey;
        _keyDataSalt = keyDataSalt;
        _blockSize = blockSize;
        _databaseKey = databaseKey;
        _hash = hash;
    }

    /// <summary>
    /// Builds a codec for an encrypted database, or returns <c>null</c> if <paramref name="databaseKey"/> is 0
    /// (the file is not encrypted). Throws if the file is encrypted but no/incorrect password is supplied, or if
    /// the scheme is not the verified Agile (AES + SHA-512) configuration.
    /// </summary>
    public static AgileEncryption? TryCreate(ReadOnlySpan<byte> page0, int databaseKey, string? password,
        JetFormatBase format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (databaseKey == 0)
            return null; // unencrypted

        // Detection keys off the actual Agile EncryptionInfo descriptor, not merely the nonzero key byte:
        // a legacy RC4 scheme (or a synthetic file with an incidental nonzero 0x3E) has no such descriptor
        // and is treated as unencrypted here rather than mis-flagged.
        XElement? enc = LocateEncryptionInfo(page0, format);
        if (enc is null)
            return null;
        if (password is null)
            throw new InvalidOperationException("This database is password-encrypted; a password is required to open it.");

        try
        {
            XElement keyData = Child(enc, "keyData");
            XElement encKey = enc.Descendants().First(e => e.Name.LocalName == "encryptedKey");

            RequireAes(keyData, encKey);
            HashAlgorithmName hash = ParseHash((string)encKey.Attribute("hashAlgorithm")!);

            byte[] keyDataSalt = B64(keyData, "saltValue");
            int blockSize = IntAttribute(keyData, "blockSize");

            byte[] pwdSalt = B64(encKey, "saltValue");
            int spinCount = IntAttribute(encKey, "spinCount");
            int keyBits = IntAttribute(encKey, "keyBits");
            int keyBytes = keyBits / 8;

            ValidateSupportedProfile(keyData, encKey, hash, blockSize, keyBits, spinCount);
            RequireLength(keyDataSalt, SupportedSaltSize, "keyData saltValue");
            RequireLength(pwdSalt, SupportedSaltSize, "encryptedKey saltValue");

            // Cross-check the descriptor's declared sizes against reality: the salt bytes must be saltSize long, the
            // hash must match hashSize, and both elements must name the same hash. A disagreement means a malformed or
            // misparsed descriptor — fail here with a clear message rather than deep in the KDF.
            VerifyDeclaredSizes(keyData, hash, keyDataSalt.Length);
            VerifyDeclaredSizes(encKey, hash, pwdSalt.Length);

            byte[] hspin = SpinHash(hash, pwdSalt, password, spinCount);
            byte[] DeriveKey(byte[] blockKey) => Fit(CryptographicOperations.HashData(hash, [.. hspin, .. blockKey]), keyBytes);

            // Verify the password before trusting anything: SHA(verifierInput) must equal verifierValue.
            byte[] encryptedVerifierInput = B64(encKey, "encryptedVerifierHashInput");
            byte[] encryptedVerifierValue = B64(encKey, "encryptedVerifierHashValue");
            byte[] encryptedKeyValue = B64(encKey, "encryptedKeyValue");
            RequireLength(encryptedVerifierInput, SupportedBlockSize, "encryptedVerifierHashInput");
            RequireLength(encryptedVerifierValue, HashSize(hash), "encryptedVerifierHashValue");
            RequireLength(encryptedKeyValue, keyBytes, "encryptedKeyValue");

            byte[] verifierInput = AesCbc(DeriveKey(BlockVerifierHashInput), pwdSalt, encryptedVerifierInput, encrypt: false);
            byte[] verifierValue = AesCbc(DeriveKey(BlockVerifierHashValue), pwdSalt, encryptedVerifierValue, encrypt: false);
            byte[] check = CryptographicOperations.HashData(hash, verifierInput);
            if (!CryptographicOperations.FixedTimeEquals(verifierValue, check))
                throw new UnauthorizedAccessException("Incorrect database password.");

            // Recover the data-encryption key.
            byte[] secretKey = AesCbc(DeriveKey(BlockKeyValue), pwdSalt, encryptedKeyValue, encrypt: false);

            return new AgileEncryption(secretKey, keyDataSalt, blockSize, databaseKey, hash);
        }
        catch (NotSupportedException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException
            or OverflowException or CryptographicException or NullReferenceException or InvalidCastException)
        {
            throw new NotSupportedException("The Agile EncryptionInfo descriptor is malformed.", ex);
        }
    }

    /// <summary>Generates a fresh Agile <c>EncryptionInfo</c> (AES-256-CBC / SHA-512, spinCount 100000) for a new
    /// password: returns the descriptor blob (8-byte version prefix + the XML, to place at page-0 <c>0x29B</c>
    /// with its 2-byte length at <c>0x299</c>) and a codec that encrypts the data pages. This is the read path
    /// run forward — generate salts + a random data key, wrap it, and emit the descriptor Access writes.</summary>
    internal static (byte[] Descriptor, AgileEncryption Codec) Create(string password, int databaseKey)
    {
        HashAlgorithmName hash = HashAlgorithmName.SHA512;
        const int keyBytes = SupportedKeyBits / 8;
        byte[] keyDataSalt = RandomNumberGenerator.GetBytes(SupportedSaltSize);
        byte[] pwdSalt = RandomNumberGenerator.GetBytes(SupportedSaltSize);
        byte[] secretKey = RandomNumberGenerator.GetBytes(keyBytes);

        byte[] hspin = SpinHash(hash, pwdSalt, password, SupportedSpinCount);
        byte[] DeriveKey(byte[] blockKey) => Fit(CryptographicOperations.HashData(hash, [.. hspin, .. blockKey]), keyBytes);

        byte[] verifierInput = RandomNumberGenerator.GetBytes(SupportedBlockSize);
        byte[] encVerifierInput = AesCbc(DeriveKey(BlockVerifierHashInput), pwdSalt, verifierInput, encrypt: true);
        byte[] encVerifierValue = AesCbc(DeriveKey(BlockVerifierHashValue), pwdSalt,
            CryptographicOperations.HashData(hash, verifierInput), encrypt: true);
        byte[] encKeyValue = AesCbc(DeriveKey(BlockKeyValue), pwdSalt, secretKey, encrypt: true);

        string b64K = Convert.ToBase64String(keyDataSalt), b64P = Convert.ToBase64String(pwdSalt);
        string profile = $"saltSize=\"{SupportedSaltSize}\" blockSize=\"{SupportedBlockSize}\" keyBits=\"{SupportedKeyBits}\" " +
            $"hashSize=\"{HashSize(hash)}\" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA512\"";
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<encryption xmlns=\"http://schemas.microsoft.com/office/2006/encryption\" " +
            "xmlns:p=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\" " +
            "xmlns:c=\"http://schemas.microsoft.com/office/2006/keyEncryptor/certificate\">" +
            $"<keyData {profile} saltValue=\"{b64K}\"/>" +
            "<keyEncryptors><keyEncryptor uri=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\">" +
            $"<p:encryptedKey spinCount=\"{SupportedSpinCount}\" {profile} " +
            $"saltValue=\"{b64P}\" encryptedVerifierHashInput=\"{Convert.ToBase64String(encVerifierInput)}\" " +
            $"encryptedVerifierHashValue=\"{Convert.ToBase64String(encVerifierValue)}\" encryptedKeyValue=\"{Convert.ToBase64String(encKeyValue)}\"/>" +
            "</keyEncryptor></keyEncryptors></encryption>";

        byte[] descriptor = [.. AgilePrefix, .. Encoding.UTF8.GetBytes(xml)];
        return (descriptor, new AgileEncryption(secretKey, keyDataSalt, SupportedBlockSize, databaseKey, hash));
    }

    public void DecryptPage(int pageNumber, Span<byte> page) => Transform(pageNumber, page, decrypt: true);

    /// <summary>Encrypts a page — the inverse of <see cref="DecryptPage"/> (AES-CBC encrypt with the same
    /// per-page IV) — for writing back to an encrypted database.</summary>
    public void EncryptPage(int pageNumber, Span<byte> page) => Transform(pageNumber, page, decrypt: false);

    private void Transform(int pageNumber, Span<byte> page, bool decrypt)
    {
        if (pageNumber == 0)
            return; // header page is not encrypted

        // blockKey = LE32(pageNumber XOR databaseKey); IV = Hash(keyDataSalt ‖ blockKey), truncated to blockSize.
        byte[] blockKey = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(blockKey, pageNumber ^ _databaseKey);
        byte[] iv = Fit(CryptographicOperations.HashData(_hash, [.. _keyDataSalt, .. blockKey]), _blockSize);

        // Transform in place (length is a whole number of AES blocks — a page).
        AesCbc(_secretKey, iv, page, encrypt: !decrypt).CopyTo(page);
    }

    // --- helpers ---

    private static XElement? LocateEncryptionInfo(ReadOnlySpan<byte> page0, JetFormatBase format)
    {
        int descriptorOffset = format.EncryptionInfoOffset;
        if (page0.Length < descriptorOffset)
            return null;
        int length = DatabaseDefinitionPage.ReadEncryptionInfoLength(page0, format);
        if (length == 0)
            return null;
        if (length > page0.Length - descriptorOffset)
            throw new NotSupportedException("The Agile EncryptionInfo descriptor extends past page 0.");

        ReadOnlySpan<byte> descriptor = page0.Slice(descriptorOffset, length);
        ReadOnlySpan<byte> prefix = AgilePrefix;
        if (descriptor.Length < prefix.Length || !descriptor[..prefix.Length].SequenceEqual(prefix))
            return null; // a binary Office-Standard descriptor may occupy the same framed field

        try
        {
            return XDocument.Parse(Encoding.UTF8.GetString(descriptor[AgilePrefix.Length..])).Root;
        }
        catch (XmlException ex)
        {
            throw new NotSupportedException("The Agile EncryptionInfo XML descriptor is malformed.", ex);
        }
    }

    private static XElement Child(XElement parent, string localName) =>
        parent.Descendants().First(e => e.Name.LocalName == localName);

    private static byte[] B64(XElement el, string attr) => Convert.FromBase64String((string)el.Attribute(attr)!);

    private static int IntAttribute(XElement element, string name)
    {
        if (!int.TryParse((string?)element.Attribute(name), out int value))
            throw new NotSupportedException($"The Agile descriptor has an invalid {name} value.");
        return value;
    }

    private static void ValidateSupportedProfile(
        XElement keyData, XElement encKey, HashAlgorithmName hash, int blockSize, int keyBits, int spinCount)
    {
        if (hash != HashAlgorithmName.SHA512
            || ParseHash((string)keyData.Attribute("hashAlgorithm")!) != HashAlgorithmName.SHA512)
            throw new NotSupportedException("Only the Access Agile SHA-512 profile is supported.");
        if (blockSize != SupportedBlockSize || IntAttribute(encKey, "blockSize") != SupportedBlockSize)
            throw new NotSupportedException($"Only the Access Agile {SupportedBlockSize}-byte AES block size is supported.");
        if (keyBits != SupportedKeyBits || IntAttribute(keyData, "keyBits") != SupportedKeyBits)
            throw new NotSupportedException($"Only the Access Agile AES-{SupportedKeyBits} profile is supported.");
        if (spinCount != SupportedSpinCount)
            throw new NotSupportedException($"Only the Access Agile {SupportedSpinCount}-spin profile is supported.");
    }

    private static void RequireLength(byte[] value, int expected, string name)
    {
        if (value.Length != expected)
            throw new NotSupportedException(
                $"The Agile descriptor {name} is {value.Length} bytes; expected {expected}.");
    }

    private static void RequireAes(XElement keyData, XElement encKey)
    {
        foreach (var el in new[] { keyData, encKey })
        {
            if ((string?)el.Attribute("cipherAlgorithm") != "AES" ||
                (string?)el.Attribute("cipherChaining") != "ChainingModeCBC")
                throw new NotSupportedException("Only AES-CBC Agile encryption is supported.");
        }
    }

    private static int HashSize(HashAlgorithmName hash) => hash.Name switch
    {
        "SHA1" => SHA1.HashSizeInBytes,
        "SHA256" => SHA256.HashSizeInBytes,
        "SHA384" => SHA384.HashSizeInBytes,
        "SHA512" => SHA512.HashSizeInBytes,
        _ => throw new NotSupportedException(),
    };

    // Asserts the element's declared saltSize/hashSize/hashAlgorithm agree with the actual salt length and the
    // hash we're using. These attributes are redundant with the data, so a mismatch signals corruption/misparse.
    private static void VerifyDeclaredSizes(XElement el, HashAlgorithmName hash, int actualSaltLength)
    {
        int saltSize = (int)el.Attribute("saltSize")!;
        if (saltSize != actualSaltLength)
            throw new NotSupportedException($"Agile descriptor saltSize ({saltSize}) disagrees with the salt value length ({actualSaltLength}).");

        int hashSize = (int)el.Attribute("hashSize")!;
        if (hashSize != HashSize(hash))
            throw new NotSupportedException($"Agile descriptor hashSize ({hashSize}) disagrees with {hash} ({HashSize(hash)}).");

        if (ParseHash((string)el.Attribute("hashAlgorithm")!) != hash)
            throw new NotSupportedException("Agile descriptor hashAlgorithm differs between keyData and encryptedKey.");
    }

    // The descriptor names its own hash, and SHA-1 is what Access writes for many files; reading them back means
    // honouring it.
    private static HashAlgorithmName ParseHash(string name) => name.Replace("-", "").ToUpperInvariant() switch
    {
        "SHA1" => HashAlgorithmName.SHA1,
        "SHA256" => HashAlgorithmName.SHA256,
        "SHA384" => HashAlgorithmName.SHA384,
        "SHA512" => HashAlgorithmName.SHA512,
        _ => throw new NotSupportedException($"Unsupported Agile hash algorithm '{name}' (SHA1/256/384/512 supported).")
    };

    /// <summary><c>H_spin</c>: <c>Hash(salt ‖ UTF16LE(password))</c>, then <paramref name="spinCount"/>
    /// iterations of <c>Hash(LE32(i) ‖ H)</c>. Opening a file and creating one both start here.</summary>
    private static byte[] SpinHash(HashAlgorithmName hash, byte[] salt, string password, int spinCount)
    {
        byte[] hspin = CryptographicOperations.HashData(hash, [.. salt, .. Encoding.Unicode.GetBytes(password)]);
        byte[] iter = new byte[sizeof(int)];
        for (int i = 0; i < spinCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(iter, i);
            hspin = CryptographicOperations.HashData(hash, [.. iter, .. hspin]);
        }
        return hspin;
    }

    /// <summary>Truncates a hash to <paramref name="length"/>, or pads with <c>0x36</c> if it is shorter.</summary>
    private static byte[] Fit(byte[] hash, int length)
    {
        if (hash.Length == length) return hash;
        var key = new byte[length];
        Array.Fill(key, (byte)0x36);
        Array.Copy(hash, key, Math.Min(hash.Length, length));
        return key;
    }

    /// <summary>AES-CBC with no padding — over the key-encryptor fields and every data page; the IV (a salt, or
    /// the page's hash) is fitted to the block.</summary>
    private static byte[] AesCbc(byte[] key, byte[] iv, ReadOnlySpan<byte> data, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        byte[] fitted = iv.Length == SupportedBlockSize ? iv : Fit(iv, SupportedBlockSize);
        return encrypt
            ? aes.EncryptCbc(data, fitted, PaddingMode.None)
            : aes.DecryptCbc(data, fitted, PaddingMode.None);
    }
}