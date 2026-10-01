using System.Reflection;
using LibRed.Crypto;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// The legacy Jet database password is half of the header region the SID keystream is folded from (page-00
/// §2.3), so setting one re-masks every stored SID. LibRed's <see cref="DatabaseEncryption.SetJetPassword"/> has
/// to write exactly what Access's own password change does: DAO's <c>NewPassword</c> on one copy, LibRed on
/// another, and every <c>MSysObjects.Owner</c> and <c>MSysACEs.SID</c> — and the password field — must agree.
/// </summary>
[Collection(AceCollection.Name)]
public class JetPasswordSidAccessTests
{
    private const string Password = "Test1";

    [Fact]
    public void A_jet_password_remasks_a_dao_databases_sids_as_access_does()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");

        string source = TemporaryDatabase.CreatePath("jetpw_dao_", ".mdb");
        try
        {
            object database = Invoke(engine!, "CreateDatabase", source, ";LANGID=0x0409;CP=1252;COUNTRY=0", 64)!; // dbVersion40
            Invoke(database, "Close");
            Compare(engine!, source);
        }
        finally { TemporaryDatabase.Delete(source); }
    }

    [Fact]
    public void A_jet_password_remasks_a_libred_databases_sids_as_access_does()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");

        string source = TemporaryDatabase.CreatePath("jetpw_libred_", ".mdb");
        try
        {
            DatabaseCreator.CreateEmpty(source, version: 0x01);
            Compare(engine!, source);
        }
        finally { TemporaryDatabase.Delete(source); }
    }

    private static void Compare(object engine, string source)
    {
        string byAccess = TemporaryDatabase.CopyPath(source, "jetpw_access_");
        string byLibRed = TemporaryDatabase.CopyPath(source, "jetpw_ours_");
        try
        {
            object database = Invoke(engine, "OpenDatabase", byAccess, true, false, "")!;
            Invoke(database, "NewPassword", "", Password);
            Invoke(database, "Close");
            AceTestDatabase.ReleaseAbandonedComObjects();

            using (var db = JetDatabase.Open(byLibRed, readOnly: false, exclusive: true))
                DatabaseEncryption.SetJetPassword(db, Password);

            List<byte[]> access = StoredSids(byAccess), ours = StoredSids(byLibRed);
            Assert.NotEqual(StoredSids(source, null)[0], access[0]); // Access did re-mask them
            Assert.Equal(access, ours);
            Assert.Equal(File.ReadAllBytes(byAccess)[0x42..0x6A], File.ReadAllBytes(byLibRed)[0x42..0x6A]);
        }
        finally
        {
            TemporaryDatabase.Delete(byAccess);
            TemporaryDatabase.Delete(byLibRed);
        }
    }

    /// <summary>Every MSysObjects.Owner and MSysACEs.SID, in stored order.</summary>
    private static List<byte[]> StoredSids(string path, string? password = Password)
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

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
