using LibRed.Storage;
using LibRed.Crypto;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Legacy Jet 4 (.mdb) "Set Database Password" (password-only obfuscation): the 40-byte field at 0x42 is
/// UTF-16LE(password) XOR (int)creationDateDouble, inside the header-masked region (recipe from jackcess, the
/// inverse of its read path). Verified byte-identical to Access's own output: each fixture below is
/// <c>2002plain.mdb</c> with the named password set in Access, so re-encoding on a copy must reproduce it exactly.
/// The password and encoding mechanics use generated Jet 4 inputs and run on every platform.
/// </summary>
public class LegacyJetPasswordTests
{
    private static string CreateSyntheticJet4()
    {
        string path = TemporaryDatabase.CreatePath("libred_jet4_", ".mdb");
        byte[] file = new byte[4096 * 3];
        DatabaseCreator.BuildDefinitionPage(
            version: 0x01, isAccdb: false, codePage: 1252, collation: LibRed.Catalog.Collation.GeneralLegacy,
            creationDays: 45000.25).CopyTo(file, 0);
        new Random(1701).NextBytes(file.AsSpan(4096));
        File.WriteAllBytes(path, file);
        return path;
    }

    /// <summary>The Access-output fixtures: <c>2002plain.mdb</c> plus copies of it with each password set by
    /// Access itself. They are deliberately not committed, so this is located by convention (or the
    /// <c>LIBRED_ENCTEST_DIR</c> environment variable) and <b>skips with a reason</b> when absent — a silent
    /// early `return` would report a pass for a test that never ran.</summary>
    private static string? FixtureDirectory
    {
        get
        {
            string directory = Environment.GetEnvironmentVariable("LIBRED_ENCTEST_DIR")
                ?? Path.Combine(AppContext.BaseDirectory, "enctest");
            return Directory.Exists(directory) ? directory : null;
        }
    }

