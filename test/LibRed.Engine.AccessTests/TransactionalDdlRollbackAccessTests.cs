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
        string path = Fresh("ddl-create-rollback-");
        try
        {
            CreateBaseline(path);
            byte[] before = File.ReadAllBytes(path);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("BEGIN TRANSACTION");
                e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_DdlTxn_Code ON DdlTxn (Code)");
                e.ExecuteNonQuery("CREATE VIEW DdlTxnView AS SELECT Id, V FROM DdlTxn");
                e.ExecuteNonQuery("CREATE TABLE TransientDdl (Id LONG PRIMARY KEY)");
                e.ExecuteNonQuery("ALTER TABLE DdlTxn ALTER COLUMN V TEXT(80)");

                TableDef changed = db.Catalog.FindTable("DdlTxn")!;
                Assert.Contains(changed.Indexes, i => i.Name == "UX_DdlTxn_Code");
                Assert.Equal(160, changed.FindColumn("V")!.Length);
                Assert.NotNull(db.Catalog.FindTable("TransientDdl"));
                Assert.Single(e.ExecuteQuery("SELECT Id FROM DdlTxnView").Rows);

                e.ExecuteNonQuery("ROLLBACK");
                TableDef restored = db.Catalog.FindTable("DdlTxn")!;
                Assert.DoesNotContain(restored.Indexes, i => i.Name == "UX_DdlTxn_Code");
                Assert.Equal(40, restored.FindColumn("V")!.Length);
                Assert.Null(db.Catalog.FindTable("TransientDdl"));
                Assert.Throws<LibRed.Sql.Binding.SqlBindException>(() => e.ExecuteQuery("SELECT Id FROM DdlTxnView"));
            }

            Assert.Equal(before, File.ReadAllBytes(path));
            using var connection = AceTestDatabase.Open(path);
            Execute(connection, "INSERT INTO DdlTxn (Id, Code, N, V) VALUES (2, 10, 7, 'duplicate allowed')");
            AssertScalar(connection, "SELECT COUNT(*) FROM DdlTxn WHERE Code = 10", 2);
            Assert.Throws<OleDbException>(() => ExecuteScalar(connection, "SELECT * FROM DdlTxnView"));
            Assert.Throws<OleDbException>(() => ExecuteScalar(connection, "SELECT * FROM TransientDdl"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Full_rollback_restores_dropped_objects_and_rebuilt_column_byte_for_byte()
    {
        string path = Fresh("ddl-drop-rollback-");
        try
        {
            CreateBaseline(path, withObjects: true);
            byte[] before = File.ReadAllBytes(path);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("BEGIN TRANSACTION");
                e.ExecuteNonQuery("DROP INDEX UX_DdlTxn_Code ON DdlTxn");
                e.ExecuteNonQuery("DROP VIEW DdlTxnView");
                e.ExecuteNonQuery("ALTER TABLE DdlTxn ALTER COLUMN N DOUBLE");
                e.ExecuteNonQuery("DROP TABLE KeptTable");

                Assert.DoesNotContain(db.Catalog.FindTable("DdlTxn")!.Indexes, i => i.Name == "UX_DdlTxn_Code");
                Assert.Equal(JetDataType.Double, db.Catalog.FindTable("DdlTxn")!.FindColumn("N")!.Type);
                Assert.Null(db.Catalog.FindTable("KeptTable"));
                Assert.Throws<LibRed.Sql.Binding.SqlBindException>(() => e.ExecuteQuery("SELECT Id FROM DdlTxnView"));

                e.ExecuteNonQuery("ROLLBACK");
                TableDef restored = db.Catalog.FindTable("DdlTxn")!;
                Assert.Contains(restored.Indexes, i => i.Name == "UX_DdlTxn_Code");
                Assert.Equal(JetDataType.Int32, restored.FindColumn("N")!.Type);
                Assert.NotNull(db.Catalog.FindTable("KeptTable"));
                Assert.Single(e.ExecuteQuery("SELECT Id FROM DdlTxnView").Rows);
            }

            Assert.Equal(before, File.ReadAllBytes(path));
            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, "SELECT COUNT(*) FROM DdlTxnView", 1);
            AssertScalar(connection, "SELECT COUNT(*) FROM KeptTable", 1);
            Assert.Throws<OleDbException>(() =>
                Execute(connection, "INSERT INTO DdlTxn (Id, Code, N, V) VALUES (2, 10, 7, 'duplicate')"));
        }
        finally { TemporaryDatabase.Delete(path); }
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

                TableDef restoredOuter = db.Catalog.FindTable("DdlTxn")!;
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
