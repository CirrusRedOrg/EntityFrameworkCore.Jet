using LibRed.Formats;
using LibRed.Pages;
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
/// <item>verifier block = <c>LE32(0)</c>; per-page block = <c>pageNumber XOR databaseKey</c> (the 4-byte
/// database key at page-0 <c>0x3E</c>).</item>
/// <item>cipher: RC4 (stream, re-keyed per page; the verifier + verifier-hash decrypt as one continuous stream)
/// or AES-ECB.</item>
/// </list>
/// </remarks>
public sealed class OfficeStandardEncryption : IPageCodec
{

    internal const int MajorVersionOffset = 0;
    internal const int MinorVersionOffset = 2;
    internal const int FlagsOffset = 4;

    /// <summary>Binary form: the EncryptionHeader's size, and then the header itself.</summary>
    internal const int HeaderSizeOffset = 8;
    internal const int HeaderOffset = 12;

    /// <summary>The minor version of every binary (Standard/CryptoAPI) EncryptionInfo; the major is 2, 3 or 4.</summary>
    internal const ushort StandardMinorVersion = 2;
    internal const ushort StandardMinMajorVersion = 2;
    internal const ushort StandardMaxMajorVersion = 4;

    /// <summary>The major version Access writes on a binary EncryptionInfo it creates (verified against real files).</summary>
    internal const ushort StandardCreatedMajorVersion = 4;

    /// <summary>The largest EncryptionHeader a binary EncryptionInfo is taken to have when one is searched for.</summary>
    internal const int MaxHeaderSize = 512;

    // --- EncryptionHeader (MS-OFFCRYPTO §2.3.2), from its own start ---

    internal const int HeaderFlagsOffset = 0;
    internal const int HeaderSizeExtraOffset = 4;
    internal const int HeaderAlgIdOffset = 8;
    internal const int HeaderAlgIdHashOffset = 12;
    internal const int HeaderKeySizeOffset = 16;
    internal const int HeaderProviderTypeOffset = 20;

    /// <summary>The fixed fields — Flags, SizeExtra, AlgID, AlgIDHash, KeySize, ProviderType and two reserved words —
    /// ahead of the null-terminated UTF-16LE CSP name.</summary>
    internal const int HeaderFixedSize = 32;

    // --- EncryptionVerifier (MS-OFFCRYPTO §2.3.3), from its own start ---

    internal const int VerifierSaltSizeOffset = 0;
    internal const int VerifierSaltOffset = 4;

    /// <summary>The salt size every Standard verifier carries.</summary>
    internal const int VerifierSaltSize = 16;

    /// <summary>The encrypted verifier that follows the salt; the verifier-hash size and the encrypted hash come next.</summary>
    internal const int VerifierSize = 16;

    // --- Field values ---

    /// <summary>The cipher AlgIDs (CALG_*): RC4, and AES with a 128-, 192- or 256-bit key.</summary>
    internal const uint AlgIdRc4 = 0x6801;
    internal const uint AlgIdAes128 = 0x660E;
    internal const uint AlgIdAes192 = 0x660F;
    internal const uint AlgIdAes256 = 0x6610;

    /// <summary>The CALG_* class of every block cipher (0x66xx) and stream cipher (0x68xx), in the AlgID's second byte.</summary>
    internal const uint AlgIdClassMask = 0xFF00;
    internal const uint AlgIdBlockCipherClass = 0x6600;
    internal const uint AlgIdStreamCipherClass = 0x6800;

    /// <summary>The ProviderType of the Base and the Enhanced RSA/AES cryptographic providers.</summary>
    internal const uint ProviderTypeBase = 0x01;
    internal const uint ProviderTypeEnhancedAes = 0x18;

    private const int VerifierBlock = 0;

    private readonly bool _rc4;
    private readonly HashAlgorithmName _hashName; // hashing algorithm from AlgIDHash (MD5/SHA-1/256/384/512)
    private readonly byte[] _baseHash;
    private readonly int _iterations;
    private readonly int _truncateLen; // logical key length in bytes derived from the hash (5 for RC4-40, 32 for AES-256)
    private readonly int _finalLen;    // actual cipher key length (RC4 <128-bit keys are zero-padded to 16 bytes)
    private readonly int _databaseKey; // page-0 0x3E

