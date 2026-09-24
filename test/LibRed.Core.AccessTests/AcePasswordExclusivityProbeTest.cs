using System.Data.OleDb;
using Xunit;

namespace LibRed.Core.Tests;

// Whether ACE will set, change or clear a database password while another connection holds the file open.
// LibRed's own password operations rewrite the database, so what they may assume about other users decides
// the whole design: if ACE demands exclusivity then so can LibRed, and a rewrite has only itself to worry
// about. Asked of ACE rather than assumed.
[Collection(AceCollection.Name)]
public class AcePasswordExclusivityProbeTest(ITestOutputHelper output)
{
    private const string Password = "pr0be-pw";

    [Fact]
    public void Ace_setting_a_password_with_another_connection_open()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "pwexcl-");
        try
        {
            string set = $"ALTER DATABASE PASSWORD [{Password}] NULL";

            // The connection running the statement is itself shared — refused even with nobody else attached,
            // so the requirement is on the WRITER's own mode, not merely on the absence of other users.
            string shared = Attempt(path, null, set, exclusive: false);
            output.WriteLine($"shared connection, alone:        {shared}");
            Assert.Contains("shared open database", shared, StringComparison.Ordinal);

            // Exclusive connection while another user is attached: the OPEN is what fails, and ACE names the
            // user and machine — which it can only know from the lock file.
            string contended;
            using (OleDbConnection other = AceTestDatabase.Open(path))
                contended = Attempt(path, null, set, exclusive: true);
            output.WriteLine($"exclusive, another user open:    {contended}");
            Assert.Contains("already opened by user", contended, StringComparison.Ordinal);

            // Exclusive connection, nobody else.
            string alone = Attempt(path, null, set, exclusive: true);
            output.WriteLine($"exclusive, alone:                {alone}");
            Assert.Equal("accepted", alone);

            // It took: the file no longer opens without the password, and does with it.
            string without = Attempt(path, null, "SELECT COUNT(*) FROM Shippers", exclusive: false);
            string with = Attempt(path, Password, "SELECT COUNT(*) FROM Shippers", exclusive: false);
            output.WriteLine($"open without the password:       {without}");
            output.WriteLine($"open with the password:          {with}");
            Assert.NotEqual("accepted", without);
            Assert.Equal("accepted", with);

            // And clearing it takes the same exclusive open.
            string cleared = Attempt(path, Password, $"ALTER DATABASE PASSWORD NULL [{Password}]", exclusive: true);
            output.WriteLine($"clearing it, exclusive:          {cleared}");
            Assert.Equal("accepted", cleared);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Runs one statement on its own connection, shared or exclusive, reporting what ACE did rather
    /// than throwing. The exclusive form needs its own connection string: <c>AceTestDatabase.Open</c> only
    /// opens shared, which is the mode every other test wants.</summary>
    private static string Attempt(string path, string? password, string sql, bool exclusive)
    {
        try
        {
            using OleDbConnection connection = exclusive ? OpenExclusive(path, password) : AceTestDatabase.Open(path, password, attempts: 1);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            return "accepted";
        }
        catch (Exception e) { return $"{e.GetType().Name}: {e.Message.ReplaceLineEndings(" ").Trim()}"; }
    }

    private static OleDbConnection OpenExclusive(string path, string? password)
    {
        Exception? last = null;
        foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            string passwordPart = password is null ? "" : $"Jet OLEDB:Database Password={password};";
            var connection = new OleDbConnection(
                $"Provider={provider};Data Source={path};Mode=Share Exclusive;{passwordPart}OLE DB Services=-4;");
            try { connection.Open(); return connection; }
            catch (Exception e) { last = e; connection.Dispose(); }
        }
        throw last!;
    }
}
