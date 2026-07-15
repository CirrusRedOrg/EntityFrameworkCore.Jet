using System.Buffers.Binary;
using LibRed.Crypto;
using Xunit;

namespace LibRed.Core.Tests;

// Fixture-free known-answer tests for the Office Standard/CryptoAPI codec. The salt + EncryptionVerifier blocks
// below are constants extracted from the jackcess-encrypt fixtures (db2007-oldenc = RC4-40, db-nonstandard =
// AES-256 non-standard); the password verifier depends only on salt+password, so a synthetic page-0 carrying
// just the binary EncryptionInfo descriptor exercises the whole key-derivation + verifier path without the DB.
public class OfficeStandardEncryptionTests
{
    // db2007-oldenc.accdb — RC4-40, password "Test123"
    private const uint AlgRc4 = 0x6801;
    private static readonly byte[] Rc4Salt = Convert.FromHexString("78da7d5196c71492eed3b4471a479449");
    private static readonly byte[] Rc4EncVerifier = Convert.FromHexString("27f28425cc153a8f0c296d467fc6fb30");
    private static readonly byte[] Rc4EncVerifierHash = Convert.FromHexString("4dc1ea9024bbc3bf372ef0d987974c85fa729d62"); // 20

    // db-nonstandard.accdb — AES-256 (non-standard, 0 iterations), password "password"
    private const uint AlgAes256 = 0x6610;
    private static readonly byte[] AesSalt = Convert.FromHexString("7413eec1b30f2d38a62d1160e1b00ce7");
    private static readonly byte[] AesEncVerifier = Convert.FromHexString("cad9594b612a162f52b00350e54537f8");
    private static readonly byte[] AesEncVerifierHash = Convert.FromHexString("19f94c8655326a58a04a82d61699a884acf288030d14442371fdce77c9e63bca"); // 32

    [Fact]
    public void Rc4_authenticates_correct_password() =>
        Assert.NotNull(OfficeStandardEncryption.TryCreate(BuildPage0(AlgRc4, 0x04, 40, Rc4Salt, Rc4EncVerifier, Rc4EncVerifierHash), 0x12345678, "Test123"));

    [Fact]
    public void Aes_authenticates_correct_password() =>
        Assert.NotNull(OfficeStandardEncryption.TryCreate(BuildPage0(AlgAes256, 0x0C, 256, AesSalt, AesEncVerifier, AesEncVerifierHash), 0x12345678, "password"));

    [Fact]
    public void Wrong_password_throws() =>
        Assert.Throws<UnauthorizedAccessException>(() =>
            OfficeStandardEncryption.TryCreate(BuildPage0(AlgAes256, 0x0C, 256, AesSalt, AesEncVerifier, AesEncVerifierHash), 0x12345678, "wrong"));

    [Fact]
    public void Missing_password_throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            OfficeStandardEncryption.TryCreate(BuildPage0(AlgRc4, 0x04, 40, Rc4Salt, Rc4EncVerifier, Rc4EncVerifierHash), 0x12345678, null));

    [Fact]
    public void Unencrypted_returns_null() =>
        Assert.Null(OfficeStandardEncryption.TryCreate(new byte[4096], databaseKey: 0, password: "x"));

    [Theory]
    [InlineData(true)]   // RC4-40 (symmetric stream)
    [InlineData(false)]  // AES-256 (ECB, distinct encrypt/decrypt directions)
    public void Encrypt_then_decrypt_round_trips(bool rc4)
    {
        var codec = rc4
            ? OfficeStandardEncryption.TryCreate(BuildPage0(AlgRc4, 0x04, 40, Rc4Salt, Rc4EncVerifier, Rc4EncVerifierHash), 0x12345678, "Test123")!
            : OfficeStandardEncryption.TryCreate(BuildPage0(AlgAes256, 0x0C, 256, AesSalt, AesEncVerifier, AesEncVerifierHash), 0x12345678, "password")!;

        var page = new byte[4096];
        new Random(5).NextBytes(page);
        var original = (byte[])page.Clone();
        codec.EncryptPage(4, page);
        Assert.NotEqual(original, page);
        codec.DecryptPage(4, page);
        Assert.Equal(original, page);
    }

    // Lays a binary EncryptionInfo (version 4.2) into a synthetic 4 KB page 0: [ver major/minor][flags]
    // [headerSize][EncryptionHeader: Flags,SizeExtra,AlgID,AlgIDHash,KeySize,ProviderType,Reserved1,Reserved2]
    // [EncryptionVerifier: SaltSize,Salt(16),EncryptedVerifier(16),VerifierHashSize,EncryptedVerifierHash].
    private static byte[] BuildPage0(uint algId, uint flags, int keyBits, byte[] salt, byte[] encVerifier, byte[] encVerifierHash)
    {
        var page = new byte[4096];
        int ei = 0x100;
        const int headerSize = 32;
        void U16(int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(o), v);
        void U32(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(o), v);

        U16(ei, 4); U16(ei + 2, 2);          // version 4.2
        U32(ei + 4, flags);
        U32(ei + 8, headerSize);
        int h = ei + 12;
        U32(h, flags);                        // header Flags
        U32(h + 8, algId);                    // AlgID
        U32(h + 12, 0x8004);                  // AlgIDHash = SHA1
        U32(h + 16, (uint)keyBits);           // KeySize
        int v = h + headerSize;
        U32(v, (uint)salt.Length);
        salt.CopyTo(page, v + 4);
        encVerifier.CopyTo(page, v + 4 + salt.Length);
        U32(v + 4 + salt.Length + 16, 20u);    // VerifierHashSize (SHA1)
        encVerifierHash.CopyTo(page, v + 4 + salt.Length + 16 + 4);
        return page;
    }
}
