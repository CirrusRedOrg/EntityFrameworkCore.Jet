using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A native BIT (YesNo) column written by LibRed. The boolean value is stored in the row's null-bitmap bit
/// (set = true); LibRed coerces the inserted value (1/-1/0/TRUE/FALSE) with Access truthiness. Access reads
/// the bits back correctly and a bare-boolean predicate returns the right rows.
/// </summary>
[Collection(AceCollection.Name)]
public class BooleanWriteAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_reads_libred_written_bit_values()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "bit-");
        try
        {
            using (var conn = OpenOleDb(path))
            using (var c = conn.CreateCommand())
            { c.CommandText = "CREATE TABLE Bits (Id LONG, Flag BIT NOT NULL)"; c.ExecuteNonQuery(); }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var t = db.OpenTable("Bits");
                t.Insert([1, 1]);      // integer 1 → true
                t.Insert([2, 0]);      // integer 0 → false
                t.Insert([3, true]);   // bool true
                t.Insert([4, false]);  // bool false
                t.Insert([5, -1]);     // -1 → true
            }

            using var conn2 = OpenOleDb(path);
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Bits WHERE Flag = TRUE";
                Assert.Equal(3, Convert.ToInt32(c.ExecuteScalar())); // ids 1, 3, 5
            }
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT Flag FROM Bits WHERE Id = 1";
                Assert.Equal(true, c.ExecuteScalar());
            }
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT Flag FROM Bits WHERE Id = 2";
                Assert.Equal(false, c.ExecuteScalar());
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The same values into an INDEXED column, so ACE answers through the index rather than the row bits. The
    // index key has to follow the same truthiness as the row — -1 and 7 are true — and be written the way
    // ACE writes a Yes/No key, or ACE's seek finds nothing at all.
    [Fact]
    public void Access_seeks_libred_written_bit_values_through_an_index()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "bit-ix-");
        try
        {
            using (var conn = OpenOleDb(path))
            {
                using (var c = conn.CreateCommand()) { c.CommandText = "CREATE TABLE Bits (Id LONG, Flag BIT NOT NULL)"; c.ExecuteNonQuery(); }
                using (var c = conn.CreateCommand()) { c.CommandText = "CREATE INDEX IX_Flag ON Bits (Flag)"; c.ExecuteNonQuery(); }
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var t = db.OpenTable("Bits");
                t.Insert([1, 1]);
                t.Insert([2, 0]);
                t.Insert([3, true]);
                t.Insert([4, false]);
                t.Insert([5, -1]);
                t.Insert([6, 7]);
            }

            using var conn2 = OpenOleDb(path);
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Bits WHERE Flag = TRUE";
                Assert.Equal(4, Convert.ToInt32(c.ExecuteScalar())); // ids 1, 3, 5, 6
            }
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Bits WHERE Flag = FALSE";
                Assert.Equal(2, Convert.ToInt32(c.ExecuteScalar())); // ids 2, 4
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Libred_seeks_an_access_written_bit_index()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "bit-ix-ace-");
        try
        {
            using (var conn = OpenOleDb(path))
                foreach (string sql in new[]
                {
                    "CREATE TABLE Bits (Id LONG, Flag BIT NOT NULL)",
                    "CREATE INDEX IX_Flag ON Bits (Flag)",
                    "INSERT INTO Bits (Id, Flag) VALUES (1, TRUE)",
                    "INSERT INTO Bits (Id, Flag) VALUES (2, FALSE)",
                    "INSERT INTO Bits (Id, Flag) VALUES (3, TRUE)",
                })
                    using (var c = conn.CreateCommand()) { c.CommandText = sql; c.ExecuteNonQuery(); }

            using var db = JetDatabase.Open(path);
            var t = db.OpenTable("Bits");
            var index = t.Definition.Indexes.Single(i => i.Name == "IX_Flag");
            Assert.Equal(2, t.SeekRows(index, [null, true]).Count());
            Assert.Single(t.SeekRows(index, [null, false]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