    // The ground truth behind the whole codec: byte-identity with what Access itself writes. The mechanics
    // tests below are synthetic and run everywhere; this one is the only thing that can catch the transform
    // drifting away from Access, so keep it runnable rather than deleting it with the fixtures unavailable.
    [Theory]
    [InlineData("2002plainpw.mdb", "Test1")]
    [InlineData("2002plainTest2.mdb", "Test2")]
    [InlineData("2002plain -aaaa.mdb", "AAAA")]
    [InlineData("2002plain - z.mdb", "z")]
    public void SetJetPassword_matches_access_output(string accessFile, string password)
    {
        string? directory = FixtureDirectory;
        Assert.SkipWhen(directory is null,
            "Access-set .mdb fixtures are not present; set LIBRED_ENCTEST_DIR to run this.");

        string plain = Path.Combine(directory!, "2002plain.mdb");
        string reference = Path.Combine(directory!, accessFile);
        Assert.SkipUnless(File.Exists(plain) && File.Exists(reference),
            $"'{accessFile}' or its 2002plain.mdb base is missing from {directory}.");

        string tmp = TemporaryDatabase.CopyPath(plain, "libred_jetpw_", overwrite: true);
        try
        {
            DatabaseEncryption.SetJetPassword(tmp, password);

            // The 40-byte password field at 0x42 must match Access byte-for-byte.
            Assert.Equal(
                File.ReadAllBytes(reference)[0x42..(0x42 + 40)],
                File.ReadAllBytes(tmp)[0x42..(0x42 + 40)]);
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void RemoveJetPassword_matches_plain()
    {
        string tmp = CreateSyntheticJet4();
        try
        {
            byte[] original = File.ReadAllBytes(tmp);
            DatabaseEncryption.SetJetPassword(tmp, "Test1");
            Assert.NotEqual(original[0x42..(0x42 + 40)], File.ReadAllBytes(tmp)[0x42..(0x42 + 40)]);
            DatabaseEncryption.RemoveJetPassword(tmp);

            byte[] ours = File.ReadAllBytes(tmp);
            Assert.Equal(original[0x42..(0x42 + 40)], ours[0x42..(0x42 + 40)]); // back to the unpassworded field
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void SetJetEncoding_roundtrips_and_stays_readable()
    {
        string tmp = CreateSyntheticJet4();
        try
        {
            byte[] before = File.ReadAllBytes(tmp);
            DatabaseEncryption.SetJetEncoding(tmp);
            byte[] encoded = File.ReadAllBytes(tmp);

            Assert.NotEqual(before, encoded);                                   // pages actually changed
            Assert.NotEqual(0u, BitConverter.ToUInt32(encoded, 0x3E));          // dbKey masked-nonzero on disk
            DatabaseEncryption.RemoveJetEncoding(tmp);
            Assert.Equal(before, File.ReadAllBytes(tmp));                       // decode → byte-identical to original
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void Encode_and_password_are_independent()
    {
        string tmp = CreateSyntheticJet4();
        try
        {
            DatabaseEncryption.SetJetPassword(tmp, "Test1");
            byte[] passwordField = File.ReadAllBytes(tmp)[0x42..(0x42 + 40)];
            DatabaseEncryption.SetJetEncoding(tmp);

            // The password field is on page 0, which page encoding must never transform.
            byte[] both = File.ReadAllBytes(tmp);
            Assert.Equal(passwordField, both[0x42..(0x42 + 40)]);

            // Removing the encoding leaves the password field intact.
            DatabaseEncryption.RemoveJetEncoding(tmp);
            Assert.Equal(passwordField, File.ReadAllBytes(tmp)[0x42..(0x42 + 40)]);
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void SetJetPassword_rejects_accdb()
    {
        string tmp = TemporaryDatabase.CopyPath(TestDatabases.WideTableAccdb, "libred_jetpw_", overwrite: true);
        try { Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetJetPassword(tmp, "x")); }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void Jet_password_accepts_twenty_characters_and_rejects_longer_or_empty_without_writing()
    {
        string tmp = CreateSyntheticJet4();
        try
        {
            DatabaseEncryption.SetJetPassword(tmp, new string('x', 20));
            byte[] withMaximumPassword = File.ReadAllBytes(tmp);

            Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetJetPassword(tmp, new string('y', 21)));
            Assert.Equal(withMaximumPassword, File.ReadAllBytes(tmp));

            Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetJetPassword(tmp, ""));
            Assert.Equal(withMaximumPassword, File.ReadAllBytes(tmp));

            DatabaseEncryption.RemoveJetPassword(tmp);
            Assert.NotEqual(withMaximumPassword[0x42..(0x42 + 40)], File.ReadAllBytes(tmp)[0x42..(0x42 + 40)]);
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void Rejected_jet_encoding_operations_leave_the_file_byte_identical()
    {
        string tmp = CreateSyntheticJet4();
        try
        {
            byte[] plain = File.ReadAllBytes(tmp);
            Assert.Throws<InvalidOperationException>(() => DatabaseEncryption.RemoveJetEncoding(tmp));
            Assert.Equal(plain, File.ReadAllBytes(tmp));

            DatabaseEncryption.SetJetEncoding(tmp);
            byte[] encoded = File.ReadAllBytes(tmp);
            Assert.Throws<InvalidOperationException>(() => DatabaseEncryption.SetJetEncoding(tmp));
            Assert.Equal(encoded, File.ReadAllBytes(tmp));
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void Jet_encoding_rejects_accdb_and_jet3_without_writing()
    {
        string accdb = TemporaryDatabase.CopyPath(TestDatabases.WideTableAccdb, "libred_jetenc_mismatch_");
        string jet3 = CreateSyntheticJet4();
        try
        {
            byte[] accdbBefore = File.ReadAllBytes(accdb);
            Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetJetEncoding(accdb));
            Assert.Equal(accdbBefore, File.ReadAllBytes(accdb));

            byte[] jet3Bytes = File.ReadAllBytes(jet3);
            jet3Bytes[0x14] = 0;
            File.WriteAllBytes(jet3, jet3Bytes);
            Assert.Throws<NotSupportedException>(() => DatabaseEncryption.SetJetEncoding(jet3));
            Assert.Equal(jet3Bytes, File.ReadAllBytes(jet3));
        }
        finally
        {
            TemporaryDatabase.Delete(accdb);
            TemporaryDatabase.Delete(jet3);
        }
    }
}
