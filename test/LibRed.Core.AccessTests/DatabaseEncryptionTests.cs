using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Crypto;
using Xunit;

namespace LibRed.Core.Tests;

[Collection(AceCollection.Name)]
public class DatabaseEncryptionTests
{
    // Resolve against the test assembly's output (where the csproj copies Data\*.accdb), not a hardcoded
    // machine path — the latter only exists on one dev box and breaks in CI (checkout is under D:\a\...).
    private static readonly string Plain = TestDatabases.WideTableAccdb;

    private static string Copy()
    {
        string p = TemporaryDatabase.CopyPath(Plain, "libred_enc_", overwrite: true);
        return p;
    }

    /// <summary>Opens the database the way every encryption operation requires — writable and exclusive, under
    /// the password it currently carries. A wrong password is rejected here rather than by the operation.</summary>
    private static JetDatabase OpenExclusive(string path, string? password = null) =>
        JetDatabase.Open(path, readOnly: false, password: password, exclusive: true);

    private static int TableRows(string path, string? password)
    {
        using var db = JetDatabase.Open(path, readOnly: true, password: password);
        return db.OpenTable("WideTable").Rows().Count();
    }

    [Theory]
    [InlineData(AccessEncryption.OfficeStandardAes)]
    [InlineData(AccessEncryption.OfficeStandardRc4)]
    [InlineData(AccessEncryption.Agile)]
    public void Set_then_read_with_password_then_remove(AccessEncryption scheme)
    {
        string path = Copy();
        try
        {
            int rows = TableRows(path, null); // readable plaintext to start

            using (JetDatabase db = OpenExclusive(path))
                DatabaseEncryption.SetPassword(db, "S3cret!", scheme);

            Assert.Equal(rows, TableRows(path, "S3cret!"));                              // opens with password
            var missing = Assert.Throws<InvalidOperationException>(() => TableRows(path, null));
            Assert.Contains("password is required", missing.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Throws<UnauthorizedAccessException>(() => TableRows(path, "wrong"));  // rejects wrong one

            using (JetDatabase db = OpenExclusive(path, "S3cret!"))
                DatabaseEncryption.RemovePassword(db);
            Assert.Equal(rows, TableRows(path, null));                                   // plaintext again
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData(40, StandardHash.Sha1)]    // the Access 2007 default
    [InlineData(40, StandardHash.Md5)]
    [InlineData(56, StandardHash.Sha1)]
    [InlineData(128, StandardHash.Sha1)]   // enhanced key length
    [InlineData(128, StandardHash.Sha256)] // enhanced hash
    [InlineData(128, StandardHash.Sha384)]
    [InlineData(120, StandardHash.Sha512)]
    public void SetPasswordRc4_with_options_roundtrips(int keyBits, StandardHash hash)
    {
        string path = Copy();
        try
        {
            int rows = TableRows(path, null);
            using (JetDatabase db = OpenExclusive(path))
                DatabaseEncryption.SetPasswordRc4(db, "S3cret!", keyBits, hash);

            Assert.Equal(rows, TableRows(path, "S3cret!"));                              // opens with password
            var missing = Assert.Throws<InvalidOperationException>(() => TableRows(path, null));
            Assert.Contains("password is required", missing.Message, StringComparison.OrdinalIgnoreCase);

            using (JetDatabase db = OpenExclusive(path, "S3cret!"))
                DatabaseEncryption.RemovePassword(db);
            Assert.Equal(rows, TableRows(path, null));                                   // plaintext again
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData(33)]   // not a multiple of 8
    [InlineData(160)]  // above 128
    [InlineData(32)]   // below 40
    public void SetPasswordRc4_rejects_invalid_key_length(int keyBits)
    {
        string path = Copy();
        try
        {
            using JetDatabase db = OpenExclusive(path);
            Assert.Throws<ArgumentOutOfRangeException>(() => DatabaseEncryption.SetPasswordRc4(db, "pw", keyBits));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Change_password_re_encrypts()
    {
        string path = Copy();
        try
        {
            int rows = TableRows(path, null);
            using (JetDatabase db = OpenExclusive(path))
                DatabaseEncryption.SetPassword(db, "old-pass", AccessEncryption.OfficeStandardAes);
            using (JetDatabase db = OpenExclusive(path, "old-pass"))
                DatabaseEncryption.ChangePassword(db, "new-pass", AccessEncryption.OfficeStandardRc4);

            Assert.Equal(rows, TableRows(path, "new-pass"));                             // new password works
            Assert.Throws<UnauthorizedAccessException>(() => TableRows(path, "old-pass")); // old one doesn't
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static OleDbConnection OpenAce(string path, string password) => AceTestDatabase.Open(path, password);

    [Fact]
    public void Change_rc4_password_can_change_key_length_and_hash()
    {
        string path = Copy();
        try
        {
            int rows = TableRows(path, null);
            using (JetDatabase db = OpenExclusive(path))
                DatabaseEncryption.SetPasswordRc4(db, "old-pass", 40, StandardHash.Sha1);
            using (JetDatabase db = OpenExclusive(path, "old-pass"))
                DatabaseEncryption.ChangePasswordRc4(db, "new-pass", 128, StandardHash.Sha512);

            Assert.Equal(rows, TableRows(path, "new-pass"));
            Assert.Throws<UnauthorizedAccessException>(() => TableRows(path, "old-pass"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData(AccessEncryption.OfficeStandardRc4)]
    [InlineData(AccessEncryption.OfficeStandardAes)]
    [InlineData(AccessEncryption.Agile)]
    public void Ace_opens_reads_and_modifies_a_libred_encrypted_database(AccessEncryption scheme)
    {
        const string password = "S3cret!";
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-ace-encrypted-");
        try
        {
            using (JetDatabase db = OpenExclusive(path))
                DatabaseEncryption.SetPassword(db, password, scheme);

            using (var connection = OpenAce(path, password))
            {
                using var count = connection.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM Shippers";
                Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar()));

                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO Shippers (ShipperID, CompanyName, Phone) " +
                    "VALUES (4, 'ACE over LibRed encryption', '(08) 5550 0004')";
                Assert.Equal(1, insert.ExecuteNonQuery());
            }

            using var reader = JetDatabase.Open(path, readOnly: true, password: password);
            var shippers = reader.OpenTable("Shippers");
            int id = shippers.Definition.FindColumn("ShipperID")!.Index;
            int company = shippers.Definition.FindColumn("CompanyName")!.Index;
            Assert.Contains(shippers.Rows(), row =>
                Convert.ToInt32(row[id]) == 4 && (string?)row[company] == "ACE over LibRed encryption");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Failed_change_validation_leaves_original_encryption_intact()
    {
        string path = Copy();
        try
        {
            int rows = TableRows(path, null);
            using (JetDatabase db = OpenExclusive(path))
                DatabaseEncryption.SetPassword(db, "old-pass", AccessEncryption.OfficeStandardAes);

            // One handle survives all four rejections: a rejected change never touches the database it holds.
            using (JetDatabase db = OpenExclusive(path, "old-pass"))
            {
                Assert.Throws<ArgumentException>(() =>
                    DatabaseEncryption.ChangePassword(db, "", AccessEncryption.OfficeStandardRc4));
                Assert.Throws<ArgumentException>(() =>
                    DatabaseEncryption.ChangePassword(db, "new-pass", AccessEncryption.None));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    DatabaseEncryption.ChangePasswordRc4(db, "new-pass", 33));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    DatabaseEncryption.ChangePasswordRc4(db, "new-pass", 40, (StandardHash)999));
            }

            Assert.Equal(rows, TableRows(path, "old-pass"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Rejected_operations_leave_the_file_byte_identical()
    {
        string path = Copy();
        try
        {
            byte[] plaintext = File.ReadAllBytes(path);
            using (JetDatabase db = OpenExclusive(path))
                Assert.Throws<ArgumentException>(() =>
                    DatabaseEncryption.SetPassword(db, "pw", AccessEncryption.None));
            Assert.Equal(plaintext, File.ReadAllBytes(path));

            using (JetDatabase db = OpenExclusive(path))
                DatabaseEncryption.SetPassword(db, "old-pass", AccessEncryption.OfficeStandardAes);
            byte[] encrypted = File.ReadAllBytes(path);

            using (JetDatabase db = OpenExclusive(path, "old-pass"))
            {
                Assert.Throws<InvalidOperationException>(() =>
                    DatabaseEncryption.SetPassword(db, "other", AccessEncryption.OfficeStandardRc4));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    DatabaseEncryption.ChangePasswordRc4(db, "new-pass", 40, (StandardHash)(-1)));
            }
            Assert.Equal(encrypted, File.ReadAllBytes(path));

            // A wrong password no longer reaches an operation at all: it fails the open it would have to pass.
            Assert.Throws<UnauthorizedAccessException>(() => OpenExclusive(path, "wrong"));
            Assert.Equal(encrypted, File.ReadAllBytes(path));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_shared_open_is_refused_and_the_database_is_left_alone()
    {
        string path = Copy();
        try
        {
            byte[] plaintext = File.ReadAllBytes(path);
            using (var shared = JetDatabase.Open(path, readOnly: false))
            {
                var refused = Assert.Throws<InvalidOperationException>(
                    () => DatabaseEncryption.SetPassword(shared, "pw", AccessEncryption.Agile));
                Assert.Contains("exclusively", refused.Message, StringComparison.Ordinal);
            }
            Assert.Equal(plaintext, File.ReadAllBytes(path));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4095)]
    [InlineData(ushort.MaxValue)]
    public void Malformed_encryption_info_length_is_rejected_without_writing(int descriptorLength)
    {
        string path = Copy();
        try
        {
            using (JetDatabase db = OpenExclusive(path))
                DatabaseEncryption.SetPassword(db, "pw", AccessEncryption.OfficeStandardAes);
            byte[] malformed = File.ReadAllBytes(path);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                malformed.AsSpan(TestDatabases.FormatOf(Plain).EncryptionInfoLengthOffset, 2), checked((ushort)descriptorLength));
            File.WriteAllBytes(path, malformed);

            Exception? error = Record.Exception(() =>
            {
                using var _ = JetDatabase.Open(path, readOnly: true, password: "pw");
            });

            Assert.NotNull(error);
            Assert.True(error is InvalidDataException or InvalidOperationException or NotSupportedException or ArgumentException,
                $"Unexpected exception type: {error.GetType().FullName}");
            Assert.Equal(malformed, File.ReadAllBytes(path));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Set_on_already_encrypted_throws()
    {
        string path = Copy();
        try
        {
            using (JetDatabase db = OpenExclusive(path))
                DatabaseEncryption.SetPassword(db, "pw", AccessEncryption.OfficeStandardAes);

            using JetDatabase encrypted = OpenExclusive(path, "pw");
            Assert.Throws<InvalidOperationException>(() =>
                DatabaseEncryption.SetPassword(encrypted, "pw2", AccessEncryption.OfficeStandardAes));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Remove_on_plaintext_throws()
    {
        string path = Copy();
        try
        {
            using JetDatabase db = OpenExclusive(path);
            Assert.Throws<InvalidOperationException>(() => DatabaseEncryption.RemovePassword(db));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Invalid_schemes_are_rejected()
    {
        string path = Copy();
        try
        {
            using JetDatabase db = OpenExclusive(path);
            // LegacyJet on an .accdb is a format mismatch; None is not a set-scheme.
            Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetPassword(db, "pw", AccessEncryption.LegacyJet));
            Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetPassword(db, "pw", AccessEncryption.None));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}