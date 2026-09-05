using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LibRed.Crypto;

/// <summary>
/// Office "Standard"/CryptoAPI encryption (MS-OFFCRYPTO §2.3.4/§2.3.5) as Access applies it to an
/// <c>.accdb</c> — the pre-Agile scheme carried by a <b>binary</b> <c>EncryptionInfo</c> header (version x.2),
/// covering RC4-CryptoAPI and the AES "non-standard" variant. Distinct from ACE Agile (XML descriptor,
/// <see cref="AgileEncryption"/>) and legacy Jet RC4 (<see cref="JetLegacyEncryption"/>).
/// </summary>
/// <remarks>
/// Algorithm verified against the jackcess-encrypt providers and real fixtures (db2007-oldenc = RC4-40 /
/// Test123; db-nonstandard = AES-256 / password):
/// <list type="bullet">
/// <item><c>baseHash = SHA1(salt ‖ UTF16LE(password))</c>.</item>
/// <item>per-block key: <c>iterHash = iterate(baseHash, iterations)</c> (0 iterations for RC4-CryptoAPI and the
/// AES "non-standard" variant; 50000 for ECMA-standard AES); <c>H = SHA1(iterHash ‖ block)</c>; then AES uses the
/// <c>0x36/0x5C</c> expansion <c>key = (SHA1(0x36pad⊕H) ‖ SHA1(0x5Cpad⊕H))[0..keyLen]</c> while RC4 uses
/// <c>key = H[0..keyLen]</c> (a 40-bit RC4 key is then zero-padded to 16 bytes).</item>
/// <item>verifier block = <c>LE32(0)</c>; per-page block = <c>LE32(pageNumber) XOR encodingKey</c> (the 4-byte
/// database key at page-0 <c>0x3E</c>).</item>
/// <item>cipher: RC4 (stream, re-keyed per page; the verifier + verifier-hash decrypt as one continuous stream)
/// or AES-ECB.</item>
/// </list>
/// </remarks>
public sealed class OfficeStandardEncryption : IPageCodec
{
    private const uint AlgIdRc4 = 0x6801;
    private const int VerifierBlock = 0;

    private readonly bool _rc4;
    private readonly HashAlgorithmName _hashName; // hashing algorithm from AlgIDHash (MD5/SHA-1/256/384/512)
    private readonly byte[] _baseHash;
    private readonly int _iterations;
    private readonly int _truncateLen; // logical key length in bytes derived from the hash (5 for RC4-40, 32 for AES-256)
    private readonly int _finalLen;    // actual cipher key length (RC4 <128-bit keys are zero-padded to 16 bytes)
    private readonly byte[] _encodingKey; // 4-byte database key (page-0 0x3E)

    private readonly bool _aesExpand; // AES: apply the CryptDeriveKey 0x36/0x5C expansion vs. use the truncated hash

    private OfficeStandardEncryption(bool rc4, HashAlgorithmName hashName, byte[] baseHash, int iterations, int truncateLen, int finalLen, bool aesExpand, byte[] encodingKey)
    {
        _rc4 = rc4; _hashName = hashName; _baseHash = baseHash; _iterations = iterations; _truncateLen = truncateLen; _finalLen = finalLen; _aesExpand = aesExpand; _encodingKey = encodingKey;
    }

    // AlgIDHash → (.NET hash algorithm, unencrypted-hash length in bytes). Access/the CryptoAPI let the encrypting
    // tool pick the hashing algorithm independently of the cipher; MD2/MD4 (0x8001/0x8002) have no managed
    // implementation and are not produced by Access, so they surface as "unsupported" rather than a crash.
    private static (HashAlgorithmName Name, int Len)? MapHash(uint algIdHash) => algIdHash switch
    {
        0x8003 => (HashAlgorithmName.MD5, 16),
        0x8004 => (HashAlgorithmName.SHA1, 20),
        0x800c => (HashAlgorithmName.SHA256, 32),
        0x800d => (HashAlgorithmName.SHA384, 48),
        0x800e => (HashAlgorithmName.SHA512, 64),
        _ => null,
    };

    // Reverse of MapHash: the CALG_* AlgIDHash to write into a descriptor for a given .NET hash.
    private static uint HashAlgId(HashAlgorithmName name) => name.Name switch
    {
        "MD5" => 0x8003,
        "SHA1" => 0x8004,
        "SHA256" => 0x800c,
        "SHA384" => 0x800d,
        "SHA512" => 0x800e,
        _ => throw new NotSupportedException($"Unsupported Office-Standard hash {name}."),
    };

