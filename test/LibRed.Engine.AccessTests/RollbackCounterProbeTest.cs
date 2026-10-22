using System.Data.OleDb;
using System.Reflection;
using LibRed.Formats;
using Xunit;

namespace LibRed.Engine.Tests;

// PROBE: what a rolled-back transaction leaves of the TDEF counters an insert advances — the AutoNumber high-water
// at 0x14 and the row count at 0x10 — on disk after close, in the same session, and in the next one.
[Collection(AceCollection.Name)]
public class RollbackCounterProbeTest(ITestOutputHelper output)
{
    [Fact]
    public void Probe_counters_after_a_rolled_back_insert()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string path = TemporaryDatabase.CreatePath("rollback-counter-");
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", 2)!;
            Invoke(Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!, "Close");
            AceTestDatabase.ReleaseAbandonedComObjects();

            using (OleDbConnection c = AceTestDatabase.Open(path))
            {
                Exec(c, "CREATE TABLE T (Id COUNTER CONSTRAINT pkT PRIMARY KEY, X LONG)");
                Exec(c, "INSERT INTO T (X) VALUES (1)");
                Exec(c, "BEGIN TRANSACTION");
                Exec(c, "INSERT INTO T (X) VALUES (2)");
                Exec(c, "INSERT INTO T (X) VALUES (3)");
                Exec(c, "ROLLBACK TRANSACTION");
            }
            OleDbConnection.ReleaseObjectPool();
            Report(path, "T", "after rollback, closed");

            using (OleDbConnection c = AceTestDatabase.Open(path))
            {
                Exec(c, "INSERT INTO T (X) VALUES (4)");
                output.WriteLine($"next session's id: {Scalar(c, "SELECT Id FROM T WHERE X = 4")}");
                Exec(c, "BEGIN TRANSACTION");
                Exec(c, "INSERT INTO T (X) VALUES (5)");
                Exec(c, "ROLLBACK TRANSACTION");
                Exec(c, "INSERT INTO T (X) VALUES (6)");
                output.WriteLine($"same session's id after a rollback: {Scalar(c, "SELECT Id FROM T WHERE X = 6")}");
                Exec(c, "BEGIN TRANSACTION");
                Exec(c, "INSERT INTO T (X) VALUES (7)");
                Exec(c, "ROLLBACK TRANSACTION");
            }
            OleDbConnection.ReleaseObjectPool();
            Report(path, "T", "after a last rollback, closed");

            // An insert that fails on its own, outside any transaction: a required column left null.
            using (OleDbConnection c = AceTestDatabase.Open(path))
            {
                Exec(c, "CREATE TABLE R (Id COUNTER CONSTRAINT pkR PRIMARY KEY, X LONG NOT NULL)");
                Exec(c, "INSERT INTO R (X) VALUES (1)");
                try { Exec(c, "INSERT INTO R (X) VALUES (NULL)"); }
                catch (OleDbException e) { output.WriteLine($"failed insert: {e.Message}"); }
                Exec(c, "INSERT INTO R (X) VALUES (2)");
                output.WriteLine($"id after a failed insert: {Scalar(c, "SELECT Id FROM R WHERE X = 2")}");

                // A missing parent, a duplicate unique key and a failed CHECK, each followed by a good insert.
                Exec(c, "CREATE TABLE Parent (Id LONG CONSTRAINT pkParent PRIMARY KEY)");
                Exec(c, "INSERT INTO Parent (Id) VALUES (1)");
                Exec(c, "CREATE TABLE Child (Id COUNTER CONSTRAINT pkChild PRIMARY KEY, P LONG CONSTRAINT fkChild REFERENCES Parent (Id), " +
                    "U LONG CONSTRAINT uqChild UNIQUE, X LONG, CONSTRAINT ckChild CHECK (X >= 0))");
                Exec(c, "INSERT INTO Child (P, U, X) VALUES (1, 1, 1)");
                foreach ((string label, string values) in new[] { ("missing parent", "99, 2, 1"), ("duplicate key", "1, 1, 1"), ("failed CHECK", "1, 3, -1") })
                {
                    try { Exec(c, $"INSERT INTO Child (P, U, X) VALUES ({values})"); }
                    catch (OleDbException e) { output.WriteLine($"{label}: {e.Message}"); }
                    Exec(c, "INSERT INTO Child (P, U, X) VALUES (1, NULL, 5)");
                    output.WriteLine($"id after a {label}: {Scalar(c, "SELECT MAX(Id) FROM Child")}");
                }
            }
            OleDbConnection.ReleaseObjectPool();

