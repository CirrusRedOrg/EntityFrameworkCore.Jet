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
    private readonly byte[] _baseHash;
    private readonly int _iterations;
    private readonly int _keyLen;      // logical key length in bytes (5 for RC4-40, 32 for AES-256)
    private readonly int _rc4KeyPad;   // RC4 key padded to this many bytes (16 for 40-bit), else 0
    private readonly byte[] _encodingKey; // 4-byte database key (page-0 0x3E)

    private OfficeStandardEncryption(bool rc4, byte[] baseHash, int iterations, int keyLen, int rc4KeyPad, byte[] encodingKey)
    {
        _rc4 = rc4; _baseHash = baseHash; _iterations = iterations; _keyLen = keyLen; _rc4KeyPad = rc4KeyPad; _encodingKey = encodingKey;
    }

    /// <summary>Builds a codec for an <c>.accdb</c> that uses a binary (non-Agile) EncryptionInfo descriptor, or
    /// null if the file is not encrypted / carries no such descriptor. Throws if a password is required/incorrect.</summary>
    public static OfficeStandardEncryption? TryCreate(ReadOnlySpan<byte> page0, int databaseKey, string? password)
    {
        if (databaseKey == 0)
            return null;

        int ei = LocateBinaryEncryptionInfo(page0);
        if (ei < 0)
            return null;
        if (password is null)
            throw new InvalidOperationException("This database is password-encrypted; a password is required to open it.");

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(ei + 8, 4));
        int h = ei + 12;
        uint algId = BinaryPrimitives.ReadUInt32LittleEndian(page0.Slice(h + 8, 4));
        int keyBits = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(h + 16, 4));

        int v = h + headerSize;                       // EncryptionVerifier
        int saltSize = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(v, 4));
        byte[] salt = page0.Slice(v + 4, saltSize).ToArray();
        byte[] encVerifier = page0.Slice(v + 4 + saltSize, 16).ToArray();
        int verifierHashSize = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(v + 4 + saltSize + 16, 4));
        bool rc4 = algId == AlgIdRc4;
        int encHashLen = rc4 ? 20 : (verifierHashSize + 15) / 16 * 16; // RC4: raw hash; AES: padded to block
        byte[] encVerifierHash = page0.Slice(v + 4 + saltSize + 16 + 4, encHashLen).ToArray();

        byte[] baseHash = SHA1.HashData(Concat(salt, Encoding.Unicode.GetBytes(password)));
        int keyLen = keyBits / 8;
        int rc4Pad = rc4 && keyBits == 40 ? 16 : 0;
        byte[] encodingKey = BitConverter.GetBytes(databaseKey);

        // RC4-CryptoAPI has no iteration; AES may be ECMA-standard (50000) or the "non-standard" variant (0).
        // The verifier tells us which — try each and keep the one that authenticates.
        foreach (int iterations in rc4 ? [0] : new[] { 0, 50000 })
        {
            var codec = new OfficeStandardEncryption(rc4, baseHash, iterations, keyLen, rc4Pad, encodingKey);
            if (codec.VerifyPassword(encVerifier, encVerifierHash, verifierHashSize))
                return codec;
        }
        throw new UnauthorizedAccessException("Incorrect database password.");
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
        int rc4Pad = !aes && keyBits == 40 ? 16 : 0;
        var codec = new OfficeStandardEncryption(!aes, baseHash, 0, keyLen, rc4Pad, BitConverter.GetBytes(databaseKey));

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

        return (BuildDescriptor(algId, aes ? 0x0Cu : 0x04u, keyBits, salt, encVerifier, encVerifierHash), codec);
    }

    // Builds the binary EncryptionInfo (version 4.2 + EncryptionHeader incl. ProviderType + CSP name +
    // EncryptionVerifier) byte-for-byte as Access writes it (verified against real files).
    private static byte[] BuildDescriptor(uint algId, uint flags, int keyBits, byte[] salt, byte[] encVerifier, byte[] encVerifierHash)
    {
        bool aes = algId != AlgIdRc4;
        uint providerType = aes ? 0x18u : 0x01u;
        string csp = aes ? "Microsoft Enhanced RSA and AES Cryptographic Provider" : "Microsoft Base Cryptographic Provider v1.0";
        byte[] cspBytes = Concat(Encoding.Unicode.GetBytes(csp), new byte[2]); // null-terminated UTF-16LE

        var b = new List<byte>();
        void U16(ushort x) => b.AddRange(BitConverter.GetBytes(x));
        void U32(uint x) => b.AddRange(BitConverter.GetBytes(x));

        int headerSize = 8 * 4 + cspBytes.Length; // Flags,SizeExtra,AlgID,AlgIDHash,KeySize,ProviderType,Reserved1,Reserved2 + CSP
        U16(4); U16(2);            // EncryptionVersionInfo 4.2
        U32(flags);                // EncryptionInfo flags
        U32((uint)headerSize);
        U32(flags); U32(0); U32(algId); U32(0x8004); U32((uint)keyBits); U32(providerType); U32(0); U32(0);
        b.AddRange(cspBytes);
        // EncryptionVerifier
        U32((uint)salt.Length); b.AddRange(salt);
        b.AddRange(encVerifier);
        U32(20); b.AddRange(encVerifierHash); // VerifierHashSize = SHA1
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
        return SHA1.HashData(verifier).AsSpan(0, hashSize).SequenceEqual(storedHash.AsSpan(0, hashSize));
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
                iterHash = SHA1.HashData(Concat(it.ToArray(), iterHash));
            }
        }
        byte[] blk = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(blk, block);
        byte[] hf = SHA1.HashData(Concat(iterHash, blk));

        byte[] key = _rc4 ? Fix(hf, _keyLen) : Fix(Concat(GenX(hf, 0x36), GenX(hf, 0x5C)), _keyLen);
        return _rc4KeyPad > 0 ? Fix(key, _rc4KeyPad) : key;
    }

    // --- helpers ---

    private static byte[] GenX(byte[] hf, byte pad)
    {
        byte[] buf = new byte[64];
        Array.Fill(buf, pad);
        for (int i = 0; i < hf.Length; i++) buf[i] ^= hf[i];
        return SHA1.HashData(buf);
    }

    private static int LocateBinaryEncryptionInfo(ReadOnlySpan<byte> page0)
    {
        // A binary EncryptionInfo begins: uint16 major, uint16 minor(=2 for standard/CryptoAPI), uint32 flags
        // (fCryptoAPI=0x04 set), uint32 headerSize. Validate against a known cipher AlgID to avoid false hits.
        for (int i = 0x100; i + 32 < page0.Length && i < 0x400; i++)
        {
            ushort major = BinaryPrimitives.ReadUInt16LittleEndian(page0.Slice(i, 2));
            ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(page0.Slice(i + 2, 2));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(page0.Slice(i + 4, 2 + 2));
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(page0.Slice(i + 8, 4));
            if (major is < 2 or > 4 || minor != 2 || (flags & 0x04) == 0 || headerSize is <= 0 or > 512)
                continue;
            uint algId = BinaryPrimitives.ReadUInt32LittleEndian(page0.Slice(i + 12 + 8, 4));
            if (algId is 0x6801 or 0x660E or 0x660F or 0x6610) // RC4, AES-128/192/256
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