    private readonly bool _aesExpand; // AES: apply the CryptDeriveKey 0x36/0x5C expansion vs. use the truncated hash

    private OfficeStandardEncryption(bool rc4, HashAlgorithmName hashName, byte[] baseHash, int iterations, int truncateLen, int finalLen, bool aesExpand, int databaseKey)
    {
        _rc4 = rc4; _hashName = hashName; _baseHash = baseHash; _iterations = iterations; _truncateLen = truncateLen; _finalLen = finalLen; _aesExpand = aesExpand; _databaseKey = databaseKey;
    }

    // AlgIDHash ↔ (.NET hash algorithm, unencrypted-hash length in bytes), read by MapHash and written by HashAlgId.
    // Access/the CryptoAPI let the encrypting tool pick the hashing algorithm independently of the cipher; MD2/MD4
    // (0x8001/0x8002) have no managed implementation and are not produced by Access, so they surface as
    // "unsupported" rather than a crash.
    private static readonly (uint AlgIdHash, HashAlgorithmName Name, int Len)[] Hashes =
    [
        (0x8003, HashAlgorithmName.MD5, 16),
        (0x8004, HashAlgorithmName.SHA1, 20),
        (0x800c, HashAlgorithmName.SHA256, 32),
        (0x800d, HashAlgorithmName.SHA384, 48),
        (0x800e, HashAlgorithmName.SHA512, 64),
    ];

    private static (HashAlgorithmName Name, int Len)? MapHash(uint algIdHash) =>
        Array.Find(Hashes, h => h.AlgIdHash == algIdHash) is { Name.Name: not null } h ? (h.Name, h.Len) : null;

    // The CALG_* AlgIDHash to write into a descriptor for a given .NET hash.
    private static uint HashAlgId(HashAlgorithmName name) =>
        Array.Find(Hashes, h => h.Name == name) is { Name.Name: not null } h
            ? h.AlgIdHash
            : throw new NotSupportedException($"Unsupported Office-Standard hash {name}.");

    // The CryptoAPI CryptDeriveKey 0x36/0x5C expansion uses a fixed 64-byte pad buffer for every hash algorithm
    // (it is not HMAC, so SHA-384/512 do NOT switch to their 128-byte block size). Verified: AES-256 + SHA-512.
    private const int DeriveKeyPadSize = 64;

    /// <summary>Asserts that <paramref name="count"/> bytes are readable at <paramref name="offset"/> within
    /// the descriptor, so a length taken from the file cannot index past it.</summary>
    private static void Require(ReadOnlySpan<byte> descriptor, int offset, int count, string field)
    {
        if (offset < 0 || count < 0 || (long)offset + count > descriptor.Length)
            throw new InvalidDataException(
                $"Office-Standard {field} needs {count} bytes at offset {offset}, past the {descriptor.Length}-byte descriptor.");
    }

