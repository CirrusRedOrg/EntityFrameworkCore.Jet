using LibRed;
using LibRed.Crypto;
using Xunit;

namespace LibRed.Core.Tests;

public class DatabaseEncryptionTests
{
    private static readonly string Plain = @"D:\toolkits\efcorejetlibred\test\LibRed.Core.Tests\Data\WideTable.accdb";

    private static string Copy()
    {
        string p = Path.Combine(Path.GetTempPath(), $"libred_enc_{Guid.NewGuid():N}.accdb");
        File.Copy(Plain, p, overwrite: true);
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
            Assert.ThrowsAny<Exception>(() => TableRows(path, null));                    // requires it
            Assert.Throws<UnauthorizedAccessException>(() => TableRows(path, "wrong"));  // rejects wrong one

            DatabaseEncryption.RemovePassword(path, "S3cret!");
            Assert.Equal(rows, TableRows(path, null));                                   // plaintext again
        }
        finally { File.Delete(path); }
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
        finally { File.Delete(path); }
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
        finally { File.Delete(path); }
    }

    [Fact]
    public void Remove_on_plaintext_throws()
    {
        string path = Copy();
        try { Assert.Throws<InvalidOperationException>(() => DatabaseEncryption.RemovePassword(path, "pw")); }
        finally { File.Delete(path); }
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
        finally { File.Delete(path); }
    }
}
