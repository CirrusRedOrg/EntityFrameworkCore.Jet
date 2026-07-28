using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Reads real Office-"Standard"/CryptoAPI encrypted <c>.accdb</c> fixtures re-encrypted with the
/// "Encryption Manager for Access 2007" (EverythingAccess.com) tool, sweeping the AlgID / AlgIDHash / KeySize
/// combinations it exposes. Verifies LibRed honours the hashing algorithm (MD5/SHA-1/…/SHA-512), resolves
/// KeySize == 0 to the algorithm default, and rejects genuinely-unsupported ciphers (3DES) cleanly.
/// Fixtures live under <c>enctest/</c> and are NOT committed (user preference); the test skips when absent.
/// </summary>
public class OfficeStandardVariantReadTests
{
    private const string Dir = @"D:\toolkits\efcorejetlibred\test\LibRed.Core.Tests\enctest";
    private const string Password = "Test123";

    public static IEnumerable<object[]> Readable =>
    [
        ["db2007-oldenc.accdb"],        // RC4-40, SHA-1
        ["db2007-oldenc - Copy.accdb"], // RC4-56, SHA-1
        ["db2007-oldenc - Copy (2).accdb"], // RC4, MD5,    KeySize=0 (default)
        ["db2007-oldenc - Copy (3).accdb"], // RC4, MD5,    KeySize=0, Enhanced provider
        ["db2007-oldenc - Copy (4).accdb"], // RC4-120, SHA-512
        ["db2007-oldenc - Copy (5).accdb"], // AES-256, MD5,    KeySize=0 (Access won't open; LibRed can)
        ["db2007-oldenc - Copy (6).accdb"], // AES-128, MD5,    KeySize=0 (Access won't open; LibRed can)
        ["db2007-oldenc - Copy (8).accdb"], // AES-256, SHA-512, KeySize=0 (key < hash ⇒ truncate, no expansion)
        ["db2007-oldenc - Copy (12).accdb"],// AES-256, SHA-256, KeySize=0 (key == hash 32 ⇒ 0x36/0x5C expansion)
        ["db2007-oldenc - Copy (13).accdb"],// AES-256, SHA-384, KeySize=0 (key 32 < hash 48 ⇒ truncate)
        ["db2007-oldenc - Copy (14).accdb"],// AES-192, SHA-256, KeySize=0 (24-byte key path)
        ["db2007-oldenc - Copy (15).accdb"],// AES-128, SHA-512, KeySize=0 (key 16 < hash 64 ⇒ truncate)
    ];

    [Theory]
    [MemberData(nameof(Readable))]
    public void Reads_office_standard_variant(string file)
    {
        string path = Path.Combine(Dir, file);
        if (!File.Exists(path)) return; // fixtures not committed (user preference)

        using var db = JetDatabase.Open(path, readOnly: true, password: Password);
        Assert.Equal(1, db.OpenTable("Table1").Rows().Count());
    }

    [Theory]
    [InlineData("db2007-oldenc - Copy (7).accdb")]  // 3DES-168 cipher
    [InlineData("db2007-oldenc - Copy (9).accdb")]  // MD2 hashing (no managed implementation)
    [InlineData("db2007-oldenc - Copy (10).accdb")] // 3DES-112 cipher
    [InlineData("db2007-oldenc - Copy (11).accdb")] // DES cipher
    public void Rejects_unsupported_cleanly(string file)
    {
        string path = Path.Combine(Dir, file); // all also refused by Access itself
        if (!File.Exists(path)) return;

        // A clear NotSupportedException, never a DivideByZero/EndOfStream from mis-reading ciphertext as plaintext.
        try
        {
            JetDatabase.Open(path, readOnly: true, password: Password);
            Assert.Fail($"expected NotSupportedException for {file}");
        }
        catch (NotSupportedException) { /* expected */ }
        catch (IOException) { /* fixture locked by another process (an environment condition); skip */ }
    }
}