            // The complex id at 0x1C, drawn by a DAO AddNew rolled back with its workspace's transaction.
            object database = Invoke(workspace, "OpenDatabase", path)!;
            object owner = Invoke(database, "CreateTableDef", "C")!;
            object fields = Get(owner, "Fields");
            Invoke(fields, "Append", Invoke(owner, "CreateField", "X", 4)!);
            Invoke(fields, "Append", Invoke(owner, "CreateField", "M", 104)!);
            Invoke(Get(database, "TableDefs"), "Append", owner);
            object rows = Invoke(database, "OpenRecordset", "C")!;
            Invoke(rows, "AddNew");
            Set(Item(Get(rows, "Fields"), "X"), "Value", 1);
            Invoke(rows, "Update");
            Invoke(workspace, "BeginTrans");
            Invoke(rows, "AddNew");
            Set(Item(Get(rows, "Fields"), "X"), "Value", 2);
            Invoke(rows, "Update");
            Invoke(workspace, "Rollback");
            Invoke(rows, "AddNew");
            Set(Item(Get(rows, "Fields"), "X"), "Value", 3);
            Invoke(rows, "Update");
            Invoke(rows, "Close");
            Invoke(database, "Close");
            AceTestDatabase.ReleaseAbandonedComObjects();
            Report(path, "C", "complex owner after a rolled-back AddNew, closed");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Every TDEF counter — rows, AutoNumber, each real index's total and unique entries — of the catalog tables a
    // CREATE TABLE writes and of an indexed table, after each kind of statement rolled back, live and after close.
    [Fact]
    public void Probe_every_tdef_counter_after_rolled_back_statements()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string path = TemporaryDatabase.CreatePath("rollback-tdef-");
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", 2)!;
            Invoke(Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!, "Close");
            AceTestDatabase.ReleaseAbandonedComObjects();
            using (OleDbConnection c = AceTestDatabase.Open(path))
            {
                Exec(c, "CREATE TABLE U (Id COUNTER CONSTRAINT pkU PRIMARY KEY, K LONG, T TEXT(20))");
                Exec(c, "CREATE INDEX ixUK ON U (K)");
                for (int i = 1; i <= 5; i++) Exec(c, $"INSERT INTO U (K, T) VALUES ({i}, 'row {i}')");
                Counters(path, "start");
                foreach ((string label, string[] work) in new[]
                {
                    ("CREATE TABLE", new[] { "CREATE TABLE X (Id LONG CONSTRAINT pkX PRIMARY KEY, Y TEXT(10))" }),
                    ("INSERT", new[] { "INSERT INTO U (K, T) VALUES (10, 'a')", "INSERT INTO U (K, T) VALUES (11, 'b')" }),
                    ("DELETE", new[] { "DELETE FROM U WHERE K <= 2" }),
                    ("UPDATE", new[] { "UPDATE U SET K = K + 100 WHERE K = 3" }),
                })
                {
                    Exec(c, "BEGIN TRANSACTION");
                    foreach (string sql in work) Exec(c, sql);
                    Exec(c, "ROLLBACK TRANSACTION");
                    Counters(path, $"after a rolled-back {label}");
                }

                // Inserts that fail — a missing parent, a duplicate key — on their own, then inside a committed
                // transaction.
                Exec(c, "CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY)");
                Exec(c, "INSERT INTO P (Id) VALUES (1)");
                Exec(c, "CREATE TABLE F (Id LONG CONSTRAINT pkF PRIMARY KEY, K LONG, Pid LONG CONSTRAINT fkF REFERENCES P (Id))");
                Exec(c, "CREATE INDEX ixFK ON F (K)");
                Exec(c, "INSERT INTO F (Id, K, Pid) VALUES (1, 1, 1)");
                Counters(path, "F start", "F");
                foreach ((string label, string sql) in new[]
                {
                    ("missing parent", "INSERT INTO F (Id, K, Pid) VALUES (2, 2, 99)"),
                    ("duplicate key", "INSERT INTO F (Id, K, Pid) VALUES (1, 3, 1)"),
                })
                {
                    try { Exec(c, sql); }
                    catch (OleDbException) { }
                    Counters(path, $"F after a failed insert, {label}", "F");
                }
                Exec(c, "BEGIN TRANSACTION");
                foreach (string sql in new[] { "INSERT INTO F (Id, K, Pid) VALUES (2, 4, 99)", "INSERT INTO F (Id, K, Pid) VALUES (1, 5, 1)" })
                {
                    try { Exec(c, sql); }
                    catch (OleDbException) { }
                }
                Exec(c, "COMMIT TRANSACTION");
                Counters(path, "F after both failing in a transaction", "F");

                // A row refused by a CHECK once written, and a three-row INSERT…SELECT failing on its last row.
                Exec(c, "CREATE TABLE G (Id LONG CONSTRAINT pkG PRIMARY KEY, K LONG, CONSTRAINT ckG CHECK (K >= 0))");
                Exec(c, "CREATE INDEX ixGK ON G (K)");
                Exec(c, "INSERT INTO G (Id, K) VALUES (100, 1)");
                Counters(path, "G start", "G");
                try { Exec(c, "INSERT INTO G (Id, K) VALUES (1, -1)"); }
                catch (OleDbException) { }
                Counters(path, "G after a CHECK refusal", "G");
                Exec(c, "CREATE TABLE H (N LONG)");
                for (int n = 1; n <= 3; n++) Exec(c, $"INSERT INTO H (N) VALUES ({n})");
                try { Exec(c, "INSERT INTO G (Id, K) SELECT IIf(N = 3, 100, 100 + N), N + 10 FROM H ORDER BY N"); }
                catch (OleDbException e) { output.WriteLine($"G multi-row: {e.Message}"); }
                Counters(path, "G after an INSERT…SELECT failing on its last row", "G");
            }
            OleDbConnection.ReleaseObjectPool();
            Counters(path, "closed");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private void Counters(string path, string label, params string[] tables)
    {
        Thread.Sleep(3000);
        string snapshot = TemporaryDatabase.CopyPath(path, "rollback-tdef-live-");
        try
        {
            using var database = JetDatabase.Open(snapshot, readOnly: true);
            JetFormatBase format = database.Format;
            var text = new System.Text.StringBuilder(label + ":");
            foreach (string table in tables.Length > 0 ? tables : ["MSysObjects", "MSysACEs", "U"])
            {
                Catalog.TableDefinition definition = database.Catalog.FindTable(table)!;
                ReadOnlySpan<byte> tdef = database.Channel.ReadPage(definition.DefinitionPage).Span;
                text.Append($" {table} rows {BitConverter.ToInt32(tdef[format.TdefRowCountOffset..])}" +
                    $" auto {BitConverter.ToInt32(tdef[format.TdefLastAutoNumberOffset..])} indexes");
                foreach (int ordinal in definition.RealIndexes.Select(i => i.RealIndexOrdinal).Distinct().Order())
                {
                    (int total, int unique) = Catalog.TableDefinition.ReadIndexCounts(tdef, format, ordinal);
                    string names = string.Join("+", definition.Indexes.Where(i => i.RealIndexOrdinal == ordinal).Select(i => i.Name));
                    text.Append($" {names}={total}/{unique}");
                }
                text.Append(';');
            }
            output.WriteLine(text.ToString());
        }
        finally { TemporaryDatabase.Delete(snapshot); }
    }

