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
// >= 0x02. See docs/format/system-catalog.md.
//
// Ace_runs_ddl_against_a_libred_created_database is the regression guard and asserts; the rest report.
// Keep the guard: LibRed reading its own file back proves nothing about whether Access will accept it.
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

    // The control: DAO creates a database through the real engine, and its files are known to omit the
    // NavPane/AccessStorage tables that Access adds on first open. If ACE will run DDL in a DAO-created
    // database, those tables are not the blocker and the difference is elsewhere.
    [Fact]
    public void Probe_ace_ddl_against_a_dao_created_database()
    {
        object? engine = null;
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { engine = Activator.CreateInstance(type); break; } catch (Exception) { }
        }
        if (engine is null) { output.WriteLine("DAO unavailable."); return; }

        string path = TemporaryDatabase.CreatePath("ace-ddl-dao-");
        File.Delete(path);   // DAO creates the file itself and refuses an existing one
        try
        {
            object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            Invoke(database, "Close");

            output.WriteLine($"DAO-created system tables: {string.Join(", ", SystemTables(path))}");

            using var connection = AceTestDatabase.Open(path);
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE AceMade (K TEXT(30), V LONG)";
                command.ExecuteNonQuery();
                output.WriteLine("   CREATE TABLE       OK  -> the missing system tables are NOT the blocker");
            }
            catch (Exception ex) { output.WriteLine($"   CREATE TABLE       {ex.GetType().Name}: {ex.Message.Trim()}"); }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Isolate the required table: start from a DAO-created database (where ACE DDL works), drop one system
    // table with LibRed, and see whether ACE then refuses. Whatever flips it is what CREATE TABLE needs.
    [Theory]
    [InlineData("MSysComplexColumns")]
    [InlineData("MSysComplexType_Text")]
    [InlineData("MSysQueries")]
    public void Probe_which_system_table_ace_ddl_needs(string drop)
    {
        object? engine = null;
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { engine = Activator.CreateInstance(type); break; } catch (Exception) { }
        }
        if (engine is null) { output.WriteLine("DAO unavailable."); return; }

        string path = TemporaryDatabase.CreatePath($"ace-ddl-drop-");
        File.Delete(path);
        try
        {
            object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            Invoke(database, "Close");

            try
            {
                using var db = JetDatabase.Open(path, readOnly: false);
                db.DropTable(drop);
            }
            catch (Exception ex) { output.WriteLine($"dropping {drop}: {ex.GetType().Name}: {ex.Message.Trim()}"); return; }

            using var connection = AceTestDatabase.Open(path);
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE AceMade (K TEXT(30), V LONG)";
                command.ExecuteNonQuery();
                output.WriteLine($"without {drop,-22} CREATE TABLE still OK");
            }
            catch (Exception ex) { output.WriteLine($"without {drop,-22} CREATE TABLE {ex.GetType().Name}: {ex.Message.Trim()}"); }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Does ACE actually WRITE to MSysComplexColumns when it creates an ordinary table, or does it only need
    // the table to exist? That decides whether LibRed must populate it or merely provide an empty one — and
    // the dumped schema is what LibRed would have to build.
    [Fact]
    public void Probe_what_ace_writes_to_complex_columns()
    {
        object? engine = null;
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { engine = Activator.CreateInstance(type); break; } catch (Exception) { }
        }
        if (engine is null) { output.WriteLine("DAO unavailable."); return; }

        string path = TemporaryDatabase.CreatePath("complex-cols-");
        File.Delete(path);
        try
        {
            object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            Invoke(database, "Close");

            DumpSchema(path);
            output.WriteLine($"rows before any DDL: {RowCount(path)}");

            using (var connection = AceTestDatabase.Open(path))
                foreach (string sql in new[]
                {
                    "CREATE TABLE Plain (K TEXT(30), V LONG)",
                    "CREATE TABLE Typed (A LONG, B TEXT(20), C MEMO, D DATETIME, E CURRENCY, F GUID, G OLEOBJECT)",
                    "CREATE INDEX IX_Plain ON Plain (K)",
                })
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                    output.WriteLine($"after {sql[..Math.Min(34, sql.Length)],-36} rows = {RowCount(path)}");
                }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // NOT probed by dropping indexes: removing MSysComplexColumns' indexes with LibRed and reopening makes
    // ACE fault natively (0xC0000005) rather than report anything, so that route says the file is malformed,
    // not whether the indexes are required. The constructive test — build the table *without* indexes in
    // DatabaseCreator and see whether ACE DDL works — belongs with the implementation.

    // WHAT is ACE doing with MSysComplexColumns? It reads it and never writes it, so the question is which
    // operations need it at all. If only CREATE fails, ACE consults it per-creation; if the whole DDL surface
    // fails while DML keeps working, it is binding the table as part of a fixed system-table set and the
    // "lookup" is really a bind of the catalog the DDL path expects to exist.
    [Fact]
    public void Probe_which_operations_need_complex_columns()
    {
        object? engine = null;
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { engine = Activator.CreateInstance(type); break; } catch (Exception) { }
        }
        if (engine is null) { output.WriteLine("DAO unavailable."); return; }

        string path = TemporaryDatabase.CreatePath("complex-need-");
        File.Delete(path);
        try
        {
            object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            Invoke(database, "Close");

            // Pre-build the objects the later statements act on, while the registry still exists.
            using (var setup = AceTestDatabase.Open(path))
                foreach (string sql in new[]
                {
                    "CREATE TABLE Existing (K TEXT(30), V LONG)",
                    "CREATE TABLE Doomed (K TEXT(30))",
                    "CREATE TABLE Altered (K TEXT(30))",
                    "INSERT INTO Existing (K, V) VALUES ('a', 1)",
                })
                {
                    using var command = setup.CreateCommand();
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }

            using (var db = JetDatabase.Open(path, readOnly: false)) db.DropTable("MSysComplexColumns");
            output.WriteLine("dropped MSysComplexColumns; now exercising the surface:");

            using var connection = AceTestDatabase.Open(path);
            foreach ((string what, string sql) in new[]
            {
                ("SELECT",        "SELECT COUNT(*) FROM Existing"),
                ("INSERT",        "INSERT INTO Existing (K, V) VALUES ('b', 2)"),
                ("UPDATE",        "UPDATE Existing SET V = 3 WHERE K = 'a'"),
                ("DELETE",        "DELETE FROM Existing WHERE K = 'b'"),
                ("CREATE TABLE",  "CREATE TABLE Fresh (K TEXT(30))"),
                ("CREATE INDEX",  "CREATE INDEX IX_Existing ON Existing (K)"),
                ("ALTER ADD COL", "ALTER TABLE Altered ADD COLUMN Extra LONG"),
                ("CREATE VIEW",   "CREATE VIEW V1 AS SELECT K FROM Existing"),
                ("DROP TABLE",    "DROP TABLE Doomed"),
            })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                Exception? error = Record.Exception(() => command.ExecuteNonQuery());
                output.WriteLine($"   {what,-14} {(error is null ? "OK" : error.Message.Trim())}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static int RowCount(string path)
    {
        using var db = JetDatabase.Open(path);
        return db.OpenTable("MSysComplexColumns").Rows().Count();
    }

    private void DumpSchema(string path)
    {
        using var db = JetDatabase.Open(path);
        TableDef table = db.Catalog.Tables.Single(t => t.Name == "MSysComplexColumns");
        output.WriteLine("MSysComplexColumns schema:");
        foreach (ColumnDef c in table.Columns.OrderBy(c => c.Index))
            output.WriteLine($"   {c.Index}  {c.Name,-16} {c.Type,-12} len={c.Length,-4} fixed={c.IsFixedLength} nullable={c.IsNullable}");
        foreach (IndexDef i in table.Indexes)
            output.WriteLine($"   index {i.Name,-16} unique={i.IsUnique} pk={i.IsPrimaryKey} cols={string.Join(",", i.Columns.Select(c => c.Column.Name))}");
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, System.Reflection.BindingFlags.InvokeMethod, null, target, args);

    private static string[] SystemTables(string path)
    {
        using var db = JetDatabase.Open(path);
        return [.. db.Catalog.Tables.Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
    }
}