    /// <summary>Builds a codec for an <c>.accdb</c> that uses a binary (non-Agile) EncryptionInfo descriptor, or
    /// null if the file is not encrypted / carries no such descriptor. Throws if a password is required/incorrect.</summary>
    public static OfficeStandardEncryption? TryCreate(ReadOnlySpan<byte> page0, int databaseKey, string? password,
        JetFormatBase format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (databaseKey == 0)
            return null;

        int descriptorOffset = format.EncryptionInfoOffset;
        if (page0.Length < descriptorOffset)
            throw new InvalidDataException("Page 0 is too short to contain an ACE EncryptionInfo frame.");
        int descriptorLength = DatabaseDefinitionPage.ReadEncryptionInfoLength(page0, format);
        if (descriptorLength == 0)
            return null;
        if (descriptorLength > page0.Length - descriptorOffset)
            throw new InvalidDataException("The declared Office-Standard EncryptionInfo extends beyond page 0.");
        page0 = page0.Slice(descriptorOffset, descriptorLength);

        if (LocateBinaryEncryptionInfo(page0) is not var (ei, headerSize, algId))
            return null;
        if (password is null)
            throw new InvalidOperationException("This database is password-encrypted; a password is required to open it.");

        // Everything below indexes with lengths read out of the descriptor, so each one is bounded against the
        // descriptor's own frame first. The outer frame was checked above; the interior was not, and a small
        // frame with a large headerSize walked `v` past the end — throwing ArgumentOutOfRangeException out of
        // PageChannel.Open, where the contract for a damaged file is InvalidDataException. The Agile sibling
        // bounds every field it reads; this is that, for the fields this descriptor has.
        int h = ei + HeaderOffset;
        Require(page0, h, HeaderKeySizeOffset + sizeof(int), "EncryptionHeader");
        uint algIdHash = BinaryPrimitives.ReadUInt32LittleEndian(page0.Slice(h + HeaderAlgIdHashOffset, sizeof(uint)));
        int keyBits = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(h + HeaderKeySizeOffset, sizeof(int)));

        bool rc4 = algId == AlgIdRc4;
        // We've committed to a binary EncryptionInfo descriptor (dbKey != 0 + located header). Anything we can't
        // honour is a genuinely unsupported/invalid file (e.g. Access rejects AES/3DES combos with certain hashes),
        // so throw a clear error rather than falling through to read ciphertext as plaintext.
        if (!rc4 && algId is not (AlgIdAes128 or AlgIdAes192 or AlgIdAes256))
            throw new NotSupportedException($"Unsupported Office-Standard cipher AlgID 0x{algId:X4}.");
        if (MapHash(algIdHash) is not var (hashName, hashLength))
            throw new NotSupportedException($"Unsupported Office-Standard hash AlgID 0x{algIdHash:X4}.");
        if (keyBits != 0 && (rc4
                ? keyBits is < 40 or > 128 || keyBits % 8 != 0
                : keyBits is not (128 or 192 or 256)))
            throw new NotSupportedException(
                $"Unsupported Office-Standard {(rc4 ? "RC4" : "AES")} key size {keyBits} bits.");

