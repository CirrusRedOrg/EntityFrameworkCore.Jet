using LibRed.Storage;
using LibRed.Crypto;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Legacy Jet 4 (.mdb) "Set Database Password" (password-only obfuscation): the 40-byte field at 0x42 is
/// UTF-16LE(password) XOR (int)creationDateDouble, inside the header-masked region (recipe from jackcess, the
/// inverse of its read path). Verified byte-identical to Access's own output: each fixture below is
/// <c>2002plain.mdb</c> with the named password set in Access, so re-encoding on a copy must reproduce it exactly.
/// The password and encoding mechanics run on every platform against a Jet 4 database LibRed creates itself.
/// </summary>
public class LegacyJetPasswordTests
{
    /// <summary>A real Access 2000 (<c>.mdb</c>) database — the four core system tables at the pages page 0's
    /// bootstrap pointers name — so every operation below runs against a file that opens.</summary>
    private static string CreateJet4()
    {
        string path = TemporaryDatabase.CreatePath("libred_jet4_", ".mdb");
        DatabaseCreator.CreateEmpty(path, version: 0x01);
        return path;
    }

    /// <summary>Opens the database the way every operation here requires — writable and exclusive.</summary>
    private static JetDatabase OpenExclusive(string path) =>
        JetDatabase.Open(path, readOnly: false, exclusive: true);

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
    // tests below run everywhere; this one is the only thing that can catch the transform drifting away from
    // Access, so keep it runnable rather than deleting it with the fixtures unavailable.
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
            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetJetPassword(db, password);

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
        string tmp = CreateJet4();
        try
        {
            byte[] original = File.ReadAllBytes(tmp);
            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetJetPassword(db, "Test1");
            Assert.NotEqual(original[0x42..(0x42 + 40)], File.ReadAllBytes(tmp)[0x42..(0x42 + 40)]);

            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.RemoveJetPassword(db);

            byte[] ours = File.ReadAllBytes(tmp);
            Assert.Equal(original[0x42..(0x42 + 40)], ours[0x42..(0x42 + 40)]); // back to the unpassworded field
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void SetJetEncoding_roundtrips_and_stays_readable()
    {
        string tmp = CreateJet4();
        try
        {
            byte[] before = File.ReadAllBytes(tmp);
            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetJetEncoding(db);
            byte[] encoded = File.ReadAllBytes(tmp);

            Assert.NotEqual(before, encoded);                                   // pages actually changed
            Assert.NotEqual(0u, BitConverter.ToUInt32(encoded, 0x3E));          // dbKey masked-nonzero on disk

            // Encoded is still a database: the key at 0x3E is all a reader needs, so it opens without a password.
            using (var db = JetDatabase.Open(tmp))
                Assert.Contains(db.Catalog.Tables, t => t.Name == "MSysObjects");

            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.RemoveJetEncoding(db);
            Assert.Equal(before, File.ReadAllBytes(tmp));                       // decode → byte-identical to original
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void Encode_and_password_are_independent()
    {
        string tmp = CreateJet4();
        try
        {
            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetJetPassword(db, "Test1");
            byte[] passwordField = File.ReadAllBytes(tmp)[0x42..(0x42 + 40)];

            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetJetEncoding(db);

            // The password field is on page 0, which page encoding must never transform.
            byte[] both = File.ReadAllBytes(tmp);
            Assert.Equal(passwordField, both[0x42..(0x42 + 40)]);

            // Removing the encoding leaves the password field intact.
            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.RemoveJetEncoding(db);
            Assert.Equal(passwordField, File.ReadAllBytes(tmp)[0x42..(0x42 + 40)]);
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void SetJetPassword_rejects_accdb()
    {
        string tmp = TemporaryDatabase.CopyPath(TestDatabases.WideTableAccdb, "libred_jetpw_", overwrite: true);
        try
        {
            using JetDatabase db = OpenExclusive(tmp);
            Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetJetPassword(db, "x"));
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void Jet_password_accepts_twenty_characters_and_rejects_longer_or_empty_without_writing()
    {
        string tmp = CreateJet4();
        try
        {
            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetJetPassword(db, new string('x', 20));
            byte[] withMaximumPassword = File.ReadAllBytes(tmp);

            using (JetDatabase db = OpenExclusive(tmp))
            {
                Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetJetPassword(db, new string('y', 21)));
                Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetJetPassword(db, ""));
            }
            Assert.Equal(withMaximumPassword, File.ReadAllBytes(tmp));

            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.RemoveJetPassword(db);
            Assert.NotEqual(withMaximumPassword[0x42..(0x42 + 40)], File.ReadAllBytes(tmp)[0x42..(0x42 + 40)]);
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void Rejected_jet_encoding_operations_leave_the_file_byte_identical()
    {
        string tmp = CreateJet4();
        try
        {
            byte[] plain = File.ReadAllBytes(tmp);
            using (JetDatabase db = OpenExclusive(tmp))
                Assert.Throws<InvalidOperationException>(() => DatabaseEncryption.RemoveJetEncoding(db));
            Assert.Equal(plain, File.ReadAllBytes(tmp));

            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetJetEncoding(db);
            byte[] encoded = File.ReadAllBytes(tmp);

            using (JetDatabase db = OpenExclusive(tmp))
                Assert.Throws<InvalidOperationException>(() => DatabaseEncryption.SetJetEncoding(db));
            Assert.Equal(encoded, File.ReadAllBytes(tmp));
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void Jet_encoding_rejects_an_accdb_without_writing()
    {
        string accdb = TemporaryDatabase.CopyPath(TestDatabases.WideTableAccdb, "libred_jetenc_mismatch_");
        try
        {
            byte[] before = File.ReadAllBytes(accdb);
            using (JetDatabase db = OpenExclusive(accdb))
                Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetJetEncoding(db));
            Assert.Equal(before, File.ReadAllBytes(accdb));
        }
        finally { TemporaryDatabase.Delete(accdb); }
    }

    [Fact]
    public void A_shared_open_is_refused_and_the_database_is_left_alone()
    {
        string tmp = CreateJet4();
        try
        {
            byte[] before = File.ReadAllBytes(tmp);
            using (var shared = JetDatabase.Open(tmp, readOnly: false))
            {
                var refused = Assert.Throws<InvalidOperationException>(
                    () => DatabaseEncryption.SetJetEncoding(shared));
                Assert.Contains("exclusively", refused.Message, StringComparison.Ordinal);
            }
            Assert.Equal(before, File.ReadAllBytes(tmp));
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }
}
