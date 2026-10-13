using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using LibRed.Tests.Shared;
using Xunit;

namespace LibRed.Engine.Tests;

// A write to a read-only database is refused as ACE refuses it: once the statement is compiled — its names' legality,
// the tables, indexes, views, columns and constraints it finds or drops — and before anything checked while running
// it, with ACE's message for the kind of write. ACE and LibRed each open the same file read-only and run the same
// statement. Where ACE refuses as read-only, LibRed gives ACE's message; where ACE reports something else first,
// LibRed must not answer read-only either.
[Collection(AceCollection.Name)]
public class ReadOnlyRefusalAccessTests
{
    private static readonly string[] ReadOnlyMessages =
    [
        "Operation must use an updateable query.",
        "Could not delete from specified tables.",
        "It is in a read-only database.",
        "Cannot update. Database or object is read-only.",
    ];

    [Theory]
    // Refused as read-only, whatever else is wrong with the statement.
    [InlineData("INSERT INTO Probe (C) VALUES ('ab')")]
    [InlineData("INSERT INTO Probe (C) VALUES ('abcdef')")]          // over-long
    [InlineData("INSERT INTO Probe (Nope) VALUES (1)")]              // no such column
    [InlineData("INSERT INTO Probe (N) VALUES ('x')")]               // type mismatch
    [InlineData("UPDATE Probe SET V = 'abcdef'")]
    [InlineData("UPDATE Probe SET N = 'x'")]
    [InlineData("DELETE FROM Probe")]
    [InlineData("DELETE FROM Probe WHERE N = 1")]
    [InlineData("CREATE TABLE X (A LONG)")]
    [InlineData("CREATE TABLE Probe (A LONG)")]                      // name taken
    [InlineData("CREATE TABLE Y (A LONG, CONSTRAINT FK FOREIGN KEY (A) REFERENCES NoSuch (A))")]
    [InlineData("ALTER TABLE Probe ADD COLUMN C LONG")]              // column taken
    [InlineData("ALTER TABLE Probe ALTER COLUMN V TEXT(1)")]
    [InlineData("ALTER TABLE Probe DROP COLUMN N")]
    [InlineData("ALTER TABLE Probe ADD CONSTRAINT FK FOREIGN KEY (N) REFERENCES NoSuch (A)")]
    [InlineData("CREATE INDEX IX ON Probe (N)")]
    [InlineData("CREATE INDEX IX ON Probe (Nope)")]
    [InlineData("DROP INDEX IXP ON Probe")]
    [InlineData("DROP TABLE Probe")]
    [InlineData("CREATE VIEW V AS SELECT N FROM Probe")]
    [InlineData("CREATE VIEW QP AS SELECT N FROM Probe")]            // name taken
    [InlineData("DROP VIEW QP")]
    [InlineData("CREATE PROCEDURE P AS SELECT N FROM Probe")]
    [InlineData("SELECT N INTO Z FROM Probe")]
    // Another error first: what the statement names is missing, or a name is not one ACE can use.
    [InlineData("INSERT INTO NoSuch VALUES (1)")]
    [InlineData("UPDATE NoSuch SET A = 1")]
    [InlineData("UPDATE Probe SET Nope = 1")]
    [InlineData("DELETE FROM NoSuch")]
    [InlineData("DROP TABLE NoSuch")]
    [InlineData("CREATE INDEX IX ON NoSuch (A)")]
    [InlineData("DROP INDEX NoIx ON Probe")]
    [InlineData("DROP INDEX IXP ON NoSuch")]
    [InlineData("ALTER TABLE NoSuch ADD COLUMN Z LONG")]
    [InlineData("ALTER TABLE Probe DROP COLUMN Nope")]
    [InlineData("ALTER TABLE Probe ALTER COLUMN Nope TEXT(5)")]
    [InlineData("ALTER TABLE Probe DROP CONSTRAINT NoSuchConstraint")]
    [InlineData("DROP VIEW NoView")]
    [InlineData("DROP PROCEDURE NoProc")]
    [InlineData("SELECT N INTO Z FROM NoSuch")]
    [InlineData("SELECT N INTO Probe FROM Probe")]                   // source and destination the same table
    [InlineData("CREATE TABLE [Bad!Name] (A LONG)")]
    [InlineData("CREATE TABLE LLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLL (A LONG)")]
    [InlineData("ALTER TABLE Probe ADD COLUMN LLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLL LONG")]
    public void A_write_to_a_read_only_database_is_refused_where_ACE_refuses_it(string sql)
    {
        string path = CreateProbeDatabase();
        try
        {
            string ace = Outcome(() =>
            {
                using OleDbConnection connection = AceTestDatabase.Open(path, readOnly: true);
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            });
            OleDbConnection.ReleaseObjectPool();
            string libred = Outcome(() =>
            {
                using var db = JetDatabase.Open(path);
                new QueryEngine(db).ExecuteNonQuery(sql);
            });

            if (IsReadOnlyRefusal(ace))
                Assert.Equal(ace, libred);
            else
                Assert.False(libred is "accepted" || IsReadOnlyRefusal(libred), $"ACE: {ace}\nLibRed: {libred}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ACE lets a read-only connection begin a transaction and read inside it, and so does LibRed.
    [Fact]
    public void A_read_only_database_begins_a_transaction_and_reads_inside_it()
    {
        string path = CreateProbeDatabase();
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(path, readOnly: true))
            using (OleDbTransaction transaction = connection.BeginTransaction())
            {
                using OleDbCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT COUNT(*) FROM Probe";
                Assert.Equal(1, Convert.ToInt32(command.ExecuteScalar()));
                transaction.Commit();
            }
            OleDbConnection.ReleaseObjectPool();

            using var db = JetDatabase.Open(path);
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery("BEGIN TRANSACTION");
            Assert.Equal(1, Convert.ToInt32(engine.ExecuteQuery("SELECT COUNT(*) FROM Probe").Rows.Single()[0]));
            engine.ExecuteNonQuery("COMMIT");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A row written through the table API rather than SQL is refused as ACE refuses a DAO recordset's AddNew, Edit
    // and Delete on a read-only database — with neither SQL message.
    [Theory]
    [InlineData("Insert")]
    [InlineData("Update")]
    [InlineData("Delete")]
    public void A_row_written_through_the_table_api_is_refused_as_ACE_refuses_a_recordset(string write)
    {
        Type? daoType = Type.GetTypeFromProgID("DAO.DBEngine.120");
        Assert.SkipWhen(daoType is null, "DAO is not registered in this process.");
        string path = CreateProbeDatabase();
        try
        {
            string ace = Outcome(() =>
            {
                dynamic engine = Activator.CreateInstance(daoType!)!;
                dynamic database = engine.OpenDatabase(path, false, true);
                try
                {
                    dynamic recordset = database.OpenRecordset("Probe");
                    switch (write)
                    {
                        case "Insert": recordset.AddNew(); recordset.Fields["N"].Value = 2; recordset.Update(); break;
                        case "Update": recordset.Edit(); recordset.Fields["N"].Value = 2; recordset.Update(); break;
                        default: recordset.Delete(); break;
                    }
                }
                finally { database.Close(); }
            });
            string libred = Outcome(() =>
            {
                using var db = JetDatabase.Open(path);
                Table table = db.OpenTable("Probe");
                (RowId id, object?[] row) = table.Rows().WithIds().First();
                switch (write)
                {
                    case "Insert": table.Insert([null, null, 2]); break;
                    case "Update": table.Update(id, [row[0], row[1], 2]); break;
                    default: table.Delete(id); break;
                }
            });

            Assert.Contains("Database or object is read-only", ace);
            Assert.Equal(ace, libred);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string CreateProbeDatabase()
    {
        string path = TemporaryDatabase.CreatePath("read-only-");
        JetDatabase.Create(path);
        using (OleDbConnection connection = AceTestDatabase.Open(path))
            foreach (string sql in (string[])
                     [
                         "CREATE TABLE Probe (C CHAR(3), V TEXT(3), N LONG)",
                         "INSERT INTO Probe VALUES ('a', 'b', 1)",
                         "CREATE INDEX IXP ON Probe (V)",
                         "CREATE VIEW QP AS SELECT C FROM Probe",
                     ])
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        OleDbConnection.ReleaseObjectPool();
        return path;
    }

    private static string Outcome(Action run)
    {
        try
        {
            run();
            return "accepted";
        }
        catch (Exception e) when (e is OleDbException or System.Runtime.InteropServices.COMException
                                      or InvalidOperationException or ArgumentException or NotSupportedException
                                      or LibRed.Sql.Binding.SqlBindException)
        {
            return e.Message;
        }
    }

    private static bool IsReadOnlyRefusal(string message) => ReadOnlyMessages.Any(message.Contains);
}
