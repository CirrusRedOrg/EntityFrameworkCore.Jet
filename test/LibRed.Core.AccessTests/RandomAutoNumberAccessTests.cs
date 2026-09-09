using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful check: a LibRed-written "Random" AutoNumber (COUNTER DEFAULT GenUniqueID()) is read by Access.
// ACE opens the file without repair, sees it as a proper AutoNumber (rejects a supplied Id, auto-assigns on
// insert), and continues issuing random-looking (non-sequential) ids of its own.
public class RandomAutoNumberAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    // A "Random" AutoNumber: an AutoNumber column carrying DefaultValue = GenUniqueID() (byte-identical to the
    // UI-authored fixture database4.accdb). Created here via the Core CreateTable API with a column default.
    private static void CreateRandomAutoNumberTable(string path)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable("R",
        [
            new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
            new ColumnSpec("Name", JetDataType.Text, 40, IsFixedLength: false),
        ],
        columnDefaults: [("Id", "GenUniqueID()")]);
    }

    [Fact]
    public void Access_reads_a_libred_written_random_autonumber_and_continues_it()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "rand-");
        try
        {
            CreateRandomAutoNumberTable(path);

            using var conn = OpenOleDb(path);

            // ACE treats it as an AutoNumber: on a bare INSERT it assigns the Id itself.
            void Insert(string n) { using var c = conn.CreateCommand(); c.CommandText = $"INSERT INTO R (Name) VALUES ('{n}')"; c.ExecuteNonQuery(); }
            Insert("a"); Insert("b"); Insert("c");

            var ids = new List<int>();
            using (var q = conn.CreateCommand())
            {
                q.CommandText = "SELECT Id FROM R";
                using var r = q.ExecuteReader();
                while (r.Read()) ids.Add(Convert.ToInt32(r[0]));
            }

            // ACE issues its own random (non-sequential, non-zero, distinct) ids into LibRed's table.
            Assert.Equal(3, ids.Count);
            Assert.Equal(3, ids.Distinct().Count());
            Assert.DoesNotContain(0, ids);
            Assert.False(ids.Zip(ids.Skip(1)).All(p => p.Second - p.First == 1), "ids should not be sequential");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_reads_a_libred_written_plain_long_genuniqueid_default_and_applies_it()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "plain-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("R",
                [
                    new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("V", JetDataType.Int32, 4, IsFixedLength: true),
                ],
                primaryKey: ["K"],
                columnDefaults: [("V", "GenUniqueID()")]);

            using var conn = OpenOleDb(path);
            void Insert(int k) { using var c = conn.CreateCommand(); c.CommandText = $"INSERT INTO R (K) VALUES ({k})"; c.ExecuteNonQuery(); }
            Insert(1); Insert(2); Insert(3);

            var vs = new List<int>();
            using (var q = conn.CreateCommand())
            {
                q.CommandText = "SELECT V FROM R";
                using var r = q.ExecuteReader();
                while (r.Read()) vs.Add(Convert.ToInt32(r[0]));
            }

            // ACE opened the LibRed-written file and applied the GenUniqueID() default itself: random, distinct.
            Assert.Equal(3, vs.Count);
            Assert.Equal(3, vs.Distinct().Count());
            Assert.DoesNotContain(0, vs);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Random_autonumber_descriptor_round_trips_through_libred()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "rand-desc-");
        try
        {
            CreateRandomAutoNumberTable(path);

            // Read back through LibRed — same shape as the UI-authored fixture (database4.accdb):
            // an AutoNumber column carrying DefaultValue = GenUniqueID().
            using var reopened = JetDatabase.Open(path);
            var col = reopened.Catalog.UserTables.Single(t => t.Name == "R").Columns.Single(c => c.Name == "Id");
            Assert.True(col.IsAutoNumber);
            Assert.True(col.IsRandomAutoNumber);
            Assert.Equal("GenUniqueID()", col.DefaultValue);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
