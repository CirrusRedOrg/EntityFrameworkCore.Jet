using LibRed;
using LibRed.Crypto;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>Reading a password-encrypted ACCDB (Office Agile encryption) end-to-end.</summary>
public class AgileEncryptionTests
{
    [Fact]
    public void Opens_and_reads_encrypted_database_with_password()
    {
        using var db = JetDatabase.Open(TestDatabases.EncryptedAccdb, password: TestDatabases.EncryptedPassword);

        // Page 0 header decodes even without the password; here it confirms the file is encrypted.
        Assert.NotEqual(0, db.DefinitionPage.DatabaseKey);

        // Reading the catalog forces data-page decryption. A correctly decrypted database yields the
        // usual Access system objects.
        var msys = db.OpenTable("MSysObjects");
        int nameIdx = msys.Definition.FindColumn("Name")!.Index;
        var names = msys.Rows().Select(r => r[nameIdx]?.ToString()).ToList();

        Assert.NotEmpty(names);
        Assert.Contains("Relationships", names);
        Assert.Contains("MSysObjects", names);
    }

    [Fact]
    public void Wrong_password_is_rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            JetDatabase.Open(TestDatabases.EncryptedAccdb, password: "not the password"));
    }

    [Fact]
    public void Missing_password_on_encrypted_database_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            JetDatabase.Open(TestDatabases.EncryptedAccdb));
    }

    [Theory]
    [InlineData("spinCount=\"100000\"", "spinCount=\"100001\"")]
    [InlineData("keyBits=\"256\"", "keyBits=\"257\"")]
    [InlineData("blockSize=\"16\"", "blockSize=\"15\"")]
    public void Unsupported_agile_profile_dimensions_are_rejected_before_kdf_or_crypto(
        string original, string replacement)
    {
        byte[] page0 = MutatePage0(original, replacement);
        Assert.Throws<NotSupportedException>(() =>
            AgileEncryption.TryCreate(page0, databaseKey: 1, TestDatabases.EncryptedPassword));
    }

    [Fact]
    public void Xml_outside_the_declared_encryption_info_frame_is_not_claimed()
    {
        byte[] original = File.ReadAllBytes(TestDatabases.EncryptedAccdb)[..4096];
        int length = BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(0x299, 2));
        byte[] descriptor = original.AsSpan(0x29B, length).ToArray();
        var page0 = new byte[4096];
        descriptor.CopyTo(page0, 0x100);
        BinaryPrimitives.WriteUInt16LittleEndian(page0.AsSpan(0x299, 2), 0);

        Assert.Null(AgileEncryption.TryCreate(page0, databaseKey: 1, TestDatabases.EncryptedPassword));
    }

    private static byte[] MutatePage0(string original, string replacement)
    {
        Assert.Equal(original.Length, replacement.Length);
        byte[] page0 = File.ReadAllBytes(TestDatabases.EncryptedAccdb)[..4096];
        ReadOnlySpan<byte> find = Encoding.ASCII.GetBytes(original);
        ReadOnlySpan<byte> replace = Encoding.ASCII.GetBytes(replacement);
        int changes = 0;
        for (int offset = 0; offset <= page0.Length - find.Length;)
        {
            int relative = page0.AsSpan(offset).IndexOf(find);
            if (relative < 0) break;
            int at = offset + relative;
            replace.CopyTo(page0.AsSpan(at, replace.Length));
            changes++;
            offset = at + replace.Length;
        }
        Assert.True(changes > 0);
        return page0;
    }
}
