using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful check: a LibRed-written DATETIME column with a NOW() default is read by Access, which opens the
// file without repair and applies the default itself on a bare insert (a current timestamp).
public class DateTimeDefaultAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_reads_and_applies_a_libred_written_now_default()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "nowdef-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T",
                [
                    new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("V", JetDataType.DateTime, 8, IsFixedLength: true),
                ],
                primaryKey: ["K"],
                columnDefaults: [("V", "NOW()")]);

            // Round-trips through LibRed with the default preserved.
            using (var reopened = JetDatabase.Open(path))
            {
                var col = reopened.Catalog.UserTables.Single(t => t.Name == "T").Columns.Single(c => c.Name == "V");
                Assert.Equal("NOW()", col.DefaultValue);
            }

            DateTime before = DateTime.Now.AddMinutes(-2);
            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K) VALUES (1)"; c.ExecuteNonQuery(); }

            DateTime v;
            using (var c = conn.CreateCommand()) { c.CommandText = "SELECT V FROM T"; v = Convert.ToDateTime(c.ExecuteScalar()); }
            DateTime after = DateTime.Now.AddMinutes(2);

            // ACE applied its own NOW() into LibRed's table — a current timestamp with a time component.
            Assert.InRange(v, before, after);
            Assert.True(v.TimeOfDay > TimeSpan.Zero);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
