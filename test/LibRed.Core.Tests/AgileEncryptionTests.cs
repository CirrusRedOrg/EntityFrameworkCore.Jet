using LibRed;
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
}
