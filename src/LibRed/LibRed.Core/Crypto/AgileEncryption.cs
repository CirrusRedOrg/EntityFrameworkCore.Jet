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
/// <c>IV = Hash(keyDataSalt ‖ (LE32(N) ⊕ dbEncodingKey))</c>. The <c>⊕ dbEncodingKey</c> (the 4-byte key at
/// page-0 <c>0x3E</c>) is Access's deviation from stock Agile. Verified byte-for-byte against a real
/// password-protected file (decrypted pages match the unencrypted twin).
/// </remarks>
public sealed class AgileEncryption : IPageCodec
{
    private const int EncryptionInfoLengthOffset = 0x299;
    private const int EncryptionInfoOffset = 0x29B;
    private const int SupportedBlockSize = 16;
    private const int SupportedKeyBits = 256;
    private const int SupportedSpinCount = 100000;
    private static ReadOnlySpan<byte> AgilePrefix => [0x04, 0x00, 0x04, 0x00, 0x40, 0x00, 0x00, 0x00];

    // Block keys that salt the key-derivation for each purpose (MS-OFFCRYPTO §2.3.4.13/§2.3.4.14).
    private static readonly byte[] BlockVerifierHashInput = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static readonly byte[] BlockVerifierHashValue = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static readonly byte[] BlockKeyValue = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];

    private readonly byte[] _secretKey;    // the data-encryption key
    private readonly byte[] _keyDataSalt;
    private readonly int _blockSize;
    private readonly byte[] _encodingKey;  // 4-byte database key (page-0 0x3E)
    private readonly HashKind _hash;       // the descriptor's hashAlgorithm (also used per-page for the IV)

    private AgileEncryption(byte[] secretKey, byte[] keyDataSalt, int blockSize, byte[] encodingKey, HashKind hash)
    {
        _secretKey = secretKey;
        _keyDataSalt = keyDataSalt;
        _blockSize = blockSize;
        _encodingKey = encodingKey;
        _hash = hash;
    }

    /// <summary>
    /// Builds a codec for an encrypted database, or returns <c>null</c> if <paramref name="databaseKey"/> is 0
    /// (the file is not encrypted). Throws if the file is encrypted but no/incorrect password is supplied, or if
    /// the scheme is not the verified Agile (AES + SHA-512) configuration.
    /// </summary>
    public static AgileEncryption? TryCreate(ReadOnlySpan<byte> page0, int databaseKey, string? password)
    {
        if (databaseKey == 0)
            return null; // unencrypted

        // Detection keys off the actual Agile EncryptionInfo descriptor, not merely the nonzero key byte:
        // a legacy RC4 scheme (or a synthetic file with an incidental nonzero 0x3E) has no such descriptor
        // and is treated as unencrypted here rather than mis-flagged.
        XElement? enc = LocateEncryptionInfo(page0);
        if (enc is null)
            return null;
        if (password is null)
            throw new InvalidOperationException("This database is password-encrypted; a password is required to open it.");

        try
        {
            XElement keyData = Child(enc, "keyData");
            XElement encKey = enc.Descendants().First(e => e.Name.LocalName == "encryptedKey");

            RequireAes(keyData, encKey);
            HashKind hash = ParseHash((string)encKey.Attribute("hashAlgorithm")!);

            byte[] keyDataSalt = B64(keyData, "saltValue");
            int blockSize = IntAttribute(keyData, "blockSize");

            byte[] pwdSalt = B64(encKey, "saltValue");
            int spinCount = IntAttribute(encKey, "spinCount");
            int keyBits = IntAttribute(encKey, "keyBits");
            int keyBytes = keyBits / 8;

            ValidateSupportedProfile(keyData, encKey, hash, blockSize, keyBits, spinCount);
            RequireLength(keyDataSalt, 16, "keyData saltValue");
            RequireLength(pwdSalt, 16, "encryptedKey saltValue");

        // Cross-check the descriptor's declared sizes against reality: the salt bytes must be saltSize long, the
        // hash must match hashSize, and both elements must name the same hash. A disagreement means a malformed or
        // misparsed descriptor — fail here with a clear message rather than deep in the KDF.
            VerifyDeclaredSizes(keyData, hash, keyDataSalt.Length);
            VerifyDeclaredSizes(encKey, hash, pwdSalt.Length);

        // H_spin = Hash(salt ‖ UTF16LE(password)), then spinCount iterations of Hash(LE32(i) ‖ H).
            byte[] hspin = Hash(hash, pwdSalt, Encoding.Unicode.GetBytes(password));
            Span<byte> iter = stackalloc byte[4];
            for (int i = 0; i < spinCount; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(iter, i);
                hspin = Hash(hash, iter.ToArray(), hspin);
            }

            byte[] DeriveKey(byte[] blockKey) => Fit(Hash(hash, hspin, blockKey), keyBytes);

        // Verify the password before trusting anything: SHA(verifierInput) must equal verifierValue.
            byte[] encryptedVerifierInput = B64(encKey, "encryptedVerifierHashInput");
            byte[] encryptedVerifierValue = B64(encKey, "encryptedVerifierHashValue");
            byte[] encryptedKeyValue = B64(encKey, "encryptedKeyValue");
            RequireLength(encryptedVerifierInput, SupportedBlockSize, "encryptedVerifierHashInput");
            RequireLength(encryptedVerifierValue, HashSize(hash), "encryptedVerifierHashValue");
            RequireLength(encryptedKeyValue, keyBytes, "encryptedKeyValue");

            byte[] verifierInput = AesCbcDecrypt(DeriveKey(BlockVerifierHashInput), pwdSalt, encryptedVerifierInput);
            byte[] verifierValue = AesCbcDecrypt(DeriveKey(BlockVerifierHashValue), pwdSalt, encryptedVerifierValue);
            byte[] check = Hash(hash, verifierInput);
            if (!CryptographicOperations.FixedTimeEquals(verifierValue, check))
                throw new UnauthorizedAccessException("Incorrect database password.");

            // Recover the data-encryption key.
            byte[] secretKey = AesCbcDecrypt(DeriveKey(BlockKeyValue), pwdSalt, encryptedKeyValue);

            byte[] encodingKey = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(encodingKey, databaseKey);
            return new AgileEncryption(secretKey, keyDataSalt, blockSize, encodingKey, hash);
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
        const HashKind hash = HashKind.Sha512;
        const int keyBytes = 32, blockSize = 16, spinCount = 100000;
        byte[] keyDataSalt = RandomBytes(16), pwdSalt = RandomBytes(16), secretKey = RandomBytes(keyBytes);

        // H_spin = Hash(pwdSalt ‖ UTF16LE(password)), then spinCount folds of Hash(LE32(i) ‖ H).
        byte[] hspin = Hash(hash, pwdSalt, Encoding.Unicode.GetBytes(password));
        byte[] iter = new byte[4];
        for (int i = 0; i < spinCount; i++) { BinaryPrimitives.WriteInt32LittleEndian(iter, i); hspin = Hash(hash, iter, hspin); }
        byte[] DeriveKey(byte[] blockKey) => Fit(Hash(hash, hspin, blockKey), keyBytes);

        byte[] verifierInput = RandomBytes(16);
        byte[] encVerifierInput = AesCbcEncrypt(DeriveKey(BlockVerifierHashInput), pwdSalt, verifierInput);
        byte[] encVerifierValue = AesCbcEncrypt(DeriveKey(BlockVerifierHashValue), pwdSalt, Hash(hash, verifierInput));
        byte[] encKeyValue = AesCbcEncrypt(DeriveKey(BlockKeyValue), pwdSalt, secretKey);

        string b64K = Convert.ToBase64String(keyDataSalt), b64P = Convert.ToBase64String(pwdSalt);
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<encryption xmlns=\"http://schemas.microsoft.com/office/2006/encryption\" " +
            "xmlns:p=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\" " +
            "xmlns:c=\"http://schemas.microsoft.com/office/2006/keyEncryptor/certificate\">" +
            $"<keyData saltSize=\"16\" blockSize=\"16\" keyBits=\"256\" hashSize=\"64\" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA512\" saltValue=\"{b64K}\"/>" +
            "<keyEncryptors><keyEncryptor uri=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\">" +
            "<p:encryptedKey spinCount=\"100000\" saltSize=\"16\" blockSize=\"16\" keyBits=\"256\" hashSize=\"64\" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA512\" " +
            $"saltValue=\"{b64P}\" encryptedVerifierHashInput=\"{Convert.ToBase64String(encVerifierInput)}\" " +
            $"encryptedVerifierHashValue=\"{Convert.ToBase64String(encVerifierValue)}\" encryptedKeyValue=\"{Convert.ToBase64String(encKeyValue)}\"/>" +
            "</keyEncryptor></keyEncryptors></encryption>";

        // EncryptionInfo = version 4.4 + flags 0x40 + the UTF-8 XML (verified against a real file).
        byte[] descriptor = Concat([0x04, 0x00, 0x04, 0x00, 0x40, 0x00, 0x00, 0x00], Encoding.UTF8.GetBytes(xml));
        byte[] encodingKey = BitConverter.GetBytes(databaseKey);
        return (descriptor, new AgileEncryption(secretKey, keyDataSalt, blockSize, encodingKey, hash));
    }

    private static byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = iv.Length == 16 ? iv : Fit(iv, 16);
        return aes.EncryptCbc(data, aes.IV, PaddingMode.None);
    }

    private static byte[] RandomBytes(int n) { byte[] b = new byte[n]; RandomNumberGenerator.Fill(b); return b; }

    public void DecryptPage(int pageNumber, Span<byte> page) => Transform(pageNumber, page, decrypt: true);

    /// <summary>Encrypts a page — the inverse of <see cref="DecryptPage"/> (AES-CBC encrypt with the same
    /// per-page IV) — for writing back to an encrypted database.</summary>
    public void EncryptPage(int pageNumber, Span<byte> page) => Transform(pageNumber, page, decrypt: false);

    private void Transform(int pageNumber, Span<byte> page, bool decrypt)
    {
        if (pageNumber == 0)
            return; // header page is not encrypted

        // blockKey = LE32(pageNumber) XOR dbEncodingKey; IV = Hash(keyDataSalt ‖ blockKey), truncated to blockSize.
        Span<byte> blockKey = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(blockKey, pageNumber);
        for (int i = 0; i < 4; i++) blockKey[i] ^= _encodingKey[i];

        byte[] iv = Fit(Hash(_hash, _keyDataSalt, blockKey.ToArray()), _blockSize);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = _secretKey;
        aes.IV = iv;
        // Transform in place (length is a whole number of AES blocks — a page).
        byte[] result = decrypt ? aes.DecryptCbc(page, iv, PaddingMode.None) : aes.EncryptCbc(page, iv, PaddingMode.None);
        result.CopyTo(page);
    }

    // --- helpers ---

    private enum HashKind { Sha1, Sha256, Sha384, Sha512 }

    private static XElement? LocateEncryptionInfo(ReadOnlySpan<byte> page0)
    {
        if (page0.Length < EncryptionInfoOffset)
            return null;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(page0.Slice(EncryptionInfoLengthOffset, 2));
        if (length == 0)
            return null;
        if (length > page0.Length - EncryptionInfoOffset)
            throw new NotSupportedException("The Agile EncryptionInfo descriptor extends past page 0.");

        ReadOnlySpan<byte> descriptor = page0.Slice(EncryptionInfoOffset, length);
        if (descriptor.Length < AgilePrefix.Length || !descriptor[..AgilePrefix.Length].SequenceEqual(AgilePrefix))
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
        XElement keyData, XElement encKey, HashKind hash, int blockSize, int keyBits, int spinCount)
    {
        if (hash != HashKind.Sha512
            || ParseHash((string)keyData.Attribute("hashAlgorithm")!) != HashKind.Sha512)
            throw new NotSupportedException("Only the Access Agile SHA-512 profile is supported.");
        if (blockSize != SupportedBlockSize || IntAttribute(encKey, "blockSize") != SupportedBlockSize)
            throw new NotSupportedException("Only the Access Agile 16-byte AES block size is supported.");
        if (keyBits != SupportedKeyBits || IntAttribute(keyData, "keyBits") != SupportedKeyBits)
            throw new NotSupportedException("Only the Access Agile AES-256 profile is supported.");
        if (spinCount != SupportedSpinCount)
            throw new NotSupportedException("Only the Access Agile 100000-spin profile is supported.");
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

    private static int HashSize(HashKind kind) => kind switch
    {
        HashKind.Sha1 => 20,
        HashKind.Sha256 => 32,
        HashKind.Sha384 => 48,
        HashKind.Sha512 => 64,
        _ => throw new NotSupportedException(),
    };

    // Asserts the element's declared saltSize/hashSize/hashAlgorithm agree with the actual salt length and the
    // hash we're using. These attributes are redundant with the data, so a mismatch signals corruption/misparse.
    private static void VerifyDeclaredSizes(XElement el, HashKind hash, int actualSaltLength)
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

    private static HashKind ParseHash(string name) => name.Replace("-", "").ToUpperInvariant() switch
    {
        "SHA1" => HashKind.Sha1,
        "SHA256" => HashKind.Sha256,
        "SHA384" => HashKind.Sha384,
        "SHA512" => HashKind.Sha512,
        _ => throw new NotSupportedException($"Unsupported Agile hash algorithm '{name}' (SHA1/256/384/512 supported).")
    };

    private static byte[] Hash(HashKind kind, params byte[][] parts)
    {
        byte[] all = parts.Length == 1 ? parts[0] : Concat(parts);
        return kind switch
        {
            HashKind.Sha1 => SHA1.HashData(all),
            HashKind.Sha256 => SHA256.HashData(all),
            HashKind.Sha384 => SHA384.HashData(all),
            HashKind.Sha512 => SHA512.HashData(all),
            _ => throw new NotSupportedException()
        };
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        int o = 0;
        foreach (var p in parts) { p.CopyTo(result, o); o += p.Length; }
        return result;
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

    private static byte[] AesCbcDecrypt(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = iv.Length == 16 ? iv : Fit(iv, 16);
        return aes.DecryptCbc(data, aes.IV, PaddingMode.None);
    }
}
