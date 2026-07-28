using LibRed.Crypto;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Legacy Jet 4 (.mdb) "Set Database Password" (password-only obfuscation): the 40-byte field at 0x42 is
/// UTF-16LE(password) XOR (int)creationDateDouble, inside the header-masked region (recipe from jackcess, the
/// inverse of its read path). Verified byte-identical to Access's own output: each fixture below is
/// <c>2002plain.mdb</c> with the named password set in Access, so re-encoding on a copy must reproduce it exactly.
/// Fixtures under <c>enctest/</c> are NOT committed (user preference); tests skip when absent.
/// </summary>
public class LegacyJetPasswordTests
{
    private const string Dir = @"D:\toolkits\efcorejetlibred\test\LibRed.Core.Tests\enctest";
    private const string Plain = "2002plain.mdb";

    public static IEnumerable<object[]> Cases =>
    [
        ["2002plainpw.mdb", "Test1"],
        ["2002plainTest2.mdb", "Test2"],
        ["2002plain -aaaa.mdb", "AAAA"],
        ["2002plain - z.mdb", "z"],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void SetJetPassword_matches_access_output(string accessFile, string password)
    {
        string plain = Path.Combine(Dir, Plain);
        string reference = Path.Combine(Dir, accessFile);
        if (!File.Exists(plain) || !File.Exists(reference)) return;

        string tmp = Path.Combine(Path.GetTempPath(), $"libred_jetpw_{Guid.NewGuid():N}.mdb");
        File.Copy(plain, tmp, overwrite: true);
        try
        {
            DatabaseEncryption.SetJetPassword(tmp, password);

            byte[] ours = File.ReadAllBytes(tmp);
            byte[] access = File.ReadAllBytes(reference);
            // The 40-byte password field at 0x42 must match Access byte-for-byte.
            Assert.Equal(access[0x42..(0x42 + 40)], ours[0x42..(0x42 + 40)]);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RemoveJetPassword_matches_plain()
    {
        string plain = Path.Combine(Dir, Plain);
        if (!File.Exists(plain)) return;

        string tmp = Path.Combine(Path.GetTempPath(), $"libred_jetpw_{Guid.NewGuid():N}.mdb");
        File.Copy(plain, tmp, overwrite: true);
        try
        {
            DatabaseEncryption.SetJetPassword(tmp, "Test1");
            DatabaseEncryption.RemoveJetPassword(tmp);

            byte[] ours = File.ReadAllBytes(tmp);
            byte[] original = File.ReadAllBytes(plain);
            Assert.Equal(original[0x42..(0x42 + 40)], ours[0x42..(0x42 + 40)]); // back to the unpassworded field
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void SetJetEncoding_roundtrips_and_stays_readable()
    {
        string plain = Path.Combine(Dir, Plain);
        if (!File.Exists(plain)) return;

        string tmp = Path.Combine(Path.GetTempPath(), $"libred_jetenc_{Guid.NewGuid():N}.mdb");
        File.Copy(plain, tmp, overwrite: true);
        try
        {
            byte[] before = File.ReadAllBytes(tmp);
            DatabaseEncryption.SetJetEncoding(tmp);
            byte[] encoded = File.ReadAllBytes(tmp);

            Assert.NotEqual(before, encoded);                                   // pages actually changed
            Assert.NotEqual(0u, BitConverter.ToUInt32(encoded, 0x3E));          // dbKey masked-nonzero on disk
            using (var db = LibRed.JetDatabase.Open(tmp, readOnly: true))       // encoded output opens via the codec
                Assert.Contains("Table1", db.Catalog.UserTables.Select(t => t.Name));

            DatabaseEncryption.RemoveJetEncoding(tmp);
            Assert.Equal(before, File.ReadAllBytes(tmp));                       // decode → byte-identical to original
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Encode_and_password_are_independent()
    {
        string plain = Path.Combine(Dir, Plain);
        string pwRef = Path.Combine(Dir, "2002plainpw.mdb"); // 2002plain + Access-set "Test1"
        if (!File.Exists(plain) || !File.Exists(pwRef)) return;

        string tmp = Path.Combine(Path.GetTempPath(), $"libred_jetboth_{Guid.NewGuid():N}.mdb");
        File.Copy(plain, tmp, overwrite: true);
        try
        {
            DatabaseEncryption.SetJetPassword(tmp, "Test1");
            DatabaseEncryption.SetJetEncoding(tmp);

            // The password field (0x42, page 0, never page-encrypted) still matches Access's password-only output.
            byte[] both = File.ReadAllBytes(tmp);
            byte[] pwOnly = File.ReadAllBytes(pwRef);
            Assert.Equal(pwOnly[0x42..(0x42 + 40)], both[0x42..(0x42 + 40)]);

            // Removing the encoding leaves the password field intact.
            DatabaseEncryption.RemoveJetEncoding(tmp);
            Assert.Equal(pwOnly[0x42..(0x42 + 40)], File.ReadAllBytes(tmp)[0x42..(0x42 + 40)]);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Reads_access_encoded_plus_password_file()
    {
        // 2002encodedpw.mdb is a real Access file that is BOTH page-encoded (nonzero 0x3E key) AND password-set.
        string enc = Path.Combine(Dir, "2002encodedpw.mdb");
        string plainpw = Path.Combine(Dir, "2002plainpw.mdb");
        if (!File.Exists(enc)) return;

        // LibRed reads Access's combined file via the stored encoding key — proves our RC4 decode matches Access.
        using (var db = LibRed.JetDatabase.Open(enc, readOnly: true))
            Assert.Contains("Table1", db.Catalog.UserTables.Select(t => t.Name));

        // The password field (page 0, never page-encrypted) is byte-identical to the password-only file.
        if (File.Exists(plainpw))
            Assert.Equal(File.ReadAllBytes(plainpw)[0x42..(0x42 + 40)], File.ReadAllBytes(enc)[0x42..(0x42 + 40)]);

        // Decoding it in place yields a valid plaintext database that still opens.
        string tmp = Path.Combine(Path.GetTempPath(), $"libred_decenc_{Guid.NewGuid():N}.mdb");
        File.Copy(enc, tmp, overwrite: true);
        try
        {
            DatabaseEncryption.RemoveJetEncoding(tmp);
            using var db = LibRed.JetDatabase.Open(tmp, readOnly: true);
            Assert.Contains("Table1", db.Catalog.UserTables.Select(t => t.Name));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void SetJetPassword_rejects_accdb()
    {
        string accdb = Path.Combine(Dir, "..", "Data", "WideTable.accdb");
        if (!File.Exists(accdb)) return;
        string tmp = Path.Combine(Path.GetTempPath(), $"libred_jetpw_{Guid.NewGuid():N}.accdb");
        File.Copy(accdb, tmp, overwrite: true);
        try { Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetJetPassword(tmp, "x")); }
        finally { File.Delete(tmp); }
    }
}
