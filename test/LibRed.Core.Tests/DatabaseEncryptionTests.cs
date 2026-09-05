using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Crypto;
using Xunit;

namespace LibRed.Core.Tests;

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

            DatabaseEncryption.SetPassword(path, "S3cret!", scheme);

            Assert.Equal(rows, TableRows(path, "S3cret!"));                              // opens with password
            var missing = Assert.Throws<InvalidOperationException>(() => TableRows(path, null));
            Assert.Contains("password is required", missing.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Throws<UnauthorizedAccessException>(() => TableRows(path, "wrong"));  // rejects wrong one

            DatabaseEncryption.RemovePassword(path, "S3cret!");
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
            DatabaseEncryption.SetPasswordRc4(path, "S3cret!", keyBits, hash);

            Assert.Equal(rows, TableRows(path, "S3cret!"));                              // opens with password
            var missing = Assert.Throws<InvalidOperationException>(() => TableRows(path, null));
            Assert.Contains("password is required", missing.Message, StringComparison.OrdinalIgnoreCase);

            DatabaseEncryption.RemovePassword(path, "S3cret!");
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
        try { Assert.Throws<ArgumentOutOfRangeException>(() => DatabaseEncryption.SetPasswordRc4(path, "pw", keyBits)); }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Change_password_re_encrypts()
    {
        string path = Copy();
        try
        {
            int rows = TableRows(path, null);
            DatabaseEncryption.SetPassword(path, "old-pass", AccessEncryption.OfficeStandardAes);
            DatabaseEncryption.ChangePassword(path, "old-pass", "new-pass", AccessEncryption.OfficeStandardRc4);

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
            DatabaseEncryption.SetPasswordRc4(path, "old-pass", 40, StandardHash.Sha1);
            DatabaseEncryption.ChangePasswordRc4(path, "old-pass", "new-pass", 128, StandardHash.Sha512);

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
            DatabaseEncryption.SetPassword(path, password, scheme);

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

            using var db = JetDatabase.Open(path, readOnly: true, password: password);
            var shippers = db.OpenTable("Shippers");
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
            DatabaseEncryption.SetPassword(path, "old-pass", AccessEncryption.OfficeStandardAes);

            Assert.Throws<ArgumentException>(() =>
                DatabaseEncryption.ChangePassword(path, "old-pass", "", AccessEncryption.OfficeStandardRc4));
            Assert.Equal(rows, TableRows(path, "old-pass"));

            Assert.Throws<ArgumentException>(() =>
                DatabaseEncryption.ChangePassword(path, "old-pass", "new-pass", AccessEncryption.None));
            Assert.Equal(rows, TableRows(path, "old-pass"));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DatabaseEncryption.ChangePasswordRc4(path, "old-pass", "new-pass", 33));
            Assert.Equal(rows, TableRows(path, "old-pass"));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DatabaseEncryption.ChangePasswordRc4(path, "old-pass", "new-pass", 40, (StandardHash)999));
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
            Assert.Throws<ArgumentException>(() =>
                DatabaseEncryption.SetPassword(path, "pw", AccessEncryption.None));
            Assert.Equal(plaintext, File.ReadAllBytes(path));

            DatabaseEncryption.SetPassword(path, "old-pass", AccessEncryption.OfficeStandardAes);
            byte[] encrypted = File.ReadAllBytes(path);

            Assert.Throws<InvalidOperationException>(() =>
                DatabaseEncryption.SetPassword(path, "other", AccessEncryption.OfficeStandardRc4));
            Assert.Equal(encrypted, File.ReadAllBytes(path));

            Assert.Throws<UnauthorizedAccessException>(() =>
                DatabaseEncryption.RemovePassword(path, "wrong"));
            Assert.Equal(encrypted, File.ReadAllBytes(path));

            Assert.Throws<UnauthorizedAccessException>(() =>
                DatabaseEncryption.ChangePassword(path, "wrong", "new-pass", AccessEncryption.Agile));
            Assert.Equal(encrypted, File.ReadAllBytes(path));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DatabaseEncryption.ChangePasswordRc4(path, "old-pass", "new-pass", 40, (StandardHash)(-1)));
            Assert.Equal(encrypted, File.ReadAllBytes(path));
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
            DatabaseEncryption.SetPassword(path, "pw", AccessEncryption.OfficeStandardAes);
            byte[] malformed = File.ReadAllBytes(path);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                malformed.AsSpan(0x299, 2), checked((ushort)descriptorLength));
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
            DatabaseEncryption.SetPassword(path, "pw", AccessEncryption.OfficeStandardAes);
            Assert.Throws<InvalidOperationException>(() =>
                DatabaseEncryption.SetPassword(path, "pw2", AccessEncryption.OfficeStandardAes));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Remove_on_plaintext_throws()
    {
        string path = Copy();
        try { Assert.Throws<InvalidOperationException>(() => DatabaseEncryption.RemovePassword(path, "pw")); }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Invalid_schemes_are_rejected()
    {
        string path = Copy();
        try
        {
            // LegacyJet on an .accdb is a format mismatch; None is not a set-scheme.
            Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetPassword(path, "pw", AccessEncryption.LegacyJet));
            Assert.Throws<ArgumentException>(() => DatabaseEncryption.SetPassword(path, "pw", AccessEncryption.None));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
