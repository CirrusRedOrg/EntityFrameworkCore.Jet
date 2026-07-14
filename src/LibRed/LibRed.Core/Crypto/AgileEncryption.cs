using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
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
    // Block keys that salt the key-derivation for each purpose (MS-OFFCRYPTO §2.3.4.13/§2.3.4.14).
    private static readonly byte[] BlockVerifierHashInput = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static readonly byte[] BlockVerifierHashValue = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static readonly byte[] BlockKeyValue = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];

    private readonly byte[] _secretKey;    // the data-encryption key
    private readonly byte[] _keyDataSalt;
    private readonly int _blockSize;
    private readonly byte[] _encodingKey;  // 4-byte database key (page-0 0x3E)

    private AgileEncryption(byte[] secretKey, byte[] keyDataSalt, int blockSize, byte[] encodingKey)
    {
        _secretKey = secretKey;
        _keyDataSalt = keyDataSalt;
        _blockSize = blockSize;
        _encodingKey = encodingKey;
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

        XElement keyData = Child(enc, "keyData");
        XElement encKey = enc.Descendants().First(e => e.Name.LocalName == "encryptedKey");

        RequireAes(keyData, encKey);
        HashKind hash = ParseHash((string)encKey.Attribute("hashAlgorithm")!);

        byte[] keyDataSalt = B64(keyData, "saltValue");
        int blockSize = (int)keyData.Attribute("blockSize")!;

        byte[] pwdSalt = B64(encKey, "saltValue");
        int spinCount = (int)encKey.Attribute("spinCount")!;
        int keyBytes = (int)encKey.Attribute("keyBits")! / 8;

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
        byte[] verifierInput = AesCbcDecrypt(DeriveKey(BlockVerifierHashInput), pwdSalt, B64(encKey, "encryptedVerifierHashInput"));
        byte[] verifierValue = AesCbcDecrypt(DeriveKey(BlockVerifierHashValue), pwdSalt, B64(encKey, "encryptedVerifierHashValue"));
        byte[] check = Hash(hash, verifierInput);
        if (!check.AsSpan(0, verifierValue.Length).SequenceEqual(verifierValue))
            throw new UnauthorizedAccessException("Incorrect database password.");

        // Recover the data-encryption key.
        byte[] secretKey = AesCbcDecrypt(DeriveKey(BlockKeyValue), pwdSalt, B64(encKey, "encryptedKeyValue"));

        byte[] encodingKey = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(encodingKey, databaseKey);
        return new AgileEncryption(secretKey, keyDataSalt, blockSize, encodingKey);
    }

    public void DecryptPage(int pageNumber, Span<byte> page)
    {
        if (pageNumber == 0)
            return; // header page is not encrypted

        // blockKey = LE32(pageNumber) XOR dbEncodingKey; IV = Hash(keyDataSalt ‖ blockKey), truncated to blockSize.
        Span<byte> blockKey = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(blockKey, pageNumber);
        for (int i = 0; i < 4; i++) blockKey[i] ^= _encodingKey[i];

        byte[] iv = Fit(SHA512.HashData(Concat(_keyDataSalt, blockKey.ToArray())), _blockSize);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = _secretKey;
        aes.IV = iv;
        // Decrypt in place (length is a whole number of AES blocks — a page).
        byte[] clear = aes.DecryptCbc(page, iv, PaddingMode.None);
        clear.CopyTo(page);
    }

    // --- helpers ---

    private enum HashKind { Sha512 }

    private static XElement? LocateEncryptionInfo(ReadOnlySpan<byte> page0)
    {
        // The descriptor is an ASCII XML document embedded in page 0.
        ReadOnlySpan<byte> open = "<?xml"u8;
        ReadOnlySpan<byte> closeTag = "</encryption>"u8;
        int start = IndexOf(page0, open);
        if (start < 0) return null;
        int end = IndexOf(page0[start..], closeTag);
        if (end < 0) return null;
        string xml = Encoding.UTF8.GetString(page0.Slice(start, end + closeTag.Length));
        return XDocument.Parse(xml).Root;
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        return -1;
    }

    private static XElement Child(XElement parent, string localName) =>
        parent.Descendants().First(e => e.Name.LocalName == localName);

    private static byte[] B64(XElement el, string attr) => Convert.FromBase64String((string)el.Attribute(attr)!);

    private static void RequireAes(XElement keyData, XElement encKey)
    {
        foreach (var el in new[] { keyData, encKey })
        {
            if ((string?)el.Attribute("cipherAlgorithm") != "AES" ||
                (string?)el.Attribute("cipherChaining") != "ChainingModeCBC")
                throw new NotSupportedException("Only AES-CBC Agile encryption is supported.");
        }
    }

    private static HashKind ParseHash(string name) => name switch
    {
        "SHA512" => HashKind.Sha512,
        _ => throw new NotSupportedException($"Unsupported Agile hash algorithm '{name}' (only SHA512 is verified).")
    };

    private static byte[] Hash(HashKind kind, params byte[][] parts)
    {
        byte[] all = parts.Length == 1 ? parts[0] : Concat(parts);
        return kind switch { HashKind.Sha512 => SHA512.HashData(all), _ => throw new NotSupportedException() };
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
