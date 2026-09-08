using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// A fixed-length text (CHAR/NCHAR) column stores space-padded to its full width — matching ACE. Previously
// LibRed's encoder didn't pad, so inserting a short value into a fixed CHAR threw
// "Column ... encoded to N bytes, expected M". This covers the round-trip and the ACE read-back.
public class FixedCharEncodingTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Libred_inserts_and_reads_a_fixed_char_column()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "fc-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("T",
                [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                 new ColumnSpec("V", JetDataType.Text, 100, IsFixedLength: true)],  // CHAR(50) → 100 bytes fixed
                primaryKey: ["K"]);
            db.OpenTable("T").Insert([1, "Eastern"]);

            object? v = db.OpenTable("T").Rows().First()[1];
            Assert.Equal("Eastern".PadRight(50), v);   // fixed CHAR reads back space-padded (like ACE)
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_reads_a_libred_inserted_fixed_char()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "fca-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("RegionX",
                    [new ColumnSpec("RegionID", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("RegionDescription", JetDataType.Text, 100, IsFixedLength: true)],
                    primaryKey: ["RegionID"]);
                db.OpenTable("RegionX").Insert([1, "Eastern"]);
            }

            using var conn = OpenOleDb(path);
            using var c = conn.CreateCommand();
            c.CommandText = "SELECT RegionDescription FROM RegionX WHERE RegionID = 1";
            string v = (string)c.ExecuteScalar()!;
            Assert.Equal("Eastern", v.TrimEnd());   // ACE reads it (space-padded); trimmed content matches
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
