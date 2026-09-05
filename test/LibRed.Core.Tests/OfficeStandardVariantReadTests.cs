using System.Buffers.Binary;
using LibRed;
using LibRed.Crypto;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>Fixture-free rejection coverage for unsupported Office-Standard descriptor variants.</summary>
public class OfficeStandardVariantReadTests
{
    private const int DescriptorOffset = 0x29B;
    private const int AlgorithmOffset = DescriptorOffset + 12 + 8;
    private const int HashOffset = DescriptorOffset + 12 + 12;

    [Theory]
    [InlineData(0x6603u)] // 3DES-168
    [InlineData(0x6609u)] // 3DES-112
    [InlineData(0x6601u)] // DES
    public void Unsupported_cipher_is_rejected_cleanly(uint algorithm)
    {
        string path = CreateEncryptedCopy();
        try
        {
            MutateUInt32(path, AlgorithmOffset, algorithm);
            Assert.Throws<NotSupportedException>(() => JetDatabase.Open(path, readOnly: true, password: "Test123"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData(0x8001u)] // MD2
    [InlineData(0x8002u)] // MD4
    [InlineData(0xDEADu)] // unknown
    public void Unsupported_hash_is_rejected_cleanly(uint hashAlgorithm)
    {
        string path = CreateEncryptedCopy();
        try
        {
            MutateUInt32(path, HashOffset, hashAlgorithm);
            Assert.Throws<NotSupportedException>(() => JetDatabase.Open(path, readOnly: true, password: "Test123"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The descriptor length at 0x299 is authoritative, not a hint: Access reads a file whose key and
    // descriptor are both present but whose length is zero as *plaintext*, and offers to "recover" it
    // (verified experiment, page-00-database.md). A reader that instead scans page 0 for the EncryptionInfo
    // signature would decrypt this file happily — succeeding where ACE cannot, which is treating corruption
    // as valid. LibRed reaches the same verdict as ACE (this is not a readable encrypted database) but
    // reports it rather than surfacing ciphertext as data: the 0x3E key says "encrypted", no descriptor is
    // readable within the declared frame, so the scheme is unsupported. The password being correct is
    // deliberate — it must not rescue the file.
    [Fact]
    public void A_zero_length_descriptor_is_read_as_unencrypted_like_ace()
    {
        string path = CreateEncryptedCopy();
        try
        {
            MutateUInt16(path, 0x299, 0);
            var error = Assert.Throws<NotSupportedException>(
                () => JetDatabase.Open(path, readOnly: true, password: "Test123"));
            Assert.Contains("unsupported scheme", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string CreateEncryptedCopy()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.WideTableAccdb, "office-standard-variant-");
        DatabaseEncryption.SetPasswordRc4(path, "Test123");
        return path;
    }

    private static void MutateUInt32(string path, int offset, uint value)
    {
        byte[] file = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset, 4), value);
        File.WriteAllBytes(path, file);
    }

    private static void MutateUInt16(string path, int offset, ushort value)
    {
        byte[] file = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset, 2), value);
        File.WriteAllBytes(path, file);
    }
}
