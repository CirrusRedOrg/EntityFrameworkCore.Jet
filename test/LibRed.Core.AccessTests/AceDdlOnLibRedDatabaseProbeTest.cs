using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Why would ACE not run DDL against a LibRed-created database? ACE opened one, read it, and INSERTed into a
// table LibRed made (AceCreatedDatabaseTests) — but CREATE TABLE through the OLE DB provider failed with
// "Cannot find table or constraint", on both collation versions.
//
// ANSWERED: the file was missing MSysComplexColumns. ACE consults it whenever it creates a catalog object —
// CREATE TABLE and CREATE VIEW, and only those; DML, CREATE INDEX, ALTER ADD COLUMN and DROP TABLE all work
// without it. DatabaseCreator.CreateEmpty now writes it (and the nine MSysComplexType_* tables) for version
// >= 0x02. See docs/format/system-catalog.md. The DAO probes that isolated it — dropping one system table at a
// time from a DAO-created database — are in git history.
//
// Ace_runs_ddl_against_a_libred_created_database is the regression guard and asserts. Keep it: LibRed reading
// its own file back proves nothing about whether Access will accept it.
[Collection(AceCollection.Name)]
public class AceDdlOnLibRedDatabaseProbeTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("v0", 0)]
    [InlineData("v1", 1)]
    public void Ace_runs_ddl_against_a_libred_created_database(string label, byte version)
    {
        string path = TemporaryDatabase.CreatePath($"ace-ddl-{label}-");
        try
        {
            DatabaseCreator.CreateEmpty(path, collation: new Collation(CollatingOrder.General, version));

            using var connection = AceTestDatabase.Open(path);
            output.WriteLine($"{label}: ACE opened the database");

            foreach ((string what, string sql) in new[]
            {
                ("CREATE TABLE", "CREATE TABLE AceMade (K TEXT(30), V LONG)"),
                ("CREATE TABLE + PK", "CREATE TABLE AceMade2 (K TEXT(30) CONSTRAINT PK PRIMARY KEY)"),
                ("CREATE INDEX", "CREATE INDEX IX_AceMade ON AceMade (K)"),
            })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                Exception? error = Record.Exception(() => command.ExecuteNonQuery());
                output.WriteLine($"   {what,-18} {(error is null ? "OK" : $"{error.GetType().Name}: {error.Message.Trim()}")}");
                Assert.Null(error);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Which system tables does an Access-authored database carry that a LibRed-created one does not? The
    // Access-authored side needs a real file, so this reports what it can and says so when it cannot.
    [Fact]
    public void Probe_system_tables_libred_creates_versus_access()
    {
        string libred = TemporaryDatabase.CreatePath("systables-libred-");
        try
        {
            DatabaseCreator.CreateEmpty(libred);
            string[] mine = SystemTables(libred);
            output.WriteLine($"LibRed-created ({mine.Length}): {string.Join(", ", mine)}");

            string? authored = Environment.GetEnvironmentVariable("LIBRED_V1_FIXTURE") is { } f && File.Exists(f) ? f : null;
            if (authored is null)
            {
                output.WriteLine("Access-authored: LIBRED_V1_FIXTURE not set — cannot compare.");
                return;
            }

            string[] theirs = SystemTables(authored);
            output.WriteLine($"Access-authored ({theirs.Length}): {string.Join(", ", theirs)}");
            output.WriteLine($"missing from LibRed: {string.Join(", ", theirs.Except(mine))}");
            output.WriteLine($"only in LibRed:      {string.Join(", ", mine.Except(theirs))}");
        }
        finally { TemporaryDatabase.Delete(libred); }
    }

    private static string[] SystemTables(string path)
    {
        using var db = JetDatabase.Open(path);
        return [.. db.Catalog.Tables.Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
    }
}