    // What a DAO-created text field and an SQL-created one carry for AllowZeroLength, and whether ACE takes '' in each.
    [Fact]
    public void Probe_allow_zero_length_properties()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string path = TemporaryDatabase.CreatePath("allow-zero-length-");
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", 2)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            object table = Invoke(database, "CreateTableDef", "D")!;
            Invoke(Get(table, "Fields"), "Append", Invoke(table, "CreateField", "T", 10, 50)!);
            object allowed = Invoke(table, "CreateField", "A", 10, 50)!;
            Set(allowed, "AllowZeroLength", true);
            Invoke(Get(table, "Fields"), "Append", allowed);
            Invoke(Get(database, "TableDefs"), "Append", table);
            Invoke(database, "Close");
            AceTestDatabase.ReleaseAbandonedComObjects();
            using (OleDbConnection c = AceTestDatabase.Open(path))
            {
                Exec(c, "CREATE TABLE S (T TEXT(50), M MEMO)");
                foreach (string sql in new[] { "INSERT INTO D (T) VALUES ('')", "INSERT INTO D (A) VALUES ('')", "INSERT INTO S (T, M) VALUES ('', '')" })
                {
                    try { Exec(c, sql); output.WriteLine($"{sql}: ok"); }
                    catch (OleDbException e) { output.WriteLine($"{sql}: {e.Message}"); }
                }
            }
            OleDbConnection.ReleaseObjectPool();
            using var db = JetDatabase.Open(path, readOnly: true);
            Storage.Table objects = db.OpenTable("MSysObjects");
            Catalog.TableDefinition mo = objects.Definition;
            foreach (object?[] row in objects.Rows().Where(r => (string?)r[mo.RequireColumn("Name").Index] is "D" or "S"))
                if (row[mo.RequireColumn("LvProp").Index] is byte[] { Length: > 0 } blob)
                    output.WriteLine($"{row[mo.RequireColumn("Name").Index]}: " + string.Join("; ", Catalog.PropertyBlob.Read(blob)
                        .Select(p => $"[{p.Owner}]{p.Name}={p.Value} flags {p.Flags}")));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ConcurrentAllocationTests' sequence, through ACE: two connections each insert in a transaction, the first
    // commits, the second rolls back and inserts again. Which ids each draws, and what the retry gets.
    [Fact]
    public void Probe_ids_of_two_connections_when_one_rolls_back()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "rollback-two-");
        try
        {
            using (OleDbConnection c = AceTestDatabase.Open(path))
            {
                Exec(c, "CREATE TABLE ConcurrentAlloc (Id COUNTER PRIMARY KEY, K TEXT(30), M MEMO)");
                Exec(c, "CREATE UNIQUE INDEX UX_ConcurrentAlloc_K ON ConcurrentAlloc (K)");
            }
            OleDbConnection.ReleaseObjectPool();
            using OleDbConnection first = AceTestDatabase.Open(path), second = AceTestDatabase.Open(path);
            void Step(string label, Action action)
            {
                try { action(); output.WriteLine($"{label}: ok"); }
                catch (OleDbException e) { output.WriteLine($"{label}: {e.Message.Trim()}"); }
            }
            Step("first BEGIN", () => Exec(first, "BEGIN TRANSACTION"));
            Step("second BEGIN", () => Exec(second, "BEGIN TRANSACTION"));
            Step("first INSERT", () => Exec(first, $"INSERT INTO ConcurrentAlloc (K, M) VALUES ('first', '{new string('A', 24000)}')"));
            Step("second INSERT", () => Exec(second, $"INSERT INTO ConcurrentAlloc (K, M) VALUES ('stale', '{new string('B', 28000)}')"));
            output.WriteLine($"first @@IDENTITY {Scalar(first, "SELECT @@IDENTITY")}, second @@IDENTITY {Scalar(second, "SELECT @@IDENTITY")}");
            Step("first COMMIT", () => Exec(first, "COMMIT TRANSACTION"));
            Step("second ROLLBACK", () => Exec(second, "ROLLBACK TRANSACTION"));
            Step("second BEGIN", () => Exec(second, "BEGIN TRANSACTION"));
            Step("second retry INSERT", () => Exec(second, $"INSERT INTO ConcurrentAlloc (K, M) VALUES ('retry', '{new string('C', 32000)}')"));
            output.WriteLine($"retry @@IDENTITY {Scalar(second, "SELECT @@IDENTITY")}");
            Step("second COMMIT", () => Exec(second, "COMMIT TRANSACTION"));
            using OleDbCommand rows = first.CreateCommand();
            rows.CommandText = "SELECT Id, K FROM ConcurrentAlloc ORDER BY Id";
            using OleDbDataReader reader = rows.ExecuteReader();
            while (reader.Read()) output.WriteLine($"row {reader[0]} {reader[1]}");
        }
        finally
        {
            OleDbConnection.ReleaseObjectPool();
            TemporaryDatabase.Delete(path);
        }
    }

    private void Report(string path, string table, string label)
    {
        using var database = JetDatabase.Open(path, readOnly: true);
        JetFormatBase format = database.Format;
        Catalog.TableDefinition definition = database.Catalog.FindTable(table)!;
        ReadOnlySpan<byte> tdef = database.Channel.ReadPage(definition.DefinitionPage).Span;
        output.WriteLine($"{label}: rows 0x10 = {BitConverter.ToInt32(tdef[format.TdefRowCountOffset..])}, " +
            $"last AutoNumber 0x14 = {BitConverter.ToInt32(tdef[format.TdefLastAutoNumberOffset..])}, " +
            $"complex 0x1C = {Catalog.TableDefinition.ReadComplexAutoNumber(tdef, format)}, " +
            $"rows [{string.Join(" ", database.OpenTable(table).Rows().Select(r => string.Join(",", r)))}]");
    }

    private static object Get(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null)!;

    private static void Set(object target, string member, object value) =>
        target.GetType().InvokeMember(member, BindingFlags.SetProperty, null, target, [value]);

    private static object Item(object collection, object key) =>
        collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, [key])!;

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
