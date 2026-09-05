using System.Data.Common;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Upgrading a file to the ACE 17 format is one byte: page 0 offset 0x14, 0x02 -> 0x06. That was established by
// diffing an ACE-authored DATETIME2 table against a control that added an ordinary DATETIME column to the same
// DAO-created ACE 12 baseline; the only other byte either arm touched was the opening user's commit slot, which
// moves for any write at all. See docs/format/page-00-database.md.
//
// Requires DAO and the ACE OLE DB provider; skips when DAO is absent, as the other ACE probes do. ACE
// heap-corrupts (0xC0000374) under connection churn in this shape, reproducibly, and takes the test process
// with it - so each phase uses ONE connection for all of its statements. A connection is only reopened where
// the file has to be closed in between.
public class AceDateTime2UpgradeTests(ITestOutputHelper output)
{
    // The guard for the half of the finding LibRed would come to depend on: that the byte is SUFFICIENT, not
    // merely necessary. If a future ACE wanted a companion flag, LibRed would be silently writing files Access
    // rejects, and nothing else in the suite would catch it - LibRed reading its own file back proves nothing
    // about whether Access will accept it.
    [Fact]
    public void Writing_the_version_byte_is_a_complete_upgrade_to_datetime2()
    {
        if (!TryCreateAce12Database("dt2-upgrade-", out string path)) return;
        try
        {
            Assert.Equal(0x02, VersionByte(path));

            // Start from a file that already holds data, which is what a real upgrade has to preserve.
            using (DbConnection connection = AceTestDatabase.Open(path))
            {
                Execute(connection, "CREATE TABLE T (Id LONG, D DATETIME)");
                Execute(connection, "INSERT INTO T (Id, D) VALUES (1, #2020-01-02 03:04:05#)");
            }
            Assert.Equal(0x02, VersionByte(path));   // an ordinary DATETIME does not move it

            SetVersionByte(path, 0x06);
            Assert.Equal(0x06, VersionByte(path));

            using (DbConnection connection = AceTestDatabase.Open(path))
            {
                // The data written before the upgrade is still there and still correct.
                Assert.Equal(
                    new DateTime(2020, 1, 2, 3, 4, 5),
                    Convert.ToDateTime(Scalar(connection, "SELECT D FROM T WHERE Id = 1")));

                // And the type the upgrade exists for is now usable, in both the forms that matter: added to
                // the existing table, and used by a new one. The value is verified below through LibRed rather
                // than ACE, whose own DATETIME2 read-back is off by a month (see the other test).
                Execute(connection, "ALTER TABLE T ADD COLUMN E DATETIME2");
                Execute(connection, "INSERT INTO T (Id, E) VALUES (2, #2021-03-04 05:06:07#)");
                Execute(connection, "CREATE TABLE T2 (D2 DATETIME2)");
            }

            // ACE left the version byte where we put it - it had no upgrade of its own left to do.
            Assert.Equal(0x06, VersionByte(path));

            using var db = JetDatabase.Open(path);
            ColumnDef extended = db.Catalog.Tables.Single(t => t.Name == "T").Columns.Single(c => c.Name == "E");
            Assert.Equal(JetDataType.DateTimeExtended, extended.Type);
            Assert.Equal(42, extended.Length);
            Assert.True(extended.IsFixedLength);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ACE's own OLE DB provider reads a DATETIME2 column back with the MONTH ONE SHORT. Measured against
    // Microsoft.ACE.OLEDB.16.0 (engine 04.00.0000) on 2026-08-26, inserting and reading in one connection:
    //
    //     literal                  ACE DATETIME2                ACE DATETIME   LibRed
    //     #2021-01-15 05:06:07#    ArgumentOutOfRangeException   correct        correct
    //     #2021-03-04 05:06:07#    2021-02-04 05:06:07           correct        correct
    //     #2021-12-25 13:14:15#    2021-11-25 13:14:15           correct        correct
    //     #2020-02-29 00:00:00#    2020-01-29 00:00:00           correct        correct
    //
    // January throws instead of shifting because the month arrives as 0, which is not a representable DateTime:
    // System.Data.OleDb builds one straight out of the DBTIMESTAMP struct (ColumnBinding.Value_DBTIMESTAMP). An
    // ordinary DATETIME column in the SAME row, read by the SAME reader, is correct - so the managed layer is
    // fine and it is the provider's DATETIME2 -> DBTIMESTAMP conversion that is off by one. Note 2020-02-29
    // becomes a valid 2020-01-29: outside January this corrupts silently rather than throwing.
    //
    // The bytes on disk are right - which is what this asserts. ACE's misreading is recorded above rather than
    // asserted: it is not our defect, and a fix on Microsoft's side should not fail our suite.
    [Fact]
    public void LibRed_decodes_datetime2_values_that_ace_reads_back_wrongly()
    {
        (string Literal, DateTime Expected)[] cases =
        [
            ("#2021-01-15 05:06:07#", new DateTime(2021, 1, 15, 5, 6, 7)),
            ("#2021-03-04 05:06:07#", new DateTime(2021, 3, 4, 5, 6, 7)),
            ("#2021-12-25 13:14:15#", new DateTime(2021, 12, 25, 13, 14, 15)),
            ("#2020-02-29 00:00:00#", new DateTime(2020, 2, 29)),
        ];

        if (!TryCreateAce12Database("dt2-decode-", out string path)) return;
        try
        {
            SetVersionByte(path, 0x06);

            using (DbConnection connection = AceTestDatabase.Open(path))
            {
                Execute(connection, "CREATE TABLE X (Id LONG, E DATETIME2)");
                for (int i = 0; i < cases.Length; i++)
                    Execute(connection, $"INSERT INTO X (Id, E) VALUES ({i}, {cases[i].Literal})");
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("X");
            int id = table.Definition.Columns.Single(c => c.Name == "Id").Index;
            int extended = table.Definition.Columns.Single(c => c.Name == "E").Index;

            var stored = table.Rows().ToDictionary(r => Convert.ToInt32(r[id]), r => (DateTime)r[extended]!);
            Assert.Equal(cases.Length, stored.Count);
            for (int i = 0; i < cases.Length; i++)
                Assert.Equal(cases[i].Expected, stored[i]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Creates an ACE 12 (version byte <c>0x02</c>) database through DAO — the format LibRed itself
    /// creates. Returns false, having reported it, when DAO is not installed.</summary>
    private bool TryCreateAce12Database(string prefix, out string path)
    {
        // Both tests here have ACE itself create or read a DATETIME2 column, which an ACE below 17 cannot do
        // at all — CI installs the 2016 redistributable. Skip rather than fail: a machine without the type
        // proves nothing either way about the upgrade.
        Assert.SkipUnless(
            AceTestDatabase.SupportsColumnType(TestDatabases.NorthwindAccdb, "DATETIME2"),
            AceTestDatabase.UnsupportedColumnTypeReason("DATETIME2"));

        path = "";
        object? engine = null;
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { engine = Activator.CreateInstance(type); break; } catch (Exception) { }
        }
        if (engine is null) { output.WriteLine("DAO unavailable - skipped."); return false; }

        path = TemporaryDatabase.CreatePath(prefix);
        File.Delete(path);   // DAO creates the file itself and refuses an existing one

        // 128 == dbVersion120, the ACE 12 / Access 2007 format.
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        Invoke(database, "Close");
        return true;
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(DbConnection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static byte VersionByte(string path)
    {
        using var stream = File.OpenRead(path);
        stream.Seek(0x14, SeekOrigin.Begin);
        int b = stream.ReadByte();
        return b < 0 ? throw new EndOfStreamException() : (byte)b;
    }

    private static void SetVersionByte(string path, byte version)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
        stream.Seek(0x14, SeekOrigin.Begin);
        stream.WriteByte(version);
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, System.Reflection.BindingFlags.InvokeMethod, null, target, args);
}
