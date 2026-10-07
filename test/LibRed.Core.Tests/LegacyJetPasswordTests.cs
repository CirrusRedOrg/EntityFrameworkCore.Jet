using LibRed.Storage;
using LibRed.Crypto;
using LibRed.Pages;
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
        JetDatabase.Create(path, version: 0x01);
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
            Assert.Equal(StoredPasswordField(reference), StoredPasswordField(tmp));
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    [Fact]
    public void RemoveJetPassword_matches_plain()
    {
        string tmp = CreateJet4();
        try
        {
            byte[] original = StoredPasswordField(tmp);
            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetJetPassword(db, "Test1");
            Assert.NotEqual(original, StoredPasswordField(tmp));

            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.RemoveJetPassword(db);

            Assert.Equal(original, StoredPasswordField(tmp)); // back to the unpassworded field
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    // The password field is half of what the SID keystream is folded from (page-00 §2.3), so setting a password
    // must re-mask every stored SID to the new keystream — the system tables' owner still has to be Engine under
    // it — and removing the password must give back the very bytes the file started with.
    [Fact]
    public void A_password_change_remasks_every_stored_sid()
    {
        string tmp = CreateJet4();
        try
        {
            List<byte[]> original = StoredSids(tmp, null);

            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetJetPassword(db, "Test1");

            byte[] page0 = Page0(tmp);
            List<byte[]> masked = StoredSids(tmp, "Test1");
            Assert.Equal(original.Count, masked.Count);
            Assert.NotEqual(original[0], masked[0]);
            using (var db = JetDatabase.Open(tmp, password: "Test1"))
                Assert.Equal(SidKeystream.MaskAccount(page0, SidKeystream.EngineAccount, db.Format), OwnerOf(db, "MSysObjects"));

            using (JetDatabase db = OpenExclusive(tmp, "Test1"))
                DatabaseEncryption.RemoveJetPassword(db);

            Assert.Equal(original, StoredSids(tmp, null));
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    // An .accdb's whole-file encryption writes the same 0x42 field, as Access does: 40 copies of the low byte of the
    // new database key at 0x3E (zero once the password is removed). That moves the SID keystream just as a Jet
    // password does, so every set, change and removal re-masks the SIDs, and removal gives back the original bytes.
    [Fact]
    public void An_accdb_password_change_writes_the_key_field_and_remasks_every_stored_sid()
    {
        string tmp = TemporaryDatabase.CreatePath("libred_accdbpw_", ".accdb");
        JetDatabase.Create(tmp);
        try
        {
            byte[] originalField = StoredPasswordField(tmp);
            List<byte[]> original = StoredSids(tmp, null);

            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetPassword(db, "Test1", AccessEncryption.Agile);
            AssertKeyFieldAndEngineOwner(tmp, "Test1");

            using (JetDatabase db = OpenExclusive(tmp, "Test1"))
                DatabaseEncryption.ChangePassword(db, "Ab", AccessEncryption.OfficeStandardAes);
            AssertKeyFieldAndEngineOwner(tmp, "Ab");

            using (JetDatabase db = OpenExclusive(tmp, "Ab"))
                DatabaseEncryption.RemovePassword(db);
            Assert.Equal(originalField, StoredPasswordField(tmp));
            Assert.Equal(original, StoredSids(tmp, null));
        }
        finally { TemporaryDatabase.Delete(tmp); }
    }

    private static void AssertKeyFieldAndEngineOwner(string path, string password)
    {
        Formats.JetFormatBase format = TestDatabases.FormatOf(path);
        byte[] page0 = Page0(path);
        byte[] creationDate = new byte[sizeof(double)];
        DatabaseDefinitionPage.ReadMasked(page0, format.CreationDateOffset, creationDate, format);
        byte[] field = new byte[format.PasswordSize];
        DatabaseDefinitionPage.ReadMasked(page0, format.PasswordOffset, field, format);
        DatabaseDefinitionPage.XorPasswordDateMask(field, creationDate);
        byte keyLowByte = (byte)DatabaseDefinitionPage.ReadDatabaseKey(page0, format);
        Assert.All(field, b => Assert.Equal(keyLowByte, b));

        using var db = JetDatabase.Open(path, password: password);
        Assert.Equal(SidKeystream.MaskAccount(page0, SidKeystream.EngineAccount, format), OwnerOf(db, "MSysObjects"));
    }

    /// <summary>Page 0 of the file at <paramref name="path"/>, as stored.</summary>
    private static byte[] Page0(string path) => File.ReadAllBytes(path)[..TestDatabases.FormatOf(path).PageSize];

    /// <summary>The password field (<c>0x42</c>, 40 bytes) of the file at <paramref name="path"/>, as stored.</summary>
    private static byte[] StoredPasswordField(string path)
    {
        Formats.JetFormatBase format = TestDatabases.FormatOf(path);
        return File.ReadAllBytes(path)[format.PasswordOffset..(format.PasswordOffset + format.PasswordSize)];
    }

    private static JetDatabase OpenExclusive(string path, string password) =>
        JetDatabase.Open(path, readOnly: false, password: password, exclusive: true);

    /// <summary>Every MSysObjects.Owner and MSysACEs.SID, in stored order.</summary>
    private static List<byte[]> StoredSids(string path, string? password)
    {
        using var db = JetDatabase.Open(path, password: password);
        var sids = new List<byte[]>();
        foreach ((string table, string column) in new[] { ("MSysObjects", "Owner"), ("MSysACEs", "SID") })
        {
            var t = db.OpenTable(table);
            int index = t.Definition.RequireColumn(column).Index;
            foreach (object?[] row in t.Rows())
                if (row[index] is byte[] sid) sids.Add(sid);
        }
        return sids;
    }

    private static byte[] OwnerOf(JetDatabase db, string name)
    {
        var objects = db.OpenTable("MSysObjects");
        int nameIndex = objects.Definition.RequireColumn("Name").Index, owner = objects.Definition.RequireColumn("Owner").Index;
        return (byte[])objects.Rows().First(r => r[nameIndex] as string == name)[owner]!;
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
            Assert.NotEqual(0u, BitConverter.ToUInt32(encoded, TestDatabases.FormatOf(tmp).DatabaseKeyOffset)); // dbKey masked-nonzero on disk

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
            byte[] passwordField = StoredPasswordField(tmp);

            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.SetJetEncoding(db);

            // The password field is on page 0, which page encoding must never transform.
            Assert.Equal(passwordField, StoredPasswordField(tmp));

            // Removing the encoding leaves the password field intact.
            using (JetDatabase db = OpenExclusive(tmp))
                DatabaseEncryption.RemoveJetEncoding(db);
            Assert.Equal(passwordField, StoredPasswordField(tmp));
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
            Formats.JetFormatBase format = TestDatabases.FormatOf(tmp);
            Assert.NotEqual(withMaximumPassword[format.PasswordOffset..(format.PasswordOffset + format.PasswordSize)],
                StoredPasswordField(tmp));
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