    private static byte[] Hash(HashAlgorithmName name, byte[] data) => name.Name switch
    {
        "MD5" => MD5.HashData(data),
        "SHA1" => SHA1.HashData(data),
        "SHA256" => SHA256.HashData(data),
        "SHA384" => SHA384.HashData(data),
        "SHA512" => SHA512.HashData(data),
        _ => throw new NotSupportedException($"Unsupported hash algorithm {name}."),
    };

    // The CryptoAPI CryptDeriveKey 0x36/0x5C expansion uses a fixed 64-byte pad buffer for every hash algorithm
    // (it is not HMAC, so SHA-384/512 do NOT switch to their 128-byte block size). Verified: AES-256 + SHA-512.
    private const int DeriveKeyPadSize = 64;

    /// <summary>Builds a codec for an <c>.accdb</c> that uses a binary (non-Agile) EncryptionInfo descriptor, or
    /// null if the file is not encrypted / carries no such descriptor. Throws if a password is required/incorrect.</summary>
    public static OfficeStandardEncryption? TryCreate(ReadOnlySpan<byte> page0, int databaseKey, string? password)
    {
        if (databaseKey == 0)
            return null;

        const int descriptorOffset = 0x29B;
        if (page0.Length < descriptorOffset)
            throw new InvalidDataException("Page 0 is too short to contain an ACE EncryptionInfo frame.");
        int descriptorLength = BinaryPrimitives.ReadUInt16LittleEndian(page0.Slice(0x299, 2));
        if (descriptorLength == 0)
            return null;
        if (descriptorLength > page0.Length - descriptorOffset)
            throw new InvalidDataException("The declared Office-Standard EncryptionInfo extends beyond page 0.");
        page0 = page0.Slice(descriptorOffset, descriptorLength);

        int ei = LocateBinaryEncryptionInfo(page0);
        if (ei < 0)
            return null;
        if (password is null)
            throw new InvalidOperationException("This database is password-encrypted; a password is required to open it.");

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(ei + 8, 4));
        int h = ei + 12;
        uint algId = BinaryPrimitives.ReadUInt32LittleEndian(page0.Slice(h + 8, 4));
        uint algIdHash = BinaryPrimitives.ReadUInt32LittleEndian(page0.Slice(h + 12, 4));
        int keyBits = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(h + 16, 4));

        bool rc4 = algId == AlgIdRc4;
        // We've committed to a binary EncryptionInfo descriptor (dbKey != 0 + located header). Anything we can't
        // honour is a genuinely unsupported/invalid file (e.g. Access rejects AES/3DES combos with certain hashes),
        // so throw a clear error rather than falling through to read ciphertext as plaintext.
        if (!rc4 && algId is not (0x660E or 0x660F or 0x6610))
            throw new NotSupportedException($"Unsupported Office-Standard cipher AlgID 0x{algId:X4}.");
        if (MapHash(algIdHash) is not var (hashName, hashLength))
            throw new NotSupportedException($"Unsupported Office-Standard hash AlgID 0x{algIdHash:X4}.");
        if (keyBits != 0 && (rc4
                ? keyBits is < 40 or > 128 || keyBits % 8 != 0
                : keyBits is not (128 or 192 or 256)))
            throw new NotSupportedException(
                $"Unsupported Office-Standard {(rc4 ? "RC4" : "AES")} key size {keyBits} bits.");

        int v = h + headerSize;                       // EncryptionVerifier
        int saltSize = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(v, 4));
        if (saltSize != 16)
            throw new NotSupportedException($"Unsupported Office-Standard salt size {saltSize}; expected 16 bytes.");
        byte[] salt = page0.Slice(v + 4, saltSize).ToArray();
        byte[] encVerifier = page0.Slice(v + 4 + saltSize, 16).ToArray();
        int verifierHashSize = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(v + 4 + saltSize + 16, 4));
        if (verifierHashSize != hashLength)
            throw new NotSupportedException(
                $"Office-Standard verifier hash size {verifierHashSize} does not match {hashName.Name} ({hashLength} bytes).");
        int encHashLen = rc4 ? verifierHashSize : (verifierHashSize + 15) / 16 * 16; // RC4: raw hash; AES: padded to block
        byte[] encVerifierHash = page0.Slice(v + 4 + saltSize + 16 + 4, encHashLen).ToArray();

        byte[] baseHash = Hash(hashName, Concat(salt, Encoding.Unicode.GetBytes(password)));
        byte[] encodingKey = BitConverter.GetBytes(databaseKey);

        // Try each plausible (key length, RC4 pad, iteration count) combination and keep whichever authenticates —
        // KeySize == 0 means "the algorithm default" (which the descriptor doesn't spell out), RC4 keys shorter than
        // 128 bits are zero-padded to 16 bytes by the base provider, and AES may be ECMA-standard (50000 iterations)
        // or the "non-standard" 0-iteration variant. The verifier disambiguates all of them.
        foreach ((int truncateLen, int finalLen) in KeyCandidates(rc4, keyBits))
            foreach (int iterations in rc4 ? [0] : new[] { 0, 50000 })
                foreach (bool aesExpand in rc4 ? [false] : new[] { false, true })
                {
                    var codec = new OfficeStandardEncryption(rc4, hashName, baseHash, iterations, truncateLen, finalLen, aesExpand, encodingKey);
                    if (codec.VerifyPassword(encVerifier, encVerifierHash, verifierHashSize))
                        return codec;
                }
        throw new UnauthorizedAccessException("Incorrect database password.");
    }

    // Candidate (truncate, final) key lengths in bytes. keyBits > 0 is authoritative; keyBits == 0 ("default") is
    // resolved by trying the standard defaults. RC4 keys < 16 bytes are also tried zero-padded to 16 (the base
    // CryptoAPI provider's exportable-key behaviour); the caller's verifier check selects the real one.
    private static IEnumerable<(int Truncate, int Final)> KeyCandidates(bool rc4, int keyBits)
    {
        int[] lens = keyBits > 0 ? [keyBits / 8] : rc4 ? [5, 16] : [16, 24, 32];
        foreach (int len in lens)
        {
            yield return (len, len);
            if (rc4 && len < 16)
                yield return (len, 16);
        }
    }

    /// <summary>Generates a fresh Office-Standard <c>EncryptionInfo</c> for a new password: returns the
    /// descriptor blob (to place at page-0 <c>0x29B</c>, with its 2-byte length at <c>0x299</c>) and a codec that
    /// encrypts the data pages. <paramref name="aes"/> selects AES-256 (the 0-iteration variant Access accepts on
    /// a created file) vs RC4-40; <paramref name="databaseKey"/> is a fresh random <c>0x3E</c> key.</summary>
    internal static (byte[] Descriptor, OfficeStandardEncryption Codec) Create(string password, bool aes, int databaseKey)
    {
        uint algId = aes ? 0x6610u : AlgIdRc4;
        int keyBits = aes ? 256 : 40;
        byte[] salt = RandomBytes(16);
        byte[] baseHash = SHA1.HashData(Concat(salt, Encoding.Unicode.GetBytes(password)));
        int keyLen = keyBits / 8;
        int finalLen = !aes && keyBits == 40 ? 16 : keyLen;
        // AES-256 (32 bytes) from SHA-1 (20 bytes) needs the 0x36/0x5C expansion; RC4 never expands.
        var codec = new OfficeStandardEncryption(!aes, HashAlgorithmName.SHA1, baseHash, 0, keyLen, finalLen, aesExpand: aes, BitConverter.GetBytes(databaseKey));

        byte[] verKey = codec.ComputeKey(VerifierBlock);
        byte[] verifier = RandomBytes(16);
        byte[] verifierHash = SHA1.HashData(verifier);
        byte[] encVerifier, encVerifierHash;
        if (aes)
        {
            encVerifier = AesEcbEncrypt(verKey, verifier);
            encVerifierHash = AesEcbEncrypt(verKey, Fix(verifierHash, 32)); // pad the 20-byte hash to a cipher block
        }
        else
        {
            byte[] stream = Concat(verifier, verifierHash); // one continuous RC4 stream
            Rc4(verKey, stream);
            encVerifier = stream[..16];
            encVerifierHash = stream[16..];
        }

        var (provType, csp) = Provider(aes, keyBits, HashAlgorithmName.SHA1);
        return (BuildDescriptor(algId, 0x8004, aes ? 0x0Cu : 0x04u, keyBits, provType, csp, salt, encVerifier, encVerifierHash, 20), codec);
    }

    /// <summary>Generates a fresh Office-Standard RC4 <c>EncryptionInfo</c> with a caller-chosen key length
    /// (<paramref name="keyBits"/>, 40–128) and hashing algorithm (<paramref name="hash"/>). RC4 is the only Standard
    /// cipher a stock/add-in Access reads back; AES-Standard is exposed only via the AES <see cref="Create"/> path.</summary>
    internal static (byte[] Descriptor, OfficeStandardEncryption Codec) CreateRc4(string password, int keyBits, HashAlgorithmName hash, int databaseKey)
    {
        byte[] salt = RandomBytes(16);
        byte[] baseHash = Hash(hash, Concat(salt, Encoding.Unicode.GetBytes(password)));
        int keyLen = keyBits / 8;
        int finalLen = keyBits == 40 ? 16 : keyLen; // 40-bit is the one length the base provider zero-pads to 128
        var codec = new OfficeStandardEncryption(rc4: true, hash, baseHash, 0, keyLen, finalLen, aesExpand: false, BitConverter.GetBytes(databaseKey));

        byte[] verKey = codec.ComputeKey(VerifierBlock);
        byte[] verifier = RandomBytes(16);
        byte[] verifierHash = Hash(hash, verifier);
        byte[] stream = Concat(verifier, verifierHash); // one continuous RC4 stream
        Rc4(verKey, stream);

        var (provType, csp) = Provider(aes: false, keyBits, hash);
        return (BuildDescriptor(AlgIdRc4, HashAlgId(hash), 0x04u, keyBits, provType, csp, salt, stream[..16], stream[16..], verifierHash.Length), codec);
    }

    // Picks the (ProviderType, CSP name) pair the way the CryptoAPI does: the Base provider handles RC4 ≤56-bit with
    // MD-family/SHA-1 hashes; larger keys or SHA-2 hashes require the Enhanced RSA/AES provider ("enhanced mode").
    private static (uint ProviderType, string Csp) Provider(bool aes, int keyBits, HashAlgorithmName hash)
    {
        bool sha2 = hash.Name is "SHA256" or "SHA384" or "SHA512";
        bool enhanced = aes || keyBits > 56 || sha2;
        return enhanced
            ? (0x18u, "Microsoft Enhanced RSA and AES Cryptographic Provider")
            : (0x01u, "Microsoft Base Cryptographic Provider v1.0");
    }

    // Builds the binary EncryptionInfo (version 4.2 + EncryptionHeader incl. ProviderType + CSP name +
    // EncryptionVerifier) byte-for-byte as Access writes it (verified against real files).
    private static byte[] BuildDescriptor(uint algId, uint algIdHash, uint flags, int keyBits, uint providerType, string csp,
        byte[] salt, byte[] encVerifier, byte[] encVerifierHash, int verifierHashSize)
    {
        byte[] cspBytes = Concat(Encoding.Unicode.GetBytes(csp), new byte[2]); // null-terminated UTF-16LE

        var b = new List<byte>();
        void U16(ushort x) => b.AddRange(BitConverter.GetBytes(x));
        void U32(uint x) => b.AddRange(BitConverter.GetBytes(x));

        int headerSize = 8 * 4 + cspBytes.Length; // Flags,SizeExtra,AlgID,AlgIDHash,KeySize,ProviderType,Reserved1,Reserved2 + CSP
        U16(4); U16(2);            // EncryptionVersionInfo 4.2
        U32(flags);                // EncryptionInfo flags
        U32((uint)headerSize);
        U32(flags); U32(0); U32(algId); U32(algIdHash); U32((uint)keyBits); U32(providerType); U32(0); U32(0);
        b.AddRange(cspBytes);
        // EncryptionVerifier
        U32((uint)salt.Length); b.AddRange(salt);
        b.AddRange(encVerifier);
        U32((uint)verifierHashSize); b.AddRange(encVerifierHash);
        return b.ToArray();
    }

    private static byte[] AesEcbEncrypt(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        return aes.EncryptEcb(data, PaddingMode.None);
    }

    private static byte[] RandomBytes(int n) { byte[] b = new byte[n]; RandomNumberGenerator.Fill(b); return b; }

    private bool VerifyPassword(byte[] encVerifier, byte[] encVerifierHash, int hashSize)
    {
        byte[] key = ComputeKey(VerifierBlock);
        byte[] verifier, storedHash;
        if (_rc4)
        {
            // one continuous RC4 stream over verifier(16) ‖ verifierHash
            byte[] stream = Concat(encVerifier, encVerifierHash);
            Rc4(key, stream);
            verifier = stream[..16];
            storedHash = stream[16..];
        }
        else
        {
            verifier = AesEcb(key, encVerifier);
            storedHash = AesEcb(key, encVerifierHash);
        }
        byte[] computed = Hash(_hashName, verifier);
        return computed.Length == hashSize && storedHash.Length >= hashSize
            && CryptographicOperations.FixedTimeEquals(computed, storedHash.AsSpan(0, hashSize));
    }

    public void DecryptPage(int pageNumber, Span<byte> page) => Transform(pageNumber, page, decrypt: true);

    // RC4 is symmetric; AES-ECB uses the encrypt direction when writing.
    public void EncryptPage(int pageNumber, Span<byte> page) => Transform(pageNumber, page, decrypt: false);

    private void Transform(int pageNumber, Span<byte> page, bool decrypt)
    {
        if (pageNumber == 0)
            return;

        // block = LE32(pageNumber) XOR encodingKey
        Span<byte> block = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(block, pageNumber);
        for (int i = 0; i < 4; i++) block[i] ^= _encodingKey[i];

        byte[] key = ComputeKey(BinaryPrimitives.ReadInt32LittleEndian(block));
        if (_rc4)
            Rc4(key, page);            // symmetric
        else
            AesEcbInPlace(key, page, decrypt);
    }

    private byte[] ComputeKey(int block)
    {
        byte[] iterHash = _baseHash;
        if (_iterations > 0)
        {
            Span<byte> it = stackalloc byte[4];
            for (int i = 0; i < _iterations; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(it, i);
                iterHash = Hash(_hashName, Concat(it.ToArray(), iterHash));
            }
        }
        byte[] blk = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(blk, block);
        byte[] hf = Hash(_hashName, Concat(iterHash, blk));

        // RC4 keys are always the truncated hash. AES either uses the truncated hash or the CryptDeriveKey
        // 0x36/0x5C expansion — the choice does not follow a clean keyLen-vs-hashLen rule (AES-128 from MD5 (16==16)
        // expands, yet AES-256 from SHA-256 (32==32) truncates), so both are tried and the verifier selects.
        byte[] derived = !_rc4 && _aesExpand ? Concat(GenX(hf, 0x36), GenX(hf, 0x5C)) : hf;
        byte[] key = Fix(derived, _truncateLen);
        return _finalLen != _truncateLen ? Fix(key, _finalLen) : key;
    }

    // --- helpers ---

    private byte[] GenX(byte[] hf, byte pad)
    {
        byte[] buf = new byte[DeriveKeyPadSize];
        Array.Fill(buf, pad);
        for (int i = 0; i < hf.Length; i++) buf[i] ^= hf[i];
        return Hash(_hashName, buf);
    }

    private static int LocateBinaryEncryptionInfo(ReadOnlySpan<byte> page0)
    {
        // A binary EncryptionInfo begins: uint16 major, uint16 minor(=2 for standard/CryptoAPI), uint32 flags
        // (fCryptoAPI=0x04 set), uint32 headerSize. Validate against a known cipher AlgID to avoid false hits.
        for (int i = 0; i + 32 < page0.Length; i++)
        {
            ushort major = BinaryPrimitives.ReadUInt16LittleEndian(page0.Slice(i, 2));
            ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(page0.Slice(i + 2, 2));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(page0.Slice(i + 4, 2 + 2));
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(i + 8, 4));
            if (major is < 2 or > 4 || minor != 2 || (flags & 0x04) == 0 || headerSize is <= 0 or > 512)
                continue;
            // Recognise the descriptor by a CALG_* cipher id (0x66xx block ciphers, 0x68xx stream). This covers the
            // ciphers we support (RC4, AES-128/192/256) *and* ones we don't (e.g. 3DES 0x6603) so those surface as a
            // clean "unsupported" error in TryCreate instead of being mistaken for an unencrypted file.
            uint algId = BinaryPrimitives.ReadUInt32LittleEndian(page0.Slice(i + 12 + 8, 4));
            if ((algId & 0xFF00) is 0x6600 or 0x6800)
                return i;
        }
        return -1;
    }

    private static byte[] AesEcb(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        return aes.DecryptEcb(data, PaddingMode.None);
    }

    private static void AesEcbInPlace(byte[] key, Span<byte> page, bool decrypt)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        int len = page.Length - page.Length % 16;
        byte[] result = decrypt ? aes.DecryptEcb(page[..len], PaddingMode.None) : aes.EncryptEcb(page[..len], PaddingMode.None);
        result.CopyTo(page);
    }

    private static void Rc4(ReadOnlySpan<byte> key, Span<byte> data)
    {
        Span<byte> s = stackalloc byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }
        for (int n = 0, a = 0, b = 0; n < data.Length; n++)
        {
            a = (a + 1) & 0xFF;
            b = (b + s[a]) & 0xFF;
            (s[a], s[b]) = (s[b], s[a]);
            data[n] ^= s[(s[a] + s[b]) & 0xFF];
        }
    }

    private static byte[] Fix(byte[] b, int len)
    {
        if (b.Length == len) return b;
        byte[] r = new byte[len];
        Array.Copy(b, r, Math.Min(b.Length, len));
        return r;
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        byte[] r = new byte[a.Length + b.Length];
        a.CopyTo(r, 0);
        b.CopyTo(r, a.Length);
        return r;
    }
}