        if (headerSize < 0)
            throw new InvalidDataException($"Office-Standard EncryptionHeader declares a negative size ({headerSize}).");
        int v = h + headerSize;                       // EncryptionVerifier
        Require(page0, v, VerifierSaltOffset, "EncryptionVerifier");
        int saltSize = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(v + VerifierSaltSizeOffset, sizeof(int)));
        if (saltSize != VerifierSaltSize)
            throw new NotSupportedException(
                $"Unsupported Office-Standard salt size {saltSize}; expected {VerifierSaltSize} bytes.");
        int verifier = v + VerifierSaltOffset + saltSize;
        int hashSizeField = verifier + VerifierSize;
        int encHash = hashSizeField + sizeof(int);
        Require(page0, v + VerifierSaltOffset, encHash - (v + VerifierSaltOffset),
            "EncryptionVerifier salt and verifier");
        byte[] salt = page0.Slice(v + VerifierSaltOffset, saltSize).ToArray();
        byte[] encVerifier = page0.Slice(verifier, VerifierSize).ToArray();
        int verifierHashSize = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(hashSizeField, sizeof(int)));
        if (verifierHashSize != hashLength)
            throw new NotSupportedException(
                $"Office-Standard verifier hash size {verifierHashSize} does not match {hashName.Name} ({hashLength} bytes).");
        int encHashLen = rc4 ? verifierHashSize : (verifierHashSize + 15) / 16 * 16; // RC4: raw hash; AES: padded to block
        Require(page0, encHash, encHashLen, "EncryptionVerifier hash");
        byte[] encVerifierHash = page0.Slice(encHash, encHashLen).ToArray();

        // MD5 and SHA-1 are not a choice here: the descriptor NAMES the algorithm, and a file Access wrote with one
        // cannot be opened with anything else.
        byte[] baseHash = CryptographicOperations.HashData(hashName, [.. salt, .. Encoding.Unicode.GetBytes(password)]);

        // Try each plausible (key length, RC4 pad, iteration count) combination and keep whichever authenticates —
        // KeySize == 0 means "the algorithm default" (which the descriptor doesn't spell out), RC4 keys shorter than
        // 128 bits are zero-padded to 16 bytes by the base provider, and AES may be ECMA-standard (50000 iterations)
        // or the "non-standard" 0-iteration variant. The verifier disambiguates all of them.
        foreach ((int truncateLen, int finalLen) in KeyCandidates(rc4, keyBits))
            foreach (int iterations in rc4 ? [0] : new[] { 0, 50000 })
                foreach (bool aesExpand in rc4 ? [false] : new[] { false, true })
                {
                    var codec = new OfficeStandardEncryption(rc4, hashName, baseHash, iterations, truncateLen, finalLen, aesExpand, databaseKey);
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

    /// <summary>Generates a fresh Office-Standard AES-256 <c>EncryptionInfo</c> for a new password — the 0-iteration
    /// variant Access accepts on a created file: returns the descriptor blob (to place at page-0 <c>0x29B</c>, with
    /// its 2-byte length at <c>0x299</c>) and a codec that encrypts the data pages. <paramref name="databaseKey"/> is
    /// a fresh random <c>0x3E</c> key. RC4 is <see cref="CreateRc4"/>.</summary>
    internal static (byte[] Descriptor, OfficeStandardEncryption Codec) Create(string password, int databaseKey)
    {
        const int keyBits = 256;
        byte[] salt = RandomNumberGenerator.GetBytes(VerifierSaltSize);
        // The Office-Standard scheme hashes the password with SHA-1 by definition — a file created with anything
        // else is not this scheme, and Access would not open it.
        HashAlgorithmName hash = HashAlgorithmName.SHA1;
        byte[] baseHash = CryptographicOperations.HashData(hash, [.. salt, .. Encoding.Unicode.GetBytes(password)]);
        int keyLen = keyBits / 8;
        // AES-256 (32 bytes) from SHA-1 (20 bytes) needs the 0x36/0x5C expansion.
        var codec = new OfficeStandardEncryption(rc4: false, hash, baseHash, 0, keyLen, keyLen, aesExpand: true, databaseKey);

        byte[] verKey = codec.ComputeKey(VerifierBlock);
        byte[] verifier = RandomNumberGenerator.GetBytes(VerifierSize);
        byte[] verifierHash = CryptographicOperations.HashData(hash, verifier);
        byte[] encVerifier = AesEcb(verKey, verifier, decrypt: false);
        byte[] encVerifierHash = AesEcb(verKey, Fix(verifierHash, 32), decrypt: false); // pad the 20-byte hash to a cipher block

        var (provType, csp) = Provider(aes: true, keyBits, hash);
        return (BuildDescriptor(AlgIdAes256, HashAlgId(hash),
            EncryptionInfoFlags.CryptoApi | EncryptionInfoFlags.DocProps, keyBits, provType, csp, salt, encVerifier,
            encVerifierHash, verifierHash.Length), codec);
    }

    /// <summary>Generates a fresh Office-Standard RC4 <c>EncryptionInfo</c> with a caller-chosen key length
    /// (<paramref name="keyBits"/>, 40–128) and hashing algorithm (<paramref name="hash"/>). RC4 is the only Standard
    /// cipher a stock/add-in Access reads back; AES-Standard is exposed only via the AES <see cref="Create"/> path.</summary>
    internal static (byte[] Descriptor, OfficeStandardEncryption Codec) CreateRc4(string password, int keyBits, HashAlgorithmName hash, int databaseKey)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(VerifierSaltSize);
        byte[] baseHash = CryptographicOperations.HashData(hash, [.. salt, .. Encoding.Unicode.GetBytes(password)]);
        int keyLen = keyBits / 8;
        int finalLen = keyBits == 40 ? 16 : keyLen; // 40-bit is the one length the base provider zero-pads to 128
        var codec = new OfficeStandardEncryption(rc4: true, hash, baseHash, 0, keyLen, finalLen, aesExpand: false, databaseKey);

        byte[] verKey = codec.ComputeKey(VerifierBlock);
        byte[] verifier = RandomNumberGenerator.GetBytes(VerifierSize);
        byte[] verifierHash = CryptographicOperations.HashData(hash, verifier);
        byte[] stream = [.. verifier, .. verifierHash]; // one continuous RC4 stream
        Rc4Cipher.Apply(verKey, stream);

        var (provType, csp) = Provider(aes: false, keyBits, hash);
        return (BuildDescriptor(AlgIdRc4, HashAlgId(hash), EncryptionInfoFlags.CryptoApi, keyBits,
            provType, csp, salt, stream[..VerifierSize], stream[VerifierSize..],
            verifierHash.Length), codec);
    }

    // Picks the (ProviderType, CSP name) pair the way the CryptoAPI does: the Base provider handles RC4 ≤56-bit with
    // MD-family/SHA-1 hashes; larger keys or SHA-2 hashes require the Enhanced RSA/AES provider ("enhanced mode").
    private static (uint ProviderType, string Csp) Provider(bool aes, int keyBits, HashAlgorithmName hash)
    {
        bool sha2 = hash.Name is "SHA256" or "SHA384" or "SHA512";
        bool enhanced = aes || keyBits > 56 || sha2;
        return enhanced
            ? (ProviderTypeEnhancedAes, "Microsoft Enhanced RSA and AES Cryptographic Provider")
            : (ProviderTypeBase, "Microsoft Base Cryptographic Provider v1.0");
    }

    // Builds the binary EncryptionInfo (version 4.2 + EncryptionHeader incl. ProviderType + CSP name +
    // EncryptionVerifier) byte-for-byte as Access writes it (verified against real files).
    private static byte[] BuildDescriptor(uint algId, uint algIdHash, EncryptionInfoFlags flags, int keyBits, uint providerType,
        string csp, byte[] salt, byte[] encVerifier, byte[] encVerifierHash, int verifierHashSize)
    {
        byte[] cspBytes = [.. Encoding.Unicode.GetBytes(csp), 0, 0]; // null-terminated UTF-16LE

        var b = new List<byte>();
        void U16(ushort x) => b.AddRange(BitConverter.GetBytes(x));
        void U32(uint x) => b.AddRange(BitConverter.GetBytes(x));

        // Written in field order, using the same layout as the reader.
        int headerSize = HeaderFixedSize + cspBytes.Length;
        U16(StandardCreatedMajorVersion); U16(StandardMinorVersion);
        U32((uint)flags);
        U32((uint)headerSize);
        U32((uint)flags); U32(0); U32(algId); U32(algIdHash); U32((uint)keyBits); U32(providerType); U32(0); U32(0);
        b.AddRange(cspBytes);
        // EncryptionVerifier
        U32((uint)salt.Length); b.AddRange(salt);
        b.AddRange(encVerifier);
        U32((uint)verifierHashSize); b.AddRange(encVerifierHash);
        return b.ToArray();
    }

    private bool VerifyPassword(byte[] encVerifier, byte[] encVerifierHash, int hashSize)
    {
        byte[] key = ComputeKey(VerifierBlock);
        byte[] verifier, storedHash;
        if (_rc4)
        {
            // one continuous RC4 stream over verifier(16) ‖ verifierHash
            byte[] stream = [.. encVerifier, .. encVerifierHash];
            Rc4Cipher.Apply(key, stream);
            verifier = stream[..VerifierSize];
            storedHash = stream[VerifierSize..];
        }
        else
        {
            verifier = AesEcb(key, encVerifier, decrypt: true);
            storedHash = AesEcb(key, encVerifierHash, decrypt: true);
        }
        byte[] computed = CryptographicOperations.HashData(_hashName, verifier);
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

        byte[] key = ComputeKey(pageNumber ^ _databaseKey);
        if (_rc4)
        {
            Rc4Cipher.Apply(key, page);            // symmetric
        }
        else
        {
            int len = page.Length - page.Length % 16;
            AesEcb(key, page[..len], decrypt).CopyTo(page);
        }
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
                iterHash = CryptographicOperations.HashData(_hashName, [.. it, .. iterHash]);
            }
        }
        byte[] blk = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(blk, block);
        byte[] hf = CryptographicOperations.HashData(_hashName, [.. iterHash, .. blk]);

        // RC4 keys are always the truncated hash. AES either uses the truncated hash or the CryptDeriveKey
        // 0x36/0x5C expansion — the choice does not follow a clean keyLen-vs-hashLen rule (AES-128 from MD5 (16==16)
        // expands, yet AES-256 from SHA-256 (32==32) truncates), so both are tried and the verifier selects.
        byte[] derived = !_rc4 && _aesExpand ? [.. GenX(hf, 0x36), .. GenX(hf, 0x5C)] : hf;
        byte[] key = Fix(derived, _truncateLen);
        return _finalLen != _truncateLen ? Fix(key, _finalLen) : key;
    }

    // --- helpers ---

    private byte[] GenX(byte[] hf, byte pad)
    {
        byte[] buf = new byte[DeriveKeyPadSize];
        Array.Fill(buf, pad);
        for (int i = 0; i < hf.Length; i++) buf[i] ^= hf[i];
        return CryptographicOperations.HashData(_hashName, buf);
    }

    /// <summary>Finds the binary EncryptionInfo in <paramref name="descriptor"/>, returning where it starts and the two
    /// fields it was recognised by — its header's size and cipher AlgID — or null when there is none.</summary>
    private static (int Offset, int HeaderSize, uint AlgId)? LocateBinaryEncryptionInfo(ReadOnlySpan<byte> descriptor)
    {
        // A binary EncryptionInfo begins: uint16 major, uint16 minor(=2 for standard/CryptoAPI), uint32 flags
        // (fCryptoAPI set), uint32 headerSize. Validate against a known cipher AlgID to avoid false hits.
        const int algIdAt = HeaderOffset + HeaderAlgIdOffset;
        for (int i = 0; i + algIdAt + sizeof(uint) <= descriptor.Length; i++)
        {
            ReadOnlySpan<byte> info = descriptor[i..];
            ushort major = BinaryPrimitives.ReadUInt16LittleEndian(info[MajorVersionOffset..]);
            ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(info[MinorVersionOffset..]);
            var flags = (EncryptionInfoFlags)BinaryPrimitives.ReadUInt32LittleEndian(info[FlagsOffset..]);
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(info[HeaderSizeOffset..]);
            if (major is < StandardMinMajorVersion or > StandardMaxMajorVersion
                || minor != StandardMinorVersion || !flags.HasFlag(EncryptionInfoFlags.CryptoApi)
                || headerSize is <= 0 or > MaxHeaderSize)
                continue;
            // Recognise the descriptor by a CALG_* cipher id (0x66xx block ciphers, 0x68xx stream). This covers the
            // ciphers we support (RC4, AES-128/192/256) *and* ones we don't (e.g. 3DES 0x6603) so those surface as a
            // clean "unsupported" error in TryCreate instead of being mistaken for an unencrypted file.
            uint algId = BinaryPrimitives.ReadUInt32LittleEndian(info[algIdAt..]);
            if ((algId & AlgIdClassMask)
                is AlgIdBlockCipherClass or AlgIdStreamCipherClass)
                return (i, headerSize, algId);
        }
        return null;
    }

    /// <summary>AES-ECB with no padding — the verifier fields and every data page.</summary>
    private static byte[] AesEcb(byte[] key, ReadOnlySpan<byte> data, bool decrypt)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return decrypt ? aes.DecryptEcb(data, PaddingMode.None) : aes.EncryptEcb(data, PaddingMode.None);
    }

    private static byte[] Fix(byte[] b, int len)
    {
        if (b.Length == len) return b;
        byte[] r = new byte[len];
        Array.Copy(b, r, Math.Min(b.Length, len));
        return r;
    }
}