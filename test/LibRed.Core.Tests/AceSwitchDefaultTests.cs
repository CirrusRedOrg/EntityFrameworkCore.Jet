using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful: a LibRed-written Switch() default is read and applied by ACE (which has the VBA Switch
// function), confirming LibRed's Switch matches ACE.
public class AceSwitchDefaultTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Theory]
    [InlineData("Switch(1=1, 10, 1=2, 20)", 10)]
    [InlineData("Switch(1=2, 10, 1=1, 20)", 20)]
    public void Access_reads_and_applies_a_libred_written_switch_default(string def, int expected)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "sw-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("V", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["K"], columnDefaults: [("V", def)]);

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K) VALUES (1)"; c.ExecuteNonQuery(); }
            object? v; using (var c = conn.CreateCommand()) { c.CommandText = "SELECT V FROM T"; v = c.ExecuteScalar(); }

            Assert.Equal(expected, Convert.ToInt32(v));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
