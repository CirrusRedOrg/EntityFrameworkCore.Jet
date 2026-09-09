using System.Data.OleDb;
using System.Text;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// The column-id high-water (TDEF 0x29) never decrements on DROP COLUMN, so once 255 ids have been handed out
// no further column can be added — even when the *live* count is lower — until the database is compacted.
// ACE enforces this ("Too many fields defined"); LibRed must too, rather than write a 256th id ACE can't read.
public class ColumnIdHighWaterTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Ace_rejects_add_column_after_255_ids_used_even_with_dropped_columns()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "c255a-");
        try
        {
            using var conn = OpenOleDb(path);
            void Exec(string sql) { using var c = conn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }

            var sb = new StringBuilder("CREATE TABLE C255 (");
            for (int i = 1; i <= 255; i++) sb.Append($"c{i} SHORT{(i < 255 ? ", " : "")}");
            Exec(sb.Append(')').ToString());
            for (int i = 1; i <= 10; i++) Exec($"ALTER TABLE C255 DROP COLUMN c{i}");   // live 245, high-water still 255

            using var add = conn.CreateCommand();
            add.CommandText = "ALTER TABLE C255 ADD COLUMN cNew SHORT";
            var ex = Assert.ThrowsAny<OleDbException>(() => add.ExecuteNonQuery());
            Assert.Contains("Too many fields", ex.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Libred_rejects_add_column_once_the_id_high_water_reaches_255()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "c255l-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            ColumnSpec Short(string n) => new(n, JetDataType.Int16, 2, IsFixedLength: true);

            // 254 columns → high-water 254; one more is still allowed (→ 255).
            db.CreateTable("C", Enumerable.Range(1, 254).Select(i => Short($"c{i}")).ToList());
            Assert.True(db.AddColumn("C", Short("c255")));   // 255th id — OK
            Assert.Equal(255, db.Catalog.FindTable("C")!.Columns.Count);

            // Now the high-water is 255: dropping columns frees the live count but NOT the id space.
            for (int i = 1; i <= 10; i++) db.DropColumn("C", $"c{i}");
            Assert.Equal(245, db.Catalog.FindTable("C")!.Columns.Count);

            var ex = Assert.Throws<NotSupportedException>(() => db.AddColumn("C", Short("cNew")));
            Assert.Contains("too many fields", ex.Message);
            Assert.Equal(245, db.Catalog.FindTable("C")!.Columns.Count);   // unchanged — nothing written
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
