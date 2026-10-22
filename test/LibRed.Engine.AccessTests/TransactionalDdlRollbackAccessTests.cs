using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

[Collection(AceCollection.Name)]
public class TransactionalDdlRollbackAccessTests
{
    [Fact]
    public void Full_rollback_removes_created_index_view_table_and_column_alter_byte_for_byte()
    {
        string path = Fresh("ddl-create-rollback-"), before = "";
        try
        {
            CreateBaseline(path);
            before = TemporaryDatabase.CopyPath(path, "ddl-create-rollback-before-");
            string[] statements =
            [
                "CREATE UNIQUE INDEX UX_DdlTxn_Code ON DdlTxn (Code)",
                "CREATE VIEW DdlTxnView AS SELECT Id, V FROM DdlTxn",
                "CREATE TABLE TransientDdl (Id LONG PRIMARY KEY)",
                "ALTER TABLE DdlTxn ALTER COLUMN V TEXT(80)",
            ];

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("BEGIN TRANSACTION");
                foreach (string sql in statements) e.ExecuteNonQuery(sql);

                TableDefinition changed = db.Catalog.FindTable("DdlTxn")!;
                Assert.Contains(changed.Indexes, i => i.Name == "UX_DdlTxn_Code");
                Assert.Equal(160, changed.FindColumn("V")!.Length);
                Assert.NotNull(db.Catalog.FindTable("TransientDdl"));
                Assert.Single(e.ExecuteQuery("SELECT Id FROM DdlTxnView").Rows);

                e.ExecuteNonQuery("ROLLBACK");
                TableDefinition restored = db.Catalog.FindTable("DdlTxn")!;
                Assert.DoesNotContain(restored.Indexes, i => i.Name == "UX_DdlTxn_Code");
                Assert.Equal(40, restored.FindColumn("V")!.Length);
                Assert.Null(db.Catalog.FindTable("TransientDdl"));
                Assert.Throws<LibRed.Sql.Binding.SqlBindException>(() => e.ExecuteQuery("SELECT Id FROM DdlTxnView"));
            }

            AssertSameAsAce(before, path, statements);
            using var connection = AceTestDatabase.Open(path);
            Execute(connection, "INSERT INTO DdlTxn (Id, Code, N, V) VALUES (2, 10, 7, 'duplicate allowed')");
            AssertScalar(connection, "SELECT COUNT(*) FROM DdlTxn WHERE Code = 10", 2);
            Assert.Throws<OleDbException>(() => ExecuteScalar(connection, "SELECT * FROM DdlTxnView"));
            Assert.Throws<OleDbException>(() => ExecuteScalar(connection, "SELECT * FROM TransientDdl"));
        }
        finally
        {
            TemporaryDatabase.Delete(path);
            TemporaryDatabase.Delete(before);
        }
    }

    [Fact]
    public void Full_rollback_restores_dropped_objects_and_rebuilt_column_byte_for_byte()
    {
        string path = Fresh("ddl-drop-rollback-"), before = "";
        try
        {
            CreateBaseline(path, withObjects: true);
            before = TemporaryDatabase.CopyPath(path, "ddl-drop-rollback-before-");
            string[] statements =
            [
                "DROP INDEX UX_DdlTxn_Code ON DdlTxn",
                "DROP VIEW DdlTxnView",
                "ALTER TABLE DdlTxn ALTER COLUMN N DOUBLE",
                "DROP TABLE KeptTable",
            ];

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("BEGIN TRANSACTION");
                foreach (string sql in statements) e.ExecuteNonQuery(sql);

                Assert.DoesNotContain(db.Catalog.FindTable("DdlTxn")!.Indexes, i => i.Name == "UX_DdlTxn_Code");
                Assert.Equal(JetDataType.Double, db.Catalog.FindTable("DdlTxn")!.FindColumn("N")!.Type);
                Assert.Null(db.Catalog.FindTable("KeptTable"));
                Assert.Throws<LibRed.Sql.Binding.SqlBindException>(() => e.ExecuteQuery("SELECT Id FROM DdlTxnView"));

                e.ExecuteNonQuery("ROLLBACK");
                TableDefinition restored = db.Catalog.FindTable("DdlTxn")!;
                Assert.Contains(restored.Indexes, i => i.Name == "UX_DdlTxn_Code");
                Assert.Equal(JetDataType.Int32, restored.FindColumn("N")!.Type);
                Assert.NotNull(db.Catalog.FindTable("KeptTable"));
                Assert.Single(e.ExecuteQuery("SELECT Id FROM DdlTxnView").Rows);
            }

            AssertSameAsAce(before, path, statements);
            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, "SELECT COUNT(*) FROM DdlTxnView", 1);
            AssertScalar(connection, "SELECT COUNT(*) FROM KeptTable", 1);
            Assert.Throws<OleDbException>(() =>
                Execute(connection, "INSERT INTO DdlTxn (Id, Code, N, V) VALUES (2, 10, 7, 'duplicate')"));
        }
        finally
        {
            TemporaryDatabase.Delete(path);
            TemporaryDatabase.Delete(before);
        }
    }

    /// <summary>Asserts that LibRed's file after its rolled-back transaction is ACE's after the same one: ACE runs
    /// <paramref name="statements"/> between a BEGIN and a ROLLBACK on a copy of <paramref name="before"/>, the file
    /// LibRed started from. A rollback is not a byte-for-byte restore in ACE either — the session's reservation and
    /// the catalog's counts outlive it (page-05-usage-maps.md, page-02a-tdef.md).</summary>
    private static void AssertSameAsAce(string before, string libred, string[] statements)
    {
        string ace = TemporaryDatabase.CopyPath(before, "ddl-rollback-ace-");
        string noise = TemporaryDatabase.CopyPath(before, "ddl-rollback-noise-");
        try
        {
            string?[] errors = WholeFileParityProbeTest.RunThroughAce(ace, ["BEGIN TRANSACTION", .. statements, "ROLLBACK TRANSACTION"]);
            Assert.True(errors.All(e => e is null), string.Join("; ", errors.Where(e => e is not null)));
            WholeFileParityProbeTest.RunThroughAce(noise, []);
            (string report, int pages) = WholeFileParityProbeTest.Compare(before, ace, noise, libred);
            Assert.True(pages == 0, report);
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(noise);
        }
    }

    [Fact]
    public void Inner_ddl_rollback_restores_outer_created_objects_which_then_commit_for_ace()
    {
        string path = Fresh("ddl-savepoint-");
        try
        {
            CreateBaseline(path);
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("BEGIN TRANSACTION");
                e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_DdlTxn_Code ON DdlTxn (Code)");
                e.ExecuteNonQuery("CREATE VIEW DdlTxnView AS SELECT Id, V FROM DdlTxn");

                e.ExecuteNonQuery("BEGIN TRANSACTION");
                e.ExecuteNonQuery("DROP INDEX UX_DdlTxn_Code ON DdlTxn");
                e.ExecuteNonQuery("DROP VIEW DdlTxnView");
                e.ExecuteNonQuery("ALTER TABLE DdlTxn ALTER COLUMN N DOUBLE");
                e.ExecuteNonQuery("CREATE TABLE InnerOnly (Id LONG PRIMARY KEY)");
                e.ExecuteNonQuery("ROLLBACK");

                TableDefinition restoredOuter = db.Catalog.FindTable("DdlTxn")!;
                Assert.Contains(restoredOuter.Indexes, i => i.Name == "UX_DdlTxn_Code");
                Assert.Equal(JetDataType.Int32, restoredOuter.FindColumn("N")!.Type);
                Assert.Null(db.Catalog.FindTable("InnerOnly"));
                Assert.Single(e.ExecuteQuery("SELECT Id FROM DdlTxnView").Rows);

                e.ExecuteNonQuery("COMMIT");
            }

            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, "SELECT COUNT(*) FROM DdlTxnView", 1);
            Assert.Throws<OleDbException>(() => ExecuteScalar(connection, "SELECT * FROM InnerOnly"));
            Assert.Throws<OleDbException>(() =>
                Execute(connection, "INSERT INTO DdlTxn (Id, Code, N, V) VALUES (2, 10, 7, 'duplicate')"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Fresh(string prefix) =>
        TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), prefix);

    private static void CreateBaseline(string path, bool withObjects = false)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        var e = new QueryEngine(db);
        e.ExecuteNonQuery("CREATE TABLE DdlTxn (Id LONG PRIMARY KEY, Code LONG, N LONG, V TEXT(20))");
        e.ExecuteNonQuery("INSERT INTO DdlTxn (Id, Code, N, V) VALUES (1, 10, 42, 'original')");
        if (!withObjects) return;
        e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_DdlTxn_Code ON DdlTxn (Code)");
        e.ExecuteNonQuery("CREATE VIEW DdlTxnView AS SELECT Id, V FROM DdlTxn");
        e.ExecuteNonQuery("CREATE TABLE KeptTable (Id LONG PRIMARY KEY)");
        e.ExecuteNonQuery("INSERT INTO KeptTable (Id) VALUES (1)");
    }

    private static object? ExecuteScalar(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void AssertScalar(OleDbConnection connection, string sql, int expected) =>
        Assert.Equal(expected, Convert.ToInt32(ExecuteScalar(connection, sql)));
}